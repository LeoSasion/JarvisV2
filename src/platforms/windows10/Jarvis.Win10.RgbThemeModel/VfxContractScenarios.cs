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

        RetainedVectorScene vectorScene =
            RetainedVectorSceneFactory.CreateContractProbe();
        Add(
            scenarios,
            "canonical-retained-vector-scene-compiles",
            () =>
            {
                VectorSceneCompilationReceipt receipt =
                    RetainedVectorSceneCompiler.Compile(vectorScene);
                return
                    receipt.Result ==
                        "compiled-retained-vector-scene" &&
                    receipt.CommandCount == 8 &&
                    receipt.PointCount == 1 &&
                    receipt.LineCount == 1 &&
                    receipt.PolylineCount == 1 &&
                    receipt.ArcCount == 4 &&
                    receipt.PathCount == 1 &&
                    receipt.RectangleCount == 1 &&
                    receipt.EllipseCount == 1 &&
                    receipt.PlaneCount == 1 &&
                    receipt.StaticCommandCount == 7 &&
                    receipt.PerFrameCommandCount == 1 &&
                    receipt.SharedSignalCommandCount == 1;
            });
        Add(
            scenarios,
            "vector-bitmap-resource-rejected",
            () => HasVectorFailure(
                vectorScene with
                {
                    BitmapResourcesRequested = true,
                },
                "vector-scene-bitmap-resource-forbidden"));
        Add(
            scenarios,
            "vector-runtime-effect-rejected",
            () => HasVectorFailure(
                vectorScene with
                {
                    RuntimeEffectsRequested = true,
                },
                "vector-scene-runtime-effect-forbidden"));
        Add(
            scenarios,
            "vector-command-reordering-rejected",
            () => HasVectorFailure(
                vectorScene with
                {
                    Commands =
                        vectorScene.Commands.Reverse().ToArray(),
                },
                "vector-scene-command-order-invalid"));
        Add(
            scenarios,
            "vector-literal-color-channel-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "horizontal-datum",
                    command => command with
                    {
                        Material = command.Material with
                        {
                            ColorChannel = "#00FF9A",
                        },
                    }),
                "vector-command-invalid:horizontal-datum"));
        Add(
            scenarios,
            "bounded-vector-overscan-accepted",
            () =>
            {
                VectorSceneCompilationReceipt receipt =
                    RetainedVectorSceneCompiler.Compile(
                        ReplaceVectorCommand(
                            vectorScene,
                            "focus-junction",
                            command =>
                                ((VectorPointCommand)command) with
                                {
                                    Center =
                                        new VectorPoint(
                                            -6.0,
                                            78.5),
                                }));
                return
                    receipt.Result ==
                        "compiled-retained-vector-scene";
            });
        Add(
            scenarios,
            "excessive-vector-overscan-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "focus-junction",
                    command =>
                        ((VectorPointCommand)command) with
                        {
                            Center =
                                new VectorPoint(
                                    -65.0,
                                    78.5),
                        }),
                "vector-command-invalid:focus-junction"));
        Add(
            scenarios,
            "degenerate-vector-plane-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "background-plane",
                    command =>
                        ((VectorPlaneCommand)command) with
                        {
                            Points =
                            [
                                new(0.0, 0.0),
                                new(1.0, 1.0),
                                new(2.0, 2.0),
                            ],
                        }),
                "vector-command-invalid:background-plane"));
        Add(
            scenarios,
            "vector-quality-budget-drift-rejected",
            () => HasVectorFailure(
                vectorScene with
                {
                    Budget = vectorScene.Budget with
                    {
                        MaxCommands =
                            vectorScene.Budget.MaxCommands - 1,
                    },
                },
                "vector-scene-quality-budget-invalid"));
        Add(
            scenarios,
            "duplicate-vector-command-id-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "horizontal-datum",
                    command =>
                        ((VectorLineCommand)command) with
                        {
                            Id = "background-plane",
                        }),
                "vector-scene-command-id-invalid"));
        Add(
            scenarios,
            "invalid-vector-arc-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "tangent-corner",
                    command =>
                        ((VectorArcCommand)command) with
                        {
                            RadiusX = 0.0,
                        }),
                "vector-command-invalid:tangent-corner"));
        Add(
            scenarios,
            "empty-vector-path-figure-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "compound-path",
                    command =>
                        ((VectorPathCommand)command) with
                        {
                            Figures =
                            [
                                new(
                                    new(100.0, 100.0),
                                    [],
                                    false),
                            ],
                        }),
                "vector-command-invalid:compound-path"));
        Add(
            scenarios,
            "invalid-vector-path-arc-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "compound-path",
                    command =>
                    {
                        VectorPathCommand path =
                            (VectorPathCommand)command;
                        VectorPathFigure figure =
                            path.Figures.Single();
                        VectorPathSegment[] segments =
                            figure.Segments.ToArray();
                        segments[1] =
                            ((VectorPathArcSegment)segments[1]) with
                            {
                                RadiusX = 0.0,
                            };
                        return path with
                        {
                            Figures =
                            [
                                figure with
                                {
                                    Segments = segments,
                                },
                            ],
                        };
                    }),
                "vector-command-invalid:compound-path"));
        Add(
            scenarios,
            "invalid-vector-rectangle-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "registration-rectangle",
                    command =>
                        ((VectorRectangleCommand)command) with
                        {
                            Width = 0.0,
                        }),
                "vector-command-invalid:registration-rectangle"));
        Add(
            scenarios,
            "invalid-vector-ellipse-rejected",
            () => HasVectorFailure(
                ReplaceVectorCommand(
                    vectorScene,
                    "signal-ring",
                    command =>
                        ((VectorEllipseCommand)command) with
                        {
                            RadiusY = 0.0,
                        }),
                "vector-command-invalid:signal-ring"));
        Add(
            scenarios,
            "invalid-vector-scene-resolves-empty",
            () =>
            {
                VectorSceneCompilationReceipt receipt =
                    RetainedVectorSceneCompiler.Compile(
                        vectorScene with
                        {
                            VisualSignalBinding =
                                "component-local-color",
                        });
                return
                    receipt.Result ==
                        "blocked-empty-vector-scene" &&
                    receipt.Failures.Contains(
                        "vector-scene-visual-signal-binding-invalid",
                        StringComparer.Ordinal) &&
                    receipt.SafeScene.Commands.Count == 0 &&
                    receipt.SafeScene.QualityProfile == "low-power" &&
                    !receipt.SafeScene.BitmapResourcesRequested &&
                    !receipt.SafeScene.RuntimeEffectsRequested &&
                    RetainedVectorSceneCompiler.Compile(
                        receipt.SafeScene).Result ==
                        "compiled-retained-vector-scene";
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

    private static bool HasVectorFailure(
        RetainedVectorScene scene,
        string failure) =>
        RetainedVectorSceneCompiler.Compile(scene).Failures.Contains(
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

    private static RetainedVectorScene ReplaceVectorCommand(
        RetainedVectorScene scene,
        string commandId,
        Func<VectorCommand, VectorCommand> mutate) =>
        scene with
        {
            Commands = scene.Commands
                .Select(command =>
                    command.Id == commandId
                        ? mutate(command)
                        : command)
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
