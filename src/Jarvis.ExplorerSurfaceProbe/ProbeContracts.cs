namespace Jarvis.ExplorerSurfaceProbe;

internal sealed record ExactTargetRequest(
    nint WindowHandle,
    uint ProcessId,
    uint ThreadId,
    string ExpectedTitle,
    DateTime ExpectedProcessStartTimeUtc,
    uint ExpectedDesktopShellProcessId);

internal sealed record ExactTargetObservation(
    string WindowHandle,
    string WindowClass,
    string WindowTitle,
    bool WindowVisible,
    uint ProcessId,
    uint ThreadId,
    uint DesktopShellProcessId,
    string ProcessName,
    DateTime ProcessStartTimeUtc,
    bool SeparateProcess);

internal sealed record AutomationNodeObservation(
    string NodeKey,
    string? ParentKey,
    int Depth,
    int SiblingOrdinal,
    string ClassName,
    string AutomationId,
    string ControlType,
    bool IsControlElement,
    bool IsContentElement,
    bool IsOffscreen);

internal sealed record SurfaceHint(
    string Role,
    string NodeKey,
    string ClassName,
    string AutomationId,
    string EvidenceState);

internal sealed record AutomationTreeSnapshot(
    int NodeCount,
    int MaximumDepthObserved,
    bool Truncated,
    string TopologySha256,
    IReadOnlyList<AutomationNodeObservation> Nodes,
    IReadOnlyList<SurfaceHint> SurfaceHints,
    IReadOnlyList<string> Errors);

internal sealed record SurfaceProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTime InspectedAtUtc,
    ExactTargetObservation? Target,
    AutomationTreeSnapshot? Tree,
    bool ReadyForXamlSelectorVerification,
    bool ReadyForPreview,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);
