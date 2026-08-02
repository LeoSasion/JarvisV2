using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jarvis.ControlCenter;

public sealed record DesktopRuntimeBootstrapReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string ResolutionSource,
    string? NodeExecutablePath,
    string? SidecarHostPath,
    string WorkspaceRoot,
    bool PackagedLayout,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class DesktopRuntimeBootstrap
{
    public const string PackagedNodeRelativePath =
        @"runtime\node\node.exe";
    public const string PackagedSidecarRelativePath =
        @"runtime\pi-agent\src\host.mjs";
    public const string PackagedContractRelativePath =
        @"runtime\pi-agent\config\pi-agent-desktop-host-contract.json";
    public const string PackagedReceiptRelativePath =
        "package-receipt.json";

    public static DesktopRuntimeBootstrapReceipt Resolve(
        string workspaceRoot,
        string? applicationBaseDirectory = null,
        string? pathEnvironment = null,
        string? configuredNodePath = null)
    {
        List<string> failures = [];
        string workspace;
        try
        {
            workspace = Path.GetFullPath(workspaceRoot);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException)
        {
            return Failed(workspaceRoot, "invalid-workspace", exception.Message);
        }
        if (!Directory.Exists(workspace))
        {
            return Failed(
                workspace,
                "missing-workspace",
                "The admitted workspace directory does not exist.");
        }

        string baseDirectory = Path.GetFullPath(
            applicationBaseDirectory ?? AppContext.BaseDirectory);
        string packagedNode = Path.Combine(
            baseDirectory,
            PackagedNodeRelativePath);
        string packagedSidecar = Path.Combine(
            baseDirectory,
            PackagedSidecarRelativePath);
        string packagedReceipt = Path.Combine(
            baseDirectory,
            PackagedReceiptRelativePath);
        bool packagedStatePresent =
            Directory.Exists(Path.Combine(baseDirectory, "runtime")) ||
            File.Exists(packagedNode) ||
            File.Exists(packagedSidecar) ||
            File.Exists(packagedReceipt);
        if (
            ValidateNode(packagedNode) &&
            ValidateSidecar(packagedSidecar) &&
            ValidatePackagedReceipt(baseDirectory, packagedReceipt))
        {
            return Passed(
                workspace,
                packagedNode,
                packagedSidecar,
                "packaged-layout",
                packaged: true);
        }
        if (packagedStatePresent)
        {
            return Failed(
                workspace,
                "packaged-layout",
                "The packaged runtime or its hash receipt failed admission.");
        }

        string? sidecar = FindDeveloperSidecar(workspace, baseDirectory);
        if (sidecar is null)
        {
            failures.Add(
                "No packaged or source-tree Pi sidecar with installed dependencies was found.");
        }
        string? node = ResolveNode(
            configuredNodePath,
            pathEnvironment ?? Environment.GetEnvironmentVariable("PATH"));
        if (node is null)
        {
            failures.Add(
                "No absolute node.exe was found in JARVIS2_NODE_PATH or PATH.");
        }
        if (node is null || sidecar is null)
        {
            return new DesktopRuntimeBootstrapReceipt(
                1,
                "jarvisv2-desktop-runtime-bootstrap",
                "failed",
                "developer-layout",
                node,
                sidecar,
                workspace,
                false,
                false,
                failures);
        }
        return Passed(
            workspace,
            node,
            sidecar,
            "developer-layout",
            packaged: false);
    }

    private static string? ResolveNode(
        string? configuredNodePath,
        string? pathEnvironment)
    {
        string? configured = string.IsNullOrWhiteSpace(configuredNodePath)
            ? Environment.GetEnvironmentVariable("JARVIS2_NODE_PATH")
            : configuredNodePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string candidate = Path.GetFullPath(configured);
            if (ValidateNode(candidate))
            {
                return candidate;
            }
        }
        if (string.IsNullOrWhiteSpace(pathEnvironment))
        {
            return null;
        }
        foreach (string rawEntry in pathEnvironment.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            string entry = rawEntry.Trim('"');
            if (entry.Length == 0 || !Path.IsPathFullyQualified(entry))
            {
                continue;
            }
            string candidate = Path.Combine(entry, "node.exe");
            if (ValidateNode(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static string? FindDeveloperSidecar(
        string workspaceRoot,
        string applicationBaseDirectory)
    {
        foreach (string root in EnumerateCandidateRoots(
            workspaceRoot,
            applicationBaseDirectory))
        {
            string candidate = Path.Combine(
                root,
                @"src\common\Jarvis.PiAgentHost\src\host.mjs");
            if (ValidateSidecar(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidateRoots(
        params string[] startingPaths)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        foreach (string startingPath in startingPaths)
        {
            DirectoryInfo? current = new(Path.GetFullPath(startingPath));
            while (current is not null)
            {
                if (visited.Add(current.FullName))
                {
                    yield return current.FullName;
                }
                current = current.Parent;
            }
        }
    }

    private static bool ValidateNode(string path) =>
        Path.IsPathFullyQualified(path) &&
        File.Exists(path) &&
        string.Equals(
            Path.GetFileName(path),
            "node.exe",
            StringComparison.OrdinalIgnoreCase);

    private static bool ValidateSidecar(string path)
    {
        if (
            !Path.IsPathFullyQualified(path) ||
            !File.Exists(path) ||
            !string.Equals(
                Path.GetFileName(path),
                "host.mjs",
                StringComparison.Ordinal))
        {
            return false;
        }
        DirectoryInfo? projectRoot = Directory.GetParent(path)?.Parent;
        bool contractAvailable = projectRoot is not null &&
            (File.Exists(Path.Combine(
                projectRoot.FullName,
                @"config\pi-agent-desktop-host-contract.json")) ||
            File.Exists(Path.GetFullPath(Path.Combine(
                projectRoot.FullName,
                @"..\..\..\config\pi-agent-desktop-host-contract.json"))));
        return
            projectRoot is not null &&
            contractAvailable &&
            File.Exists(Path.Combine(projectRoot.FullName, "package.json")) &&
            File.Exists(Path.Combine(
                projectRoot.FullName,
                @"node_modules\@earendil-works\pi-ai\package.json")) &&
            File.Exists(Path.Combine(
                projectRoot.FullName,
                @"node_modules\@earendil-works\pi-coding-agent\package.json"));
    }

    private static bool ValidatePackagedReceipt(
        string baseDirectory,
        string receiptPath)
    {
        try
        {
            if (!File.Exists(receiptPath) ||
                new FileInfo(receiptPath).Length is <= 0 or > 1_048_576 ||
                HasReparsePoint(baseDirectory) ||
                HasReparsePoint(receiptPath))
            {
                return false;
            }
            using FileStream receiptStream = File.OpenRead(receiptPath);
            using JsonDocument document = JsonDocument.Parse(receiptStream);
            JsonElement root = document.RootElement;
            if (
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("receiptType").GetString() !=
                    "jarvisv2-portable-control-center-package" ||
                root.GetProperty("result").GetString() != "passed" ||
                root.GetProperty("piSidecarNetworkAllowed").GetBoolean() ||
                root.GetProperty("piSidecarCredentialTransportAllowed")
                    .GetBoolean() ||
                root.GetProperty("activationPermitted").GetBoolean() ||
                root.GetProperty("systemMutationPerformed").GetBoolean())
            {
                return false;
            }

            Dictionary<string, string> hashes = new(
                StringComparer.Ordinal);
            foreach (JsonElement entry in root
                .GetProperty("criticalHashes")
                .EnumerateArray())
            {
                string? relativePath = entry.GetProperty("path").GetString();
                string? sha256 = entry.GetProperty("sha256").GetString();
                if (
                    string.IsNullOrWhiteSpace(relativePath) ||
                    !IsSha256(sha256) ||
                    !hashes.TryAdd(relativePath, sha256!))
                {
                    return false;
                }
            }
            foreach (string requiredPath in new[]
            {
                "jarvis-control-center.dll",
                "jarvis-pi-agent-desktop-bridge.dll",
                "runtime/node/node.exe",
                "runtime/pi-agent/src/host.mjs",
                "runtime/pi-agent/config/pi-agent-desktop-host-contract.json",
            })
            {
                if (!ValidateReceiptHash(baseDirectory, requiredPath, hashes))
                {
                    return false;
                }
            }
            foreach ((string relativePath, string expected) in hashes)
            {
                if (!ValidateFileHash(baseDirectory, relativePath, expected))
                {
                    return false;
                }
            }

            Dictionary<string, string> packageHashes = new(
                StringComparer.Ordinal);
            Dictionary<string, string> packageVersions = new(
                StringComparer.Ordinal);
            foreach (JsonElement entry in root
                .GetProperty("portableNodePackages")
                .EnumerateArray())
            {
                string? name = entry.GetProperty("name").GetString();
                string? version = entry.GetProperty("version").GetString();
                string? sha256 = entry
                    .GetProperty("packageJsonSha256")
                    .GetString();
                if (
                    !IsPackageName(name) ||
                    string.IsNullOrWhiteSpace(version) ||
                    !IsSha256(sha256) ||
                    !packageHashes.TryAdd(name!, sha256!) ||
                    !packageVersions.TryAdd(name!, version))
                {
                    return false;
                }
            }
            foreach ((string packageName, string hash) in packageHashes)
            {
                if (!ValidatePackageManifest(
                        baseDirectory,
                        packageName,
                        packageVersions[packageName],
                        hash))
                {
                    return false;
                }
            }
            foreach (string packageName in new[]
            {
                "@earendil-works/pi-ai",
                "@earendil-works/pi-coding-agent",
            })
            {
                if (
                    !packageHashes.ContainsKey(packageName) ||
                    !packageVersions.TryGetValue(
                        packageName,
                        out string? version) ||
                    version != "0.82.1")
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidOperationException or
                KeyNotFoundException or
                FormatException or
                ArgumentException or
                NotSupportedException)
        {
            return false;
        }
    }

    private static bool ValidateReceiptHash(
        string baseDirectory,
        string relativePath,
        IReadOnlyDictionary<string, string> hashes) =>
        hashes.TryGetValue(relativePath, out string? expected) &&
        ValidateFileHash(baseDirectory, relativePath, expected);

    private static bool ValidateFileHash(
        string baseDirectory,
        string relativePath,
        string expected)
    {
        string normalizedRelativePath = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(
            baseDirectory,
            normalizedRelativePath));
        string admittedPrefix = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(baseDirectory)) + Path.DirectorySeparatorChar;
        if (
            !fullPath.StartsWith(
                admittedPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) ||
            HasReparsePoint(fullPath))
        {
            return false;
        }
        using FileStream stream = File.OpenRead(fullPath);
        string actual = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static bool ValidatePackageManifest(
        string baseDirectory,
        string packageName,
        string expectedVersion,
        string expectedHash)
    {
        string relativePath = string.Concat(
            "runtime/pi-agent/node_modules/",
            packageName,
            "/package.json");
        if (!ValidateFileHash(baseDirectory, relativePath, expectedHash))
        {
            return false;
        }
        string fullPath = Path.GetFullPath(Path.Combine(
            baseDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        using FileStream stream = File.OpenRead(fullPath);
        using JsonDocument manifest = JsonDocument.Parse(stream);
        JsonElement root = manifest.RootElement;
        return root.GetProperty("name").GetString() == packageName &&
            root.GetProperty("version").GetString() == expectedVersion;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsPackageName(string? value) =>
        value is { Length: > 0 and <= 214 } &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        !value.StartsWith('/') &&
        !value.EndsWith('/') &&
        value.Count(character => character == '/') <= 1 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '@' or '/' or '.' or '_' or '-');

    private static bool HasReparsePoint(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (
                (File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static DesktopRuntimeBootstrapReceipt Passed(
        string workspace,
        string node,
        string sidecar,
        string source,
        bool packaged) =>
        new(
            1,
            "jarvisv2-desktop-runtime-bootstrap",
            "passed",
            source,
            Path.GetFullPath(node),
            Path.GetFullPath(sidecar),
            workspace,
            packaged,
            false,
            []);

    private static DesktopRuntimeBootstrapReceipt Failed(
        string workspace,
        string source,
        string failure) =>
        new(
            1,
            "jarvisv2-desktop-runtime-bootstrap",
            "failed",
            source,
            null,
            null,
            workspace,
            false,
            false,
            [failure]);
}
