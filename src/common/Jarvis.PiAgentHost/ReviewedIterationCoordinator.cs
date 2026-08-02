using System.Text;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentReviewedIterationDecisionResult(
    PiAgentWorkspaceEditSnapshot Edit,
    PiAgentReviewedIterationSnapshot Iteration,
    PiAgentConversationTurn? ContinuedTurn);

public sealed record PiAgentTrustedValidationDecisionResult(
    PiAgentTrustedValidationReceipt? Validation,
    PiAgentReviewedIterationSnapshot Iteration,
    PiAgentConversationTurn? ContinuedTurn);

public sealed class PiAgentReviewedIterationSnapshotChangedEventArgs(
    PiAgentReviewedIterationSnapshot? snapshot) : EventArgs
{
    public PiAgentReviewedIterationSnapshot? Snapshot { get; } = snapshot;
}

public sealed class PiAgentReviewedIterationCoordinator
{
    public const int MaximumApprovedEdits = 4;
    public const int PolicyLifetimeHours = 6;
    public const string ContinuationModel =
        "desktop-continue-only-after-separate-owner-approved-trusted-validation";
    public const string ApprovalModel =
        "desktop-owner-one-shot-per-edit-no-model-decision-authority";
    public const string ValidationApprovalModel =
        "desktop-owner-one-shot-pinned-head-tests-no-model-execution-authority";

    private readonly PiAgentConversationState conversation;
    private readonly string workspaceRoot;
    private readonly PiAgentReviewedIterationStore store;
    private readonly PiAgentReviewedIterationRepositoryGate repositoryGate;
    private readonly PiAgentReviewedIterationTrustedValidator trustedValidator;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object snapshotGate = new();
    private readonly object validationOperationGate = new();
    private CancellationTokenSource? validationOperationCancellation;
    private PiAgentReviewedIterationSnapshot? snapshot;

    private PiAgentReviewedIterationCoordinator(
        PiAgentConversationState conversation,
        string workspaceRoot,
        PiAgentReviewedIterationStore store,
        PiAgentReviewedIterationRepositoryGate repositoryGate,
        PiAgentReviewedIterationTrustedValidator trustedValidator,
        PiAgentReviewedIterationSnapshot? snapshot)
    {
        this.conversation = conversation;
        this.workspaceRoot = workspaceRoot;
        this.store = store;
        this.repositoryGate = repositoryGate;
        this.trustedValidator = trustedValidator;
        this.snapshot = snapshot;
    }

    public event EventHandler<
        PiAgentReviewedIterationSnapshotChangedEventArgs>? SnapshotChanged;

    public PiAgentReviewedIterationSnapshot? Snapshot
    {
        get
        {
            lock (snapshotGate)
            {
                return snapshot;
            }
        }
    }

