using System.Text;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentReviewedIterationDecisionResult(
    PiAgentWorkspaceEditSnapshot Edit,
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
        "desktop-auto-continue-after-owner-approved-edit-and-fixed-gate";
    public const string ApprovalModel =
        "desktop-owner-one-shot-per-edit-no-model-decision-authority";

    private readonly PiAgentConversationState conversation;
    private readonly string workspaceRoot;
    private readonly PiAgentReviewedIterationStore store;
    private readonly PiAgentReviewedIterationRepositoryGate repositoryGate;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object snapshotGate = new();
    private PiAgentReviewedIterationSnapshot? snapshot;

    private PiAgentReviewedIterationCoordinator(
        PiAgentConversationState conversation,
        string workspaceRoot,
        PiAgentReviewedIterationStore store,
        PiAgentReviewedIterationRepositoryGate repositoryGate,
        PiAgentReviewedIterationSnapshot? snapshot)
    {
        this.conversation = conversation;
        this.workspaceRoot = workspaceRoot;
        this.store = store;
        this.repositoryGate = repositoryGate;
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
        PiAgentReviewedIterationStore? store = null,
        PiAgentReviewedIterationRepositoryGate? repositoryGate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        string canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        PiAgentReviewedIterationStore admittedStore =
            store ?? new PiAgentReviewedIterationStore();
        PiAgentReviewedIterationSnapshot? restored =
            await admittedStore.LoadLatestAsync(
                canonicalRoot,
                cancellationToken);
        PiAgentReviewedIterationCoordinator coordinator = new(
            conversation,
            canonicalRoot,
            admittedStore,
            repositoryGate ??
                new PiAgentReviewedIterationRepositoryGate(),
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
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            PiAgentReviewedIterationSnapshot next = new(
                1,
                1,
                $"review-loop-{startedAtUtc:yyyyMMddHHmmssfff}-" +
                    Guid.NewGuid().ToString("N")[..16],
                mission.Trim(),
                MaximumApprovedEdits,
                startedAtUtc,
                startedAtUtc.AddHours(PolicyLifetimeHours),
                baseline.Head,
                baseline.ValidationProfile,
                true,
                PiAgentReviewedIterationStatus.ReadyToContinue,
                1,
                null,
                null,
                0,
                baseline.RepositoryDigest,
                "Owner policy armed from a clean Git HEAD. Preparing the first bounded Pi turn.",
                [],
                startedAtUtc);
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
            bool limitReached =
                approvedCount >= currentAfterValidation.MaximumApprovedEdits;
            bool expired =
                validationCompletedAtUtc >=
                    currentAfterValidation.ExpiresAtUtc;
            PiAgentReviewedIterationStatus nextStatus = expired
                ? PiAgentReviewedIterationStatus.Expired
                : limitReached
                    ? PiAgentReviewedIterationStatus.Completed
                    : PiAgentReviewedIterationStatus.ReadyToContinue;
            string detail = expired
                ? "The repository gate passed, but the owner policy expired before another turn could begin."
                : limitReached
                    ? "The repository gate passed and the owner policy reached its approved-edit limit."
                    : "The repository gate passed. The desktop may continue to the next bounded proposal turn.";
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
                approvedCount);
            if (nextStatus != PiAgentReviewedIterationStatus.ReadyToContinue)
            {
                return new(edit, passed, null);
            }

            PiAgentConversationTurn nextTurn =
                await BeginNextTurnLockedAsync(cancellationToken);
            return new(edit, RequireSnapshot(), nextTurn);
        }
        finally
        {
            operationGate.Release();
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

    public async Task<PiAgentConversationTurn> ResumeAsync(
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
            await PersistAsync(
                current with
                {
                    Status = PiAgentReviewedIterationStatus.ReadyToContinue,
                    RepositoryDigest = validation.RepositoryDigest,
                    StatusDetail =
                        "Repository receipts revalidated. The owner explicitly re-armed one bounded continuation.",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                },
                cancellationToken);
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
            "You cannot approve, commit, run shell commands, change policy, or bypass the desktop repository gate.");
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
        int? approvedEditCount = null)
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
            recordedAtUtc);
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
