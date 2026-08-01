using Jarvis.Win10.HostAdmission;

namespace Jarvis.Win10.ShellSurfaceProbe;

public sealed record ExplorerProcessObservation(
    uint ProcessId,
    DateTime StartTimeUtc,
    bool DesktopShell);

public sealed record WindowRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

public sealed record WindowNodeObservation(
    string NodeKey,
    string? ParentKey,
    int Depth,
    int SiblingOrdinal,
    string WindowHandle,
    string ClassName,
    bool Visible,
    uint ProcessId,
    uint ThreadId,
    WindowRectangle Rectangle);

public sealed record SurfaceTreeObservation(
    string SurfaceKind,
    string RootClass,
    string RootWindow,
    uint RootProcessId,
    uint RootThreadId,
    int NodeCount,
    int MaximumDepthObserved,
    bool Truncated,
    string TopologySha256,
    IReadOnlyDictionary<string, int> ClassHistogram,
    IReadOnlyList<WindowNodeObservation> Nodes);

public sealed record ShellSurfaceInventory(
    uint DesktopShellProcessId,
    IReadOnlyList<ExplorerProcessObservation> ExplorerProcesses,
    IReadOnlyList<SurfaceTreeObservation> DesktopSurfaces,
    IReadOnlyList<SurfaceTreeObservation> ExplorerWindows,
    IReadOnlyList<SurfaceTreeObservation> PrimaryTaskbars,
    IReadOnlyList<SurfaceTreeObservation> SecondaryTaskbars,
    bool ExactDesktopHostObserved,
    bool ExactPrimaryTaskbarObserved,
    bool ExplorerWindowObserved,
    bool CompleteSurfaceSetObserved);

public sealed record ShellSurfaceProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    Windows10HostAdmissionReceipt Admission,
    ShellSurfaceInventory? Inventory,
    string Scope,
    bool WindowTextCollected,
    bool ExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);
