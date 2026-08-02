using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentTrustedValidationProfileReceipt(
    int SchemaVersion,
    string ReceiptType,
    string ProfileId,
    string ManifestPath,
    string ProfileDigest,
    string Runner,
    int TimeoutSeconds,
    IReadOnlyList<string> TestFiles,
    string CommandDisplay,
    DateTimeOffset CapturedAtUtc);

public sealed record PiAgentTrustedValidationReceipt(
    int SchemaVersion,
    string ReceiptType,
    bool Passed,
    string Result,
    string ProfileId,
    string ProfileDigest,
    string CommandDisplay,
    int? ExitCode,
    string OutputDigest,
    int OutputCharacters,
    bool TimedOut,
    string ReceiptDigest,
    string? ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed class PiAgentReviewedIterationTrustedValidator
{
    public const string ManifestRelativePath =
        "config/pi-agent-trusted-validation.json";
    public const string ProcessModel =
        "desktop-owner-approved-pinned-head-node-test-direct-no-shell";
    public const int MaximumManifestUtf8Bytes = 16_384;
    public const int MaximumTestFiles = 8;
    public const int MaximumOutputCharacters = 262_144;
    public const int MinimumTimeoutSeconds = 5;
    public const int MaximumTimeoutSeconds = 120;

    private static readonly Regex ProfileIdPattern = new(
        "\\A[a-z0-9][a-z0-9.-]{2,63}\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly string nodeExecutable;
    private readonly PiAgentReviewedIterationRepositoryGate repositoryGate;
    private readonly object activeRunGate = new();
    private CancellationTokenSource? activeRunCancellation;
    private Process? activeProcess;

    public PiAgentReviewedIterationTrustedValidator(
        string nodeExecutable,
        PiAgentReviewedIterationRepositoryGate repositoryGate)
    {
        ArgumentNullException.ThrowIfNull(repositoryGate);
        if (
            string.IsNullOrWhiteSpace(nodeExecutable) ||
            !Path.IsPathFullyQualified(nodeExecutable) ||
            !File.Exists(nodeExecutable) ||
            !string.Equals(
                Path.GetFileName(nodeExecutable),
                "node.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Trusted validation requires an existing absolute node.exe.",
                nameof(nodeExecutable));
        }
        this.nodeExecutable = Path.GetFullPath(nodeExecutable);
        this.repositoryGate = repositoryGate;
    }

    public void CancelActiveRun()
    {
        lock (activeRunGate)
        {
            try
            {
                activeRunCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            TryTerminate(activeProcess);
        }
    }

    public async Task<PiAgentTrustedValidationProfileReceipt>
        CaptureProfileAsync(
            string workspaceRoot,
            string expectedHead,
            CancellationToken cancellationToken = default)
    {
        string manifest = await repositoryGate.ReadHeadUtf8FileAsync(
            workspaceRoot,
            expectedHead,
            ManifestRelativePath,
            MaximumManifestUtf8Bytes,
            cancellationToken);
        using JsonDocument document = JsonDocument.Parse(
            manifest,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The trusted validation manifest must be a JSON object.");
        }
        HashSet<string> propertyNames = root
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        string[] exactProperties =
        [
            "schemaVersion",
            "profileId",
            "runner",
            "timeoutSeconds",
            "testFiles",
        ];
        if (
            propertyNames.Count != exactProperties.Length ||
            exactProperties.Any(property => !propertyNames.Contains(property)) ||
            root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException(
                "The trusted validation manifest failed its exact schema boundary.");
        }

        string profileId = root.GetProperty("profileId").GetString() ?? "";
        string runner = root.GetProperty("runner").GetString() ?? "";
        int timeoutSeconds = root.GetProperty("timeoutSeconds").GetInt32();
        if (
            !ProfileIdPattern.IsMatch(profileId) ||
            runner != "node-test" ||
            timeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
        {
            throw new InvalidDataException(
                "The trusted validation manifest rejected its profile, runner, or timeout.");
        }

        JsonElement testFileArray = root.GetProperty("testFiles");
        if (testFileArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The trusted validation manifest testFiles value must be an array.");
        }
        List<string> testFiles = [];
        HashSet<string> uniqueFiles = new(StringComparer.Ordinal);
        foreach (JsonElement element in testFileArray.EnumerateArray())
        {
            string relativePath = element.GetString() ?? "";
            AdmitTestFilePath(relativePath);
            if (!uniqueFiles.Add(relativePath))
            {
                throw new InvalidDataException(
                    "The trusted validation manifest repeated a test file.");
            }
            testFiles.Add(relativePath);
        }
        if (testFiles.Count is < 1 or > MaximumTestFiles)
        {
            throw new InvalidDataException(
                "The trusted validation manifest requires one to eight test files.");
        }
        foreach (string relativePath in testFiles)
        {
            _ = await repositoryGate.ReadHeadUtf8FileAsync(
                workspaceRoot,
                expectedHead,
                relativePath,
                65_536,
                cancellationToken);
        }

        string digest = HashText(string.Join(
            '\0',
            new[]
            {
                "trusted-validation-profile-v1",
                profileId,
                runner,
                timeoutSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            }.Concat(testFiles)));
        string commandDisplay = "node.exe --test " + string.Join(
            ' ',
            testFiles.Select(path => $"\"{path}\""));
        return new PiAgentTrustedValidationProfileReceipt(
            1,
            "jarvisv2-pi-agent-trusted-validation-profile",
            profileId,
            ManifestRelativePath,
            digest,
            runner,
            timeoutSeconds,
            testFiles.ToArray(),
            commandDisplay,
            DateTimeOffset.UtcNow);
    }

    public async Task<PiAgentTrustedValidationReceipt> RunAsync(
        string workspaceRoot,
        string expectedHead,
        string expectedProfileId,
        string expectedProfileDigest,
        IReadOnlyCollection<string> expectedChangedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedChangedPaths);
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        PiAgentTrustedValidationProfileReceipt? profile = null;
        Process? process = null;
        using CancellationTokenSource ownedCancellation = new();
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                ownedCancellation.Token);
        lock (activeRunGate)
        {
            if (activeRunCancellation is not null)
            {
                throw new InvalidOperationException(
                    "Only one trusted validation process may be active.");
            }
            activeRunCancellation = ownedCancellation;
        }
        try
        {
            profile = await CaptureProfileAsync(
                workspaceRoot,
                expectedHead,
                operationCancellation.Token);
            if (
                profile.ProfileId != expectedProfileId ||
                profile.ProfileDigest != expectedProfileDigest)
            {
                return Failed(
                    profile,
                    startedAtUtc,
                    "trusted-validation-profile-drifted");
            }
            HashSet<string> changedPaths = new(
                expectedChangedPaths,
                StringComparer.Ordinal);
            if (profile.TestFiles.Any(changedPaths.Contains))
            {
                return Failed(
                    profile,
                    startedAtUtc,
                    "trusted-validation-test-file-modified");
            }
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspaceRoot));
            foreach (string testFile in profile.TestFiles)
            {
                AdmitWorktreeTestFile(root, testFile);
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = nodeExecutable,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.Environment.Clear();
            CopyEnvironment(startInfo, "SystemRoot");
            CopyEnvironment(startInfo, "WINDIR");
            CopyEnvironment(startInfo, "TEMP");
            CopyEnvironment(startInfo, "TMP");
            CopyEnvironment(startInfo, "LOCALAPPDATA");
            CopyEnvironment(startInfo, "APPDATA");
            startInfo.Environment["PI_OFFLINE"] = "1";
            startInfo.Environment["CI"] = "1";
            startInfo.Environment["NO_COLOR"] = "1";
            startInfo.Environment["JARVIS2_TRUSTED_VALIDATION"] = "1";
            startInfo.ArgumentList.Add("--test");
            foreach (string testFile in profile.TestFiles)
            {
                startInfo.ArgumentList.Add(testFile);
            }

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return Failed(
                    profile,
                    startedAtUtc,
                    "trusted-validation-process-not-started");
            }
            lock (activeRunGate)
            {
                activeProcess = process;
            }
            using CancellationTokenSource timeout = new(
                TimeSpan.FromSeconds(profile.TimeoutSeconds));
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    operationCancellation.Token,
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
                return CreateReceipt(
                    profile,
                    process.ExitCode == 0,
                    process.ExitCode == 0 ? "passed" : "failed",
                    process.ExitCode,
                    output,
                    error,
                    timedOut: false,
                    process.ExitCode == 0
                        ? null
                        : "trusted-validation-nonzero-exit",
                    startedAtUtc);
            }
            catch (OperationCanceledException)
            {
                TryTerminate(process);
                return Failed(
                    profile,
                    startedAtUtc,
                    timeout.IsCancellationRequested &&
                        !operationCancellation.IsCancellationRequested
                            ? "trusted-validation-timed-out"
                            : "trusted-validation-cancelled",
                    timedOut: timeout.IsCancellationRequested &&
                        !operationCancellation.IsCancellationRequested);
            }
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return Failed(
                profile,
                startedAtUtc,
                "trusted-validation-cancelled");
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException or
                Win32Exception or
                JsonException or
                ArgumentException)
        {
            TryTerminate(process);
            return Failed(
                profile,
                startedAtUtc,
                exception.Message.Contains(
                    "output exceeded",
                    StringComparison.Ordinal)
                        ? "trusted-validation-output-limit"
                        : "trusted-validation-admission-failed");
        }
        finally
        {
            lock (activeRunGate)
            {
                activeProcess = null;
                if (ReferenceEquals(
                        activeRunCancellation,
                        ownedCancellation))
                {
                    activeRunCancellation = null;
                }
            }
            process?.Dispose();
        }
    }

    private static PiAgentTrustedValidationReceipt CreateReceipt(
        PiAgentTrustedValidationProfileReceipt profile,
        bool passed,
        string result,
        int? exitCode,
        string standardOutput,
        string standardError,
        bool timedOut,
        string? errorCode,
        DateTimeOffset startedAtUtc)
    {
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        string combinedOutput = standardOutput + "\0" + standardError;
        string outputDigest = HashText(combinedOutput);
        int outputCharacters = standardOutput.Length + standardError.Length;
        string receiptDigest = HashText(string.Join(
            '\0',
            new[]
            {
                "trusted-validation-receipt-v1",
                profile.ProfileId,
                profile.ProfileDigest,
                result,
                exitCode?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "none",
                outputDigest,
                outputCharacters.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                timedOut ? "timed-out" : "completed",
                errorCode ?? "none",
                startedAtUtc.ToUniversalTime().ToString("O"),
                completedAtUtc.ToUniversalTime().ToString("O"),
            }));
        return new PiAgentTrustedValidationReceipt(
            1,
            "jarvisv2-pi-agent-trusted-validation",
            passed,
            result,
            profile.ProfileId,
            profile.ProfileDigest,
            profile.CommandDisplay,
            exitCode,
            outputDigest,
            outputCharacters,
            timedOut,
            receiptDigest,
            errorCode,
            startedAtUtc,
            completedAtUtc);
    }

    private static PiAgentTrustedValidationReceipt Failed(
        PiAgentTrustedValidationProfileReceipt? profile,
        DateTimeOffset startedAtUtc,
        string errorCode,
        bool timedOut = false) => CreateReceipt(
            profile ?? new PiAgentTrustedValidationProfileReceipt(
                1,
                "jarvisv2-pi-agent-trusted-validation-profile",
                "unavailable",
                ManifestRelativePath,
                new string('0', 64),
                "node-test",
                MinimumTimeoutSeconds,
                [],
                "node.exe --test <profile unavailable>",
                startedAtUtc),
            false,
            "failed",
            null,
            "",
            "",
            timedOut,
            errorCode,
            startedAtUtc);

    private static void AdmitTestFilePath(string relativePath)
    {
        if (
            string.IsNullOrWhiteSpace(relativePath) ||
            Encoding.UTF8.GetByteCount(relativePath) > 512 ||
            Path.IsPathFullyQualified(relativePath) ||
            relativePath.Contains('\\') ||
            !relativePath.EndsWith(".mjs", StringComparison.Ordinal) ||
            relativePath.Any(char.IsControl) ||
            relativePath.Split('/').Any(segment =>
                segment is "" or "." or ".." ||
                segment.Equals(
                    ".git",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The trusted validation manifest contains an invalid test file path.");
        }
    }

    private static void AdmitWorktreeTestFile(
        string root,
        string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root + Path.DirectorySeparatorChar;
        if (
            !fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            throw new InvalidDataException(
                "A trusted validation test file is missing from the admitted worktree.");
        }
        string current = root;
        foreach (string segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Trusted validation may not traverse a reparse point.");
            }
        }
    }

    private static void CopyEnvironment(
        ProcessStartInfo startInfo,
        string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[variableName] = value;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        StringBuilder output = new();
        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (read == 0)
            {
                return output.ToString();
            }
            if (output.Length + read > MaximumOutputCharacters)
            {
                throw new InvalidOperationException(
                    "Trusted validation output exceeded its boundary.");
            }
            output.Append(buffer, 0, read);
        }
    }

    private static void TryTerminate(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static string HashText(string text) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        .ToLowerInvariant();
}
