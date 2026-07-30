using System.Text.Json;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.RgbThemeModel;

internal static class VfxContractScenarios
{
    private const string ContractHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PresetHash =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    public static VfxModelTestReceipt Run(
        VfxContractDocument contract,
        VfxPresetDocument preset)
    {
        List<VfxScenarioResult> scenarios = [];

        Add(
            scenarios,
            "canonical-contract-compiles",
            () => Compile(contract).Result ==
                "compiled-parameter-contract");
        Add(
            scenarios,
            "runtime-enabled-fails-closed",
            () => HasFailure(
                contract with { RuntimeEnabled = true },
                "vfx-offline-boundary-invalid"));
        Add(
            scenarios,
            "editor-implemented-fails-closed",
            () => HasFailure(
                contract with { EditorImplemented = true },
                "vfx-offline-boundary-invalid"));
        Add(
            scenarios,
            "render-stage-order-drift-rejected",
            () => HasFailure(
                contract with
                {
                    RenderStages = contract.RenderStages
                        .Select(stage =>
                            stage.Id == "vector-content"
                                ? stage with { Order = 201 }
                                : stage)
                        .ToArray(),
                },
                "vfx-render-stage-order-invalid"));
        Add(
            scenarios,
            "quality-budget-drift-rejected",
            () => HasFailure(
                contract with
                {
                    QualityProfiles = contract.QualityProfiles
                        .Select(profile =>
                            profile.Id == "balanced"
                                ? profile with
                                {
                                    MaxParticles = 513,
                                }
                                : profile)
                        .ToArray(),
                },
                "vfx-quality-profile-invalid"));
        Add(
            scenarios,
            "enabled-particle-module-rejected",
            () => HasFailure(
                ReplaceParticleModule(
                    contract,
                    "emission",
                    module => module with
                    {
                        EnabledByDefault = true,
                    }),
                "vfx-particle-module-contract-invalid:emission"));
        Add(
            scenarios,
            "post-stack-reordering-rejected",
            () => HasFailure(
                contract with
                {
                    PostEffects =
                        contract.PostEffects.Reverse().ToArray(),
                },
                "vfx-post-module-order-invalid"));
        Add(
            scenarios,
            "missing-parameter-rejected",
            () => HasFailure(
                ReplaceParticleModule(
                    contract,
                    "trail",
                    module => module with
                    {
                        Parameters =
                            module.Parameters.Skip(1).ToArray(),
                    }),
                "vfx-parameter-set-invalid:trail"));
        Add(
            scenarios,
            "scalar-default-out-of-range-rejected",
            () => HasFailure(
                ReplaceParticleParameter(
                    contract,
                    "motion",
                    "drag",
                    parameter => parameter with
                    {
                        DefaultValue = Number(2),
                    }),
                "vfx-parameter-invalid:motion:drag"));
        Add(
            scenarios,
            "integer-fraction-rejected",
            () => HasFailure(
                ReplaceParticleParameter(
                    contract,
                    "emission",
                    "burst-count",
                    parameter => parameter with
                    {
                        DefaultValue = Number(1.5),
                    }),
                "vfx-parameter-invalid:emission:burst-count"));
        Add(
            scenarios,
            "descending-range-rejected",
            () => HasFailure(
                ReplaceParticleParameter(
                    contract,
                    "lifetime",
                    "duration",
                    parameter => parameter with
                    {
                        DefaultValue = Array(2.4, 0.8),
                    }),
                "vfx-parameter-invalid:lifetime:duration"));
        Add(
            scenarios,
            "non-monotonic-curve-rejected",
            () => HasFailure(
                ReplaceParticleParameter(
                    contract,
                    "appearance",
                    "size-over-life",
                    parameter => parameter with
                    {
                        DefaultValue =
                            Array(0, 0, 0.8, 1, 0.4, 0),
                    }),
                "vfx-parameter-invalid:appearance:size-over-life"));
        Add(
            scenarios,
            "unknown-enum-default-rejected",
            () => HasFailure(
                ReplaceParticleParameter(
                    contract,
                    "appearance",
                    "shape",
                    parameter => parameter with
                    {
                        DefaultValue = Text("sphere"),
                    }),
                "vfx-parameter-invalid:appearance:shape"));
        Add(
            scenarios,
            "invalid-color-binding-rejected",
            () => HasFailure(
                ReplacePostParameter(
                    contract,
                    "bloom",
                    "intensity",
                    parameter => parameter with
                    {
                        ColorBinding = "local-fixed-color",
                    }),
                "vfx-parameter-invalid:bloom:intensity"));
        Add(
            scenarios,
            "live-shell-capability-rejected",
            () => HasFailure(
                contract with
                {
                    Capabilities = contract.Capabilities with
                    {
                        LiveShellIntegration = true,
                    },
                },
                "vfx-capability-boundary-invalid"));
        Add(
            scenarios,
            "canonical-inert-preset-compiles",
            () => CompilePreset(
                contract,
                preset).Result == "compiled-inert-preset");
        Add(
            scenarios,
            "invalid-preset-source-hash-rejected",
            () => VfxPresetCompiler.Compile(
                    contract,
                    ContractHash,
                    preset,
                    "not-a-sha256")
                .Failures.Contains(
                    "vfx-preset-source-hash-invalid",
                    StringComparer.Ordinal));
        Add(
            scenarios,
            "unknown-preset-version-fails-closed",
            () => HasPresetFailure(
                contract,
                preset with { SchemaVersion = 2 },
                "vfx-preset-schema-version-unsupported:2"));
        Add(
            scenarios,
            "enabled-preset-module-rejected",
            () => HasPresetFailure(
                contract,
                ReplacePresetParticleModule(
                    preset,
                    "emission",
                    module => module with { Enabled = true }),
                "vfx-preset-module-activation-forbidden"));
        Add(
            scenarios,
            "unknown-preset-parameter-rejected",
            () => HasPresetFailure(
                contract,
                ReplacePresetParticleModule(
                    preset,
                    "motion",
                    module => module with
                    {
                        ParameterOverrides =
                            new Dictionary<string, JsonElement>(
                                module.ParameterOverrides,
                                StringComparer.Ordinal)
                            {
                                ["unreviewed-force"] = Number(1),
                            },
                    }),
                "vfx-preset-parameter-unknown:" +
                "motion:unreviewed-force"));
        Add(
            scenarios,
            "preset-parameter-overflow-rejected",
            () => HasPresetFailure(
                contract,
                ReplacePresetParticleModule(
                    preset,
                    "emission",
                    module => module with
                    {
                        ParameterOverrides =
                            new Dictionary<string, JsonElement>(
                                module.ParameterOverrides,
                                StringComparer.Ordinal)
                            {
                                ["max-particles"] = Number(9000),
                            },
                    }),
                "vfx-preset-parameter-invalid:" +
                "emission:max-particles"));
        Add(
            scenarios,
            "preset-device-io-rejected",
            () => HasPresetFailure(
                contract,
                preset with { PhysicalDeviceIo = true },
                "vfx-preset-offline-boundary-invalid"));
        Add(
            scenarios,
            "malformed-contract-blocks-preset-without-execution",
            () => HasPresetFailure(
                contract with
                {
                    QualityProfiles =
                    [
                        contract.QualityProfiles[0],
                        contract.QualityProfiles[0],
                        contract.QualityProfiles[2],
                    ],
                },
                preset,
                "vfx-preset-contract-not-admitted"));
        Add(
            scenarios,
            "duplicate-preset-module-rejected",
            () => HasPresetFailure(
                contract,
                preset with
                {
                    ParticleModules =
                    [
                        preset.ParticleModules[0],
                        preset.ParticleModules[0],
                        .. preset.ParticleModules.Skip(2),
                    ],
                },
                "vfx-preset-particle-module-set-invalid"));

        RgbFrame accent =
            RgbEffectEngine.Sample(
                156.235294,
                1.0,
                1.0,
                "signal-pulse",
                0.25);
        VisualSignalFrame signal =
            VisualSignalFrameFactory.Create(
                42,
                12.5,
                120.0,
                0.5,
                accent);
        Add(
            scenarios,
            "canonical-shared-visual-signal-compiles",
            () => VisualSignalFrameCompiler.Compile(signal).Result ==
                "admitted-owned-process-frame");
        Add(
            scenarios,
            "visual-signal-device-io-rejected",
            () => HasSignalFailure(
                signal with { DeviceIoRequested = true },
                "visual-signal-device-io-forbidden"));
        Add(
            scenarios,
            "visual-signal-accent-drift-rejected",
            () => HasSignalFailure(
                ReplaceSignalChannel(
                    signal,
                    "active",
                    channel => channel with
                    {
                        Color = new LinearRgbColor(0.0, 0.0, 0.0),
                    }),
                "visual-signal-shared-accent-invalid"));
        Add(
            scenarios,
            "visual-signal-accent-encoding-rejected",
            () => HasSignalFailure(
                signal with
                {
                    Accent = signal.Accent with
                    {
                        Red = 0,
                        Hex = "#000000",
                    },
                },
                "visual-signal-accent-encoding-invalid"));
        Add(
            scenarios,
            "visual-signal-safety-color-drift-rejected",
            () => HasSignalFailure(
                ReplaceSignalChannel(
                    signal,
                    "warning",
                    channel => channel with
                    {
                        Color = new LinearRgbColor(0.0, 1.0, 0.0),
                    }),
                "visual-signal-safety-color-invalid"));
        Add(
            scenarios,
            "invalid-visual-signal-resolves-inactive",
            () =>
            {
                VisualSignalCompilationReceipt receipt =
                    VisualSignalFrameCompiler.Compile(
                        signal with { TempoBpm = 481.0 });
                return
                    receipt.Result == "blocked-inactive-frame" &&
                    receipt.Failures.Contains(
                        "visual-signal-timing-invalid",
                        StringComparer.Ordinal) &&
                    receipt.SafeFrame.Accent.Hex == "#000000" &&
                    receipt.SafeFrame.SemanticChannels.All(channel =>
                        channel.Intensity == 0.0);
            });

        int passedCount =
            scenarios.Count(scenario => scenario.Passed);
        return new VfxModelTestReceipt(
            1,
            "jarvisv2-neural-void-global-vfx-model-test",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            false,
            false,
            true,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static VfxCompilationReceipt Compile(
        VfxContractDocument contract) =>
        VfxContractCompiler.Compile(contract, ContractHash);

    private static bool HasFailure(
        VfxContractDocument contract,
        string failure) =>
        Compile(contract).Failures.Contains(
            failure,
            StringComparer.Ordinal);

    private static VfxPresetCompilationReceipt CompilePreset(
        VfxContractDocument contract,
        VfxPresetDocument preset) =>
        VfxPresetCompiler.Compile(
            contract,
            ContractHash,
            preset,
            PresetHash);

    private static bool HasPresetFailure(
        VfxContractDocument contract,
        VfxPresetDocument preset,
        string failure) =>
        CompilePreset(contract, preset).Failures.Contains(
            failure,
            StringComparer.Ordinal);

    private static bool HasSignalFailure(
        VisualSignalFrame frame,
        string failure) =>
        VisualSignalFrameCompiler.Compile(frame).Failures.Contains(
            failure,
            StringComparer.Ordinal);

    private static VfxContractDocument ReplaceParticleModule(
        VfxContractDocument contract,
        string moduleId,
        Func<VfxModule, VfxModule> mutate) =>
        contract with
        {
            ParticleModules = contract.ParticleModules
                .Select(module =>
                    module.Id == moduleId
                        ? mutate(module)
                        : module)
                .ToArray(),
        };

    private static VfxContractDocument ReplaceParticleParameter(
        VfxContractDocument contract,
        string moduleId,
        string parameterId,
        Func<VfxParameterDefinition, VfxParameterDefinition> mutate) =>
        ReplaceParticleModule(
            contract,
            moduleId,
            module => module with
            {
                Parameters = module.Parameters
                    .Select(parameter =>
                        parameter.Id == parameterId
                            ? mutate(parameter)
                            : parameter)
                    .ToArray(),
            });

    private static VfxContractDocument ReplacePostParameter(
        VfxContractDocument contract,
        string moduleId,
        string parameterId,
        Func<VfxParameterDefinition, VfxParameterDefinition> mutate) =>
        contract with
        {
            PostEffects = contract.PostEffects
                .Select(module =>
                    module.Id != moduleId
                        ? module
                        : module with
                        {
                            Parameters = module.Parameters
                                .Select(parameter =>
                                    parameter.Id == parameterId
                                        ? mutate(parameter)
                                        : parameter)
                                .ToArray(),
                        })
                .ToArray(),
        };

    private static VfxPresetDocument ReplacePresetParticleModule(
        VfxPresetDocument preset,
        string moduleId,
        Func<VfxPresetModule, VfxPresetModule> mutate) =>
        preset with
        {
            ParticleModules = preset.ParticleModules
                .Select(module =>
                    module.Id == moduleId
                        ? mutate(module)
                        : module)
                .ToArray(),
        };

    private static VisualSignalFrame ReplaceSignalChannel(
        VisualSignalFrame frame,
        string channelId,
        Func<SemanticVisualColor, SemanticVisualColor> mutate) =>
        frame with
        {
            SemanticChannels = frame.SemanticChannels
                .Select(channel =>
                    channel.Id == channelId
                        ? mutate(channel)
                        : channel)
                .ToArray(),
        };

    private static JsonElement Number(double value) =>
        JsonSerializer.SerializeToElement(value);

    private static JsonElement Array(params double[] values) =>
        JsonSerializer.SerializeToElement(values);

    private static JsonElement Text(string value) =>
        JsonSerializer.SerializeToElement(value);

    private static void Add(
        ICollection<VfxScenarioResult> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new VfxScenarioResult(
                    name,
                    passed,
                    passed ? "passed" : "assertion returned false"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new VfxScenarioResult(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }
}
