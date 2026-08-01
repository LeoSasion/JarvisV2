using System.Text.Json;
using System.Windows;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionOverlay;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "model-test")
            {
                return RunModelTests();
            }

            if (args.Length == 0 || args[0] != "show")
            {
                return Usage();
            }

            IReadOnlyDictionary<string, string?> options =
                ParseOptions(args[1..]);
            string expectedWindowHandle =
                GetRequired(options, "--expected-window-handle");
            int ttlSeconds = GetInt32(options, "--ttl-seconds");
            OverlayPolicy.ValidateTtl(ttlSeconds);
            OverlayPolicy.RequireConfirmation(
                options.TryGetValue(
                    OverlayPolicy.Confirmation,
                    out string? confirmationValue) &&
                confirmationValue is null);

            OwnedOverlayGateResult overlayGate =
                OwnedOverlayGate.Inspect(expectedWindowHandle);
            ExplorerCaptionGateResult gate = overlayGate.CaptionGate;
            ExplorerCaptionTargetIdentity target = gate.Receipt.Target ??
                throw new InvalidOperationException(
                    "The exact Explorer read gate returned no target.");
            if (!overlayGate.Passed)
            {
                ExplorerCaptionGate.WriteJson(overlayGate);
                return 12;
            }

            if (!NativeOverlayTarget.TryReadExact(
                    gate.WindowHandle,
                    target,
                    out _,
                    out string detail))
            {
                throw new InvalidOperationException(detail);
            }

            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose,
            };
            OverlayWindow overlay = new(
                gate.WindowHandle,
                target,
                ttlSeconds);
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                overlay.Dispatcher.BeginInvoke(overlay.Close);
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                application.Run(overlay);
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }

            OverlaySessionReceipt receipt = new(
                1,
                "jarvisv2-win10-owned-explorer-caption-overlay-session",
                overlay.TargetRetired
                    ? "passed-target-retired-owned-overlay-closed"
                    : "passed-owned-overlay-session-completed",
                overlay.StartedAtUtc,
                overlay.CompletedAtUtc,
                gate.Receipt.HostProfileId ?? "<missing>",
                target,
                Environment.ProcessId,
                ToHex(overlay.OverlayWindowHandle),
                ttlSeconds,
                overlay.ForegroundSamples,
                overlay.HiddenSamples,
                overlay.RepositionCount,
                overlay.TargetRetired,
                overlayGate.SeparateExplorerProcessAccepted,
                overlayGate.AcceptedCaptionGateFailures,
                true,
                true,
                true,
                false,
                false,
                false,
                false,
                false);
            WriteJson(receipt);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int RunModelTests()
    {
        List<object> scenarios = [];
        RunScenario(scenarios, "ttl-minimum", () =>
        {
            OverlayPolicy.ValidateTtl(OverlayPolicy.MinimumTtlSeconds);
            return true;
        });
        RunScenario(scenarios, "ttl-maximum", () =>
        {
            OverlayPolicy.ValidateTtl(OverlayPolicy.MaximumTtlSeconds);
            return true;
        });
        RunExpectedFailure(scenarios, "ttl-below-minimum", () =>
            OverlayPolicy.ValidateTtl(OverlayPolicy.MinimumTtlSeconds - 1));
        RunExpectedFailure(scenarios, "ttl-above-maximum", () =>
            OverlayPolicy.ValidateTtl(OverlayPolicy.MaximumTtlSeconds + 1));
        RunScenario(scenarios, "confirmation-accepted", () =>
        {
            OverlayPolicy.RequireConfirmation(true);
            return true;
        });
        RunExpectedFailure(scenarios, "confirmation-required", () =>
            OverlayPolicy.RequireConfirmation(false));
        RunScenario(scenarios, "desktop-shell-explorer-accepted", () =>
            OwnedOverlayGate.Evaluate(
                new OwnedOverlayGateInputs(
                    true, false, true, true, true)).Count == 0);
        RunScenario(scenarios, "separate-explorer-process-accepted", () =>
            OwnedOverlayGate.Evaluate(
                new OwnedOverlayGateInputs(
                    false, true, true, true, true)).Count == 0);
        RunScenario(scenarios, "unobserved-process-rejected", () =>
            OwnedOverlayGate.Evaluate(
                new OwnedOverlayGateInputs(
                    false, true, true, false, true)).Count != 0);
        RunScenario(scenarios, "unrelated-caption-failure-rejected", () =>
            OwnedOverlayGate.Evaluate(
                new OwnedOverlayGateInputs(
                    false, false, true, true, true)).Count != 0);

        int passedCount = scenarios.Count(scenario =>
            (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                scenario) ?? false));
        bool passed = passedCount == scenarios.Count;
        WriteJson(new
        {
            schemaVersion = 1,
            receiptType = "jarvisv2-win10-caption-overlay-model-tests",
            result = passed ? "passed" : "failed",
            scenarioCount = scenarios.Count,
            passedCount,
            scenarios,
            explorerMutationPerformed = false,
            moduleActivationPermitted = false,
        });
        return passed ? 0 : 1;
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string?> result = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (!option.StartsWith("--", StringComparison.Ordinal) ||
                result.ContainsKey(option))
            {
                throw new ArgumentException($"Invalid option '{option}'.");
            }

            if (option == OverlayPolicy.Confirmation)
            {
                result.Add(option, null);
                continue;
            }

            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Option '{option}' requires a value.");
            }

            result.Add(option, arguments[++index]);
        }

        return result;
    }

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
        string name) =>
        int.TryParse(GetRequired(options, name), out int value)
            ? value
            : throw new ArgumentException($"Option '{name}' must be Int32.");

    private static void RunScenario(
        ICollection<object> scenarios,
        string name,
        Func<bool> action)
    {
        bool passed;
        string detail;
        try
        {
            passed = action();
            detail = passed ? "passed" : "returned false";
        }
        catch (Exception exception)
        {
            passed = false;
            detail = exception.Message;
        }

        scenarios.Add(new { Name = name, Passed = passed, Detail = detail });
    }

    private static void RunExpectedFailure(
        ICollection<object> scenarios,
        string name,
        Action action)
    {
        bool passed;
        string detail;
        try
        {
            action();
            passed = false;
            detail = "unexpected success";
        }
        catch (Exception exception)
        {
            passed = true;
            detail = exception.GetType().Name;
        }

        scenarios.Add(new { Name = name, Passed = passed, Detail = detail });
    }

    private static string ToHex(nint value) =>
        $"0x{unchecked((ulong)value.ToInt64()):X}";

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: jarvis-win10-explorer-caption-overlay show " +
            "--expected-window-handle 0x... --ttl-seconds 10..60 " +
            OverlayPolicy.Confirmation + " or model-test");
        return 2;
    }
}
