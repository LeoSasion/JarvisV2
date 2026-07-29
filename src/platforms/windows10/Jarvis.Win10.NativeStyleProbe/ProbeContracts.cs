namespace Jarvis.Win10.NativeStyleProbe;

internal sealed record ExplorerIdentity(
    string Path,
    long Size,
    string ProductVersion,
    string FileVersion,
    string Sha256);

internal sealed record WindowsHostIdentity(
    string ProductName,
    string DisplayVersion,
    string EditionId,
    string InstallationType,
    int Build,
    int Ubr,
    string Architecture,
    bool Is64BitOperatingSystem,
    ExplorerIdentity Explorer);

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

internal sealed record Windows10HostProfileCatalog(
    int SchemaVersion,
    string Platform,
    IReadOnlyList<Windows10HostProfile> Profiles);

internal sealed record Windows10HostProfile(
    string ProfileId,
    string Status,
    string DisplayVersion,
    int Build,
    int Ubr,
    string Architecture,
    string InstallationType,
    ExplorerProfile Explorer,
    IReadOnlyList<string> AllowedCapabilities,
    bool ActivationPermitted,
    string LiveExplorer);

internal sealed record ExplorerProfile(
    string ProductVersion,
    string FileVersion,
    long Size,
    string Sha256);
