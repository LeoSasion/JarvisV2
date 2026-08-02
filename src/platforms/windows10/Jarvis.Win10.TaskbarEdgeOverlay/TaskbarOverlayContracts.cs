using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

public sealed record TaskbarTargetIdentity(
    string WindowHandle,
    uint ProcessId,
    uint ThreadId,
    string RootClass,
    WindowRectangle Rectangle,
    string TopologySha256);

internal sealed record TaskbarOverlaySessionReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string HostProfileId,
    TaskbarTargetIdentity Target,
    int OverlayProcessId,
    string OverlayWindowHandle,
    int TtlSeconds,
    int VisibleSamples,
    int HiddenSamples,
    int FullscreenRetreatSamples,
    int AccessibilityRetreatSamples,
    int RepositionCount,
    int RenderedFrameCount,
    bool TargetRetiredOrIncompatible,
    string VisualContractId,
    string VisualSignalContractId,
    double EdgeHeightDips,
    bool SharedRgbBound,
    bool VectorCorePreserved,
    bool BoundedGlowPostProcessAvailable,
    bool GlowRendered,
    bool BitmapAssetsUsed,
    bool InteractiveTaskbarContentObscured,
    bool OwnedWindowOnly,
    bool MouseTransparent,
    bool NoActivate,
    bool ExplorerMutationPerformed,
    bool InjectionRequested,
    bool ExplorerRestartRequested,
    bool RegistryMutationRequested,
    bool ModuleActivationPermitted);
