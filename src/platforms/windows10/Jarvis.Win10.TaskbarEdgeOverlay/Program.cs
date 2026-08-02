using System.IO;
using System.Text.Json;
using System.Windows;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

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

            if (args.Length == 3 &&
                args[0] == "render-preview" &&
                args[1] == "--output")
            {
                TaskbarEdgePreviewMetrics metrics =
                    TaskbarEdgePreviewRenderer.Render(args[2]);
                WriteJson(new
                {
                    schemaVersion = 1,
                    receiptType =
                        "jarvisv2-win10-taskbar-edge-overlay-preview-render",
                    result = "rendered-offline-analytic-vector-preview",
                    outputPath = Path.GetFullPath(args[2]),
                    width = TaskbarEdgePreviewRenderer.PreviewWidth,
                    height = TaskbarEdgePreviewRenderer.PreviewHeight,
                    frameIndex =
                        TaskbarEdgePreviewRenderer.PreviewFrameIndex,
                    metrics.ChangedPixelCount,
                    metrics.DistinctChangedColorCount,
                    metrics.MinimumChangedX,
                    metrics.MaximumChangedX,
                    metrics.MinimumChangedY,
                    metrics.MaximumChangedY,
                    shellContacted = false,
                    explorerMutationPerformed = false,
                    moduleActivationPermitted = false,
                });
                return 0;
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
            TaskbarOverlayPolicy.ValidateTtl(ttlSeconds);
            TaskbarOverlayPolicy.RequireConfirmation(
                options.TryGetValue(
                    TaskbarOverlayPolicy.Confirmation,
                    out string? confirmationValue) &&
                confirmationValue is null);

            TaskbarOverlayGateResult gate =
                TaskbarOverlayGate.Inspect(expectedWindowHandle);
            if (!gate.Passed || gate.Target is null)
            {
                WriteJson(gate);
                return 12;
            }

            if (!NativeTaskbarTarget.TryReadExact(
                    gate.WindowHandle,
                    gate.Target,
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
                gate.Target,
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

            TaskbarOverlaySessionReceipt receipt = new(
                1,
                "jarvisv2-win10-owned-taskbar-edge-overlay-session",
                overlay.TargetRetiredOrIncompatible
                    ? "passed-target-retired-owned-overlay-closed"
                    : "passed-owned-overlay-session-completed",
                overlay.StartedAtUtc,
                overlay.CompletedAtUtc,
                gate.SurfaceProbe.Admission.Profile?.ProfileId ?? "<missing>",
                gate.Target,
                Environment.ProcessId,
                ToHex(overlay.OverlayWindowHandle),
                ttlSeconds,
                overlay.VisibleSamples,
                overlay.HiddenSamples,
                overlay.FullscreenRetreatSamples,
                overlay.AccessibilityRetreatSamples,
                overlay.RepositionCount,
                overlay.RenderedFrameCount,
                overlay.TargetRetiredOrIncompatible,
                "neural-void-taskbar-edge-canary-v1",
                VisualSignalContract.ContractId,
                TaskbarOverlayPolicy.EdgeHeightDips,
                true,
                true,
                true,
                overlay.GlowRendered,
                false,
                false,
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
            TaskbarOverlayPolicy.ValidateTtl(
                TaskbarOverlayPolicy.MinimumTtlSeconds);
            return true;
        });
        RunScenario(scenarios, "ttl-maximum", () =>
        {
            TaskbarOverlayPolicy.ValidateTtl(
                TaskbarOverlayPolicy.MaximumTtlSeconds);
            return true;
        });
        RunExpectedFailure(scenarios, "ttl-below-minimum", () =>
            TaskbarOverlayPolicy.ValidateTtl(
                TaskbarOverlayPolicy.MinimumTtlSeconds - 1));
        RunExpectedFailure(scenarios, "ttl-above-maximum", () =>
            TaskbarOverlayPolicy.ValidateTtl(
                TaskbarOverlayPolicy.MaximumTtlSeconds + 1));
        RunScenario(scenarios, "confirmation-accepted", () =>
        {
            TaskbarOverlayPolicy.RequireConfirmation(true);
            return true;
        });
        RunExpectedFailure(scenarios, "confirmation-required", () =>
            TaskbarOverlayPolicy.RequireConfirmation(false));

        TaskbarOverlayGateInputs admitted = new(
            true, true, true, true, true, true, true, true);
        RunScenario(scenarios, "exact-primary-taskbar-admitted", () =>
            TaskbarOverlayGate.Evaluate(admitted).Count == 0);
        RunScenario(scenarios, "surface-probe-failure-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { SurfaceProbePassed = false }).Count != 0);
        RunScenario(scenarios, "multiple-primary-taskbars-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { ExactlyOnePrimaryTaskbar = false }).Count != 0);
        RunScenario(scenarios, "handle-mismatch-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { ExactHandleMatched = false }).Count != 0);
        RunScenario(scenarios, "foreign-shell-pid-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { DesktopShellProcessMatched = false }).Count != 0);
        RunScenario(scenarios, "hidden-taskbar-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { RootVisible = false }).Count != 0);
        RunScenario(scenarios, "vertical-taskbar-rejected-by-gate", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { BottomHorizontalGeometry = false }).Count != 0);
        RunScenario(scenarios, "missing-capability-rejected", () =>
            TaskbarOverlayGate.Evaluate(
                admitted with { OverlayCapabilityGranted = false }).Count != 0);

        NativeRectangle bottom = new(0, 1040, 1920, 1080);
        RunScenario(scenarios, "bottom-horizontal-geometry-accepted", () =>
            NativeTaskbarTarget.IsSupportedBottomHorizontalGeometry(bottom));
        RunScenario(scenarios, "top-taskbar-geometry-rejected", () =>
            !NativeTaskbarTarget.IsSupportedBottomHorizontalGeometry(
                new NativeRectangle(0, 0, 1920, 40)));
        RunScenario(scenarios, "vertical-taskbar-geometry-rejected", () =>
            !NativeTaskbarTarget.IsSupportedBottomHorizontalGeometry(
                new NativeRectangle(1880, 0, 1920, 1080)));
        RunScenario(scenarios, "fullscreen-occlusion-detected", () =>
            NativeTaskbarTarget.OccludesTaskbarEdge(
                new NativeRectangle(0, 0, 1920, 1080),
                bottom));
        RunScenario(scenarios, "maximized-work-area-not-occlusion", () =>
            !NativeTaskbarTarget.OccludesTaskbarEdge(
                new NativeRectangle(0, 0, 1920, 1040),
                bottom));
        RunScenario(scenarios, "partial-window-not-occlusion", () =>
            !NativeTaskbarTarget.OccludesTaskbarEdge(
                new NativeRectangle(200, 200, 1600, 1080),
                bottom));
        RunScenario(scenarios, "shared-rgb-signal-compiles", () =>
        {
            RgbFrame rgb = RgbEffectEngine.Sample(
                175.18,
                0.61712,
                0.87059,
                "signal-pulse",
                0.25);
            VisualSignalCompilationReceipt receipt =
                VisualSignalFrameCompiler.Compile(
                    VisualSignalFrameFactory.Create(
                        1,
                        0.0,
                        30.0,
                        1.0,
                        rgb));
            return receipt.ReadyForOwnedProcessPrototype &&
                receipt.SharedAccentValidated &&
                !receipt.ReadyForShellMutation;
        });

        int passedCount = scenarios.Count(scenario =>
            (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                scenario) ?? false));
        bool passed = passedCount == scenarios.Count;
        WriteJson(new
        {
            schemaVersion = 1,
            receiptType = "jarvisv2-win10-taskbar-edge-overlay-model-tests",
            result = passed ? "passed" : "failed",
            scenarioCount = scenarios.Count,
            passedCount,
            scenarios,
            explorerMutationPerformed = false,
            injectionRequested = false,
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

            if (option == TaskbarOverlayPolicy.Confirmation)
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
            "Usage: jarvis-win10-taskbar-edge-overlay show " +
            "--expected-window-handle 0x... --ttl-seconds 10..60 " +
            TaskbarOverlayPolicy.Confirmation +
            " or model-test or render-preview --output <path.png>");
        return 2;
    }
}
