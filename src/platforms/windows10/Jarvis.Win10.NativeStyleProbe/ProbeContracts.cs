using Jarvis.Win10.HostAdmission;

namespace Jarvis.Win10.NativeStyleProbe;

internal sealed record SystemVisualIdentity(
    bool CompositionEnabled,
    int CompositionHResult,
    string ColorizationColor,
    bool ColorizationOpaqueBlend,
    int ColorizationHResult,
    bool HighContrast,
    bool ClientAreaAnimation);

internal sealed record HostProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    string? MatchedProfileId,
    WindowsHostIdentity? Host,
    SystemVisualIdentity? SystemVisuals,
    string Scope,
    bool OwnProcessWindowExecutionSupported,
    bool ExplorerMutationSupported,
    bool ActivationPermitted,
    bool MutationPerformed,
    string LiveExplorer,
    string? Error)
{
    public bool Passed =>
        string.Equals(
            Result,
            "passed-exact-own-process-candidate",
            StringComparison.Ordinal);
}

internal sealed record DwmStyleCall(
    string Name,
    int Attribute,
    int Value,
    int HResult);

internal sealed record OwnedWindowVerificationReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    string MatchedProfileId,
    int ProcessId,
    string WindowHandle,
    IReadOnlyList<DwmStyleCall> Calls,
    string Scope,
    bool OwnWindowMutationPerformed,
    bool ExplorerMutationSupported,
    bool ActivationPermitted,
    bool SystemMutationPerformed,
    string LiveExplorer,
    string? Error);
