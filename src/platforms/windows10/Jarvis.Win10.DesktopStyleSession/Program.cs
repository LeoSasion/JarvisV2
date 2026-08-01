using Jarvis.DesktopStyleSession;

namespace Jarvis.Win10.DesktopStyleSession;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "model-test", StringComparison.Ordinal))
            {
                return Win10DesktopStyleGate.RunModelTests();
            }

            if (args.Length == 0)
            {
                return Usage();
            }

            string command = args[0];
            IReadOnlyDictionary<string, string?> options =
                ParseOptions(args[1..]);
            Win10DesktopStyleGateReceipt gate =
                Win10DesktopStyleGate.Inspect();
            if (!gate.Passed ||
                gate.HostProfileId is null ||
                gate.ExplorerProcessId is null)
            {
                Win10DesktopStyleGate.WriteJson(gate);
                return 12;
            }

            DesktopStyleSessionController controller = new();
            DesktopStyleSessionContext context =
                DesktopStyleSessionContext.ForExactWindows10Host(
                    gate.HostProfileId);
            uint explorerProcessId = gate.ExplorerProcessId.Value;
            return command switch
            {
                "inspect" => Inspect(
                    controller,
                    context,
                    explorerProcessId,
                    options),
                "plan-preview" => Plan(
                    controller,
                    context,
                    explorerProcessId,
                    options),
                "apply-preview" => await ApplyAsync(
                    controller,
                    context,
                    explorerProcessId,
                    options),
                _ => Usage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Inspect(
        DesktopStyleSessionController controller,
        DesktopStyleSessionContext context,
        uint explorerProcessId,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(options);
        return controller.Inspect(explorerProcessId, context);
    }

    private static int Plan(
        DesktopStyleSessionController controller,
        DesktopStyleSessionContext context,
        uint explorerProcessId,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(options, "--preset", "--ttl-seconds");
        string presetId = GetWin10Preset(options);
        return controller.Plan(
            explorerProcessId,
            presetId,
            GetInt32(options, "--ttl-seconds"),
            context);
    }

    private static Task<int> ApplyAsync(
        DesktopStyleSessionController controller,
        DesktopStyleSessionContext context,
        uint explorerProcessId,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--preset",
            "--ttl-seconds",
            DesktopStylePolicy.ApplyConfirmation);
        string presetId = GetWin10Preset(options);
        return controller.ApplyAsync(
            explorerProcessId,
            presetId,
            GetInt32(options, "--ttl-seconds"),
            HasFlag(options, DesktopStylePolicy.ApplyConfirmation),
            context);
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Unexpected positional argument '{option}'.");
            }

            if (options.ContainsKey(option))
            {
                throw new ArgumentException(
                    $"Duplicate option '{option}'.");
            }

            if (option == DesktopStylePolicy.ApplyConfirmation)
            {
                options.Add(option, null);
                continue;
            }

            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Option '{option}' requires a value.");
            }

            options.Add(option, arguments[++index]);
        }

        return options;
    }

    private static void EnsureOnly(
        IReadOnlyDictionary<string, string?> options,
        params string[] allowed)
    {
        string[] unexpected = options.Keys
            .Where(option => !allowed.Contains(
                option,
                StringComparer.Ordinal))
            .ToArray();
        if (unexpected.Length != 0)
        {
            throw new ArgumentException(
                $"Unexpected option(s): {string.Join(", ", unexpected)}.");
        }
    }

    private static bool HasFlag(
        IReadOnlyDictionary<string, string?> options,
        string name) =>
        options.TryGetValue(name, out string? value) && value is null;

    private static string GetRequired(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '{name}'.");
        }

        return value;
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        string value = GetRequired(options, name);
        if (!int.TryParse(value, out int result))
        {
            throw new ArgumentException(
                $"Option '{name}' must be an Int32.");
        }

        return result;
    }

    private static string GetWin10Preset(
        IReadOnlyDictionary<string, string?> options)
    {
        string presetId = GetRequired(options, "--preset");
        Win10DesktopStyleGate.ValidatePreset(presetId);
        return presetId;
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  jarvis-win10-desktop-style-session inspect\n" +
            "  jarvis-win10-desktop-style-session plan-preview " +
            "--preset orbital-cyan|reactor-amber|neural-emerald " +
            "--ttl-seconds 10..60\n" +
            "  jarvis-win10-desktop-style-session apply-preview " +
            "--preset orbital-cyan|reactor-amber|neural-emerald " +
            "--ttl-seconds 10..60 " +
            DesktopStylePolicy.ApplyConfirmation + "\n" +
            "  jarvis-win10-desktop-style-session model-test");
        return 2;
    }
}
