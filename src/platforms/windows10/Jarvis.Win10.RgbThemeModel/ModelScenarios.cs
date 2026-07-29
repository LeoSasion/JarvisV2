namespace Jarvis.Win10.RgbThemeModel;

internal static class ModelScenarios
{
    private const string ThemeHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public static ThemeModelTestReceipt Run(ThemeDocument theme)
    {
        List<ModelScenarioResult> scenarios = [];

        Add(scenarios, "compile-approved-neural-void-intent", () =>
        {
            ThemeCompilationReceipt receipt = Compile(theme);
            return receipt.Result ==
                    "compiled-approved-offline-intent" &&
                receipt.RecommendedAccents.Count == 3 &&
                receipt.ReadyForOwnedProcessPreview &&
                !receipt.ReadyForShellMutation &&
                !receipt.ReadyForDeviceIntegration;
        });
        Add(scenarios, "block-direction-drift", () =>
            HasFailure(
                theme with { ApprovedDirection = "A-orbital-command" },
                "theme-approval-invalid"));
        Add(scenarios, "block-device-controls-in-desktop", () =>
            HasFailure(
                theme with
                {
                    ShellComposition =
                        theme.ShellComposition with
                        {
                            DeviceControlsVisible = true,
                        },
                },
                "shell-composition-device-ui-forbidden"));
        Add(scenarios, "block-peripheral-art-in-desktop", () =>
            HasFailure(
                theme with
                {
                    ShellComposition =
                        theme.ShellComposition with
                        {
                            PeripheralIllustrationsVisible = true,
                        },
                },
                "shell-composition-device-ui-forbidden"));
        Add(scenarios, "block-rgb-panel-in-desktop", () =>
            HasFailure(
                theme with
                {
                    ShellComposition =
                        theme.ShellComposition with
                        {
                            RgbSyncPanelVisible = true,
                        },
                },
                "shell-composition-device-ui-forbidden"));
        Add(scenarios, "block-missing-recommended-accent", () =>
            HasFailure(
                theme with
                {
                    RecommendedAccents =
                        theme.RecommendedAccents.Skip(1).ToArray(),
                },
                "accent-preset-set-not-exact"));
        Add(scenarios, "block-preset-concept-drift", () =>
            HasPresetFailure(
                theme,
                "orbital-cyan",
                preset => preset with { SourceConcept = "B" }));
        Add(scenarios, "block-preset-color-drift", () =>
            HasPresetFailure(
                theme,
                "reactor-amber",
                preset => preset with { Hex = "#FF0000" }));
        Add(scenarios, "block-noncontinuous-hue", () =>
            HasFailure(
                theme with
                {
                    AccentModel =
                        theme.AccentModel with
                        {
                            ContinuousHue = false,
                        },
                },
                "accent-continuous-hsv-contract-invalid"));
        Add(scenarios, "block-restricted-hue-range", () =>
            HasFailure(
                theme with
                {
                    AccentModel =
                        theme.AccentModel with
                        {
                            HueMaximumExclusive = 300.0,
                        },
                },
                "accent-continuous-hsv-contract-invalid"));
        Add(scenarios, "block-missing-semantic-consumer", () =>
            HasFailure(
                theme with
                {
                    AccentModel =
                        theme.AccentModel with
                        {
                            SemanticConsumers =
                                theme.AccentModel.SemanticConsumers
                                    .Skip(1)
                                    .ToArray(),
                        },
                },
                "accent-semantic-consumer-set-not-exact"));
        Add(scenarios, "block-missing-effect", () =>
            HasFailure(
                theme with
                {
                    AccentModel =
                        theme.AccentModel with
                        {
                            EffectModes =
                                theme.AccentModel.EffectModes
                                    .Skip(1)
                                    .ToArray(),
                        },
                },
                "accent-effect-set-not-exact"));
        Add(scenarios, "block-device-io-implementation", () =>
            HasSyncFailure(
                theme,
                sync => sync with { DeviceIoImplemented = true },
                "sync-device-integration-not-offline"));
        Add(scenarios, "block-provider-sdk-binding", () =>
            HasSyncFailure(
                theme,
                sync => sync with { ProviderSdkBound = true },
                "sync-device-integration-not-offline"));
        Add(scenarios, "block-device-transport", () =>
            HasSyncFailure(
                theme,
                sync => sync with { TransportSupported = true },
                "sync-device-integration-not-offline"));
        Add(scenarios, "block-shell-device-dependency", () =>
            HasSyncFailure(
                theme,
                sync => sync with
                {
                    ShellDependsOnDeviceBridge = true,
                },
                "sync-shell-isolation-invalid"));
        Add(scenarios, "block-execution-capability", () =>
            HasFailure(
                theme with { ExecutionSupported = true },
                "theme-offline-boundary-invalid"));
        Add(scenarios, "sample-orbital-cyan-exact", () =>
            RgbEffectEngine.Sample(
                186.117647,
                1.0,
                1.0,
                "static",
                0.0).Hex == "#00E5FF");
        Add(scenarios, "sample-reactor-amber-exact", () =>
            RgbEffectEngine.Sample(
                24.941176,
                1.0,
                1.0,
                "static",
                0.0).Hex == "#FF6A00");
        Add(scenarios, "sample-neural-emerald-exact", () =>
            RgbEffectEngine.Sample(
                156.235294,
                1.0,
                1.0,
                "static",
                0.0).Hex == "#00FF9A");
        Add(scenarios, "sample-spectrum-rotates-hue", () =>
        {
            RgbFrame frame =
                RgbEffectEngine.Sample(
                    0.0,
                    1.0,
                    1.0,
                    "spectrum",
                    0.5);
            return frame.HueDegrees == 180.0 &&
                frame.Hex == "#00FFFF";
        });
        Add(scenarios, "sample-breathe-preserves-hue", () =>
        {
            RgbFrame frame =
                RgbEffectEngine.Sample(
                    156.235294,
                    1.0,
                    1.0,
                    "breathe",
                    0.0);
            return frame.HueDegrees == 156.235294 &&
                frame.BrightnessScale == 0.65 &&
                frame.Value == 0.65;
        });
        Add(scenarios, "sample-wraps-hue-and-phase", () =>
        {
            RgbFrame frame =
                RgbEffectEngine.Sample(
                    -60.0,
                    1.0,
                    1.0,
                    "spectrum",
                    -0.5);
            return frame.HueDegrees == 120.0 &&
                frame.Phase == 0.5 &&
                frame.Hex == "#00FF00";
        });

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new ThemeModelTestReceipt(
            1,
            "jarvisv2-win10-neural-void-rgb-theme-model-test",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            false,
            true,
            false,
            false,
            false,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static ThemeCompilationReceipt Compile(
        ThemeDocument theme) =>
        ThemeCompiler.Compile(theme, ThemeHash);

    private static bool HasFailure(
        ThemeDocument theme,
        string failure) =>
        Compile(theme).Failures.Contains(
            failure,
            StringComparer.Ordinal);

    private static bool HasPresetFailure(
        ThemeDocument theme,
        string presetId,
        Func<AccentPreset, AccentPreset> mutate)
    {
        AccentPreset[] presets =
            theme.RecommendedAccents
                .Select(preset =>
                    preset.Id == presetId
                        ? mutate(preset)
                        : preset)
                .ToArray();
        return HasFailure(
            theme with { RecommendedAccents = presets },
            $"accent-preset-contract-invalid:{presetId}");
    }

    private static bool HasSyncFailure(
        ThemeDocument theme,
        Func<SyncIntent, SyncIntent> mutate,
        string failure) =>
        HasFailure(
            theme with { SyncIntent = mutate(theme.SyncIntent) },
            failure);

    private static void Add(
        ICollection<ModelScenarioResult> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    passed,
                    passed ? "passed" : "assertion returned false"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }
}
