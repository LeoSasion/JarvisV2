using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Jarvis.Supervisor;

internal static class CompatibilityInspector
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string TaskbarViewRelativePath =
        @"SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\Taskbar.View.dll";
    private const string SystemTrayRelativePath =
        @"SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\SystemTray.dll";
    private const string SearchUxRelativePath =
        @"SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchUx.UI.dll";

    public static CompatibilityReport Inspect()
    {
        string? windowsDirectory = GetWindowsDirectory();
        string? expectedExplorerPath =
            windowsDirectory is null ? null : Path.Combine(windowsDirectory, "explorer.exe");
        string? expectedTaskbarViewPath =
            windowsDirectory is null ? null : Path.Combine(windowsDirectory, TaskbarViewRelativePath);
        string? expectedSystemTrayPath =
            windowsDirectory is null ? null : Path.Combine(windowsDirectory, SystemTrayRelativePath);
        string? expectedSearchUxPath =
            windowsDirectory is null ? null : Path.Combine(windowsDirectory, SearchUxRelativePath);

        RegistrySnapshot registry = ReadRegistrySnapshot();
        FileFingerprint explorer = ProbeFile(expectedExplorerPath);
        FileFingerprint taskbarView = ProbeFile(expectedTaskbarViewPath);
        FileFingerprint systemTray = ProbeFile(expectedSystemTrayPath);
        FileFingerprint searchUx = ProbeFile(expectedSearchUxPath);

        int currentSessionId = TryGetCurrentSessionId();
        ExplorerSnapshot explorerRuntime = InspectExplorerRuntime(
            currentSessionId,
            expectedExplorerPath);

        List<CompatibilityCheck> checks =
        [
            Check(
                "platform",
                "Windows",
                OperatingSystem.IsWindows() ? "Windows" : RuntimeInformation.OSDescription,
                OperatingSystem.IsWindows()),
            Check(
                "architecture",
                "X64",
                RuntimeInformation.OSArchitecture.ToString(),
                Environment.Is64BitOperatingSystem &&
                RuntimeInformation.OSArchitecture == Architecture.X64),
            Check(
                "installationType",
                CompatibilityBaseline.InstallationType,
                registry.InstallationType,
                Equal(registry.InstallationType, CompatibilityBaseline.InstallationType)),
            Check(
                "currentBuild",
                CompatibilityBaseline.CurrentBuild,
                registry.CurrentBuild,
                Equal(registry.CurrentBuild, CompatibilityBaseline.CurrentBuild)),
            Check(
                "ubr",
                CompatibilityBaseline.Ubr.ToString(),
                registry.Ubr?.ToString(),
                registry.Ubr == CompatibilityBaseline.Ubr),
            FileCheck(
                "explorer.productVersion",
                CompatibilityBaseline.ExplorerProductVersion,
                explorer.ProductVersion,
                explorer),
            FileCheck(
                "explorer.size",
                CompatibilityBaseline.ExplorerSize.ToString(),
                explorer.Size?.ToString(),
                explorer),
            FileCheck(
                "explorer.sha256",
                CompatibilityBaseline.ExplorerSha256,
                explorer.Sha256,
                explorer),
            FileCheck(
                "taskbarView.productVersion",
                CompatibilityBaseline.TaskbarViewProductVersion,
                taskbarView.ProductVersion,
                taskbarView),
            FileCheck(
                "taskbarView.size",
                CompatibilityBaseline.TaskbarViewSize.ToString(),
                taskbarView.Size?.ToString(),
                taskbarView),
            FileCheck(
                "taskbarView.sha256",
                CompatibilityBaseline.TaskbarViewSha256,
                taskbarView.Sha256,
                taskbarView),
            FileCheck(
                "systemTray.productVersion",
                CompatibilityBaseline.SystemTrayProductVersion,
                systemTray.ProductVersion,
                systemTray),
            FileCheck(
                "systemTray.size",
                CompatibilityBaseline.SystemTraySize.ToString(),
                systemTray.Size?.ToString(),
                systemTray),
            FileCheck(
                "systemTray.sha256",
                CompatibilityBaseline.SystemTraySha256,
                systemTray.Sha256,
                systemTray),
            FileCheck(
                "searchUx.productVersion",
                CompatibilityBaseline.SearchUxProductVersion,
                searchUx.ProductVersion,
                searchUx),
            FileCheck(
                "searchUx.size",
                CompatibilityBaseline.SearchUxSize.ToString(),
                searchUx.Size?.ToString(),
                searchUx),
            FileCheck(
                "searchUx.sha256",
                CompatibilityBaseline.SearchUxSha256,
                searchUx.Sha256,
                searchUx),
            Check(
                "explorer.runtimeInspection",
                "successful",
                explorerRuntime.InspectionSucceeded ? "successful" : "failed",
                explorerRuntime.InspectionSucceeded),
            Check(
                "explorer.imagePath",
                expectedExplorerPath ?? "unavailable",
                explorerRuntime.ImagePath ?? "absent",
                explorerRuntime.ProcessId is not null &&
                EqualPath(explorerRuntime.ImagePath, expectedExplorerPath)),
            Check(
                "taskbarView.loadedPath",
                expectedTaskbarViewPath ?? "unavailable",
                JoinPaths(explorerRuntime.TaskbarViewPaths),
                ExactlyOnePathMatches(
                    explorerRuntime.TaskbarViewPaths,
                    expectedTaskbarViewPath)),
            Check(
                "systemTray.loadedPath",
                expectedSystemTrayPath ?? "unavailable",
                JoinPaths(explorerRuntime.SystemTrayPaths),
                ExactlyOnePathMatches(
                    explorerRuntime.SystemTrayPaths,
                    expectedSystemTrayPath)),
            Check(
                "searchUx.loadedPath",
                expectedSearchUxPath ?? "unavailable",
                JoinPaths(explorerRuntime.SearchUxPaths),
                ExactlyOnePathMatches(
                    explorerRuntime.SearchUxPaths,
                    expectedSearchUxPath)),
            Check(
                "legacyExplorerExtensions.loaded",
                "absent",
                JoinPaths(explorerRuntime.LegacyExplorerExtensionsPaths),
                explorerRuntime.LegacyExplorerExtensionsPaths.Count == 0),
        ];

        KillSwitchProbe killSwitch = KillSwitch.Probe();
        ActiveModuleProbe activeModule = KillSwitch.ProbeActiveModule();

        return new CompatibilityReport(
            CompatibilityBaseline.ProfileId,
            checks.All(check => check.Passed),
            DateTimeOffset.UtcNow,
            new HostSnapshot(
                RuntimeInformation.OSDescription,
                Environment.OSVersion.Version.ToString(),
                RuntimeInformation.OSArchitecture.ToString(),
                registry.CurrentBuild,
                registry.Ubr,
                registry.DisplayVersion,
                registry.BuildLabEx,
                registry.InstallationType,
                currentSessionId,
                explorerRuntime.ProcessId is int shellProcessId
                    ? [shellProcessId]
                    : [],
                KillSwitch.FlagPath,
                killSwitch.State,
                killSwitch.BlocksInitialization,
                killSwitch.Error,
                KillSwitch.ActiveModulePath,
                KillSwitch.StateGateName,
                activeModule.State,
                activeModule.ModuleId,
                activeModule.LastWriteTimeUtc,
                activeModule.ExpiresAtUtc,
                activeModule.Error),
            explorerRuntime,
            explorer,
            taskbarView,
            systemTray,
            searchUx,
            checks);
    }

    private static CompatibilityCheck FileCheck(
        string name,
        string expected,
        string? actual,
        FileFingerprint file) =>
        Check(name, expected, actual, file.Exists && Equal(actual, expected));

    private static RegistrySnapshot ReadRegistrySnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new RegistrySnapshot(null, null, null, null, null);
        }

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey, writable: false);
        return new RegistrySnapshot(
            key?.GetValue("CurrentBuild")?.ToString(),
            ToNullableInt(key?.GetValue("UBR")),
            key?.GetValue("DisplayVersion")?.ToString(),
            key?.GetValue("BuildLabEx")?.ToString(),
            key?.GetValue("InstallationType")?.ToString());
    }

    private static FileFingerprint ProbeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new FileFingerprint(path, false, null, null, null);
        }

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.SequentialScan);
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        long size = stream.Length;
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));

        return new FileFingerprint(
            Path.GetFullPath(path),
            true,
            version.ProductVersion,
            sha256,
            size);
    }

    private static ExplorerSnapshot InspectExplorerRuntime(
        int currentSessionId,
        string? expectedExplorerPath)
    {
        if (!OperatingSystem.IsWindows() ||
            currentSessionId < 0 ||
            string.IsNullOrWhiteSpace(expectedExplorerPath))
        {
            return ExplorerSnapshot.Failed(
                "Windows, a valid session, and the expected Explorer path are required.");
        }

        ShellIdentity shell = WindowsShell.Probe(
            currentSessionId,
            expectedExplorerPath);
        if (!shell.IsVerified || shell.ProcessId is not int shellProcessId)
        {
            return ExplorerSnapshot.Failed(shell.Error ?? "The desktop shell wasn't verified.");
        }

        HashSet<string> taskbarViewPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> systemTrayPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> searchUxPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> legacyExplorerExtensionsPaths = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using Process process = Process.GetProcessById(shellProcessId);
            if (process.HasExited || process.SessionId != currentSessionId)
            {
                return ExplorerSnapshot.Failed(
                    $"Verified shell process {shellProcessId} exited during module inspection.");
            }

            string? imagePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !WindowsShell.PathsEqual(imagePath, expectedExplorerPath))
            {
                return ExplorerSnapshot.Failed(
                    $"Shell process {shellProcessId} no longer had the expected image path.");
            }

            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(
                    module.ModuleName,
                    "Taskbar.View.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    taskbarViewPaths.Add(Path.GetFullPath(module.FileName));
                }
                else if (string.Equals(
                    module.ModuleName,
                    "SystemTray.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    systemTrayPaths.Add(Path.GetFullPath(module.FileName));
                }
                else if (string.Equals(
                    module.ModuleName,
                    "SearchUx.UI.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    searchUxPaths.Add(Path.GetFullPath(module.FileName));
                }
                else if (string.Equals(
                    module.ModuleName,
                    "ExplorerExtensions.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    legacyExplorerExtensionsPaths.Add(
                        Path.GetFullPath(module.FileName));
                }
            }

            ShellIdentity finalShell = WindowsShell.Probe(
                currentSessionId,
                expectedExplorerPath);
            if (!finalShell.IsVerified || finalShell.ProcessId != shellProcessId)
            {
                return ExplorerSnapshot.Failed(
                    "The desktop shell changed during module inspection.");
            }

            return new ExplorerSnapshot(
                true,
                shellProcessId,
                Path.GetFullPath(imagePath),
                taskbarViewPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                systemTrayPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                searchUxPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                legacyExplorerExtensionsPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                null);
        }
        catch (Exception exception)
        {
            return ExplorerSnapshot.Failed(
                $"Shell module inspection failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? GetWindowsDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    private static int TryGetCurrentSessionId()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            return current.SessionId;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static int? ToNullableInt(object? value)
    {
        if (value is int intValue)
        {
            return intValue;
        }

        return int.TryParse(value?.ToString(), out int parsed) ? parsed : null;
    }

    private static string JoinPaths(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? "absent" : string.Join("; ", paths);

    private static bool ExactlyOnePathMatches(
        IReadOnlyList<string> paths,
        string? expectedPath) =>
        paths.Count == 1 && EqualPath(paths[0], expectedPath);

    private static bool Equal(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool EqualPath(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static CompatibilityCheck Check(
        string name,
        string expected,
        string? actual,
        bool passed) =>
        new(name, expected, actual, passed);
}

internal static class CompatibilityBaseline
{
    public const string ProfileId = "win11-25h2-26200.8875-x64";
    public const string InstallationType = "Client";
    public const string CurrentBuild = "26200";
    public const int Ubr = 8875;

    public const string ExplorerProductVersion = "10.0.26100.8875";
    public const long ExplorerSize = 3385624;
    public const string ExplorerSha256 =
        "80B21E6F70524EFD84037A4EDA479DDC4BC55C0D6C1A33439B85A554E740F30C";

    public const string TaskbarViewProductVersion = "2605.22000.400.0";
    public const long TaskbarViewSize = 10020864;
    public const string TaskbarViewSha256 =
        "00D1BD68240ED0CDB19A98E551BC5BFBA383843CC2564FF40523CB2DCFCD09F5";

    public const string SystemTrayProductVersion = "2605.22002.100.0";
    public const long SystemTraySize = 2047488;
    public const string SystemTraySha256 =
        "C911987BF024BC162AF1ABBCEA79287C57302419156741596FF9EEB23E23F3E1";

    public const string SearchUxProductVersion = "2605.27010.300.0";
    public const long SearchUxSize = 12218880;
    public const string SearchUxSha256 =
        "A86F048BE25AFA0A18435266D774131F797EF11C8C0BE8959ECE3A8E547E4943";
}

internal sealed record CompatibilityReport(
    string ProfileId,
    bool Compatible,
    DateTimeOffset InspectedAtUtc,
    HostSnapshot Host,
    ExplorerSnapshot ExplorerRuntime,
    FileFingerprint Explorer,
    FileFingerprint TaskbarView,
    FileFingerprint SystemTray,
    FileFingerprint SearchUx,
    IReadOnlyList<CompatibilityCheck> Checks);

internal sealed record HostSnapshot(
    string OsDescription,
    string OsVersion,
    string OsArchitecture,
    string? CurrentBuild,
    int? Ubr,
    string? DisplayVersion,
    string? BuildLabEx,
    string? InstallationType,
    int SessionId,
    IReadOnlyList<int> ExplorerProcessIds,
    string KillSwitchPath,
    KillSwitchState KillSwitchState,
    bool KillSwitchBlocksInitialization,
    string? KillSwitchError,
    string ActiveModulePath,
    string StateGateName,
    ActiveModuleState ActiveModuleState,
    string? ActiveModuleId,
    DateTimeOffset? ActiveModuleLastWriteTimeUtc,
    DateTimeOffset? ActiveModuleExpiresAtUtc,
    string? ActiveModuleError);

internal sealed record ExplorerSnapshot(
    bool InspectionSucceeded,
    int? ProcessId,
    string? ImagePath,
    IReadOnlyList<string> TaskbarViewPaths,
    IReadOnlyList<string> SystemTrayPaths,
    IReadOnlyList<string> SearchUxPaths,
    IReadOnlyList<string> LegacyExplorerExtensionsPaths,
    string? Error)
{
    public static ExplorerSnapshot Failed(string error) =>
        new(false, null, null, [], [], [], [], error);
}

internal sealed record FileFingerprint(
    string? Path,
    bool Exists,
    string? ProductVersion,
    string? Sha256,
    long? Size);

internal sealed record CompatibilityCheck(
    string Name,
    string Expected,
    string? Actual,
    bool Passed);

internal sealed record RegistrySnapshot(
    string? CurrentBuild,
    int? Ubr,
    string? DisplayVersion,
    string? BuildLabEx,
    string? InstallationType);
