using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class Win10HostInspector
{
    private const string CurrentVersionKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static HostProbeReceipt Inspect()
    {
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return Blocked(
                    observedAtUtc,
                    "incompatible-host",
                    "The Windows 10 native-style probe only runs on Windows.");
            }

            WindowsHostIdentity host = ReadHostIdentity();
            SystemVisualIdentity visuals =
                Win10DwmApi.InspectSystemVisuals();
            Windows10HostProfileCatalog catalog = HostProfileCatalog.Load();
            Windows10HostProfile? profile =
                catalog.Profiles.SingleOrDefault(candidate =>
                    Matches(candidate, host));

            return new HostProbeReceipt(
                1,
                "jarvisv2-win10-native-style-host-probe",
                profile is null
                    ? "blocked-no-exact-profile"
                    : "passed-exact-own-process-candidate",
                observedAtUtc,
                profile?.ProfileId,
                host,
                visuals,
                "own-process-hwnd-only",
                profile is not null,
                false,
                false,
                false,
                "not-run",
                profile is null
                    ? "No exact Windows 10 build, UBR, architecture and Explorer identity profile matched."
                    : null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            CryptographicException or
            InvalidOperationException)
        {
            return Blocked(
                observedAtUtc,
                "failed",
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static WindowsHostIdentity ReadHostIdentity()
    {
        using RegistryKey currentVersion =
            Registry.LocalMachine.OpenSubKey(CurrentVersionKey) ??
            throw new InvalidOperationException(
                "The Windows current-version identity key is unavailable.");

        int build = ParseRequiredInt(
            currentVersion.GetValue("CurrentBuildNumber"),
            "CurrentBuildNumber");
        int ubr = ParseRequiredInt(
            currentVersion.GetValue("UBR"),
            "UBR");
        string windowsPath =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string explorerPath = Path.Combine(windowsPath, "explorer.exe");
        FileInfo explorerFile = new(explorerPath);
        if (!explorerFile.Exists)
        {
            throw new FileNotFoundException(
                "The Windows Explorer image is unavailable.",
                explorerPath);
        }

        FileVersionInfo version =
            FileVersionInfo.GetVersionInfo(explorerPath);
        using FileStream stream = File.OpenRead(explorerPath);
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));

        return new WindowsHostIdentity(
            ReadString(currentVersion, "ProductName"),
            ReadString(currentVersion, "DisplayVersion"),
            ReadString(currentVersion, "EditionID"),
            ReadString(currentVersion, "InstallationType"),
            build,
            ubr,
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.Is64BitOperatingSystem,
            new ExplorerIdentity(
                explorerPath,
                explorerFile.Length,
                version.ProductVersion ?? string.Empty,
                version.FileVersion ?? string.Empty,
                sha256));
    }

    private static bool Matches(
        Windows10HostProfile profile,
        WindowsHostIdentity host) =>
        host.Build >= 10240 &&
        host.Build < 22000 &&
        profile.Build == host.Build &&
        profile.Ubr == host.Ubr &&
        string.Equals(
            profile.Architecture,
            host.Architecture,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            profile.InstallationType,
            host.InstallationType,
            StringComparison.Ordinal) &&
        profile.Explorer.Size == host.Explorer.Size &&
        string.Equals(
            profile.Explorer.ProductVersion,
            host.Explorer.ProductVersion,
            StringComparison.Ordinal) &&
        string.Equals(
            profile.Explorer.FileVersion,
            host.Explorer.FileVersion,
            StringComparison.Ordinal) &&
        string.Equals(
            profile.Explorer.Sha256,
            host.Explorer.Sha256,
            StringComparison.OrdinalIgnoreCase) &&
        !profile.ActivationPermitted &&
        string.Equals(
            profile.LiveExplorer,
            "not-run",
            StringComparison.Ordinal);

    private static int ParseRequiredInt(object? value, string name)
    {
        if (value is int integer)
        {
            return integer;
        }

        if (int.TryParse(Convert.ToString(value), out int parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Windows identity value '{name}' is missing or invalid.");
    }

    private static string ReadString(RegistryKey key, string name) =>
        Convert.ToString(key.GetValue(name)) ?? string.Empty;

    private static HostProbeReceipt Blocked(
        DateTimeOffset observedAtUtc,
        string result,
        string error) =>
        new(
            1,
            "jarvisv2-win10-native-style-host-probe",
            result,
            observedAtUtc,
            null,
            null,
            null,
            "own-process-hwnd-only",
            false,
            false,
            false,
            false,
            "not-run",
            error);
}
