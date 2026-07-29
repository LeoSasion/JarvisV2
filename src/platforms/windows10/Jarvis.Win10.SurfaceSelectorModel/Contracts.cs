namespace Jarvis.Win10.SurfaceSelectorModel;

internal static class SelectorContract
{
    public const string Platform = "windows10";
    public const string ProfileId = "win10-22h2-19045.6466-x64";
    public const string SelectorSetId = "win10-19045-shell-classes-v1";
    public const string CandidateStatus =
        "offline-candidate-not-live-authorized";
    public const string EvidenceOrigin =
        "exact-readonly-topology-2026-07-30";

    public static readonly IReadOnlyDictionary<string, SelectorShape>
        RequiredShapes =
        new Dictionary<string, SelectorShape>(StringComparer.Ordinal)
        {
            ["desktop-icon-list"] = new(
                "desktop-icon-list",
                "desktop-host",
                ["Progman", "SHELLDLL_DefView", "SysListView32"],
                true),
            ["explorer-command-bar"] = new(
                "explorer-command-bar",
                "explorer-folder-window",
                [
                    "CabinetWClass",
                    "UIRibbonCommandBarDock",
                    "UIRibbonCommandBar",
                ],
                true),
            ["explorer-content-host"] = new(
                "explorer-content-host",
                "explorer-folder-window",
                [
                    "CabinetWClass",
                    "ShellTabWindowClass",
                    "DUIViewWndClassName",
                    "DirectUIHWND",
                ],
                true),
            ["explorer-folder-view"] = new(
                "explorer-folder-view",
                "explorer-folder-window",
                [
                    "CabinetWClass",
                    "ShellTabWindowClass",
                    "DUIViewWndClassName",
                    "DirectUIHWND",
                    "CtrlNotifySink",
                    "SHELLDLL_DefView",
                ],
                false),
            ["taskbar-start-button"] = new(
                "taskbar-start-button",
                "primary-taskbar",
                ["Shell_TrayWnd", "Start"],
                true),
            ["taskbar-task-list"] = new(
                "taskbar-task-list",
                "primary-taskbar",
                [
                    "Shell_TrayWnd",
                    "ReBarWindow32",
                    "MSTaskSwWClass",
                    "MSTaskListWClass",
                ],
                true),
            ["taskbar-notification-area"] = new(
                "taskbar-notification-area",
                "primary-taskbar",
                ["Shell_TrayWnd", "TrayNotifyWnd"],
                true),
            ["taskbar-clock"] = new(
                "taskbar-clock",
                "primary-taskbar",
                ["Shell_TrayWnd", "TrayNotifyWnd", "TrayClockWClass"],
                true),
        };
}

internal sealed record SelectorShape(
    string Id,
    string SurfaceKind,
    string[] ClassPath,
    bool RequiredVisible);

internal sealed record SelectorCandidateDocument(
    int SchemaVersion,
    string Platform,
    string ProfileId,
    string SelectorSetId,
    string Status,
    string Origin,
    SurfaceSelectorCandidate[] Selectors,
    bool StyleValuesDefined,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer);

internal sealed record SurfaceSelectorCandidate(
    string Id,
    string SurfaceKind,
    string Role,
    string[] ClassPath,
    int ExpectedMatchCount,
    bool RequiredVisible);

internal sealed record TopologyFixtureDocument(
    int SchemaVersion,
    string FixtureType,
    string ProfileId,
    string Source,
    bool WindowTextCollected,
    bool ContainsUserContent,
    SurfaceFixture[] Surfaces);

internal sealed record SurfaceFixture(
    string SurfaceKind,
    string RootClass,
    string ObservedTopologySha256,
    int SourceNodeCount,
    FixtureNode[] Nodes);

internal sealed record FixtureNode(
    string NodeKey,
    string? ParentKey,
    string ClassName,
    bool Visible);

internal sealed record SelectorResolution(
    string Id,
    string Role,
    string SurfaceKind,
    string ClassPath,
    string NodeKey,
    string SelectorFingerprint,
    bool RequiredVisible);

internal sealed record SelectorCompilationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ProfileId,
    string SelectorSetId,
    string CandidateSha256,
    string EvidenceSha256,
    IReadOnlyList<SelectorResolution> Resolutions,
    bool ReadyForVisualIntent,
    bool StyleValuesDefined,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

internal sealed record ModelScenarioResult(
    string Name,
    bool Passed,
    string Detail);

internal sealed record SelectorModelTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool ReadyForVisualIntent,
    bool StyleValuesDefined,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<ModelScenarioResult> Scenarios);
