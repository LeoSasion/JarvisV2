namespace Jarvis.Win10.RgbThemeModel;

internal static class ThemeContract
{
    public const string Platform = "windows10";
    public const string ProfileId = "win10-22h2-19045.6466-x64";
    public const string ThemeId = "neural-void-rgb-v1";
    public const string LifecycleState =
        "approved-visual-intent-offline";
    public const string ApprovedDirection = "D-neural-void";
    public const string ApprovalBasis = "user-selected-2026-07-30";

    public static readonly IReadOnlyDictionary<string, PresetContract>
        RequiredPresets =
        new Dictionary<string, PresetContract>(StringComparer.Ordinal)
        {
            ["orbital-cyan"] =
                new("A", "#00E5FF", 186.117647),
            ["reactor-amber"] =
                new("C", "#FF6A00", 24.941176),
            ["neural-emerald"] =
                new("D", "#00FF9A", 156.235294),
        };

    public static readonly IReadOnlySet<string> RequiredConsumers =
        new HashSet<string>(
            [
                "desktop-ambient-traces",
                "desktop-icon-focus",
                "explorer-active-border",
                "explorer-selection",
                "taskbar-running-indicator",
                "taskbar-status-highlights",
            ],
            StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, EffectContract>
        RequiredEffects =
        new Dictionary<string, EffectContract>(StringComparer.Ordinal)
        {
            ["static"] = new(0.0, false, false),
            ["breathe"] = new(12.0, false, true),
            ["spectrum"] = new(4.0, true, false),
            ["signal-pulse"] = new(30.0, false, true),
        };
}

internal sealed record PresetContract(
    string SourceConcept,
    string Hex,
    double HueDegrees);

internal sealed record EffectContract(
    double CyclesPerMinute,
    bool RotatesHue,
    bool ModulatesBrightness);

internal sealed record ThemeDocument(
    int SchemaVersion,
    string Platform,
    string ProfileId,
    string ThemeId,
    string LifecycleState,
    string ApprovedDirection,
    string ApprovalBasis,
    ShellComposition ShellComposition,
    NeutralPalette NeutralPalette,
    AccentPreset[] RecommendedAccents,
    AccentModel AccentModel,
    SyncIntent SyncIntent,
    bool StyleValuesDefined,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer);

internal sealed record ShellComposition(
    string DesktopVisualLanguage,
    string NeutralSurfaceSystem,
    bool DeviceControlsVisible,
    bool PeripheralIllustrationsVisible,
    bool RgbSyncPanelVisible);

internal sealed record NeutralPalette(
    string Canvas,
    string Surface,
    string Elevated,
    string Line,
    string Text,
    string MutedText);

internal sealed record AccentPreset(
    string Id,
    string SourceConcept,
    string Hex,
    double HueDegrees,
    double Saturation,
    double Value);

internal sealed record AccentModel(
    string ColorSpace,
    double HueMinimum,
    double HueMaximumExclusive,
    bool ContinuousHue,
    double SaturationMinimum,
    double SaturationMaximum,
    double ValueMinimum,
    double ValueMaximum,
    string[] SemanticConsumers,
    EffectMode[] EffectModes);

internal sealed record EffectMode(
    string Id,
    double CyclesPerMinute,
    bool RotatesHue,
    bool ModulatesBrightness);

internal sealed record SyncIntent(
    int SharedFrameContractVersion,
    string StateOwner,
    string DisplayConsumer,
    string FutureDeviceConsumer,
    bool DeviceControlsVisibleInDesktop,
    bool PhysicalDeviceIllustrationsVisible,
    bool DeviceIoImplemented,
    bool ProviderSdkBound,
    bool TransportSupported,
    bool ShellDependsOnDeviceBridge,
    string FailurePolicy);

internal sealed record CompiledPreset(
    string Id,
    string SourceConcept,
    string Hex,
    double HueDegrees,
    double Saturation,
    double Value);

internal sealed record ThemeCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ThemeId,
    string ThemeSha256,
    IReadOnlyList<CompiledPreset> RecommendedAccents,
    IReadOnlyList<string> SemanticConsumers,
    IReadOnlyList<string> EffectModes,
    bool DesktopContainsDeviceUi,
    bool ReadyForOwnedProcessPreview,
    bool ReadyForShellMutation,
    bool ReadyForDeviceIntegration,
    bool StyleValuesDefined,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

internal sealed record RgbFrame(
    int ContractVersion,
    string EffectId,
    double Phase,
    double HueDegrees,
    double Saturation,
    double Value,
    double BrightnessScale,
    byte Red,
    byte Green,
    byte Blue,
    string Hex);

internal sealed record ModelScenarioResult(
    string Name,
    bool Passed,
    string Detail);

internal sealed record ThemeModelTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool DesktopContainsDeviceUi,
    bool ReadyForOwnedProcessPreview,
    bool ReadyForShellMutation,
    bool ReadyForDeviceIntegration,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<ModelScenarioResult> Scenarios);
