using System.Text.RegularExpressions;

namespace Jarvis.Win10.RgbThemeModel;

internal static partial class ThemeCompiler
{
    private static readonly IReadOnlyDictionary<string, string>
        RequiredNeutralPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["canvas"] = "#030809",
            ["surface"] = "#071113",
            ["elevated"] = "#0A1719",
            ["line"] = "#183B35",
            ["text"] = "#D7F8EC",
            ["mutedText"] = "#6E9187",
        };

    public static ThemeCompilationReceipt Compile(
        ThemeDocument theme,
        string themeSha256)
    {
        List<string> failures = [];
        ValidateIdentity(theme, failures);
        ValidateComposition(theme.ShellComposition, failures);
        ValidateVectorGrammar(theme.VectorGrammar, failures);
        ValidateGlobalEffectsIntent(
            theme.GlobalEffectsIntent,
            failures);
        ValidateNeutralPalette(theme.NeutralPalette, failures);
        List<CompiledPreset> presets =
            ValidatePresets(theme.RecommendedAccents, failures);
        ValidateAccentModel(theme.AccentModel, failures);
        ValidateSyncIntent(theme.SyncIntent, failures);

        bool passed =
            failures.Count == 0 &&
            presets.Count == ThemeContract.RequiredPresets.Count;
        return new ThemeCompilationReceipt(
            1,
            "jarvisv2-win10-neural-void-rgb-theme-compilation",
            passed ? "compiled-approved-offline-intent" : "blocked",
            theme.ThemeId,
            themeSha256,
            presets,
            theme.AccentModel.SemanticConsumers
                .Order(StringComparer.Ordinal)
                .ToArray(),
            theme.AccentModel.EffectModes
                .Select(effect => effect.Id)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            false,
            passed,
            false,
            false,
            true,
            false,
            false,
            false,
            "not-run",
            false,
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateIdentity(
        ThemeDocument theme,
        ICollection<string> failures)
    {
        Require(
            theme.SchemaVersion == 1,
            "theme-schema-version-invalid",
            failures);
        Require(
            theme.Platform == ThemeContract.Platform,
            "theme-platform-invalid",
            failures);
        Require(
            theme.ProfileId == ThemeContract.ProfileId,
            "theme-profile-invalid",
            failures);
        Require(
            theme.ThemeId == ThemeContract.ThemeId,
            "theme-id-invalid",
            failures);
        Require(
            theme.LifecycleState == ThemeContract.LifecycleState,
            "theme-lifecycle-invalid",
            failures);
        Require(
            theme.ApprovedDirection ==
                ThemeContract.ApprovedDirection &&
            theme.ApprovalBasis == ThemeContract.ApprovalBasis,
            "theme-approval-invalid",
            failures);
        Require(
            theme.StyleValuesDefined,
            "theme-style-values-missing",
            failures);
        Require(
            !theme.ExecutionSupported &&
            !theme.MutationSupported &&
            !theme.ActivationPermitted &&
            theme.LiveExplorer == "not-run",
            "theme-offline-boundary-invalid",
            failures);
    }

    private static void ValidateComposition(
        ShellComposition composition,
        ICollection<string> failures)
    {
        Require(
            composition.DesktopVisualLanguage == "neural-void" &&
            composition.NeutralSurfaceSystem ==
                "black-ceramic-and-smoked-glass",
            "shell-composition-direction-invalid",
            failures);
        Require(
            !composition.DeviceControlsVisible &&
            !composition.PeripheralIllustrationsVisible &&
            !composition.RgbSyncPanelVisible,
            "shell-composition-device-ui-forbidden",
            failures);
    }

    private static void ValidateNeutralPalette(
        NeutralPalette palette,
        ICollection<string> failures)
    {
        Dictionary<string, string> observed =
            new(StringComparer.Ordinal)
            {
                ["canvas"] = palette.Canvas,
                ["surface"] = palette.Surface,
                ["elevated"] = palette.Elevated,
                ["line"] = palette.Line,
                ["text"] = palette.Text,
                ["mutedText"] = palette.MutedText,
            };
        foreach ((string name, string expected) in
                     RequiredNeutralPalette)
        {
            string value = observed[name];
            if (!RgbHex().IsMatch(value))
            {
                failures.Add($"neutral-color-invalid:{name}");
            }
            else if (value != expected)
            {
                failures.Add($"neutral-color-drift:{name}");
            }
        }
    }

    private static void ValidateVectorGrammar(
        VectorGrammar grammar,
        ICollection<string> failures)
    {
        Require(
            grammar.Id == ThemeContract.VectorGrammarId &&
            grammar.Selection ==
                ThemeContract.VectorGrammarSelection &&
            grammar.FrameClosure == "subtractive-open",
            "vector-grammar-identity-invalid",
            failures);
        Require(
            grammar.PrimitiveSet.Length ==
                ThemeContract.RequiredVectorPrimitives.Count &&
            grammar.PrimitiveSet
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                    ThemeContract.RequiredVectorPrimitives),
            "vector-grammar-primitive-set-not-exact",
            failures);
        Require(
            grammar.FocusJunctionCount == 2 &&
            grammar.SingleAccentFamily &&
            grammar.AccentBinding ==
                ThemeContract.VectorAccentBinding &&
            grammar.GlowPolicy ==
                ThemeContract.VectorGlowPolicy &&
            !grammar.BitmapResourcesRequired,
            "vector-grammar-render-contract-invalid",
            failures);
    }

    private static void ValidateGlobalEffectsIntent(
        GlobalEffectsIntent intent,
        ICollection<string> failures)
    {
        Require(
            intent.Architecture ==
                ThemeContract.GlobalEffectsArchitecture &&
            intent.RendererScope ==
                ThemeContract.GlobalEffectsRendererScope &&
            intent.Inspiration ==
                ThemeContract.GlobalEffectsInspiration,
            "global-effects-identity-invalid",
            failures);
        Require(
            intent.PlannedSystems.Length ==
                ThemeContract.RequiredGlobalEffectsSystems.Count &&
            intent.PlannedSystems
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                    ThemeContract.RequiredGlobalEffectsSystems),
            "global-effects-system-set-not-exact",
            failures);
        Require(
            intent.ParameterDomains.Length ==
                ThemeContract.RequiredGlobalEffectsParameterDomains.Count &&
            intent.ParameterDomains
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                    ThemeContract.RequiredGlobalEffectsParameterDomains),
            "global-effects-parameter-set-not-exact",
            failures);
        Require(
            intent.ParameterContractId ==
                ThemeContract.GlobalEffectsParameterContractId &&
            intent.ParameterContractImplemented &&
            !intent.LocalGlowImplemented &&
            intent.GlobalGlowReserved &&
            !intent.RuntimeImplemented,
            "global-effects-runtime-boundary-invalid",
            failures);
    }

    private static List<CompiledPreset> ValidatePresets(
        AccentPreset[] presets,
        ICollection<string> failures)
    {
        List<CompiledPreset> compiled = [];
        if (presets.Length != ThemeContract.RequiredPresets.Count ||
            !presets
                .Select(preset => preset.Id)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(ThemeContract.RequiredPresets.Keys))
        {
            failures.Add("accent-preset-set-not-exact");
        }

        if (presets
            .GroupBy(preset => preset.Id, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            failures.Add("accent-preset-id-duplicated");
        }

        foreach (AccentPreset preset in presets.OrderBy(
                     preset => preset.Id,
                     StringComparer.Ordinal))
        {
            if (!ThemeContract.RequiredPresets.TryGetValue(
                    preset.Id,
                    out PresetContract? expected))
            {
                continue;
            }

            bool identityMatches =
                preset.SourceConcept == expected.SourceConcept &&
                preset.Hex == expected.Hex &&
                Math.Abs(
                    preset.HueDegrees - expected.HueDegrees) <
                    0.000001 &&
                preset.Saturation == 1.0 &&
                preset.Value == 1.0;
            if (!identityMatches)
            {
                failures.Add(
                    $"accent-preset-contract-invalid:{preset.Id}");
                continue;
            }

            RgbFrame frame =
                RgbEffectEngine.Sample(
                    preset.HueDegrees,
                    preset.Saturation,
                    preset.Value,
                    "static",
                    0.0);
            if (frame.Hex != preset.Hex)
            {
                failures.Add(
                    $"accent-preset-color-mismatch:{preset.Id}");
                continue;
            }

            compiled.Add(
                new CompiledPreset(
                    preset.Id,
                    preset.SourceConcept,
                    preset.Hex,
                    preset.HueDegrees,
                    preset.Saturation,
                    preset.Value));
        }

        return compiled;
    }

    private static void ValidateAccentModel(
        AccentModel model,
        ICollection<string> failures)
    {
        Require(
            model.ColorSpace == "HSV" &&
            model.HueMinimum == 0.0 &&
            model.HueMaximumExclusive == 360.0 &&
            model.ContinuousHue &&
            model.SaturationMinimum == 0.0 &&
            model.SaturationMaximum == 1.0 &&
            model.ValueMinimum == 0.1 &&
            model.ValueMaximum == 1.0,
            "accent-continuous-hsv-contract-invalid",
            failures);
        Require(
            model.SemanticConsumers.Length ==
                ThemeContract.RequiredConsumers.Count &&
            model.SemanticConsumers
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(ThemeContract.RequiredConsumers),
            "accent-semantic-consumer-set-not-exact",
            failures);

        if (model.EffectModes.Length !=
                ThemeContract.RequiredEffects.Count ||
            !model.EffectModes
                .Select(effect => effect.Id)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(ThemeContract.RequiredEffects.Keys))
        {
            failures.Add("accent-effect-set-not-exact");
        }

        foreach (EffectMode effect in model.EffectModes)
        {
            if (!ThemeContract.RequiredEffects.TryGetValue(
                    effect.Id,
                    out EffectContract? expected))
            {
                continue;
            }

            if (effect.CyclesPerMinute !=
                    expected.CyclesPerMinute ||
                effect.RotatesHue != expected.RotatesHue ||
                effect.ModulatesBrightness !=
                    expected.ModulatesBrightness)
            {
                failures.Add(
                    $"accent-effect-contract-invalid:{effect.Id}");
            }
        }
    }

    private static void ValidateSyncIntent(
        SyncIntent sync,
        ICollection<string> failures)
    {
        Require(
            sync.SharedFrameContractVersion == 1 &&
            sync.StateOwner == "future-jarvis-rgb-state-service" &&
            sync.DisplayConsumer == "windows-shell-visuals" &&
            sync.FutureDeviceConsumer ==
                "external-device-lighting-bridge",
            "sync-frame-identity-invalid",
            failures);
        Require(
            !sync.DeviceControlsVisibleInDesktop &&
            !sync.PhysicalDeviceIllustrationsVisible,
            "sync-device-ui-forbidden",
            failures);
        Require(
            !sync.DeviceIoImplemented &&
            !sync.ProviderSdkBound &&
            !sync.TransportSupported,
            "sync-device-integration-not-offline",
            failures);
        Require(
            !sync.ShellDependsOnDeviceBridge &&
            sync.FailurePolicy ==
                "display-continues-with-last-valid-local-frame",
            "sync-shell-isolation-invalid",
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

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex RgbHex();
}
