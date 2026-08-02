using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.PiAgentHost;

public enum PiAgentReviewedIterationStatus
{
    ActiveTurn,
    AwaitingOwnerReview,
    DecisionInFlight,
    Validating,
    AwaitingTrustedValidation,
    TrustedValidationInFlight,
    ReadyToContinue,
    Completed,
    Stopped,
    Interrupted,
    Expired,
    Faulted,
}

public sealed record PiAgentReviewedIterationStepReceipt(
    int StepNumber,
    string TurnId,
    string Outcome,
    string? ProposalId,
    string? RelativePath,
    string? BeforeSha256,
    string? AfterSha256,
    string OwnerDecision,
    string ValidationResult,
    string? RepositoryDigest,
    string? ErrorCode,
    DateTimeOffset RecordedAtUtc)
{
    public IReadOnlyList<PiAgentReviewedIterationFileReceipt> Files
        { get; init; } = [];
    public string TrustedValidationResult { get; init; } = "not-run";
    public string? TrustedValidationReceiptDigest { get; init; }
    public string? TrustedValidationOutputDigest { get; init; }
    public int? TrustedValidationExitCode { get; init; }
    public DateTimeOffset? TrustedValidationCompletedAtUtc { get; init; }
}

public sealed record PiAgentReviewedIterationFileReceipt(
    int Ordinal,
    string Operation,
    string RelativePath,
    string BeforeSha256,
    string? AfterSha256);

public sealed record PiAgentReviewedIterationSnapshot(
    int SchemaVersion,
    int Revision,
    string IterationId,
    string Mission,
    int MaximumApprovedEdits,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string RepositoryHead,
    string ValidationProfile,
    bool AutoContinueAfterApproval,
    PiAgentReviewedIterationStatus Status,
    int CurrentStepNumber,
    string? CurrentTurnId,
    string? CurrentProposalId,
    int ApprovedEditCount,
    string RepositoryDigest,
    string StatusDetail,
    IReadOnlyList<PiAgentReviewedIterationStepReceipt> Steps,
    DateTimeOffset UpdatedAtUtc)
{
    public string? TrustedValidationProfileId { get; init; }
    public string? TrustedValidationProfileDigest { get; init; }
    public string? TrustedValidationCommand { get; init; }
    public int? TrustedValidationTimeoutSeconds { get; init; }

    public bool IsTerminal => Status is
        PiAgentReviewedIterationStatus.Completed or
        PiAgentReviewedIterationStatus.Stopped or
        PiAgentReviewedIterationStatus.Expired or
        PiAgentReviewedIterationStatus.Faulted;

    public string StatusLabel => Status switch
    {
        PiAgentReviewedIterationStatus.ActiveTurn => "PI WORKING",
        PiAgentReviewedIterationStatus.AwaitingOwnerReview =>
            "OWNER REVIEW REQUIRED",
        PiAgentReviewedIterationStatus.DecisionInFlight =>
            "OWNER DECISION IN FLIGHT",
        PiAgentReviewedIterationStatus.Validating =>
            "REPOSITORY GATE RUNNING",
        PiAgentReviewedIterationStatus.AwaitingTrustedValidation =>
            "VALIDATION APPROVAL REQUIRED",
        PiAgentReviewedIterationStatus.TrustedValidationInFlight =>
            "TRUSTED TESTS RUNNING",
        PiAgentReviewedIterationStatus.ReadyToContinue =>
            "VALIDATED / CONTINUING",
        PiAgentReviewedIterationStatus.Completed => "COMPLETED",
        PiAgentReviewedIterationStatus.Stopped => "STOPPED BY OWNER",
        PiAgentReviewedIterationStatus.Interrupted =>
            "INTERRUPTED / RE-ARM REQUIRED",
        PiAgentReviewedIterationStatus.Expired => "POLICY EXPIRED",
        PiAgentReviewedIterationStatus.Faulted => "FAILED CLOSED",
        _ => "UNKNOWN",
    };

    public string ProgressLabel =>
        $"{ApprovedEditCount} / {MaximumApprovedEdits} APPROVED EDITS";

    public string ExpiryLabel =>
        $"EXPIRES {ExpiresAtUtc.ToLocalTime():yyyy.MM.dd HH:mm}";

    public string HeadLabel =>
        $"HEAD {RepositoryHead[..Math.Min(12, RepositoryHead.Length)]}";

    public string ReceiptLabel =>
        $"{Steps.Count} STEPS / REV {Revision}";
}

