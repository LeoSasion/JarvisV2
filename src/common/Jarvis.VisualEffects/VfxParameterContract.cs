using System.Text.Json;

namespace Jarvis.VisualEffects;

public sealed record VfxContractDocument(
    int SchemaVersion,
    string ContractId,
    string[] PlatformScope,
    string Architecture,
    string RendererScope,
    string AuthoringModel,
    string LifecycleState,
    bool RuntimeEnabled,
    bool EditorImplemented,
    string ColorBinding,
    VfxClock Clock,
    VfxOrderedEntry[] RenderStages,
    VfxQualityProfile[] QualityProfiles,
    VfxModule[] ParticleModules,
    VfxModule[] PostEffects,
    VfxCapabilities Capabilities);

public sealed record VfxClock(
    string Mode,
    int FixedStepHz,
    bool DeterministicSeedRequired);

public sealed record VfxOrderedEntry(
    string Id,
    int Order);

public sealed record VfxQualityProfile(
    string Id,
    int MaxParticles,
    int MaxTrailPoints,
    int MaxPostPasses);

public sealed record VfxModule(
    string Id,
    string Stage,
    int Order,
    bool EnabledByDefault,
    VfxParameterDefinition[] Parameters);

public sealed record VfxParameterDefinition(
    string Id,
    string ValueType,
    double? MinimumValue,
    double? MaximumValue,
    JsonElement DefaultValue,
    string Unit,
    string[] Options,
    string ColorBinding);

public sealed record VfxCapabilities(
    string GpuBackend,
    string SoftwareReference,
    bool ComponentLocalEffects,
    bool LiveShellIntegration,
    bool PhysicalDeviceIo);

public sealed record VfxCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ContractId,
    string ContractSha256,
    IReadOnlyList<string> PlatformScope,
    int RenderStageCount,
    int QualityProfileCount,
    int ParticleModuleCount,
    int PostEffectCount,
    int ParameterCount,
    bool AllModulesDisabledByDefault,
    bool SharedRgbBindingValidated,
    bool RuntimeEnabled,
    bool EditorImplemented,
    bool ReadyForOwnedProcessPrototype,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public sealed record VfxScenarioResult(
    string Name,
    bool Passed,
    string Detail);

public sealed record VfxModelTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool RuntimeEnabled,
    bool EditorImplemented,
    bool ReadyForOwnedProcessPrototype,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<VfxScenarioResult> Scenarios);

public static class VfxContractCompiler
{
    private const string ContractId = "neural-void-global-vfx-v1";
    private const string SharedRgbFrame = "shared-rgb-frame";

