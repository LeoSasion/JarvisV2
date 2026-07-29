namespace Jarvis.Win10.HostAdmission;

public sealed record ExplorerIdentity(
    string Path,
    long Size,
    string ProductVersion,
    string FileVersion,
    string Sha256);

public sealed record WindowsHostIdentity(
    string ProductName,
    string DisplayVersion,
    string EditionId,
    string InstallationType,
    int Build,
    int Ubr,
    string Architecture,
    bool Is64BitOperatingSystem,
    ExplorerIdentity Explorer);

public sealed record Windows10HostProfileCatalog(
    int SchemaVersion,
    string Platform,
    IReadOnlyList<Windows10HostProfile> Profiles);

public sealed record Windows10HostProfile(
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

public sealed record ExplorerProfile(
    string ProductVersion,
    string FileVersion,
    long Size,
    string Sha256);

public sealed record Windows10HostAdmissionReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    WindowsHostIdentity? Host,
    Windows10HostProfile? Profile,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<string> Failures)
{
    public bool Passed =>
        string.Equals(
            Result,
            "passed-exact-windows10-host",
            StringComparison.Ordinal) &&
        Host is not null &&
        Profile is not null;
}
