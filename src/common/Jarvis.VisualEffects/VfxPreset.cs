using System.Text.Json;

namespace Jarvis.VisualEffects;

public sealed record VfxPresetDocument(
    int SchemaVersion,
    string PresetId,
    int Revision,
    string ContractId,
    string LifecycleState,
    string QualityProfile,
    int DeterministicSeed,
    string VisualSignalBinding,
    bool RuntimeEnabled,
    bool PhysicalDeviceIo,
    VfxPresetModule[] ParticleModules,
    VfxPresetModule[] PostEffects);

public sealed record VfxPresetModule(
    string Id,
    bool Enabled,
    IReadOnlyDictionary<string, JsonElement> ParameterOverrides);

public sealed record VfxPresetMigrationReceipt(
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    string Result,
    bool MigrationRequired,
    bool Admitted,
    IReadOnlyList<string> Failures);

public sealed record VfxPresetCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string PresetId,
    int Revision,
    string PresetSha256,
    string ContractId,
    string ContractSha256,
    string QualityProfile,
    int OverrideCount,
    bool AllModulesDisabled,
    bool SharedVisualSignalValidated,
    bool RuntimeEnabled,
    bool PhysicalDeviceIo,
    bool ReadyForOwnedProcessPrototype,
    bool ReadyForShellMutation,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class VfxPresetMigrator
{
    public const int CurrentSchemaVersion = 1;

    public static VfxPresetMigrationReceipt Admit(
        VfxPresetDocument preset)
    {
        if (preset.SchemaVersion == CurrentSchemaVersion)
        {
            return new(
                preset.SchemaVersion,
                CurrentSchemaVersion,
                "admitted-current-version",
                false,
                true,
                []);
        }

        return new(
            preset.SchemaVersion,
            CurrentSchemaVersion,
            "blocked-unsupported-version",
            true,
            false,
            [
                "vfx-preset-schema-version-unsupported:" +
                preset.SchemaVersion,
            ]);
    }
}

public static class VfxPresetCompiler
{
    private const string LifecycleState = "inert-parameter-preset";
    private const string VisualSignalBinding = "jarvis-visual-signal-v1";

