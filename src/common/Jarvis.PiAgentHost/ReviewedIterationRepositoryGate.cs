using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentRepositoryBaselineReceipt(
    int SchemaVersion,
    string ReceiptType,
    string RepositoryRoot,
    string Head,
    string RepositoryDigest,
    string ValidationProfile,
    DateTimeOffset ValidatedAtUtc);

public sealed record PiAgentRepositoryValidationReceipt(
    int SchemaVersion,
    string ReceiptType,
    bool Passed,
    string Result,
    string Head,
    IReadOnlyList<string> ChangedPaths,
    string? RepositoryDigest,
    IReadOnlyList<string> Checks,
    string? ErrorCode,
    DateTimeOffset ValidatedAtUtc);

public sealed class PiAgentReviewedIterationRepositoryGate
{
    public const string ProcessModel =
        "desktop-owned-fixed-git-read-and-diffcheck-no-shell";
    public const string StructuredParseModel =
        "non-executing-json-xml-xaml-parse";
    public const int ProcessTimeoutMilliseconds = 10_000;
    public const int MaximumProcessOutputCharacters = 1_048_576;

    private readonly string gitExecutable;

    public PiAgentReviewedIterationRepositoryGate()
        : this(ResolveDefaultGitExecutable())
    {
    }

    public PiAgentReviewedIterationRepositoryGate(
        string gitExecutable)
    {
        if (
            string.IsNullOrWhiteSpace(gitExecutable) ||
            (!Path.IsPathFullyQualified(gitExecutable) &&
                !string.Equals(
                    gitExecutable,
                    "git.exe",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The repository gate accepts only git.exe or an absolute executable path.",
                nameof(gitExecutable));
        }
        this.gitExecutable = gitExecutable;
    }

    private static string ResolveDefaultGitExecutable()
    {
        string bundled = Path.Combine(
            AppContext.BaseDirectory,
            "runtime",
            "git",
            "cmd",
            "git.exe");
        List<string> candidates = [bundled];
        string? programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(
                programFiles,
                "Git",
                "cmd",
                "git.exe"));
        }
        string? programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(
                programFilesX86,
                "Git",
                "cmd",
                "git.exe"));
        }
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return "git.exe";
    }

    public async Task<PiAgentRepositoryBaselineReceipt>
        CaptureCleanBaselineAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
    {
        string root = AdmitWorkspaceRoot(workspaceRoot);
        string repositoryRoot = NormalizeGitPath(
            (await RunGitAsync(
                root,
                ["rev-parse", "--show-toplevel"],
                cancellationToken)).StandardOutput.Trim());
        if (!string.Equals(
                repositoryRoot,
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Reviewed iteration requires the admitted workspace to be the Git repository root.");
        }
        string head = NormalizeHead(
            (await RunGitAsync(
                root,
                ["rev-parse", "--verify", "HEAD"],
                cancellationToken)).StandardOutput.Trim());
        GitResult status = await RunGitAsync(
            root,
            [
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
                "--ignore-submodules=none",
            ],
            cancellationToken);
        if (status.StandardOutput.Length != 0)
        {
            throw new InvalidOperationException(
                "Reviewed iteration requires a clean Git worktree before the owner policy can be armed.");
        }
        await RequireDiffCheckAsync(root, cancellationToken);
        string digest = HashText($"{head}\0clean\0");
        return new PiAgentRepositoryBaselineReceipt(
            1,
            "jarvisv2-pi-agent-repository-baseline",
            root,
            head,
            digest,
            PiAgentReviewedIterationAdmission.ValidationProfile,
            DateTimeOffset.UtcNow);
    }

    public async Task<PiAgentRepositoryValidationReceipt> ValidateAsync(
        string workspaceRoot,
        string expectedHead,
        IReadOnlyDictionary<string, string> expectedFiles,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset validatedAtUtc = DateTimeOffset.UtcNow;
        List<string> checks = [];
        try
        {
            string root = AdmitWorkspaceRoot(workspaceRoot);
            string repositoryRoot = NormalizeGitPath(
                (await RunGitAsync(
                    root,
                    ["rev-parse", "--show-toplevel"],
                    cancellationToken)).StandardOutput.Trim());
            if (!string.Equals(
                    repositoryRoot,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    expectedHead,
                    [],
                    checks,
                    "repository-root-changed",
                    validatedAtUtc);
            }
            checks.Add("repository-root-exact");

            string currentHead = NormalizeHead(
                (await RunGitAsync(
                    root,
                    ["rev-parse", "--verify", "HEAD"],
                    cancellationToken)).StandardOutput.Trim());
            if (!string.Equals(
                    currentHead,
                    expectedHead,
                    StringComparison.Ordinal))
            {
                return Failed(
                    currentHead,
                    [],
                    checks,
                    "repository-head-drifted",
                    validatedAtUtc);
            }
            checks.Add("repository-head-stable");

            GitResult status = await RunGitAsync(
                root,
                [
                    "status",
                    "--porcelain=v1",
                    "-z",
                    "--untracked-files=all",
                    "--ignore-submodules=none",
                ],
                cancellationToken);
            IReadOnlyList<string> changedPaths = ParseExactModifiedPaths(
                status.StandardOutput);
            string[] expectedPaths = expectedFiles.Keys
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!changedPaths.SequenceEqual(expectedPaths))
            {
                return Failed(
                    currentHead,
                    changedPaths,
                    checks,
                    "repository-pathset-drifted",
                    validatedAtUtc);
            }
            checks.Add("exact-modified-pathset");

            await RequireDiffCheckAsync(root, cancellationToken);
            checks.Add("git-diff-check");

            StringBuilder digestMaterial = new();
            digestMaterial.Append(currentHead).Append('\0');
            foreach (string relativePath in expectedPaths)
            {
                string fullPath = AdmitChangedFile(root, relativePath);
                string actualHash;
                await using (FileStream stream = new(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    actualHash = Convert.ToHexString(
                            await SHA256.HashDataAsync(
                                stream,
                                cancellationToken))
                        .ToLowerInvariant();
                }
                if (
                    !expectedFiles.TryGetValue(
                        relativePath,
                        out string? expectedHash) ||
                    !string.Equals(
                        actualHash,
                        expectedHash,
                        StringComparison.Ordinal))
                {
                    return Failed(
                        currentHead,
                        changedPaths,
                        checks,
                        "repository-file-hash-drifted",
                        validatedAtUtc);
                }
                ValidateStructuredText(fullPath);
                digestMaterial
                    .Append(relativePath)
                    .Append('\0')
                    .Append(actualHash)
                    .Append('\0');
            }
            checks.Add("exact-file-hashes");
            checks.Add("structured-text-parse");
            return new PiAgentRepositoryValidationReceipt(
                1,
                "jarvisv2-pi-agent-repository-validation",
                true,
                "passed",
                currentHead,
                changedPaths,
                HashText(digestMaterial.ToString()),
                checks.ToArray(),
                null,
                validatedAtUtc);
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                IOException or
                Win32Exception or
                UnauthorizedAccessException or
                JsonException or
                XmlException or
                FormatException or
                ArgumentException)
        {
            return Failed(
                expectedHead,
                [],
                checks,
                "repository-validation-failed",
                validatedAtUtc,
                exception.Message);
        }
    }

    private async Task RequireDiffCheckAsync(
        string root,
        CancellationToken cancellationToken)
    {
        GitResult result = await RunGitAsync(
            root,
            [
                "diff",
                "--check",
                "--no-ext-diff",
                "--no-textconv",
                "--no-color",
                "--",
            ],
            cancellationToken,
            allowNonZeroExit: true);
        if (
            result.ExitCode != 0 ||
            !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                "The fixed git diff check rejected the reviewed workspace state: " +
                string.Join(
                    " / ",
                    new[]
                    {
                        result.StandardOutput.Trim(),
                        result.StandardError.Trim(),
                    }.Where(value => value.Length != 0)));
        }
    }

    private async Task<GitResult> RunGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowNonZeroExit = false)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = gitExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        startInfo.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.fsmonitor=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.untrackedCache=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.hooksPath=NUL");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("diff.external=");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(root);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                "The fixed Git repository gate could not start.");
        }
        using CancellationTokenSource timeout = new(
            TimeSpan.FromMilliseconds(ProcessTimeoutMilliseconds));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
        Task<string> standardOutput = ReadBoundedAsync(
            process.StandardOutput,
            linked.Token);
        Task<string> standardError = ReadBoundedAsync(
            process.StandardError,
            linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            string output = await standardOutput;
            string error = await standardError;
            if (!allowNonZeroExit && process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The fixed Git repository gate failed with exit code {process.ExitCode}: " +
                    error.Trim());
            }
            return new GitResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException exception)
            when (
                timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new InvalidOperationException(
                "The fixed Git repository gate timed out.",
                exception);
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        StringBuilder text = new();
        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (read == 0)
            {
                return text.ToString();
            }
            if (text.Length + read > MaximumProcessOutputCharacters)
            {
                throw new InvalidOperationException(
                    "The fixed Git repository gate output exceeded its boundary.");
            }
            text.Append(buffer, 0, read);
        }
    }

    private static IReadOnlyList<string> ParseExactModifiedPaths(
        string porcelain)
    {
        if (porcelain.Length == 0)
        {
            return [];
        }
        string[] entries = porcelain.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries);
        List<string> paths = new(entries.Length);
        foreach (string entry in entries)
        {
            if (
                entry.Length < 4 ||
                entry[0] != ' ' ||
                entry[1] != 'M' ||
                entry[2] != ' ')
            {
                throw new InvalidOperationException(
                    "The reviewed repository contains a staged, untracked, renamed, deleted, conflicted, or otherwise unadmitted path.");
            }
            string path = entry[3..].Replace('\\', '/');
            if (
                string.IsNullOrWhiteSpace(path) ||
                Path.IsPathFullyQualified(path) ||
                path.Split('/').Any(segment =>
                    segment is "" or "." or ".."))
            {
                throw new InvalidOperationException(
                    "Git reported an invalid reviewed path.");
            }
            paths.Add(path);
        }
        return paths
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string AdmitChangedFile(
        string root,
        string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root + Path.DirectorySeparatorChar;
        if (
            !fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) ||
            Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                "The reviewed repository path escaped or stopped naming a regular file.");
        }
        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private static void ValidateStructuredText(string fullPath)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension == ".json")
        {
            using FileStream stream = File.OpenRead(fullPath);
            using JsonDocument document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });
            _ = document.RootElement.ValueKind;
            return;
        }
        if (extension is not
            (".xml" or ".xaml" or ".csproj" or ".props" or ".targets"))
        {
            return;
        }
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_097_152,
            MaxCharactersFromEntities = 0,
        };
        using FileStream xmlStream = File.OpenRead(fullPath);
        using XmlReader reader = XmlReader.Create(xmlStream, settings);
        while (reader.Read())
        {
        }
    }

    private static string AdmitWorkspaceRoot(string workspaceRoot)
    {
        if (
            string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Path.IsPathFullyQualified(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "The repository gate requires an existing absolute workspace root.",
                nameof(workspaceRoot));
        }
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        string? filesystemRoot = Path.GetPathRoot(root);
        if (
            string.IsNullOrWhiteSpace(filesystemRoot) ||
            string.Equals(
                root,
                Path.TrimEndingDirectorySeparator(filesystemRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The repository gate may not use a filesystem root.",
                nameof(workspaceRoot));
        }
        EnsureNoReparsePoints(root);
        return root;
    }

    private static string NormalizeGitPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Git omitted the repository root.");
        }
        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeHead(string value)
    {
        string normalized = value.ToLowerInvariant();
        if (
            normalized.Length is not (40 or 64) ||
            normalized.Any(character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                "Git returned an invalid HEAD object id.");
        }
        return normalized;
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ??
            throw new InvalidOperationException(
                "The repository gate path has no filesystem root.");
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     [
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar,
                     ],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }
            if (
                (File.GetAttributes(current) &
                    FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The repository gate may not traverse a reparse point: " +
                    current);
            }
        }
    }

    private static PiAgentRepositoryValidationReceipt Failed(
        string head,
        IReadOnlyList<string> changedPaths,
        IReadOnlyList<string> checks,
        string errorCode,
        DateTimeOffset validatedAtUtc,
        string? detail = null) =>
        new(
            1,
            "jarvisv2-pi-agent-repository-validation",
            false,
            detail is null ? "failed" : $"failed: {detail}",
            head,
            changedPaths,
            null,
            checks.ToArray(),
            errorCode,
            validatedAtUtc);

    private sealed record GitResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
