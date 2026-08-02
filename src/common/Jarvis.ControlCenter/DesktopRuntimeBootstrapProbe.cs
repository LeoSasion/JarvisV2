using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jarvis.ControlCenter;

public sealed record DesktopRuntimeBootstrapProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool PackagedLayoutPassed,
    bool DeveloperLayoutPassed,
    bool PackagedLayoutPrecedencePassed,
    bool TamperedGitRuntimeRejected,
    bool ExtraGitRuntimeFileRejected,
    bool TamperedPackageRejected,
    bool MissingRuntimeRejected,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class DesktopRuntimeBootstrapProbe
{
    public static DesktopRuntimeBootstrapProbeReceipt Run()
    {
        List<string> failures = [];
        string root = Path.Combine(
            Path.GetTempPath(),
            $"jarvisv2-runtime-bootstrap-{Guid.NewGuid():N}");
        try
        {
            string workspace = Path.Combine(root, "workspace");
            string application = Path.Combine(root, "application");
            Directory.CreateDirectory(workspace);
            CreatePackagedRuntime(application);
            DesktopRuntimeBootstrapReceipt packaged =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    application,
                    pathEnvironment: string.Empty,
                    configuredNodePath: null);
            bool packagedLayoutPassed =
                packaged.Result == "passed" &&
                packaged.PackagedLayout &&
                packaged.ResolutionSource == "packaged-layout" &&
                packaged.NodeExecutablePath == Path.GetFullPath(Path.Combine(
                    application,
                    DesktopRuntimeBootstrap.PackagedNodeRelativePath)) &&
                packaged.SidecarHostPath == Path.GetFullPath(Path.Combine(
                    application,
                    DesktopRuntimeBootstrap.PackagedSidecarRelativePath));
            AddFailure(
                failures,
                packagedLayoutPassed,
                "The packaged runtime layout was not admitted.");

            string developerRoot = Path.Combine(root, "developer");
            string developerWorkspace = Path.Combine(
                developerRoot,
                "workspace");
            string developerApplication = Path.Combine(
                developerRoot,
                "bin",
                "Release");
            string nodeDirectory = Path.Combine(developerRoot, "node");
            Directory.CreateDirectory(developerWorkspace);
            Directory.CreateDirectory(developerApplication);
            Directory.CreateDirectory(nodeDirectory);
            string developerNode = Path.Combine(nodeDirectory, "node.exe");
            File.WriteAllText(developerNode, "synthetic-node");
            CreateDeveloperSidecar(developerRoot);
            DesktopRuntimeBootstrapReceipt developer =
                DesktopRuntimeBootstrap.Resolve(
                    developerWorkspace,
                    developerApplication,
                    nodeDirectory,
                    configuredNodePath: null);
            bool developerLayoutPassed =
                developer.Result == "passed" &&
                !developer.PackagedLayout &&
                developer.ResolutionSource == "developer-layout" &&
                developer.NodeExecutablePath == Path.GetFullPath(developerNode);
            AddFailure(
                failures,
                developerLayoutPassed,
                "The developer runtime layout was not admitted.");

            DesktopRuntimeBootstrapReceipt precedence =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    application,
                    nodeDirectory,
                    developerNode);
            bool precedencePassed =
                precedence.Result == "passed" &&
                precedence.PackagedLayout &&
                precedence.ResolutionSource == "packaged-layout";
            AddFailure(
                failures,
                precedencePassed,
                "The packaged runtime did not take precedence over ambient paths.");

            string packagedGit = Path.Combine(
                application,
                DesktopRuntimeBootstrap.PackagedGitRelativePath);
            File.AppendAllText(packagedGit, "-tampered");
            DesktopRuntimeBootstrapReceipt tamperedGit =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    application,
                    nodeDirectory,
                    developerNode);
            bool tamperedGitRejected =
                tamperedGit.Result == "failed" &&
                tamperedGit.ResolutionSource == "packaged-layout";
            AddFailure(
                failures,
                tamperedGitRejected,
                "A hash-drifted bundled Git runtime was not rejected.");
            File.WriteAllText(packagedGit, "synthetic-git");

            string extraGit = Path.Combine(
                application,
                @"runtime\git\extra.dll");
            File.WriteAllText(extraGit, "unexpected-git-runtime-file");
            DesktopRuntimeBootstrapReceipt extraGitRuntime =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    application,
                    nodeDirectory,
                    developerNode);
            bool extraGitRejected =
                extraGitRuntime.Result == "failed" &&
                extraGitRuntime.ResolutionSource == "packaged-layout";
            AddFailure(
                failures,
                extraGitRejected,
                "An unreceipted bundled Git runtime file was not rejected.");
            File.Delete(extraGit);

            File.AppendAllText(
                Path.Combine(
                    application,
                    DesktopRuntimeBootstrap.PackagedSidecarRelativePath),
                "// tampered");
            DesktopRuntimeBootstrapReceipt tampered =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    application,
                    nodeDirectory,
                    developerNode);
            bool tamperedRejected =
                tampered.Result == "failed" &&
                !tampered.PackagedLayout &&
                tampered.ResolutionSource == "packaged-layout" &&
                tampered.NodeExecutablePath is null &&
                tampered.SidecarHostPath is null &&
                tampered.Failures.Count == 1;
            AddFailure(
                failures,
                tamperedRejected,
                "A hash-drifted package was not rejected before developer fallback.");

            string missingApplication = Path.Combine(root, "missing");
            Directory.CreateDirectory(missingApplication);
            DesktopRuntimeBootstrapReceipt missing =
                DesktopRuntimeBootstrap.Resolve(
                    workspace,
                    missingApplication,
                    pathEnvironment: string.Empty,
                    configuredNodePath: null);
            bool missingRejected =
                missing.Result == "failed" &&
                missing.NodeExecutablePath is null &&
                missing.SidecarHostPath is null &&
                missing.Failures.Count == 2;
            AddFailure(
                failures,
                missingRejected,
                "An incomplete desktop runtime was not rejected.");

            bool passed = failures.Count == 0;
            return new DesktopRuntimeBootstrapProbeReceipt(
                1,
                "jarvisv2-desktop-runtime-bootstrap-probe",
                passed ? "passed" : "failed",
                packagedLayoutPassed,
                developerLayoutPassed,
                precedencePassed,
                tamperedGitRejected,
                extraGitRejected,
                tamperedRejected,
                missingRejected,
                false,
                failures);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                string temporaryRoot = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.GetTempPath()));
                string fullRoot = Path.GetFullPath(root);
                if (
                    !fullRoot.StartsWith(
                        temporaryRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(fullRoot).StartsWith(
                        "jarvisv2-runtime-bootstrap-",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The bootstrap probe cleanup path failed admission.");
                }
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private static void CreatePackagedRuntime(string application)
    {
        string node = Path.Combine(
            application,
            DesktopRuntimeBootstrap.PackagedNodeRelativePath);
        string sidecar = Path.Combine(
            application,
            DesktopRuntimeBootstrap.PackagedSidecarRelativePath);
        string git = Path.Combine(
            application,
            DesktopRuntimeBootstrap.PackagedGitRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(node)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        Directory.CreateDirectory(Path.GetDirectoryName(git)!);
        File.WriteAllText(
            Path.Combine(application, "jarvis-control-center.dll"),
            "synthetic-control-center");
        File.WriteAllText(
            Path.Combine(application, "jarvis-pi-agent-desktop-bridge.dll"),
            "synthetic-pi-bridge");
        File.WriteAllText(node, "synthetic-node");
        File.WriteAllText(git, "synthetic-git");
        File.WriteAllText(sidecar, "// synthetic-sidecar");
        string projectRoot = Directory.GetParent(sidecar)!.Parent!.FullName;
        CreatePiPackageFiles(projectRoot);
        CreatePackageReceipt(application, projectRoot);
    }

    private static void CreateDeveloperSidecar(string developerRoot)
    {
        string projectRoot = Path.Combine(
            developerRoot,
            @"src\common\Jarvis.PiAgentHost");
        Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
        File.WriteAllText(
            Path.Combine(projectRoot, @"src\host.mjs"),
            "// synthetic-sidecar");
        CreatePiPackageFiles(projectRoot);
    }

    private static void CreatePiPackageFiles(string projectRoot)
    {
        File.WriteAllText(
            Path.Combine(projectRoot, "package.json"),
            "{}");
        Directory.CreateDirectory(Path.Combine(projectRoot, "config"));
        File.WriteAllText(
            Path.Combine(
                projectRoot,
                @"config\pi-agent-desktop-host-contract.json"),
            "{}");
        foreach (string package in new[]
        {
            "pi-ai",
            "pi-coding-agent",
        })
        {
            string packageRoot = Path.Combine(
                projectRoot,
                "node_modules",
                "@earendil-works",
                package);
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(
                Path.Combine(packageRoot, "package.json"),
                JsonSerializer.Serialize(new
                {
                    name = $"@earendil-works/{package}",
                    version = "0.82.1",
                }));
        }
    }

    private static void CreatePackageReceipt(
        string application,
        string projectRoot)
    {
        string[] criticalPaths =
        [
            "jarvis-control-center.dll",
            "jarvis-pi-agent-desktop-bridge.dll",
            "runtime/node/node.exe",
            "runtime/git/cmd/git.exe",
            "runtime/pi-agent/src/host.mjs",
            "runtime/pi-agent/config/pi-agent-desktop-host-contract.json",
        ];
        object[] criticalHashes = criticalPaths
            .Select(path => new
            {
                path,
                sha256 = ComputeSha256(Path.Combine(
                    application,
                    path.Replace('/', Path.DirectorySeparatorChar))),
            })
            .Cast<object>()
            .ToArray();
        object[] packages = new[]
        {
            "@earendil-works/pi-ai",
            "@earendil-works/pi-coding-agent",
        }
            .Select(name => new
            {
                name,
                version = "0.82.1",
                packageJsonSha256 = ComputeSha256(Path.Combine(
                    projectRoot,
                    "node_modules",
                    name.Replace('/', Path.DirectorySeparatorChar),
                    "package.json")),
            })
            .Cast<object>()
            .ToArray();
        string receipt = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            receiptType = "jarvisv2-portable-control-center-package",
            result = "passed",
            runtimeLayout =
                "self-contained-wpf-plus-bundled-node-pi-sidecar-and-fixed-git",
            reviewedIterationGitRuntime =
                "bundled-runtime-git-cmd-direct-no-shell",
            reviewedIterationTrustedValidation =
                "owner-approved-pinned-head-node-test-direct-no-shell-pre-post-gate",
            ownerTrustedValidationApprovalRequired = true,
            piTrustedValidationProcessAvailable = false,
            gitRuntimeFileCount = 1,
            gitRuntimeBytes = new FileInfo(Path.Combine(
                application,
                DesktopRuntimeBootstrap.PackagedGitRelativePath)).Length,
            piSidecarNetworkAllowed = false,
            piSidecarCredentialTransportAllowed = false,
            activationPermitted = false,
            systemMutationPerformed = false,
            criticalHashes,
            portableNodePackages = packages,
        });
        File.WriteAllText(
            Path.Combine(
                application,
                DesktopRuntimeBootstrap.PackagedReceiptRelativePath),
            receipt);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void AddFailure(
        ICollection<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }
}