    private static readonly IReadOnlyDictionary<string, int>
        RequiredRenderStages =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["background-particles"] = 100,
            ["vector-content"] = 200,
            ["foreground-particles"] = 300,
            ["post-processing"] = 400,
        };

    private static readonly IReadOnlyDictionary<string, VfxQualityContract>
        RequiredQualityProfiles =
        new Dictionary<string, VfxQualityContract>(StringComparer.Ordinal)
        {
            ["low-power"] = new(512, 4096, 2),
            ["balanced"] = new(2048, 16384, 4),
            ["cinematic-preview"] = new(8192, 65536, 8),
        };

    private static readonly IReadOnlyDictionary<string, VfxModuleContract>
        RequiredParticleModules =
        new Dictionary<string, VfxModuleContract>(StringComparer.Ordinal)
        {
            ["emission"] = new(
                "spawn",
                10,
                ["spawn-rate", "burst-count", "max-particles"]),
            ["motion"] = new(
                "update",
                20,
                [
                    "initial-speed",
                    "heading",
                    "spread",
                    "drag",
                    "turbulence-strength",
                    "turbulence-scale",
                ]),
            ["lifetime"] = new(
                "update",
                30,
                ["duration"]),
            ["appearance"] = new(
                "render",
                40,
                [
                    "shape",
                    "blend-mode",
                    "start-size",
                    "size-over-life",
                    "alpha-over-life",
                    "color-source",
                ]),
            ["trail"] = new(
                "render",
                50,
                ["duration", "width", "decay"]),
        };

    private static readonly IReadOnlyDictionary<string, VfxModuleContract>
        RequiredPostEffects =
        new Dictionary<string, VfxModuleContract>(StringComparer.Ordinal)
        {
            ["bloom"] = new(
                "post-process",
                10,
                ["threshold", "intensity", "radius"]),
            ["feedback-trails"] = new(
                "post-process",
                20,
                ["feedback", "decay"]),
            ["chromatic-aberration"] = new(
                "post-process",
                30,
                ["amount"]),
            ["displacement"] = new(
                "post-process",
                40,
                ["amplitude", "frequency"]),
            ["color-grade"] = new(
                "post-process",
                50,
                ["exposure", "contrast", "saturation"]),
        };

    public static VfxCompilationReceipt Compile(
        VfxContractDocument contract,
        string contractSha256)
    {
        List<string> failures = [];
        ValidateIdentity(contract, failures);
        ValidateClock(contract.Clock, failures);
        ValidateRenderStages(contract.RenderStages, failures);
        ValidateQualityProfiles(contract.QualityProfiles, failures);
        ValidateModules(
            "particle",
            contract.ParticleModules,
            RequiredParticleModules,
            failures);
        ValidateModules(
            "post",
            contract.PostEffects,
            RequiredPostEffects,
            failures);
        ValidateCapabilities(contract.Capabilities, failures);

        VfxModule[] allModules =
            [.. contract.ParticleModules, .. contract.PostEffects];
        int parameterCount =
            allModules.Sum(module => module.Parameters.Length);
        bool allDisabled =
            allModules.All(module => !module.EnabledByDefault);
        bool sharedRgbValidated =
            contract.ColorBinding == SharedRgbFrame &&
            contract.ParticleModules
                .SelectMany(module => module.Parameters)
                .Any(parameter =>
                    parameter.ColorBinding == SharedRgbFrame) &&
            contract.PostEffects
                .SelectMany(module => module.Parameters)
                .Any(parameter =>
                    parameter.ColorBinding == SharedRgbFrame);
        Require(
            allDisabled,
            "vfx-default-activation-invalid",
            failures);
        Require(
            sharedRgbValidated,
            "vfx-shared-rgb-binding-missing",
            failures);

        bool passed = failures.Count == 0;
        return new VfxCompilationReceipt(
            1,
            "jarvisv2-neural-void-global-vfx-compilation",
            passed ? "compiled-parameter-contract" : "blocked",
            contract.ContractId,
            contractSha256,
            contract.PlatformScope,
            contract.RenderStages.Length,
            contract.QualityProfiles.Length,
            contract.ParticleModules.Length,
            contract.PostEffects.Length,
            parameterCount,
            allDisabled,
            sharedRgbValidated,
            contract.RuntimeEnabled,
            contract.EditorImplemented,
            passed,
            false,
            false,
            "not-run",
            false,
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateIdentity(
        VfxContractDocument contract,
        ICollection<string> failures)
    {
        Require(
            contract.SchemaVersion == 1 &&
            contract.ContractId == ContractId,
            "vfx-contract-identity-invalid",
            failures);
        Require(
            contract.PlatformScope.SequenceEqual(
                ["windows10", "windows11"],
                StringComparer.Ordinal),
            "vfx-platform-scope-invalid",
            failures);
        Require(
            contract.Architecture ==
                "module-graph-plus-ordered-post-stack" &&
            contract.RendererScope == "desktop-global-compositor" &&
            contract.AuthoringModel ==
                "film-vfx-and-game-engine-parameters" &&
            contract.LifecycleState == "parameter-contract-only",
            "vfx-architecture-invalid",
            failures);
        Require(
            !contract.RuntimeEnabled &&
            !contract.EditorImplemented &&
            contract.ColorBinding == SharedRgbFrame,
            "vfx-offline-boundary-invalid",
            failures);
    }

    private static void ValidateClock(
        VfxClock clock,
        ICollection<string> failures)
    {
        Require(
            clock.Mode == "fixed-step-monotonic" &&
            clock.FixedStepHz == 60 &&
            clock.DeterministicSeedRequired,
            "vfx-clock-invalid",
            failures);
    }

    private static void ValidateRenderStages(
        VfxOrderedEntry[] stages,
        ICollection<string> failures)
    {
        bool exact =
            stages.Length == RequiredRenderStages.Count &&
            stages
                .Select(stage => stage.Id)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(RequiredRenderStages.Keys) &&
            stages.All(stage =>
                RequiredRenderStages.TryGetValue(
                    stage.Id,
                    out int order) &&
                stage.Order == order) &&
            stages
                .Select(stage => stage.Order)
                .SequenceEqual(stages
                    .Select(stage => stage.Order)
                    .Order());
        Require(
            exact,
            "vfx-render-stage-order-invalid",
            failures);
    }

    private static void ValidateQualityProfiles(
        VfxQualityProfile[] profiles,
        ICollection<string> failures)
    {
        bool exact =
            profiles.Length == RequiredQualityProfiles.Count &&
            profiles
                .Select(profile => profile.Id)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(RequiredQualityProfiles.Keys) &&
            profiles.All(profile =>
                RequiredQualityProfiles.TryGetValue(
                    profile.Id,
                    out VfxQualityContract? expected) &&
                profile.MaxParticles == expected.MaxParticles &&
                profile.MaxTrailPoints == expected.MaxTrailPoints &&
                profile.MaxPostPasses == expected.MaxPostPasses);
        Require(
            exact,
            "vfx-quality-profile-invalid",
            failures);

        bool monotonic =
            profiles
                .Select(profile => profile.MaxParticles)
                .SequenceEqual(profiles
                    .Select(profile => profile.MaxParticles)
                    .Order()) &&
            profiles
                .Select(profile => profile.MaxTrailPoints)
                .SequenceEqual(profiles
                    .Select(profile => profile.MaxTrailPoints)
                    .Order()) &&
            profiles
                .Select(profile => profile.MaxPostPasses)
                .SequenceEqual(profiles
                    .Select(profile => profile.MaxPostPasses)
                    .Order());
        Require(
            monotonic,
            "vfx-quality-order-invalid",
            failures);
    }

    private static void ValidateModules(
        string family,
        VfxModule[] modules,
        IReadOnlyDictionary<string, VfxModuleContract> expectedModules,
        ICollection<string> failures)
    {
        if (
            modules.Length != expectedModules.Count ||
            !modules
                .Select(module => module.Id)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedModules.Keys) ||
            modules
                .GroupBy(module => module.Id, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            failures.Add($"vfx-{family}-module-set-invalid");
        }
        if (!modules
                .Select(module => module.Order)
                .SequenceEqual(modules
                    .Select(module => module.Order)
                    .Order()))
        {
            failures.Add($"vfx-{family}-module-order-invalid");
        }

        foreach (VfxModule module in modules)
        {
            if (!expectedModules.TryGetValue(
                    module.Id,
                    out VfxModuleContract? expected))
            {
                continue;
            }
            Require(
                module.Stage == expected.Stage &&
                module.Order == expected.Order &&
                !module.EnabledByDefault,
                $"vfx-{family}-module-contract-invalid:{module.Id}",
                failures);

            string[] parameterIds =
                module.Parameters
                    .Select(parameter => parameter.Id)
                    .ToArray();
            Require(
                parameterIds.Length ==
                    expected.ParameterIds.Count &&
                parameterIds
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(expected.ParameterIds) &&
                parameterIds.Distinct(
                    StringComparer.Ordinal).Count() ==
                    parameterIds.Length,
                $"vfx-parameter-set-invalid:{module.Id}",
                failures);

            foreach (VfxParameterDefinition parameter in
                         module.Parameters)
            {
                ValidateParameter(
                    module.Id,
                    parameter,
                    failures);
            }
        }
    }

    private static void ValidateParameter(
        string moduleId,
        VfxParameterDefinition parameter,
        ICollection<string> failures)
    {
        string failure = $"vfx-parameter-invalid:{moduleId}:{parameter.Id}";
        bool commonValid =
            !string.IsNullOrWhiteSpace(parameter.Id) &&
            !string.IsNullOrWhiteSpace(parameter.Unit) &&
            parameter.ColorBinding is "none" or SharedRgbFrame &&
            parameter.Options.Distinct(
                StringComparer.Ordinal).Count() ==
                parameter.Options.Length;
        if (!commonValid)
        {
            failures.Add(failure);
            return;
        }

        bool valid = parameter.ValueType switch
        {
            "scalar" => ValidateNumber(parameter, false),
            "integer" => ValidateNumber(parameter, true),
            "range" => ValidateRange(parameter),
            "enum" => ValidateEnum(parameter),
            "curve" => ValidateCurve(parameter),
            _ => false,
        };
        if (!valid)
        {
            failures.Add(failure);
        }
    }

    private static bool ValidateNumber(
        VfxParameterDefinition parameter,
        bool integer)
    {
        if (
            parameter.MinimumValue is not double minimum ||
            parameter.MaximumValue is not double maximum ||
            !ValidBounds(minimum, maximum) ||
            parameter.DefaultValue.ValueKind !=
                JsonValueKind.Number ||
            !parameter.DefaultValue.TryGetDouble(out double value) ||
            !double.IsFinite(value) ||
            value < minimum ||
            value > maximum ||
            parameter.Options.Length != 0)
        {
            return false;
        }
        return !integer ||
            (IsInteger(minimum) &&
             IsInteger(maximum) &&
             IsInteger(value));
    }

    private static bool ValidateRange(
        VfxParameterDefinition parameter)
    {
        if (
            parameter.MinimumValue is not double minimum ||
            parameter.MaximumValue is not double maximum ||
            !ValidBounds(minimum, maximum) ||
            parameter.DefaultValue.ValueKind !=
                JsonValueKind.Array ||
            parameter.Options.Length != 0)
        {
            return false;
        }
        double[] values =
            parameter.DefaultValue
                .EnumerateArray()
                .Select(element =>
                    element.ValueKind == JsonValueKind.Number &&
                    element.TryGetDouble(out double value)
                        ? value
                        : double.NaN)
                .ToArray();
        return
            values.Length == 2 &&
            values.All(double.IsFinite) &&
            values[0] >= minimum &&
            values[1] <= maximum &&
            values[0] <= values[1];
    }

    private static bool ValidateEnum(
        VfxParameterDefinition parameter)
    {
        return
            parameter.MinimumValue is null &&
            parameter.MaximumValue is null &&
            parameter.DefaultValue.ValueKind ==
                JsonValueKind.String &&
            parameter.DefaultValue.GetString() is string value &&
            parameter.Options.Length > 0 &&
            parameter.Options.Contains(
                value,
                StringComparer.Ordinal);
    }

    private static bool ValidateCurve(
        VfxParameterDefinition parameter)
    {
        if (
            parameter.MinimumValue is not double minimum ||
            parameter.MaximumValue is not double maximum ||
            !ValidBounds(minimum, maximum) ||
            parameter.DefaultValue.ValueKind !=
                JsonValueKind.Array ||
            parameter.Options.Length != 0)
        {
            return false;
        }
        double[] values =
            parameter.DefaultValue
                .EnumerateArray()
                .Select(element =>
                    element.ValueKind == JsonValueKind.Number &&
                    element.TryGetDouble(out double value)
                        ? value
                        : double.NaN)
                .ToArray();
        if (
            values.Length < 4 ||
            values.Length % 2 != 0 ||
            values.Any(value => !double.IsFinite(value)))
        {
            return false;
        }

        double previousTime = -1;
        for (int index = 0; index < values.Length; index += 2)
        {
            double time = values[index];
            double value = values[index + 1];
            if (
                time < 0 ||
                time > 1 ||
                time < previousTime ||
                value < minimum ||
                value > maximum)
            {
                return false;
            }
            previousTime = time;
        }
        return values[0] == 0 &&
            values[^2] == 1;
    }

    private static void ValidateCapabilities(
        VfxCapabilities capabilities,
        ICollection<string> failures)
    {
        Require(
            capabilities.GpuBackend == "unselected" &&
            capabilities.SoftwareReference ==
                "deterministic-cpu-required" &&
            !capabilities.ComponentLocalEffects &&
            !capabilities.LiveShellIntegration &&
            !capabilities.PhysicalDeviceIo,
            "vfx-capability-boundary-invalid",
            failures);
    }

    private static bool ValidBounds(
        double minimum,
        double maximum) =>
        double.IsFinite(minimum) &&
        double.IsFinite(maximum) &&
        minimum <= maximum;

    private static bool IsInteger(double value) =>
        value == Math.Truncate(value);

    private static void Require(
        bool condition,
        string failure,
        ICollection<string> failures)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }

    private sealed record VfxQualityContract(
        int MaxParticles,
        int MaxTrailPoints,
        int MaxPostPasses);

    private sealed record VfxModuleContract(
        string Stage,
        int Order,
        IReadOnlySet<string> ParameterIds)
    {
        public VfxModuleContract(
            string stage,
            int order,
            IEnumerable<string> parameterIds)
            : this(
                stage,
                order,
                parameterIds.ToHashSet(StringComparer.Ordinal))
        {
        }
    }
}