internal static partial class PiAgentReviewedIterationAdmission
{
    public const int MaximumMissionBytes = 4_096;
    public const int MaximumSteps = 16;
    public const int MaximumStatusDetailBytes = 2_048;
    public const string ValidationProfile =
        "git-head-pathset-text-hash-diffcheck-structured-parse-v2";

    [GeneratedRegex(
        @"\Areview-loop-[0-9]{17}-[a-f0-9]{16}\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex IterationIdPattern();

    [GeneratedRegex(
        @"\Aiteration-turn-[a-f0-9]{32}\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex TurnIdPattern();

    [GeneratedRegex(
        @"\Aworkspace-edit-[a-f0-9]{32}\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProposalIdPattern();

    [GeneratedRegex(
        @"\A[0-9a-f]{40}([0-9a-f]{24})?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex GitObjectIdPattern();

    [GeneratedRegex(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public static PiAgentReviewedIterationSnapshot Admit(
        PiAgentReviewedIterationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (
            snapshot.SchemaVersion is not (1 or 2 or 3) ||
            snapshot.Revision is < 1 or > 64 ||
            !IterationIdPattern().IsMatch(snapshot.IterationId) ||
            string.IsNullOrWhiteSpace(snapshot.Mission) ||
            Encoding.UTF8.GetByteCount(snapshot.Mission) >
                MaximumMissionBytes ||
            snapshot.MaximumApprovedEdits is < 1 or > 4 ||
            snapshot.StartedAtUtc == default ||
            snapshot.ExpiresAtUtc <= snapshot.StartedAtUtc ||
            snapshot.ExpiresAtUtc - snapshot.StartedAtUtc >
                TimeSpan.FromHours(6) ||
            !GitObjectIdPattern().IsMatch(snapshot.RepositoryHead) ||
            snapshot.ValidationProfile != ValidationProfile ||
            snapshot.CurrentStepNumber is < 1 or > 5 ||
            snapshot.ApprovedEditCount is < 0 or > 4 ||
            snapshot.ApprovedEditCount > snapshot.MaximumApprovedEdits ||
            !Sha256Pattern().IsMatch(snapshot.RepositoryDigest) ||
            string.IsNullOrWhiteSpace(snapshot.StatusDetail) ||
            Encoding.UTF8.GetByteCount(snapshot.StatusDetail) >
                MaximumStatusDetailBytes ||
            snapshot.Steps is null ||
            snapshot.Steps.Count > MaximumSteps ||
            snapshot.UpdatedAtUtc < snapshot.StartedAtUtc ||
            snapshot.UpdatedAtUtc >
                DateTimeOffset.UtcNow.AddMinutes(5))
        {
            throw new ArgumentException(
                "The reviewed iteration snapshot failed policy admission.",
                nameof(snapshot));
        }

        bool trustedValidationSchema = snapshot.SchemaVersion >= 2;
        bool multiFileReceiptSchema = snapshot.SchemaVersion == 3;
        if (trustedValidationSchema)
        {
            if (
                snapshot.AutoContinueAfterApproval ||
                string.IsNullOrWhiteSpace(
                    snapshot.TrustedValidationProfileId) ||
                !ValidOptionalText(snapshot.TrustedValidationProfileId, 128) ||
                !Sha256Pattern().IsMatch(
                    snapshot.TrustedValidationProfileDigest ?? "") ||
                string.IsNullOrWhiteSpace(
                    snapshot.TrustedValidationCommand) ||
                !ValidOptionalText(snapshot.TrustedValidationCommand, 2_048) ||
                snapshot.TrustedValidationTimeoutSeconds is
                    null or < 5 or > 120)
            {
                throw new ArgumentException(
                    "The reviewed iteration trusted validation profile failed admission.",
                    nameof(snapshot));
            }
        }
        else if (
            !snapshot.AutoContinueAfterApproval ||
            snapshot.TrustedValidationProfileId is not null ||
            snapshot.TrustedValidationProfileDigest is not null ||
            snapshot.TrustedValidationCommand is not null ||
            snapshot.TrustedValidationTimeoutSeconds is not null)
        {
            throw new ArgumentException(
                "The legacy reviewed iteration snapshot shape diverged.",
                nameof(snapshot));
        }

        if (
            snapshot.CurrentTurnId is not null &&
            !TurnIdPattern().IsMatch(snapshot.CurrentTurnId))
        {
            throw new ArgumentException(
                "The reviewed iteration turn id was invalid.",
                nameof(snapshot));
        }
        if (
            snapshot.CurrentProposalId is not null &&
            !ProposalIdPattern().IsMatch(snapshot.CurrentProposalId))
        {
            throw new ArgumentException(
                "The reviewed iteration proposal id was invalid.",
                nameof(snapshot));
        }

        List<PiAgentReviewedIterationStepReceipt> admittedSteps =
            new(snapshot.Steps.Count);
        HashSet<string> turnIds = new(StringComparer.Ordinal);
        int approved = 0;
        for (int index = 0; index < snapshot.Steps.Count; index++)
        {
            PiAgentReviewedIterationStepReceipt? step =
                snapshot.Steps[index];
            if (
                step is null ||
                step.StepNumber != index + 1 ||
                !TurnIdPattern().IsMatch(step.TurnId) ||
                !turnIds.Add(step.TurnId) ||
                (step.ProposalId is not null &&
                    !ProposalIdPattern().IsMatch(step.ProposalId)) ||
                string.IsNullOrWhiteSpace(step.Outcome) ||
                string.IsNullOrWhiteSpace(step.OwnerDecision) ||
                string.IsNullOrWhiteSpace(step.ValidationResult) ||
                step.RecordedAtUtc < snapshot.StartedAtUtc ||
                step.RecordedAtUtc > snapshot.UpdatedAtUtc ||
                !ValidOptionalSha(step.BeforeSha256) ||
                !ValidOptionalSha(step.AfterSha256) ||
                !ValidOptionalSha(step.RepositoryDigest) ||
                !ValidOptionalStepPath(
                    step.RelativePath,
                    multiFileReceiptSchema) ||
                !ValidOptionalText(step.ErrorCode, 256) ||
                step.Files is null ||
                string.IsNullOrWhiteSpace(
                    step.TrustedValidationResult) ||
                Encoding.UTF8.GetByteCount(
                    step.TrustedValidationResult) > 128 ||
                !ValidOptionalSha(
                    step.TrustedValidationReceiptDigest) ||
                !ValidOptionalSha(
                    step.TrustedValidationOutputDigest) ||
                step.TrustedValidationExitCode is < -1 or > 65_535 ||
                (step.TrustedValidationCompletedAtUtc is not null &&
                    (step.TrustedValidationCompletedAtUtc <
                        snapshot.StartedAtUtc ||
                     step.TrustedValidationCompletedAtUtc >
                        snapshot.UpdatedAtUtc)))
            {
                throw new ArgumentException(
                    "The reviewed iteration contains an invalid step receipt.",
                    nameof(snapshot));
            }
            PiAgentReviewedIterationFileReceipt[] admittedFiles =
                AdmitFiles(
                    snapshot,
                    step,
                    multiFileReceiptSchema);
            if (
                step.OwnerDecision == "approved" &&
                step.AfterSha256 is not null)
            {
                approved++;
                if (
                    step.RelativePath is null ||
                    (step.ValidationResult == "passed" &&
                        step.RepositoryDigest is null))
                {
                    throw new ArgumentException(
                        "An approved iteration step omitted its durable hashes.",
                        nameof(snapshot));
                }
                if (trustedValidationSchema)
                {
                    bool completedValidation =
                        step.TrustedValidationResult is "passed" or "failed";
                    if (
                        completedValidation !=
                            (step.TrustedValidationReceiptDigest is not null) ||
                        completedValidation !=
                            (step.TrustedValidationOutputDigest is not null) ||
                        completedValidation !=
                            (step.TrustedValidationCompletedAtUtc is not null) ||
                        (step.TrustedValidationResult == "passed" &&
                            step.TrustedValidationExitCode != 0))
                    {
                        throw new ArgumentException(
                            "An approved iteration step has an invalid trusted validation receipt.",
                            nameof(snapshot));
                    }
                }
            }
            admittedSteps.Add(step with { Files = admittedFiles });
        }
        if (approved != snapshot.ApprovedEditCount)
        {
            throw new ArgumentException(
                "The reviewed iteration approved edit count diverged.",
                nameof(snapshot));
        }
        bool validCapabilityShape = snapshot.Status switch
        {
            PiAgentReviewedIterationStatus.ActiveTurn =>
                snapshot.CurrentTurnId is not null &&
                snapshot.CurrentProposalId is null,
            PiAgentReviewedIterationStatus.AwaitingOwnerReview or
            PiAgentReviewedIterationStatus.DecisionInFlight or
            PiAgentReviewedIterationStatus.Validating =>
                snapshot.CurrentTurnId is not null &&
                snapshot.CurrentProposalId is not null,
            PiAgentReviewedIterationStatus.AwaitingTrustedValidation or
            PiAgentReviewedIterationStatus.TrustedValidationInFlight =>
                trustedValidationSchema &&
                snapshot.CurrentTurnId is null &&
                snapshot.CurrentProposalId is null &&
                snapshot.Steps.LastOrDefault()?.TrustedValidationResult ==
                    "pending",
            _ =>
                snapshot.CurrentTurnId is null &&
                snapshot.CurrentProposalId is null,
        };
        if (!validCapabilityShape)
        {
            throw new ArgumentException(
                "The reviewed iteration capability shape diverged from its status.",
                nameof(snapshot));
        }

        if (
            trustedValidationSchema &&
            snapshot.Status is (
                PiAgentReviewedIterationStatus.ActiveTurn or
                PiAgentReviewedIterationStatus.AwaitingOwnerReview) &&
            snapshot.Steps.Any(step =>
                step.OwnerDecision == "approved" &&
                step.ValidationResult == "passed" &&
                step.TrustedValidationResult != "passed"))
        {
            throw new ArgumentException(
                "The reviewed iteration tried to continue before trusted validation passed.",
                nameof(snapshot));
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        if (bytes.Length > PiAgentReviewedIterationStore.MaximumPayloadBytes)
        {
            throw new ArgumentException(
                "The reviewed iteration snapshot exceeded its payload boundary.",
                nameof(snapshot));
        }

        return snapshot with
        {
            Steps = admittedSteps.ToArray(),
        };
    }

    private static bool ValidOptionalSha(string? value) =>
        value is null || Sha256Pattern().IsMatch(value);

    private static PiAgentReviewedIterationFileReceipt[] AdmitFiles(
        PiAgentReviewedIterationSnapshot snapshot,
        PiAgentReviewedIterationStepReceipt step,
        bool multiFileReceiptSchema)
    {
        if (!multiFileReceiptSchema)
        {
            if (step.Files.Count != 0)
            {
                throw new ArgumentException(
                    "A legacy reviewed iteration step contained file receipts.",
                    nameof(snapshot));
            }
            return [];
        }

        bool proposalRecorded = step.ProposalId is not null;
        if (!proposalRecorded)
        {
            if (
                step.Files.Count != 0 ||
                step.RelativePath is not null ||
                step.BeforeSha256 is not null ||
                step.AfterSha256 is not null)
            {
                throw new ArgumentException(
                    "A proposal-free iteration step contained workspace receipts.",
                    nameof(snapshot));
            }
            return [];
        }
        if (step.Files.Count is < 1 or > 4)
        {
            throw new ArgumentException(
                "A reviewed iteration proposal omitted its bounded file receipts.",
                nameof(snapshot));
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        PiAgentReviewedIterationFileReceipt[] files =
            new PiAgentReviewedIterationFileReceipt[step.Files.Count];
        bool mutationRecorded = step.AfterSha256 is not null;
        for (int index = 0; index < step.Files.Count; index++)
        {
            PiAgentReviewedIterationFileReceipt? file = step.Files[index];
            if (
                file is null ||
                file.Ordinal != index + 1 ||
                file.Operation is not ("replace" or "patch" or "create") ||
                !ValidWorkspaceRelativePath(file.RelativePath) ||
                !paths.Add(file.RelativePath) ||
                !Sha256Pattern().IsMatch(file.BeforeSha256) ||
                (mutationRecorded
                    ? file.AfterSha256 is null ||
                        !Sha256Pattern().IsMatch(file.AfterSha256) ||
                        file.AfterSha256 == file.BeforeSha256
                    : file.AfterSha256 is not null))
            {
                throw new ArgumentException(
                    "A reviewed iteration file receipt failed admission.",
                    nameof(snapshot));
            }
            files[index] = file;
        }

        if (files.Length == 1)
        {
            PiAgentReviewedIterationFileReceipt file = files[0];
            if (
                step.RelativePath != file.RelativePath ||
                step.BeforeSha256 != file.BeforeSha256 ||
                step.AfterSha256 != file.AfterSha256)
            {
                throw new ArgumentException(
                    "A single-file iteration receipt diverged from its file receipt.",
                    nameof(snapshot));
            }
        }
        else if (
            step.RelativePath != string.Empty ||
            step.BeforeSha256 is null ||
            (mutationRecorded &&
                !string.Equals(
                    ComputeChangeSetAfterDigest(files),
                    step.AfterSha256,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A multi-file iteration receipt diverged from its change-set digest.",
                nameof(snapshot));
        }
        return files;
    }

    private static string ComputeChangeSetAfterDigest(
        IReadOnlyList<PiAgentReviewedIterationFileReceipt> files)
    {
        StringBuilder material = new(
            "JARVIS2/workspace-change-set-after/v1");
        foreach (PiAgentReviewedIterationFileReceipt file in files)
        {
            material
                .Append('\0')
                .Append(file.Ordinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append('\0')
                .Append(file.RelativePath)
                .Append('\0')
                .Append(file.AfterSha256);
        }
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(material.ToString())))
            .ToLowerInvariant();
    }

    private static bool ValidOptionalStepPath(
        string? value,
        bool multiFileReceiptSchema) =>
        value is null ||
        (multiFileReceiptSchema && value.Length == 0) ||
        ValidOptionalText(value, 512);

    private static bool ValidWorkspaceRelativePath(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value) ||
            value.Length > 512 ||
            value.Contains('\\') ||
            value.Contains(':') ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.StartsWith('/') ||
            value.EndsWith('/') ||
            value.Any(char.IsControl) ||
            Path.IsPathFullyQualified(value))
        {
            return false;
        }
        string[] segments = value.Split('/');
        return segments.All(segment =>
            segment.Length > 0 &&
            segment is not "." and not ".." &&
            !segment.Equals(
                ".jarvis2-workspace-transaction.json",
                StringComparison.OrdinalIgnoreCase) &&
            !segment.StartsWith(
                ".jarvis2-workspace-transaction-",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidOptionalText(
        string? value,
        int maximumBytes) =>
        value is null ||
        (
            !string.IsNullOrWhiteSpace(value) &&
            Encoding.UTF8.GetByteCount(value) <= maximumBytes
        );
}
