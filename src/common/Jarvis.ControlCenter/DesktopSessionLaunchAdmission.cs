using System.IO;

namespace Jarvis.ControlCenter;

public sealed record DesktopWorkspaceAdmissionReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string? WorkspaceRoot,
    string? FailureCode,
    string? Failure,
    bool MutationPerformed);

public sealed record DesktopSessionLaunchAdmissionReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Provider,
    string? WorkspaceRoot,
    string? ResolutionSource,
    ConversationLaunchOptions? Options,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class DesktopSessionLaunchAdmission
{
    public static DesktopSessionLaunchAdmissionReceipt Admit(
        string workspaceInput,
        ConversationProviderKind provider,
        string? applicationBaseDirectory = null,
        string? pathEnvironment = null,
        string? configuredNodePath = null)
    {
        if (!Enum.IsDefined(provider))
        {
            return Failed(
                provider.ToString(),
                null,
                null,
                "The selected model provider is not admitted.");
        }

        DesktopWorkspaceAdmissionReceipt workspace =
            AdmitWorkspace(workspaceInput);
        if (workspace.Result != "passed" || workspace.WorkspaceRoot is null)
        {
            return Failed(
                ProviderName(provider),
                workspace.WorkspaceRoot,
                null,
                workspace.Failure ?? "The workspace failed admission.");
        }

        DesktopRuntimeBootstrapReceipt bootstrap;
        try
        {
            bootstrap = DesktopRuntimeBootstrap.Resolve(
                workspace.WorkspaceRoot,
                applicationBaseDirectory,
                pathEnvironment,
                configuredNodePath);
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                UnauthorizedAccessException or
                NotSupportedException)
        {
            return Failed(
                ProviderName(provider),
                workspace.WorkspaceRoot,
                null,
                $"Desktop runtime admission failed: {exception.Message}");
        }

        if (
            bootstrap.Result != "passed" ||
            bootstrap.NodeExecutablePath is null ||
            bootstrap.SidecarHostPath is null)
        {
            return Failed(
                ProviderName(provider),
                workspace.WorkspaceRoot,
                bootstrap.ResolutionSource,
                bootstrap.Failures.FirstOrDefault() ??
                    "No complete packaged or developer Pi runtime was admitted.");
        }

        ConversationLaunchOptions options = new(
            bootstrap.NodeExecutablePath,
            bootstrap.SidecarHostPath,
            workspace.WorkspaceRoot,
            provider);
        return new DesktopSessionLaunchAdmissionReceipt(
            1,
            "jarvisv2-desktop-session-launch-admission",
            "passed",
            ProviderName(provider),
            workspace.WorkspaceRoot,
            bootstrap.ResolutionSource,
            options,
            false,
            []);
    }

    public static DesktopWorkspaceAdmissionReceipt AdmitWorkspace(
        string workspaceInput)
    {
        if (
            string.IsNullOrWhiteSpace(workspaceInput) ||
            workspaceInput.Trim() != workspaceInput ||
            !Path.IsPathFullyQualified(workspaceInput) ||
            RejectsWindowsPathShape(workspaceInput))
        {
            return WorkspaceFailed(
                "invalid-workspace-root",
                "Choose a conventional absolute local workspace path.");
        }

        string workspaceRoot;
        try
        {
            workspaceRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspaceInput));
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException)
        {
            return WorkspaceFailed(
                "invalid-workspace-root",
                $"The workspace path is invalid: {exception.Message}");
        }

        string? volumeRoot = Path.GetPathRoot(workspaceRoot);
        if (
            string.IsNullOrWhiteSpace(volumeRoot) ||
            string.Equals(
                workspaceRoot,
                Path.TrimEndingDirectorySeparator(volumeRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceFailed(
                "protected-workspace-root",
                "A drive root cannot be admitted as a Pi workspace.");
        }
        if (IsProtectedWorkspace(workspaceRoot))
        {
            return WorkspaceFailed(
                "protected-workspace-root",
                "Windows, program, profile-root and application-data trees are not admitted workspaces.");
        }
        if (!Directory.Exists(workspaceRoot))
        {
            return WorkspaceFailed(
                "workspace-root-not-found",
                "Choose an existing workspace directory.");
        }
        try
        {
            EnsureNoReparsePoints(workspaceRoot);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailed(
                "workspace-root-alias-forbidden",
                exception.Message);
        }

        return new DesktopWorkspaceAdmissionReceipt(
            1,
            "jarvisv2-desktop-workspace-admission",
            "passed",
            workspaceRoot,
            null,
            null,
            false);
    }

    private static bool RejectsWindowsPathShape(string path)
    {
        if (
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            path.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            string fullPath = Path.GetFullPath(path);
            return fullPath.Length < 3 || fullPath[2..].Contains(':');
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    private static bool IsProtectedWorkspace(string workspaceRoot)
    {
        foreach (string protectedRoot in EnumerateProtectedRoots(workspaceRoot))
        {
            if (IsWithin(protectedRoot, workspaceRoot))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> EnumerateProtectedRoots(
        string workspaceRoot)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string variable in new[]
        {
            "SystemRoot",
            "WINDIR",
            "ProgramFiles",
            "ProgramFiles(x86)",
            "ProgramData",
            "USERPROFILE",
            "APPDATA",
            "LOCALAPPDATA",
        })
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                TryAddProtectedRoot(roots, value);
            }
        }
        string? volumeRoot = Path.GetPathRoot(workspaceRoot);
        if (!string.IsNullOrWhiteSpace(volumeRoot))
        {
            foreach (string child in new[]
            {
                "Program Files",
                "Program Files (x86)",
                "ProgramData",
                "Users",
            })
            {
                TryAddProtectedRoot(roots, Path.Combine(volumeRoot, child));
            }
        }
        return roots;
    }

    private static void TryAddProtectedRoot(
        ISet<string> roots,
        string candidate)
    {
        try
        {
            roots.Add(Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(candidate)));
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException)
        {
            // Candidate-volume defaults still protect the standard system trees.
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        string admittedRoot = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(
                admittedRoot,
                candidate,
                StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(
                admittedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("The workspace path has no filesystem root.");
        }
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Workspace paths may not traverse a symbolic link or junction.");
            }
        }
    }

    private static string ProviderName(ConversationProviderKind provider) =>
        provider switch
        {
            ConversationProviderKind.OpenAiResponses => "openai-responses",
            _ => "local-diagnostic",
        };

    private static DesktopWorkspaceAdmissionReceipt WorkspaceFailed(
        string code,
        string failure) =>
        new(
            1,
            "jarvisv2-desktop-workspace-admission",
            "failed",
            null,
            code,
            failure,
            false);

    private static DesktopSessionLaunchAdmissionReceipt Failed(
        string provider,
        string? workspaceRoot,
        string? source,
        string failure) =>
        new(
            1,
            "jarvisv2-desktop-session-launch-admission",
            "failed",
            provider,
            workspaceRoot,
            source,
            null,
            false,
            [failure]);
}