    public static VfxPresetCompilationReceipt Compile(
        VfxContractDocument contract,
        string contractSha256,
        VfxPresetDocument preset,
        string presetSha256)
    {
        List<string> failures = [];
        VfxCompilationReceipt contractReceipt =
            VfxContractCompiler.Compile(contract, contractSha256);
        if (contractReceipt.Result != "compiled-parameter-contract")
        {
            failures.Add("vfx-preset-contract-not-admitted");
        }
        Require(
            IsSha256(contractSha256) &&
            IsSha256(presetSha256),
            "vfx-preset-source-hash-invalid",
            failures);

        VfxPresetMigrationReceipt migration =
            VfxPresetMigrator.Admit(preset);
        failures.AddRange(migration.Failures);

        Require(
            preset.ContractId == contract.ContractId &&
            preset.PresetId == "neural-void-inert-foundation-v1" &&
            preset.Revision == 1 &&
            preset.LifecycleState == LifecycleState,
            "vfx-preset-identity-invalid",
            failures);
        Require(
            contract.QualityProfiles.Any(profile =>
                profile.Id == preset.QualityProfile),
            "vfx-preset-quality-profile-invalid",
            failures);
        Require(
            preset.DeterministicSeed >= 0,
            "vfx-preset-seed-invalid",
            failures);
        bool sharedSignalValidated =
            preset.VisualSignalBinding == VisualSignalBinding &&
            contract.ColorBinding == "shared-rgb-frame";
        Require(
            sharedSignalValidated,
            "vfx-preset-visual-signal-binding-invalid",
            failures);
        Require(
            !preset.RuntimeEnabled &&
            !preset.PhysicalDeviceIo,
            "vfx-preset-offline-boundary-invalid",
            failures);

        ValidateModules(
            "particle",
            preset.ParticleModules,
            contract.ParticleModules,
            failures);
        ValidateModules(
            "post",
            preset.PostEffects,
            contract.PostEffects,
            failures);

        VfxPresetModule[] allModules =
            [.. preset.ParticleModules, .. preset.PostEffects];
        bool allDisabled =
            allModules.All(module => !module.Enabled);
        Require(
            allDisabled,
            "vfx-preset-module-activation-forbidden",
            failures);

        VfxQualityProfile? quality =
            contract.QualityProfiles.FirstOrDefault(profile =>
                profile.Id == preset.QualityProfile);
        ValidateQualityBudget(
            preset.ParticleModules,
            preset.PostEffects,
            quality,
            failures);

        int overrideCount = allModules.Sum(module =>
            module.ParameterOverrides.Count);
        bool passed = failures.Count == 0;
        return new VfxPresetCompilationReceipt(
            1,
            "jarvisv2-neural-void-vfx-preset-compilation",
            passed
                ? "compiled-inert-preset"
                : "blocked-inactive-preset",
            preset.PresetId,
            preset.Revision,
            presetSha256,
            preset.ContractId,
            contractSha256,
            preset.QualityProfile,
            overrideCount,
            allDisabled,
            sharedSignalValidated,
            preset.RuntimeEnabled,
            preset.PhysicalDeviceIo,
            passed,
            false,
            false,
            "not-run",
            false,
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateModules(
        string family,
        VfxPresetModule[] presetModules,
        VfxModule[] contractModules,
        ICollection<string> failures)
    {
        bool exact =
            presetModules.Length == contractModules.Length &&
            presetModules.Select(module => module.Id)
                .SequenceEqual(
                    contractModules.Select(module => module.Id),
                    StringComparer.Ordinal) &&
            presetModules.Select(module => module.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == presetModules.Length;
        Require(
            exact,
            $"vfx-preset-{family}-module-set-invalid",
            failures);

        foreach (VfxPresetModule presetModule in presetModules)
        {
            VfxModule? contractModule =
                contractModules.FirstOrDefault(module =>
                    module.Id == presetModule.Id);
            if (contractModule is null)
            {
                continue;
            }

            if (presetModule.Enabled)
            {
                failures.Add(
                    $"vfx-preset-{family}-module-enabled:" +
                    presetModule.Id);
            }

            foreach ((string parameterId, JsonElement value) in
                         presetModule.ParameterOverrides)
            {
                VfxParameterDefinition? definition =
                    contractModule.Parameters.FirstOrDefault(parameter =>
                        parameter.Id == parameterId);
                if (definition is null)
                {
                    failures.Add(
                        $"vfx-preset-parameter-unknown:" +
                        $"{presetModule.Id}:{parameterId}");
                    continue;
                }
                if (!VfxParameterValueValidator.IsValid(
                        definition,
                        value))
                {
                    failures.Add(
                        $"vfx-preset-parameter-invalid:" +
                        $"{presetModule.Id}:{parameterId}");
                }
            }
        }
    }

    private static void ValidateQualityBudget(
        IEnumerable<VfxPresetModule> particleModules,
        IEnumerable<VfxPresetModule> postEffects,
        VfxQualityProfile? quality,
        ICollection<string> failures)
    {
        if (quality is null)
        {
            return;
        }

        VfxPresetModule? emission =
            particleModules.FirstOrDefault(module =>
                module.Id == "emission");
        if (emission is not null &&
            emission.ParameterOverrides.TryGetValue(
                "max-particles",
                out JsonElement maxParticles) &&
            maxParticles.TryGetInt32(out int particleCount))
        {
            Require(
                particleCount <= quality.MaxParticles,
                "vfx-preset-particle-budget-exceeded",
                failures);
        }

        int enabledPostPasses =
            postEffects.Count(module => module.Enabled);
        Require(
            enabledPostPasses <= quality.MaxPostPasses,
            "vfx-preset-post-budget-exceeded",
            failures);
    }

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

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(Uri.IsHexDigit);
}

public static class VfxParameterValueValidator
{
    public static bool IsValid(
        VfxParameterDefinition definition,
        JsonElement value) =>
        definition.ValueType switch
        {
            "scalar" => ValidateNumber(definition, value, false),
            "integer" => ValidateNumber(definition, value, true),
            "range" => ValidateRange(definition, value),
            "enum" => ValidateEnum(definition, value),
            "curve" => ValidateCurve(definition, value),
            _ => false,
        };

    private static bool ValidateNumber(
        VfxParameterDefinition definition,
        JsonElement value,
        bool integer)
    {
        if (definition.MinimumValue is not double minimum ||
            definition.MaximumValue is not double maximum ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double number) ||
            !double.IsFinite(number) ||
            number < minimum ||
            number > maximum)
        {
            return false;
        }
        return !integer || number == Math.Truncate(number);
    }

    private static bool ValidateRange(
        VfxParameterDefinition definition,
        JsonElement value)
    {
        if (definition.MinimumValue is not double minimum ||
            definition.MaximumValue is not double maximum ||
            value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        double[] values = ReadNumbers(value);
        return
            values.Length == 2 &&
            values.All(double.IsFinite) &&
            values[0] >= minimum &&
            values[1] <= maximum &&
            values[0] <= values[1];
    }

    private static bool ValidateEnum(
        VfxParameterDefinition definition,
        JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is string option &&
        definition.Options.Contains(
            option,
            StringComparer.Ordinal);

    private static bool ValidateCurve(
        VfxParameterDefinition definition,
        JsonElement value)
    {
        if (definition.MinimumValue is not double minimum ||
            definition.MaximumValue is not double maximum ||
            value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        double[] values = ReadNumbers(value);
        if (values.Length < 4 ||
            values.Length % 2 != 0 ||
            values.Any(number => !double.IsFinite(number)))
        {
            return false;
        }

        double previousTime = -1.0;
        for (int index = 0; index < values.Length; index += 2)
        {
            double time = values[index];
            double curveValue = values[index + 1];
            if (time < 0.0 ||
                time > 1.0 ||
                time < previousTime ||
                curveValue < minimum ||
                curveValue > maximum)
            {
                return false;
            }
            previousTime = time;
        }
        return values[0] == 0.0 && values[^2] == 1.0;
    }

    private static double[] ReadNumbers(JsonElement value) =>
        value.EnumerateArray()
            .Select(element =>
                element.ValueKind == JsonValueKind.Number &&
                element.TryGetDouble(out double number)
                    ? number
                    : double.NaN)
            .ToArray();
}
