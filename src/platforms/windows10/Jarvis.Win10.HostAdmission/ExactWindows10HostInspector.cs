using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Jarvis.Win10.HostAdmission;

public static class ExactWindows10HostInspector
{
    private const string CurrentVersionKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static Windows10HostAdmissionReceipt Inspect()
    {
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return Blocked(
                    observedAtUtc,
                    "incompatible-host",
                    "Exact Win10 host admission only runs on Windows.");
            }

            WindowsHostIdentity host = ReadHostIdentity();
            Windows10HostProfileCatalog catalog = HostProfileCatalog.Load();
            Windows10HostProfile? profile =
                catalog.Profiles.SingleOrDefault(candidate =>
                    Matches(candidate, host));

            return new Windows10HostAdmissionReceipt(
                1,
                "jarvisv2-win10-exact-host-admission",
                profile is null
                    ? "blocked-no-exact-profile"
                    : "passed-exact-windows10-host",
                observedAtUtc,
                host,
                profile,
                false,
                "not-run",
                false,
                profile is null
                    ? [
                        "No exact Windows 10 build, UBR, architecture and " +
                        "Explorer identity profile matched.",
                    ]
                    : []);
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

    private static Windows10HostAdmissionReceipt Blocked(
        DateTimeOffset observedAtUtc,
        string result,
        string failure) =>
        new(
            1,
            "jarvisv2-win10-exact-host-admission",
            result,
            observedAtUtc,
            null,
            null,
            false,
            "not-run",
            false,
            [failure]);
}
