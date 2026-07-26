using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Supervisor;

internal static partial class RecoveryTerminalLease
{
    private const string LeaseFileName = "m2-recovery-terminal.json";
    private const string LeaseDirectoryName = "Recovery";
    private const string LeaseReceiptType = "jarvisv2-m2-recovery-terminal-lease";
    private const string PlanReceiptType = "jarvisv2-m2-validation-session-plan";
    private const string ReadyState = "ready";
    private const string AwaitingApprovalState = "awaiting-exact-approval";
    private const string RecoveryCommand =
        @"dotnet run --project .\src\Jarvis.Supervisor --configuration Release --no-build -- arm-kill-switch";
    private const int MaximumJsonBytes = 64 * 1024;
    private static readonly TimeSpan MaximumHeartbeatAge = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly IReadOnlyDictionary<string, string> RequiredSourcePaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["planner"] = "scripts/New-M2ValidationSessionPlan.ps1",
            ["planSchema"] = "config/m2-validation-session-plan.schema.json",
            ["readinessScript"] = "scripts/Test-M2LiveReadiness.ps1",
            ["readinessSchema"] = "config/m2-live-readiness-receipt.schema.json",
            ["recoveryTerminalScript"] = "scripts/Open-M2RecoveryTerminal.ps1",
            ["recoveryLeaseSchema"] = "config/m2-recovery-terminal-lease.schema.json",
            ["observerScript"] = "scripts/Test-M2ObservationRehearsal.ps1",
            ["observerSchema"] = "config/m2-observation-rehearsal-receipt.schema.json",
            ["controlledLiveController"] = "scripts/Invoke-M2ControlledLiveValidation.ps1",
            ["nativeBuildReceipt"] = "docs/receipts/native-build-2026-07-22.json",
            ["m2Source"] = "mods/jarvis-taskbar-icon-size.wh.cpp",
            ["supervisorAssembly"] = "src/Jarvis.Supervisor/bin/Release/net8.0-windows/jarvis-supervisor.dll",
        };

    public static string LeasePath { get; } =
        Path.Combine(
            KillSwitch.StateDirectory,
            LeaseDirectoryName,
            LeaseFileName);

    public static RecoveryTerminalLeaseProbe Probe(
        string moduleId,
        string? leasePath = null)
    {
        string path = leasePath is null ? LeasePath : Path.GetFullPath(leasePath);
        try
        {
            if (!KillSwitch.IsAllowedModuleId(moduleId))
            {
                throw new InvalidOperationException(
                    $"Module id isn't allowlisted: {moduleId}");
            }

            string repositoryRoot = ResolveRepositoryRoot();
            path = ResolveLeasePath(repositoryRoot, leasePath);
            RecoveryTerminalLeaseDocument lease =
                ReadJson<RecoveryTerminalLeaseDocument>(path);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            Require(lease.SchemaVersion == 1, "lease-schema-version-invalid");
            Require(
                string.Equals(
                    lease.ReceiptType,
                    LeaseReceiptType,
                    StringComparison.Ordinal),
                "lease-receipt-type-invalid");
            Require(
                string.Equals(lease.State, ReadyState, StringComparison.Ordinal),
                "lease-not-ready");
            Require(
                string.Equals(lease.ModuleId, moduleId, StringComparison.Ordinal),
                "lease-module-mismatch");
            Require(
                RunIdPattern().IsMatch(lease.SessionPlanRunId ?? string.Empty),
                "lease-run-id-invalid");
            Require(lease.ProcessId > 0, "lease-process-id-invalid");
            Require(lease.HeartbeatSequence > 0, "lease-heartbeat-sequence-invalid");
            Require(!lease.ActivationPermitted, "lease-activation-boundary-invalid");
            Require(!lease.MutationPerformed, "lease-mutation-boundary-invalid");
            Require(
                string.Equals(
                    lease.RecoveryCommand,
                    RecoveryCommand,
                    StringComparison.Ordinal),
                "lease-recovery-command-invalid");

            DateTimeOffset processStart = ParseUtc(
                lease.ProcessStartTimeUtc,
                "lease-process-start-invalid");
            DateTimeOffset openedAt = ParseUtc(
                lease.OpenedAtUtc,
                "lease-opened-at-invalid");
            DateTimeOffset heartbeatAt = ParseUtc(
                lease.HeartbeatAtUtc,
                "lease-heartbeat-invalid");
            DateTimeOffset planExpiresAt = ParseUtc(
                lease.PlanExpiresAtUtc,
                "lease-plan-expiry-invalid");

            Require(openedAt <= heartbeatAt, "lease-time-order-invalid");
            Require(
                heartbeatAt <= now + MaximumFutureSkew,
                "lease-heartbeat-future-dated");
            Require(
                now - heartbeatAt <= MaximumHeartbeatAge,
                "lease-heartbeat-stale");
            Require(planExpiresAt > now, "lease-plan-expired");

            string planPath = ResolvePlanPath(
                repositoryRoot,
                lease.PlanPath,
                "lease-plan-path-invalid");
            string planHash = ComputeSha256(planPath);
            Require(
                HashPattern().IsMatch(lease.PlanSha256 ?? string.Empty),
                "lease-plan-hash-invalid");
            Require(
                string.Equals(
                    planHash,
                    lease.PlanSha256,
                    StringComparison.OrdinalIgnoreCase),
                "lease-plan-hash-mismatch");

            ValidationSessionPlanDocument plan =
                ReadJson<ValidationSessionPlanDocument>(planPath);
            ValidatePlan(
                plan,
                repositoryRoot,
                moduleId,
                lease.SessionPlanRunId!,
                planExpiresAt);

            using Process process = Process.GetProcessById(lease.ProcessId);
            Require(!process.HasExited, "lease-process-exited");
            Require(
                string.Equals(
                    process.ProcessName,
                    "pwsh",
                    StringComparison.OrdinalIgnoreCase),
                "lease-process-is-not-pwsh");
            DateTimeOffset actualProcessStart =
                new(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            Require(
                (actualProcessStart - processStart).Duration() <=
                    ProcessStartTolerance,
                "lease-process-start-mismatch");

            return new RecoveryTerminalLeaseProbe(
                true,
                "ready",
                path,
                moduleId,
                lease.SessionPlanRunId,
                lease.ProcessId,
                processStart,
                heartbeatAt,
                planExpiresAt,
                now,
                null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            FormatException or
            InvalidOperationException or
            ArgumentException or
            System.ComponentModel.Win32Exception)
        {
            return new RecoveryTerminalLeaseProbe(
                false,
                "blocked",
                path,
                moduleId,
                null,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                exception.Message);
        }
    }

    public static RecoveryTerminalLeaseProbe RequireReady(string moduleId)
    {
        RecoveryTerminalLeaseProbe probe = Probe(moduleId);
        if (!probe.Ready)
        {
            throw new InvalidOperationException(
                $"A fresh recovery-terminal lease is required before activation: {probe.Error}");
        }

        return probe;
    }

    private static void ValidatePlan(
        ValidationSessionPlanDocument plan,
        string repositoryRoot,
        string moduleId,
        string runId,
        DateTimeOffset leasePlanExpiresAt)
    {
        Require(plan.SchemaVersion == 1, "plan-schema-version-invalid");
        Require(
            string.Equals(plan.ReceiptType, PlanReceiptType, StringComparison.Ordinal),
            "plan-receipt-type-invalid");
        Require(
            string.Equals(plan.RunId, runId, StringComparison.Ordinal),
            "plan-run-id-mismatch");
        Require(
            string.Equals(plan.Result, "passed", StringComparison.Ordinal),
            "plan-result-invalid");
        Require(
            string.Equals(
                plan.State,
                AwaitingApprovalState,
                StringComparison.Ordinal),
            "plan-state-invalid");
        Require(
            string.Equals(plan.ModuleId, moduleId, StringComparison.Ordinal),
            "plan-module-mismatch");
        Require(!plan.ActivationPermitted, "plan-activation-boundary-invalid");
        Require(
            string.Equals(plan.LiveExplorer, "not-run", StringComparison.Ordinal),
            "plan-live-boundary-invalid");
        Require(!plan.MutationPerformed, "plan-mutation-boundary-invalid");
        Require(
            plan.Approval is not null &&
            !plan.Approval.ExactCommandApproved &&
            !plan.Approval.CanExecuteNow,
            "plan-approval-boundary-invalid");
        Require(
            plan.RecoveryTerminal is not null &&
            string.Equals(
                plan.RecoveryTerminal.Command,
                RecoveryCommand,
                StringComparison.Ordinal) &&
            !plan.RecoveryTerminal.LaunchPerformed &&
            !plan.RecoveryTerminal.TerminalAvailable,
            "plan-recovery-boundary-invalid");

        DateTimeOffset planExpiresAt = ParseUtc(
            plan.ExpiresAtUtc,
            "plan-expiry-invalid");
        Require(
            (planExpiresAt - leasePlanExpiresAt).Duration() <=
                TimeSpan.FromMilliseconds(1),
            "lease-plan-expiry-mismatch");
        Require(planExpiresAt > DateTimeOffset.UtcNow, "plan-expired");

        Dictionary<string, FileIdentityDocument> sourceIdentity =
            plan.SourceIdentity ??
            throw new InvalidOperationException("plan-source-identity-missing");
        Require(
            sourceIdentity.Count == RequiredSourcePaths.Count,
            "plan-source-identity-count-invalid");
        foreach ((string key, string relativePath) in RequiredSourcePaths)
        {
            Require(
                sourceIdentity.TryGetValue(
                    key,
                    out FileIdentityDocument? identity) &&
                identity is not null,
                $"plan-source-identity-missing:{key}");
            Require(
                string.Equals(
                    NormalizeRelativePath(identity!.RelativePath),
                    relativePath,
                    StringComparison.Ordinal),
                $"plan-source-path-invalid:{key}");

            string sourcePath = ResolveRepositoryFile(
                repositoryRoot,
                identity.RelativePath,
                $"plan-source-path-invalid:{key}");
            FileInfo item = new(sourcePath);
            Require(
                item.Length == identity.Size,
                $"plan-source-size-mismatch:{key}");
            Require(
                HashPattern().IsMatch(identity.Sha256 ?? string.Empty),
                $"plan-source-hash-invalid:{key}");
            Require(
                string.Equals(
                    ComputeSha256(sourcePath),
                    identity.Sha256,
                    StringComparison.OrdinalIgnoreCase),
                $"plan-source-hash-mismatch:{key}");

            if (string.Equals(key, "supervisorAssembly", StringComparison.Ordinal))
            {
                string executingAssembly =
                    Path.GetFullPath(typeof(Program).Assembly.Location);
                Require(
                    string.Equals(
                        executingAssembly,
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase),
                    "plan-supervisor-assembly-not-running");
            }
        }
    }

    private static T ReadJson<T>(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > MaximumJsonBytes)
        {
            throw new InvalidOperationException("json-length-invalid");
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        string json = StrictUtf8.GetString(bytes);
        return JsonSerializer.Deserialize<T>(
            json,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling =
                    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidOperationException("json-document-empty");
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (string start in new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "config",
                        "m2-validation-session-plan.schema.json")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("repository-root-not-found");
    }

    private static string ResolvePlanPath(
        string repositoryRoot,
        string? path,
        string error)
    {
        Require(!string.IsNullOrWhiteSpace(path), error);
        string fullPath = Path.GetFullPath(path!);
        string allowedRoot = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "artifacts",
                "m2-validation-session-plans",
                "runs"));
        Require(IsDescendant(fullPath, allowedRoot), error);
        Require(File.Exists(fullPath), error);
        RequireNoReparsePoints(fullPath, repositoryRoot, error);
        return fullPath;
    }

    private static string ResolveLeasePath(
        string repositoryRoot,
        string? requestedPath)
    {
        if (requestedPath is null)
        {
            string fullPath = Path.GetFullPath(LeasePath);
            Require(File.Exists(fullPath), "lease-path-missing");
            RequireNoReparsePoints(
                fullPath,
                KillSwitch.StateDirectory,
                "lease-path-reparse-point");
            return fullPath;
        }

        string fixturePath = Path.GetFullPath(requestedPath);
        string fixtureRoot = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "artifacts",
                "m2-recovery-lease-lab",
                "runs"));
        Require(
            IsDescendant(fixturePath, fixtureRoot),
            "lease-fixture-path-invalid");
        Require(File.Exists(fixturePath), "lease-fixture-path-missing");
        RequireNoReparsePoints(
            fixturePath,
            repositoryRoot,
            "lease-fixture-path-reparse-point");
        return fixturePath;
    }

    private static string ResolveRepositoryFile(
        string repositoryRoot,
        string? relativePath,
        string error)
    {
        Require(!string.IsNullOrWhiteSpace(relativePath), error);
        Require(!Path.IsPathRooted(relativePath), error);
        string fullPath = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                relativePath!.Replace('/', Path.DirectorySeparatorChar)));
        Require(IsDescendant(fullPath, repositoryRoot), error);
        Require(File.Exists(fullPath), error);
        RequireNoReparsePoints(fullPath, repositoryRoot, error);
        return fullPath;
    }

    private static void RequireNoReparsePoints(
        string path,
        string root,
        string error)
    {
        string current = Path.GetFullPath(root);
        FileAttributes rootAttributes = File.GetAttributes(current);
        Require(
            (rootAttributes & FileAttributes.ReparsePoint) == 0,
            error);
        string relative = Path.GetRelativePath(current, Path.GetFullPath(path));
        foreach (string segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);
            Require(
                (attributes & FileAttributes.ReparsePoint) == 0,
                error);
        }
    }

    private static bool IsDescendant(string candidate, string root)
    {
        string normalizedRoot =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/');

    private static DateTimeOffset ParseUtc(string? value, string error)
    {
        Require(
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind |
                    DateTimeStyles.AllowLeadingWhite |
                    DateTimeStyles.AllowTrailingWhite,
                out DateTimeOffset parsed),
            error);
        return parsed.ToUniversalTime();
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }

    [GeneratedRegex("^[0-9]{8}T[0-9]{9}Z-[a-f0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    private sealed record RecoveryTerminalLeaseDocument(
        int SchemaVersion,
        string? ReceiptType,
        string? State,
        string? ModuleId,
        string? SessionPlanRunId,
        string? PlanPath,
        string? PlanSha256,
        int ProcessId,
        string? ProcessStartTimeUtc,
        string? OpenedAtUtc,
        string? HeartbeatAtUtc,
        long HeartbeatSequence,
        string? PlanExpiresAtUtc,
        string? RecoveryCommand,
        bool ActivationPermitted,
        bool MutationPerformed);

    private sealed record ValidationSessionPlanDocument(
        int SchemaVersion,
        string? ReceiptType,
        string? RunId,
        string? CreatedAtUtc,
        string? ExpiresAtUtc,
        string? Result,
        string? State,
        string? ModuleId,
        bool ActivationPermitted,
        string? LiveExplorer,
        bool MutationPerformed,
        Dictionary<string, FileIdentityDocument>? SourceIdentity,
        JsonElement Readiness,
        RecoveryTerminalPlanDocument? RecoveryTerminal,
        ApprovalPlanDocument? Approval,
        string[]? Errors);

    private sealed record FileIdentityDocument(
        string? RelativePath,
        long Size,
        string? Sha256);

    private sealed record RecoveryTerminalPlanDocument(
        string? Command,
        string? OpenCommand,
        bool LaunchPerformed,
        bool TerminalAvailable);

    private sealed record ApprovalPlanDocument(
        string? ExactCommand,
        bool ExactCommandApproved,
        bool CanExecuteNow);
}

internal sealed record RecoveryTerminalLeaseProbe(
    bool Ready,
    string Status,
    string LeasePath,
    string ModuleId,
    string? SessionPlanRunId,
    int? ProcessId,
    DateTimeOffset? ProcessStartTimeUtc,
    DateTimeOffset? HeartbeatAtUtc,
    DateTimeOffset? PlanExpiresAtUtc,
    DateTimeOffset CheckedAtUtc,
    string? Error);
