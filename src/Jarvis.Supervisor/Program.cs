using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.Supervisor;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return ExitCodes.Success;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "inspect" => RunInspect(args),
                "inspect-recovery-terminal" => RunInspectRecoveryTerminal(args),
                "arm-kill-switch" => RunArmKillSwitch(args),
                "clear-kill-switch" => RunClearKillSwitch(args),
                "restart-explorer" => await RunRestartExplorerAsync(args),
                _ => InvalidUsage($"Unknown command: {args[0]}"),
            };
        }
        catch (Exception exception)
        {
            WriteJson(
                Console.Error,
                new CommandError(
                    "operation_failed",
                    exception.Message,
                    exception.GetType().Name));
            return ExitCodes.OperationFailed;
        }
    }

    private static int RunInspect(string[] args)
    {
        if (args.Length != 1)
        {
            return InvalidUsage("inspect does not accept arguments.");
        }

        CompatibilityReport report = CompatibilityInspector.Inspect();
        WriteJson(Console.Out, report);
        return report.Compatible
            ? ExitCodes.Success
            : ExitCodes.IncompatibleHost;
    }

    private static int RunArmKillSwitch(string[] args)
    {
        if (args.Length != 1)
        {
            return InvalidUsage("arm-kill-switch does not accept arguments.");
        }

        using StateGateLease lease = KillSwitch.AcquireStateGate();
        KillSwitchResult result = KillSwitch.ArmUnderLease(lease);
        WriteJson(Console.Out, result);
        return ExitCodes.Success;
    }

    private static int RunInspectRecoveryTerminal(string[] args)
    {
        if (args.Length is not (3 or 5) ||
            !string.Equals(args[1], "--module", StringComparison.Ordinal) ||
            (args.Length == 5 &&
             !string.Equals(args[3], "--lease-path", StringComparison.Ordinal)))
        {
            return InvalidUsage(
                "inspect-recovery-terminal requires --module <allowlisted-id> and accepts an optional read-only --lease-path <path>.");
        }

        string moduleId = args[2];
        if (!KillSwitch.IsAllowedModuleId(moduleId))
        {
            return InvalidUsage($"Module id isn't allowlisted: {moduleId}");
        }

        RecoveryTerminalLeaseProbe result =
            RecoveryTerminalLease.Probe(
                moduleId,
                args.Length == 5 ? args[4] : null);
        WriteJson(result.Ready ? Console.Out : Console.Error, result);
        return result.Ready
            ? ExitCodes.Success
            : ExitCodes.SafetyInterlock;
    }

    private static int RunClearKillSwitch(string[] args)
    {
        if (args.Length != 4 ||
            !string.Equals(args[1], "--module", StringComparison.Ordinal) ||
            !string.Equals(args[3], "--confirm", StringComparison.Ordinal))
        {
            return InvalidUsage(
                "clear-kill-switch requires the exact form: --module <allowlisted-id> --confirm.");
        }

        string moduleId = args[2];
        if (!KillSwitch.IsAllowedModuleId(moduleId))
        {
            return InvalidUsage($"Module id isn't allowlisted: {moduleId}");
        }

        using StateGateLease lease = KillSwitch.AcquireStateGate();
        CompatibilityReport report = CompatibilityInspector.Inspect();
        if (!report.Compatible)
        {
            WriteJson(
                Console.Error,
                new
                {
                    error = "incompatible_host",
                    message = "The kill switch remains armed because this host does not match an approved compatibility profile.",
                    compatibility = report,
                });
            return ExitCodes.IncompatibleHost;
        }

        ModuleActivationResult result = KillSwitch.ActivateModuleUnderLease(
            lease,
            moduleId);
        WriteJson(Console.Out, result);
        return ExitCodes.Success;
    }

    private static async Task<int> RunRestartExplorerAsync(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[1], "--confirm", StringComparison.Ordinal))
        {
            return InvalidUsage(
                "restart-explorer is intentionally inert without the exact --confirm argument.");
        }

        if (!OperatingSystem.IsWindows())
        {
            WriteJson(
                Console.Error,
                new CommandError(
                    "unsupported_platform",
                    "Explorer recovery is available only on Windows.",
                    null));
            return ExitCodes.UnsupportedPlatform;
        }

        using StateGateLease lease = KillSwitch.AcquireStateGate();
        KillSwitchProbe probe = KillSwitch.Probe();
        if (probe.State != KillSwitchState.Armed)
        {
            WriteJson(
                Console.Error,
                new CommandError(
                    "kill_switch_not_armed",
                    probe.State == KillSwitchState.Unknown
                        ? $"Refusing to restart Explorer because the emergency flag state is unknown: {probe.Error}"
                        : "Refusing to restart Explorer until arm-kill-switch succeeds.",
                    probe.State.ToString()));
            return ExitCodes.SafetyInterlock;
        }

        using KillSwitchGuard guard = KillSwitch.OpenArmedGuardUnderLease(lease);
        ExplorerRestartResult result = await ExplorerRestarter.RestartCurrentSessionAsync(
            lease,
            guard);
        WriteJson(Console.Out, result);
        return result.Succeeded ? ExitCodes.Success : ExitCodes.OperationFailed;
    }

    private static int InvalidUsage(string message)
    {
        WriteJson(Console.Error, new CommandError("invalid_usage", message, null));
        PrintUsage(Console.Error);
        return ExitCodes.InvalidUsage;
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help";

    private static void PrintUsage(TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine(
            """
            JARVIS2 native-shell safety supervisor

            Usage:
              jarvis-supervisor inspect
              jarvis-supervisor inspect-recovery-terminal --module <id>
              jarvis-supervisor arm-kill-switch
              jarvis-supervisor clear-kill-switch --module <id> --confirm
              jarvis-supervisor restart-explorer --confirm

            Safety rules:
              inspect                 Read-only host fingerprint and compatibility report.
              inspect-recovery-terminal
                                      Read-only proof that the visible recovery terminal has a fresh lease.
              arm-kill-switch         Arms disabled.flag, then revokes the module permit.
              clear-kill-switch       Atomically permits one allowlisted module after exact host and recovery-lease checks.
              restart-explorer        Holds disabled.flag against deletion for the entire recovery.

            Allowed module ids:
              jarvis-taskbar-icon-size

            jarvis-native-taskbar remains build-only until runtime revocation is implemented.

            No command reads or changes Windhawk configuration. No command restarts Explorer by default.
            """);
    }

    private static void WriteJson(TextWriter writer, object value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidUsage = 2;
    public const int UnsupportedPlatform = 3;
    public const int IncompatibleHost = 10;
    public const int SafetyInterlock = 11;
    public const int OperationFailed = 20;
}

internal sealed record CommandError(string Error, string Message, string? ExceptionType);