    public static async Task<PiAgentReviewedIterationCoordinator> OpenAsync(
        PiAgentConversationState conversation,
        string workspaceRoot,
        PiAgentSidecarOptions sidecarOptions,
        PiAgentReviewedIterationStore? store = null,
        PiAgentReviewedIterationRepositoryGate? repositoryGate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(sidecarOptions);
        string canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        PiAgentReviewedIterationStore admittedStore =
            store ?? new PiAgentReviewedIterationStore();
        PiAgentReviewedIterationSnapshot? restored =
            await admittedStore.LoadLatestAsync(
                canonicalRoot,
                cancellationToken);
        PiAgentReviewedIterationRepositoryGate admittedRepositoryGate =
            repositoryGate ?? new PiAgentReviewedIterationRepositoryGate();
        PiAgentReviewedIterationCoordinator coordinator = new(
            conversation,
            canonicalRoot,
            admittedStore,
            admittedRepositoryGate,
            new PiAgentReviewedIterationTrustedValidator(
                sidecarOptions.NodeExecutablePath,
                admittedRepositoryGate),
            restored);
        if (restored is not null && !restored.IsTerminal)
        {
            PiAgentReviewedIterationStatus restoredStatus =
                DateTimeOffset.UtcNow >= restored.ExpiresAtUtc
                    ? PiAgentReviewedIterationStatus.Expired
                    : PiAgentReviewedIterationStatus.Interrupted;
            if (
                restored.Status != restoredStatus ||
                restored.CurrentTurnId is not null ||
                restored.CurrentProposalId is not null)
            {
                await coordinator.PersistAsync(
                    restored with
                    {
                        Status = restoredStatus,
                        CurrentTurnId = null,
                        CurrentProposalId = null,
                        StatusDetail = restoredStatus ==
                            PiAgentReviewedIterationStatus.Expired
                                ? "The stored owner policy expired while the desktop was not running. No capability was restored."
                                : restored.Steps.LastOrDefault()?.TrustedValidationResult == "pending"
                                    ? "The desktop restarted before trusted validation. Revalidate and explicitly re-arm the fixed test approval; no process capability was restored."
                                    : "The desktop restarted. Validate and explicitly re-arm before Pi continues; no pending edit capability was restored.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken,
                    publish: false);
            }
        }
        return coordinator;
    }

    public async Task<PiAgentConversationTurn> StartAsync(
        string mission,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            ValidateMission(mission);
            PiAgentReviewedIterationSnapshot? current = Snapshot;
            if (current is not null && !current.IsTerminal)
            {
                throw new InvalidOperationException(
                    "A reviewed iteration policy is already active.");
            }
            if (!conversation.Snapshot.CanSubmit)
            {
                throw new InvalidOperationException(
                    "The conversation is not ready to arm a reviewed iteration policy.");
            }

            PiAgentRepositoryBaselineReceipt baseline =
                await repositoryGate.CaptureCleanBaselineAsync(
                    workspaceRoot,
                    cancellationToken);
            PiAgentTrustedValidationProfileReceipt trustedProfile =
                await trustedValidator.CaptureProfileAsync(
                    workspaceRoot,
                    baseline.Head,
                    cancellationToken);
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            PiAgentReviewedIterationSnapshot next = new(
                2,
                1,
                $"review-loop-{startedAtUtc:yyyyMMddHHmmssfff}-" +
                    Guid.NewGuid().ToString("N")[..16],
                mission.Trim(),
                MaximumApprovedEdits,
                startedAtUtc,
                startedAtUtc.AddHours(PolicyLifetimeHours),
                baseline.Head,
                baseline.ValidationProfile,
                false,
                PiAgentReviewedIterationStatus.ReadyToContinue,
                1,
                null,
                null,
                0,
                baseline.RepositoryDigest,
                "Owner policy armed from a clean Git HEAD. Preparing the first bounded Pi turn.",
                [],
                startedAtUtc)
            {
                TrustedValidationProfileId = trustedProfile.ProfileId,
                TrustedValidationProfileDigest = trustedProfile.ProfileDigest,
                TrustedValidationCommand = trustedProfile.CommandDisplay,
                TrustedValidationTimeoutSeconds =
                    trustedProfile.TimeoutSeconds,
            };
            await PersistAsync(next, cancellationToken);
            return await BeginNextTurnLockedAsync(cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task ObserveTurnCompletionAsync(
        PiAgentConversationTurnSnapshot turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot current = RequireSnapshot();
            if (
                current.Status !=
                    PiAgentReviewedIterationStatus.ActiveTurn ||
                current.CurrentTurnId != turn.TurnId)
            {
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= current.ExpiresAtUtc)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Expired,
                        CurrentTurnId = null,
                        CurrentProposalId = null,
                        StatusDetail =
                            "The owner policy expired after this turn. No further continuation is admitted.",
                        UpdatedAtUtc = now,
                    },
                    cancellationToken);
                return;
            }

            PiAgentWorkspaceEditSnapshot[] pending = turn.WorkspaceEdits
                .Where(edit => edit.Status ==
                    PiAgentWorkspaceEditStatus.Pending)
                .ToArray();
            if (
                turn.Status == PiAgentConversationTurnStatus.Completed &&
                pending.Length == 1)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus
                            .AwaitingOwnerReview,
                        CurrentProposalId = pending[0].ProposalId,
                        StatusDetail =
                            "Pi staged one exact edit. The loop is paused until the owner approves once or rejects without writing.",
                        UpdatedAtUtc = now,
                    },
                    cancellationToken);
                return;
            }

            string outcome = turn.Status switch
            {
                PiAgentConversationTurnStatus.Completed when
                    pending.Length == 0 => "completed-without-proposal",
                PiAgentConversationTurnStatus.Aborted => "turn-aborted",
                _ => "turn-failed",
            };
            PiAgentReviewedIterationStepReceipt receipt = new(
                current.Steps.Count + 1,
                turn.TurnId,
                outcome,
                null,
                null,
                null,
                null,
                "none",
                "not-run-no-approved-edit",
                null,
                turn.ErrorCode,
                now);
            await PersistAsync(
                current with
                {
                    Status = outcome == "completed-without-proposal"
                        ? PiAgentReviewedIterationStatus.Completed
                        : outcome == "turn-aborted"
                            ? PiAgentReviewedIterationStatus.Stopped
                            : PiAgentReviewedIterationStatus.Faulted,
                    CurrentTurnId = null,
                    CurrentProposalId = null,
                    StatusDetail = outcome == "completed-without-proposal"
                        ? "Pi completed the mission step without requesting a write. The reviewed iteration is complete."
                        : outcome == "turn-aborted"
                            ? "The active reviewed turn was stopped. No continuation is admitted."
                            : "The reviewed Pi turn failed closed. Start a new owner policy after inspecting the transcript.",
                    Steps = current.Steps.Append(receipt).ToArray(),
                    UpdatedAtUtc = now,
                },
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<PiAgentReviewedIterationDecisionResult>
        ApproveAndContinueAsync(
            string proposalId,
            CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot current =
                RequirePendingProposal(proposalId);
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.DecisionInFlight,
                    StatusDetail =
                        "Applying the owner's one-shot exact-hash decision. Pi has no decision authority.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);

            PiAgentWorkspaceEditSnapshot edit =
                await conversation.ApplyWorkspaceEditAsync(
                    proposalId,
                    cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (
                edit.Status != PiAgentWorkspaceEditStatus.Applied ||
                edit.AfterSha256 is null)
            {
                PiAgentReviewedIterationSnapshot failed = await RecordDecisionAsync(
                    RequireSnapshot(),
                    edit,
                    "approval-failed",
                    "approved",
                    "not-run-edit-not-applied",
                    null,
                    edit.ErrorCode ?? "workspace-edit-not-applied",
                    PiAgentReviewedIterationStatus.Faulted,
                    "The one-shot edit decision did not apply exactly. The loop stopped without continuation.",
                    now,
                    cancellationToken);
                return new(edit, failed, null);
            }

            PiAgentReviewedIterationSnapshot validating = RequireSnapshot();
            await PersistAsync(
                validating with
                {
                    Status = PiAgentReviewedIterationStatus.Validating,
                    StatusDetail =
                        "The edit applied once. Checking fixed HEAD, exact path set, hashes, diff hygiene and structured text.",
                    UpdatedAtUtc = now,
                },
                cancellationToken);
            Dictionary<string, string> expectedFiles =
                BuildExpectedFiles(RequireSnapshot(), edit);
            PiAgentRepositoryValidationReceipt validation =
                await repositoryGate.ValidateAsync(
                    workspaceRoot,
                    validating.RepositoryHead,
                    expectedFiles,
                    cancellationToken);
            DateTimeOffset validationCompletedAtUtc = DateTimeOffset.UtcNow;
            if (!validation.Passed || validation.RepositoryDigest is null)
            {
                PiAgentReviewedIterationSnapshot failed = await RecordDecisionAsync(
                    RequireSnapshot(),
                    edit,
                    "approved-validation-failed",
                    "approved",
                    validation.Result,
                    null,
                    validation.ErrorCode ?? "repository-gate-failed",
                    PiAgentReviewedIterationStatus.Faulted,
                    "The approved edit remains visible, but the repository gate failed. No next Pi turn was started.",
                    validationCompletedAtUtc,
                    cancellationToken,
                    RequireSnapshot().ApprovedEditCount + 1);
                return new(edit, failed, null);
            }

            PiAgentReviewedIterationSnapshot currentAfterValidation =
                RequireSnapshot();
            int approvedCount =
                currentAfterValidation.ApprovedEditCount + 1;
            bool expired =
                validationCompletedAtUtc >=
                    currentAfterValidation.ExpiresAtUtc;
            PiAgentReviewedIterationStatus nextStatus = expired
                ? PiAgentReviewedIterationStatus.Expired
                : PiAgentReviewedIterationStatus.AwaitingTrustedValidation;
            string detail = expired
                ? "The repository gate passed, but the owner policy expired before trusted tests were authorized. No process was started."
                : "The repository gate passed. The edit is paused until the owner separately authorizes the pinned HEAD test profile once.";
            PiAgentReviewedIterationSnapshot passed = await RecordDecisionAsync(
                currentAfterValidation,
                edit,
                "approved-validated",
                "approved",
                "passed",
                validation.RepositoryDigest,
                null,
                nextStatus,
                detail,
                validationCompletedAtUtc,
                cancellationToken,
                approvedCount,
                expired ? "not-run-policy-expired" : "pending");
            return new(edit, passed, null);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<PiAgentTrustedValidationDecisionResult>
        RunTrustedValidationAndContinueAsync(
            CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource ownedCancellation = new();
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                ownedCancellation.Token);
        lock (validationOperationGate)
        {
            if (validationOperationCancellation is not null)
            {
                throw new InvalidOperationException(
                    "A trusted validation operation is already active.");
            }
            validationOperationCancellation = ownedCancellation;
        }
        bool operationGateHeld = false;
        try
        {
            await operationGate.WaitAsync(operationCancellation.Token);
            operationGateHeld = true;
            PiAgentReviewedIterationSnapshot current = RequireSnapshot();
            if (
                current.Status !=
                    PiAgentReviewedIterationStatus.AwaitingTrustedValidation ||
                current.SchemaVersion != 2 ||
                current.Steps.LastOrDefault()?.TrustedValidationResult !=
                    "pending")
            {
                throw new InvalidOperationException(
                    "The reviewed iteration is not awaiting a trusted validation decision.");
            }
            if (DateTimeOffset.UtcNow >= current.ExpiresAtUtc)
            {
                PiAgentReviewedIterationSnapshot expired =
                    await RecordTrustedValidationAsync(
                        current,
                        "not-run-policy-expired",
                        null,
                        null,
                        "trusted-validation-policy-expired",
                        PiAgentReviewedIterationStatus.Expired,
                        "The owner policy expired before trusted validation was authorized. No process was started.",
                        DateTimeOffset.UtcNow,
                        operationCancellation.Token);
                return new(null, expired, null);
            }
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus
                        .TrustedValidationInFlight,
                    StatusDetail =
                        "The owner authorized one fixed validation run. Rechecking repository receipts before Node starts.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                operationCancellation.Token);

            current = RequireSnapshot();
            Dictionary<string, string> expectedFiles =
                BuildExpectedFiles(current);
            PiAgentRepositoryValidationReceipt before =
                await repositoryGate.ValidateAsync(
                    workspaceRoot,
                    current.RepositoryHead,
                    expectedFiles,
                    operationCancellation.Token);
            if (!before.Passed || before.RepositoryDigest is null)
            {
                PiAgentReviewedIterationSnapshot failed =
                    await RecordTrustedValidationAsync(
                        current,
                        "not-run-repository-drift",
                        null,
                        null,
                        before.ErrorCode ??
                            "trusted-validation-pre-gate-failed",
                        PiAgentReviewedIterationStatus.Faulted,
                        "Trusted validation did not start because the repository no longer matched the reviewed receipts.",
                        DateTimeOffset.UtcNow,
                        operationCancellation.Token);
                return new(null, failed, null);
            }

            PiAgentTrustedValidationReceipt validation =
                await trustedValidator.RunAsync(
                    workspaceRoot,
                    current.RepositoryHead,
                    current.TrustedValidationProfileId ?? "",
                    current.TrustedValidationProfileDigest ?? "",
                    expectedFiles.Keys.ToArray(),
                    operationCancellation.Token);
            PiAgentRepositoryValidationReceipt after =
                await repositoryGate.ValidateAsync(
                    workspaceRoot,
                    current.RepositoryHead,
                    expectedFiles,
                    operationCancellation.Token);
            bool passed =
                validation.Passed &&
                after.Passed &&
                after.RepositoryDigest is not null;
            DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
            PiAgentReviewedIterationStatus nextStatus = !passed
                ? PiAgentReviewedIterationStatus.Faulted
                : completedAtUtc >= current.ExpiresAtUtc
                    ? PiAgentReviewedIterationStatus.Expired
                    : current.ApprovedEditCount >=
                        current.MaximumApprovedEdits
                        ? PiAgentReviewedIterationStatus.Completed
                        : PiAgentReviewedIterationStatus.ReadyToContinue;
            string detail = !validation.Passed
                ? "The fixed trusted test profile failed. The reviewed loop stopped without another Pi turn."
                : !after.Passed
                    ? "The tests exited successfully, but post-run repository revalidation detected drift. The loop stopped closed."
                    : nextStatus == PiAgentReviewedIterationStatus.Expired
                        ? "Trusted tests passed and the repository remained exact, but the owner policy expired before another turn."
                        : nextStatus == PiAgentReviewedIterationStatus.Completed
                            ? "Trusted tests passed and the approved-edit limit is complete."
                            : "Trusted tests passed and the repository remained exact. The next bounded Pi turn may begin.";
            string? errorCode = passed
                ? null
                : validation.ErrorCode ??
                    after.ErrorCode ??
                    "trusted-validation-post-gate-failed";
            PiAgentReviewedIterationSnapshot recorded =
                await RecordTrustedValidationAsync(
                    current,
                    passed ? "passed" : "failed",
                    validation,
                    after.RepositoryDigest,
                    errorCode,
                    nextStatus,
                    detail,
                    completedAtUtc,
                    operationCancellation.Token);
            if (nextStatus != PiAgentReviewedIterationStatus.ReadyToContinue)
            {
                return new(validation, recorded, null);
            }
            PiAgentConversationTurn nextTurn =
                await BeginNextTurnLockedAsync(operationCancellation.Token);
            return new(validation, RequireSnapshot(), nextTurn);
        }
        finally
        {
            if (operationGateHeld)
            {
                operationGate.Release();
            }
            lock (validationOperationGate)
            {
                if (ReferenceEquals(
                        validationOperationCancellation,
                        ownedCancellation))
                {
                    validationOperationCancellation = null;
                }
            }
        }
    }

    public async Task<PiAgentReviewedIterationDecisionResult> RejectAsync(
        string proposalId,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot current =
                RequirePendingProposal(proposalId);
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.DecisionInFlight,
                    StatusDetail =
                        "Consuming the proposal without writing, then stopping this owner policy.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
            PiAgentWorkspaceEditSnapshot edit =
                await conversation.RejectWorkspaceEditAsync(
                    proposalId,
                    cancellationToken);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PiAgentReviewedIterationSnapshot result = await RecordDecisionAsync(
                RequireSnapshot(),
                edit,
                edit.Status == PiAgentWorkspaceEditStatus.Rejected
                    ? "rejected-no-write"
                    : "rejection-failed",
                "rejected",
                "not-run-no-approved-edit",
                null,
                edit.ErrorCode,
                edit.Status == PiAgentWorkspaceEditStatus.Rejected
                    ? PiAgentReviewedIterationStatus.Stopped
                    : PiAgentReviewedIterationStatus.Faulted,
                edit.Status == PiAgentWorkspaceEditStatus.Rejected
                    ? "The owner rejected the proposal without writing. The reviewed iteration stopped."
                    : "Proposal rejection failed closed. Restart the session before more work.",
                now,
                cancellationToken);
            return new(edit, result, null);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<PiAgentConversationTurn?> ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot current = RequireSnapshot();
            if (
                current.Status !=
                    PiAgentReviewedIterationStatus.Interrupted ||
                !conversation.Snapshot.CanSubmit)
            {
                throw new InvalidOperationException(
                    "The reviewed iteration is not ready for explicit re-arm.");
            }
            if (DateTimeOffset.UtcNow >= current.ExpiresAtUtc)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Expired,
                        StatusDetail =
                            "The stored owner policy expired before re-arm. No continuation was admitted.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
                throw new InvalidOperationException(
                    "The reviewed iteration owner policy expired.");
            }
            if (current.SchemaVersion != 2)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Faulted,
                        StatusDetail =
                            "This stored policy predates separate trusted validation approval. Start a new clean-HEAD policy.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
                throw new InvalidOperationException(
                    "A legacy reviewed iteration policy cannot be re-armed.");
            }
            PiAgentRepositoryValidationReceipt validation =
                await repositoryGate.ValidateAsync(
                    workspaceRoot,
                    current.RepositoryHead,
                    BuildExpectedFiles(current),
                    cancellationToken);
            if (!validation.Passed || validation.RepositoryDigest is null)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Faulted,
                        StatusDetail =
                            "Re-arm failed because repository state no longer matches the durable receipts.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
                throw new InvalidOperationException(
                    "The durable reviewed iteration failed repository revalidation.");
            }
            if (DateTimeOffset.UtcNow >= current.ExpiresAtUtc)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Expired,
                        StatusDetail =
                            "Repository receipts revalidated after the owner policy expired. No continuation was admitted.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
                throw new InvalidOperationException(
                    "The reviewed iteration owner policy expired during repository revalidation.");
            }
            PiAgentTrustedValidationProfileReceipt trustedProfile =
                await trustedValidator.CaptureProfileAsync(
                    workspaceRoot,
                    current.RepositoryHead,
                    cancellationToken);
            if (
                trustedProfile.ProfileId !=
                    current.TrustedValidationProfileId ||
                trustedProfile.ProfileDigest !=
                    current.TrustedValidationProfileDigest)
            {
                await PersistAsync(
                    current with
                    {
                        Status = PiAgentReviewedIterationStatus.Faulted,
                        StatusDetail =
                            "Re-arm failed because the pinned trusted validation profile no longer matches its durable receipt.",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
                throw new InvalidOperationException(
                    "The trusted validation profile failed durable revalidation.");
            }
            bool validationPending =
                current.Steps.LastOrDefault()?.TrustedValidationResult ==
                    "pending";
            await PersistAsync(
                current with
                {
                    Status = validationPending
                        ? PiAgentReviewedIterationStatus
                            .AwaitingTrustedValidation
                        : PiAgentReviewedIterationStatus.ReadyToContinue,
                    RepositoryDigest = validation.RepositoryDigest,
                    StatusDetail = validationPending
                        ? "Repository and profile receipts revalidated. The owner may now separately authorize the pending fixed test run once."
                        : "Repository and profile receipts revalidated. The owner explicitly re-armed one bounded continuation.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
            if (validationPending)
            {
                return null;
            }
            return await BeginNextTurnLockedAsync(cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        CancelTrustedValidationOperation();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot current = RequireSnapshot();
            if (current.IsTerminal)
            {
                return;
            }
            if (
                current.Status ==
                    PiAgentReviewedIterationStatus.AwaitingOwnerReview &&
                current.CurrentProposalId is not null)
            {
                PiAgentWorkspaceEditSnapshot edit =
                    await conversation.RejectWorkspaceEditAsync(
                        current.CurrentProposalId,
                        cancellationToken);
                _ = await RecordDecisionAsync(
                    current,
                    edit,
                    "owner-stopped-rejected-no-write",
                    "rejected",
                    "not-run-no-approved-edit",
                    null,
                    edit.ErrorCode,
                    edit.Status == PiAgentWorkspaceEditStatus.Rejected
                        ? PiAgentReviewedIterationStatus.Stopped
                        : PiAgentReviewedIterationStatus.Faulted,
                    edit.Status == PiAgentWorkspaceEditStatus.Rejected
                        ? "The owner stopped the loop and consumed the pending proposal without writing."
                        : "The owner stop failed closed while discarding the pending proposal.",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                return;
            }
            if (
                current.Status ==
                    PiAgentReviewedIterationStatus.ActiveTurn)
            {
                _ = await conversation.CancelActiveTurnAsync(
                    cancellationToken);
            }
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.Stopped,
                    CurrentTurnId = null,
                    CurrentProposalId = null,
                    StatusDetail =
                        "The owner stopped the reviewed iteration. No further turn or write is admitted.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task SuspendAsync(
        CancellationToken cancellationToken = default)
    {
        CancelTrustedValidationOperation();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            PiAgentReviewedIterationSnapshot? current = Snapshot;
            if (current is null || current.IsTerminal)
            {
                return;
            }
            if (
                current.Status ==
                    PiAgentReviewedIterationStatus.Interrupted &&
                current.CurrentTurnId is null &&
                current.CurrentProposalId is null)
            {
                return;
            }
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.Interrupted,
                    CurrentTurnId = null,
                    CurrentProposalId = null,
                    StatusDetail =
                        "Desktop shutdown suspended the owner policy. Restart requires repository validation and explicit re-arm.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<PiAgentConversationTurn> BeginNextTurnLockedAsync(
        CancellationToken cancellationToken)
    {
        PiAgentReviewedIterationSnapshot current = RequireSnapshot();
        if (DateTimeOffset.UtcNow >= current.ExpiresAtUtc)
        {
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.Expired,
                    CurrentTurnId = null,
                    CurrentProposalId = null,
                    StatusDetail =
                        "The owner policy expired before another bounded turn could begin.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
            throw new InvalidOperationException(
                "The reviewed iteration owner policy expired.");
        }
        if (
            current.Status !=
                PiAgentReviewedIterationStatus.ReadyToContinue ||
            current.IsTerminal ||
            !conversation.Snapshot.CanSubmit)
        {
            throw new InvalidOperationException(
                "The reviewed iteration cannot begin another bounded turn.");
        }
        int stepNumber = current.ApprovedEditCount + 1;
        string turnId = $"iteration-turn-{Guid.NewGuid():N}";
        PiAgentReviewedIterationSnapshot active = current with
        {
            Status = PiAgentReviewedIterationStatus.ActiveTurn,
            CurrentStepNumber = stepNumber,
            CurrentTurnId = turnId,
            CurrentProposalId = null,
            StatusDetail =
                $"Pi is working on reviewed step {stepNumber}. It may stage one exact replacement, one 2-8 hunk single-file patch, or one new UTF-8 file and cannot approve it.",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await PersistAsync(active, cancellationToken);
        try
        {
            return await conversation.SubmitAsync(
                BuildPrompt(active),
                turnId,
                cancellationToken);
        }
        catch
        {
            await PersistAsync(
                active with
                {
                    Status = PiAgentReviewedIterationStatus.Faulted,
                    CurrentTurnId = null,
                    StatusDetail =
                        "The desktop could not start the reviewed Pi turn. No mutation capability was issued.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                CancellationToken.None);
            throw;
        }
    }

    private static string BuildPrompt(
        PiAgentReviewedIterationSnapshot iteration)
    {
        StringBuilder prompt = new();
        prompt.AppendLine("[DESKTOP REVIEWED ITERATION OWNER POLICY]");
        prompt.Append("Mission: ").AppendLine(iteration.Mission);
        prompt.Append("Step: ")
            .Append(iteration.CurrentStepNumber)
            .Append(" of ")
            .AppendLine(iteration.MaximumApprovedEdits.ToString());
        prompt.Append("Pinned Git HEAD: ")
            .AppendLine(iteration.RepositoryHead);
        prompt.AppendLine(
            "Inspect with read, grep, find and ls. Choose the smallest coherent next improvement.");
        prompt.AppendLine(
            "If one text mutation advances the mission, call exactly one proposal tool: propose_edit for one exact replacement, propose_patch for 2-8 distinct non-overlapping exact replacements in one existing UTF-8 file, or propose_create_file for one missing UTF-8 file whose parent directory already exists. Then explain the evidence and expected validation.");
        prompt.AppendLine(
            "If neither safe proposal is appropriate or the mission is complete, do not propose a mutation; explain the completion or blocker.");
        prompt.AppendLine(
            "You cannot approve, commit, run shell commands or tests, change policy, or bypass the desktop repository and separately owner-approved trusted validation gates.");
        return prompt.ToString();
    }

    private async Task<PiAgentReviewedIterationSnapshot> RecordDecisionAsync(
        PiAgentReviewedIterationSnapshot current,
        PiAgentWorkspaceEditSnapshot edit,
        string outcome,
        string ownerDecision,
        string validationResult,
        string? repositoryDigest,
        string? errorCode,
        PiAgentReviewedIterationStatus status,
        string detail,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken,
        int? approvedEditCount = null,
        string trustedValidationResult = "not-run")
    {
        string turnId = current.CurrentTurnId ??
            throw new InvalidOperationException(
                "The reviewed iteration decision omitted its producing turn.");
        PiAgentReviewedIterationStepReceipt receipt = new(
            current.Steps.Count + 1,
            turnId,
            outcome,
            edit.ProposalId,
            edit.RelativePath,
            edit.BeforeSha256,
            edit.AfterSha256,
            ownerDecision,
            validationResult,
            repositoryDigest,
            errorCode,
            recordedAtUtc)
        {
            TrustedValidationResult = trustedValidationResult,
        };
        PiAgentReviewedIterationSnapshot next = current with
        {
            Status = status,
            CurrentTurnId = null,
            CurrentProposalId = null,
            ApprovedEditCount =
                approvedEditCount ?? current.ApprovedEditCount,
            RepositoryDigest =
                repositoryDigest ?? current.RepositoryDigest,
            StatusDetail = detail,
            Steps = current.Steps.Append(receipt).ToArray(),
            UpdatedAtUtc = recordedAtUtc,
        };
        await PersistAsync(next, cancellationToken);
        return next;
    }

    private async Task<PiAgentReviewedIterationSnapshot>
        RecordTrustedValidationAsync(
            PiAgentReviewedIterationSnapshot current,
            string result,
            PiAgentTrustedValidationReceipt? validation,
            string? repositoryDigest,
            string? errorCode,
            PiAgentReviewedIterationStatus status,
            string detail,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
    {
        if (
            current.Steps.Count == 0 ||
            current.Steps[^1].TrustedValidationResult != "pending")
        {
            throw new InvalidOperationException(
                "The trusted validation receipt has no pending reviewed edit.");
        }
        PiAgentReviewedIterationStepReceipt updatedStep =
            current.Steps[^1] with
            {
                TrustedValidationResult = result,
                TrustedValidationReceiptDigest =
                    validation?.ReceiptDigest,
                TrustedValidationOutputDigest =
                    validation?.OutputDigest,
                TrustedValidationExitCode = validation?.ExitCode,
                TrustedValidationCompletedAtUtc =
                    validation is null ? null : completedAtUtc,
                ErrorCode = errorCode,
                RepositoryDigest =
                    repositoryDigest ?? current.Steps[^1].RepositoryDigest,
            };
        PiAgentReviewedIterationSnapshot next = current with
        {
            Status = status,
            RepositoryDigest =
                repositoryDigest ?? current.RepositoryDigest,
            StatusDetail = detail,
            Steps = current.Steps
                .Take(current.Steps.Count - 1)
                .Append(updatedStep)
                .ToArray(),
            UpdatedAtUtc = completedAtUtc,
        };
        await PersistAsync(next, cancellationToken);
        return RequireSnapshot();
    }

    private static Dictionary<string, string> BuildExpectedFiles(
        PiAgentReviewedIterationSnapshot current,
        PiAgentWorkspaceEditSnapshot? newlyApplied = null)
    {
        Dictionary<string, string> files =
            new(StringComparer.Ordinal);
        foreach (PiAgentReviewedIterationStepReceipt step in current.Steps)
        {
            if (
                step.OwnerDecision == "approved" &&
                step.RelativePath is not null &&
                step.AfterSha256 is not null)
            {
                files[step.RelativePath] = step.AfterSha256;
            }
        }
        if (
            newlyApplied is not null &&
            newlyApplied.Status == PiAgentWorkspaceEditStatus.Applied &&
            newlyApplied.AfterSha256 is not null)
        {
            files[newlyApplied.RelativePath] = newlyApplied.AfterSha256;
        }
        return files;
    }

    private PiAgentReviewedIterationSnapshot RequirePendingProposal(
        string proposalId)
    {
        PiAgentReviewedIterationSnapshot current = RequireSnapshot();
        if (
            current.Status !=
                PiAgentReviewedIterationStatus.AwaitingOwnerReview ||
            current.CurrentProposalId != proposalId)
        {
            throw new InvalidOperationException(
                "The proposal is not the current reviewed iteration owner decision.");
        }
        return current;
    }

    private void CancelTrustedValidationOperation()
    {
        lock (validationOperationGate)
        {
            try
            {
                validationOperationCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        trustedValidator.CancelActiveRun();
    }

    private PiAgentReviewedIterationSnapshot RequireSnapshot() =>
        Snapshot ?? throw new InvalidOperationException(
            "No reviewed iteration policy is retained.");

    private async Task PersistAsync(
        PiAgentReviewedIterationSnapshot next,
        CancellationToken cancellationToken,
        bool publish = true)
    {
        PiAgentReviewedIterationSnapshot? previous = Snapshot;
        int revision =
            previous is not null &&
            previous.IterationId == next.IterationId
                ? previous.Revision + 1
                : 1;
        PiAgentReviewedIterationSnapshot admitted =
            PiAgentReviewedIterationAdmission.Admit(
                next with { Revision = revision });
        _ = await store.SaveAsync(
            workspaceRoot,
            admitted,
            cancellationToken);
        lock (snapshotGate)
        {
            snapshot = admitted;
        }
        if (publish)
        {
            Publish(admitted);
        }
    }

    private void Publish(PiAgentReviewedIterationSnapshot? next)
    {
        EventHandler<
            PiAgentReviewedIterationSnapshotChangedEventArgs>? handlers =
            SnapshotChanged;
        if (handlers is null)
        {
            return;
        }
        PiAgentReviewedIterationSnapshotChangedEventArgs eventArgs =
            new(next);
        foreach (EventHandler<
                     PiAgentReviewedIterationSnapshotChangedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
            }
        }
    }

    private static void ValidateMission(string mission)
    {
        if (
            string.IsNullOrWhiteSpace(mission) ||
            Encoding.UTF8.GetByteCount(mission) >
                PiAgentReviewedIterationAdmission.MaximumMissionBytes)
        {
            throw new ArgumentException(
                "A reviewed iteration mission must be 1-4096 UTF-8 bytes.",
                nameof(mission));
        }
    }
}
