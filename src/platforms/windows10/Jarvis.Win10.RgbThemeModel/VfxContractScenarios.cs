using System.Text.Json;

namespace Jarvis.Win10.RgbThemeModel;

internal static class VfxContractScenarios
{
    private const string ContractHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public static VfxModelTestReceipt Run(
        VfxContractDocument contract)
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
