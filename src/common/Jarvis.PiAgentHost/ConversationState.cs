using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.PiAgentHost;

public enum PiAgentConversationTurnStatus
{
    Starting,
    Running,
    Completed,
    Aborted,
    Failed,
}

public enum PiAgentConversationToolStatus
{
    Running,
    Completed,
    Failed,
}

public enum PiAgentWorkspaceEditStatus
{
    Pending,
    Applying,
    Applied,
    Rejecting,
    Rejected,
    Expired,
    Drifted,
    Failed,
}

public sealed record PiAgentConversationToolSnapshot(
    string ToolCallId,
    string ToolName,
    PiAgentConversationToolStatus Status,
    int StartedSequence,
    int? CompletedSequence);

public sealed record PiAgentWorkspaceEditSnapshot(
    int SchemaVersion,
    string ProposalId,
    string RelativePath,
    string BeforeSha256,
    string OldText,
    string NewText,
    PiAgentWorkspaceEditStatus Status,
    string? AfterSha256,
    string? ErrorCode)
{
    public bool CanDecide => Status == PiAgentWorkspaceEditStatus.Pending;
    public string StatusLabel => Status switch
    {
        PiAgentWorkspaceEditStatus.Pending => "OWNER REVIEW REQUIRED",
        PiAgentWorkspaceEditStatus.Applying => "APPLYING ONCE",
        PiAgentWorkspaceEditStatus.Applied => "APPLIED",
        PiAgentWorkspaceEditStatus.Rejecting => "REJECTING",
        PiAgentWorkspaceEditStatus.Rejected => "REJECTED / NO WRITE",
        PiAgentWorkspaceEditStatus.Expired => "EXPIRED ON SHUTDOWN / NO WRITE",
        PiAgentWorkspaceEditStatus.Drifted => "DRIFTED / NO WRITE",
        PiAgentWorkspaceEditStatus.Failed => "FAILED CLOSED",
        _ => "UNKNOWN",
    };
    public string BeforeHashLabel => $"BEFORE  {BeforeSha256}";
    public string AfterHashLabel => AfterSha256 is null
        ? Status switch
        {
            PiAgentWorkspaceEditStatus.Pending =>
                "AFTER   PENDING OWNER DECISION",
            PiAgentWorkspaceEditStatus.Rejected =>
                "AFTER   UNCHANGED BY JARVIS",
            PiAgentWorkspaceEditStatus.Expired =>
                "AFTER   SESSION CLOSED WITHOUT WRITE",
            PiAgentWorkspaceEditStatus.Drifted =>
                "AFTER   EXTERNAL DRIFT PRESERVED",
            _ => "AFTER   NOT COMMITTED",
        }
        : $"AFTER   {AfterSha256}";
}

public sealed record PiAgentConversationTurnSnapshot(
    string TurnId,
    string UserText,
    string AssistantText,
    PiAgentConversationTurnStatus Status,
    int LastEventSequence,
    bool CancelRequested,
    IReadOnlyList<PiAgentConversationToolSnapshot> Tools,
    IReadOnlyList<PiAgentWorkspaceEditSnapshot> WorkspaceEdits,
    string? ErrorCode);

public sealed record PiAgentConversationSnapshot(
    int Revision,
    string? ActiveTurnId,
    bool CanSubmit,
    bool CanCancel,
    IReadOnlyList<PiAgentConversationTurnSnapshot> Turns);

public sealed class PiAgentConversationSnapshotChangedEventArgs(
    PiAgentConversationSnapshot snapshot) : EventArgs
{
    public PiAgentConversationSnapshot Snapshot { get; } = snapshot;
}

public sealed record PiAgentConversationTurn(
    string TurnId,
    Task<PiAgentConversationTurnSnapshot> Completion);

public sealed record PiAgentConversationCheckpointTurn(
    string TurnId,
    string UserText,
    string AssistantText);

public sealed record PiAgentConversationCheckpoint(
    int SchemaVersion,
    IReadOnlyList<PiAgentConversationCheckpointTurn> Turns);

public sealed class PiAgentConversationState
{
    public const int MaximumRetainedTurns = 128;
    public const int MaximumAssistantCharacters = 262_144;
    public const int MaximumCheckpointTurns = 32;
    public const int MaximumCheckpointBytes = 32_768;
    public const int MaximumCheckpointTextBytes = 16_384;

    private sealed class MutableTool
    {
        public required string ToolCallId { get; init; }
        public required string ToolName { get; init; }
        public required int StartedSequence { get; init; }
        public PiAgentConversationToolStatus Status { get; set; } =
            PiAgentConversationToolStatus.Running;
        public int? CompletedSequence { get; set; }
    }

    private sealed class MutableTurn
    {
        public required string TurnId { get; init; }
        public required string UserText { get; init; }
        public StringBuilder AssistantText { get; } = new();
        public List<MutableTool> Tools { get; } = [];
        public List<MutableWorkspaceEdit> WorkspaceEdits { get; } = [];
        public PiAgentConversationTurnStatus Status { get; set; } =
            PiAgentConversationTurnStatus.Starting;
        public int LastEventSequence { get; set; }
        public bool CancelRequested { get; set; }
        public string? ErrorCode { get; set; }
    }

    private sealed class MutableWorkspaceEdit
    {
        public required int SchemaVersion { get; init; }
        public required string ProposalId { get; init; }
        public required string RelativePath { get; init; }
        public required string BeforeSha256 { get; init; }
        public required string OldText { get; init; }
        public required string NewText { get; init; }
        public PiAgentWorkspaceEditStatus Status { get; set; } =
            PiAgentWorkspaceEditStatus.Pending;
        public string? AfterSha256 { get; set; }
        public string? ErrorCode { get; set; }
    }

    private static readonly Regex TurnIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\z",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions
        CheckpointSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    private readonly object gate = new();
    private readonly PiAgentSidecarController controller;
    private readonly SynchronizationContext? notificationContext;
    private readonly List<MutableTurn> turns = [];
    private string? activeTurnId;
    private int revision;
    private int generatedTurnSequence;
    private int abortRequestSequence;
    private int workspaceEditDecisionSequence;
    private bool acceptingSubmissions = true;
    private string? pendingWorkspaceEditId;
    private Task workspaceEditDecisionTask = Task.CompletedTask;
    private TaskCompletionSource<bool> idleCompletion =
        CreateCompletedIdleSource();

    public PiAgentConversationState(
        PiAgentSidecarController controller,
        SynchronizationContext? notificationContext = null,
        PiAgentConversationCheckpoint? checkpoint = null)
    {
        this.controller = controller ??
            throw new ArgumentNullException(nameof(controller));
        this.notificationContext = notificationContext;
        PiAgentConversationCheckpoint? admittedCheckpoint =
            AdmitCheckpoint(checkpoint);
        if (admittedCheckpoint is null)
        {
            return;
        }

        foreach (PiAgentConversationCheckpointTurn restored in
                 admittedCheckpoint.Turns)
        {
            MutableTurn turn = new()
            {
                TurnId = restored.TurnId,
                UserText = restored.UserText,
                Status = PiAgentConversationTurnStatus.Completed,
            };
            turn.AssistantText.Append(restored.AssistantText);
            turns.Add(turn);
            RestoreGeneratedTurnSequence(restored.TurnId);
        }
        if (turns.Count != 0)
        {
            revision = 1;
        }
    }

    public event EventHandler<
        PiAgentConversationSnapshotChangedEventArgs>? SnapshotChanged;
    internal event Action<PiAgentConversationCheckpoint>?
        TerminalCheckpointAvailable;

    public PiAgentConversationSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return BuildSnapshotLocked();
            }
        }
    }

    public PiAgentConversationCheckpoint ExportCheckpoint()
    {
        lock (gate)
        {
            PiAgentConversationCheckpointTurn[] completed = turns
                .Where(turn =>
                    turn.Status ==
                        PiAgentConversationTurnStatus.Completed)
                .Select(turn =>
                    new PiAgentConversationCheckpointTurn(
                        turn.TurnId,
                        turn.UserText,
                        turn.AssistantText.ToString()))
                .ToArray();
            List<PiAgentConversationCheckpointTurn> selected = [];
            for (
                int index = completed.Length - 1;
                index >= 0 &&
                    selected.Count < MaximumCheckpointTurns;
                index--)
            {
                PiAgentConversationCheckpointTurn candidate =
                    completed[index];
                if (
                    !IsValidCheckpointText(candidate.UserText) ||
                    !IsValidCheckpointText(candidate.AssistantText))
                {
                    break;
                }
                selected.Insert(0, candidate);
                PiAgentConversationCheckpoint draft = new(
                    1,
                    selected);
                if (
                    GetCheckpointByteCount(draft) >
                        MaximumCheckpointBytes)
                {
                    selected.RemoveAt(0);
                    break;
                }
            }
            return new PiAgentConversationCheckpoint(
                1,
                selected.ToArray());
        }
    }

    internal static PiAgentConversationCheckpoint? AdmitCheckpoint(
        PiAgentConversationCheckpoint? checkpoint)
    {
        if (checkpoint is null)
        {
            return null;
        }
        if (
            checkpoint.SchemaVersion != 1 ||
            checkpoint.Turns is null ||
            checkpoint.Turns.Count > MaximumCheckpointTurns)
        {
            throw new ArgumentException(
                "Conversation checkpoint schema or turn count is invalid.",
                nameof(checkpoint));
        }

        HashSet<string> turnIds = new(StringComparer.Ordinal);
        List<PiAgentConversationCheckpointTurn> admitted =
            new(checkpoint.Turns.Count);
        foreach (PiAgentConversationCheckpointTurn? turn in
                 checkpoint.Turns)
        {
            if (
                turn is null ||
                !TurnIdPattern.IsMatch(turn.TurnId) ||
                !turnIds.Add(turn.TurnId) ||
                !IsValidCheckpointText(turn.UserText) ||
                !IsValidCheckpointText(turn.AssistantText))
            {
                throw new ArgumentException(
                    "Conversation checkpoint contains an invalid text turn.",
                    nameof(checkpoint));
            }
            admitted.Add(
                new PiAgentConversationCheckpointTurn(
                    turn.TurnId,
                    turn.UserText,
                    turn.AssistantText));
        }

        PiAgentConversationCheckpoint normalized = new(
            1,
            admitted.ToArray());
        if (
            GetCheckpointByteCount(normalized) >
                MaximumCheckpointBytes)
        {
            throw new ArgumentException(
                "Conversation checkpoint exceeds its UTF-8 byte limit.",
                nameof(checkpoint));
        }
        return normalized;
    }

    public async Task<PiAgentConversationTurn> SubmitAsync(
        string text,
        string? turnId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateText(text);
        string admittedTurnId =
            turnId ??
            $"desktop-turn-{Interlocked.Increment(
                ref generatedTurnSequence):D8}";
        ValidateTurnId(admittedTurnId);

        PiAgentConversationSnapshot startingSnapshot;
        lock (gate)
        {
            if (!acceptingSubmissions)
            {
                throw new InvalidOperationException(
                    "The desktop conversation is quiescing.");
            }
            if (activeTurnId is not null)
            {
                throw new InvalidOperationException(
                    "The desktop conversation already has an active turn.");
            }
            if (turns.Any(turn =>
                    turn.TurnId == admittedTurnId))
            {
                throw new InvalidOperationException(
                    "The desktop conversation turn id is already retained.");
            }
            TrimTerminalTurnsLocked();
            turns.Add(
                new MutableTurn
                {
                    TurnId = admittedTurnId,
                    UserText = text,
                });
            activeTurnId = admittedTurnId;
            idleCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            startingSnapshot = AdvanceSnapshotLocked();
        }
        Publish(startingSnapshot);

        PiAgentTurnHandle handle;
        try
        {
            handle = await controller.StartTurnAsync(
                text,
                admittedTurnId,
                cancellationToken);
        }
        catch
        {
            PiAgentConversationSnapshot failedSnapshot;
            lock (gate)
            {
                MutableTurn turn = FindTurnLocked(admittedTurnId);
                turn.Status = PiAgentConversationTurnStatus.Failed;
                turn.ErrorCode = "turn-start-failed";
                activeTurnId = null;
                idleCompletion.TrySetResult(true);
                failedSnapshot = AdvanceSnapshotLocked();
            }
            Publish(failedSnapshot);
            throw;
        }

        PiAgentConversationSnapshot runningSnapshot;
        lock (gate)
        {
            MutableTurn turn = FindTurnLocked(admittedTurnId);
            turn.Status = PiAgentConversationTurnStatus.Running;
            runningSnapshot = AdvanceSnapshotLocked();
        }
        Publish(runningSnapshot);

        Task<PiAgentConversationTurnSnapshot> completion =
            ConsumeTurnAsync(handle);
        return new PiAgentConversationTurn(
            admittedTurnId,
            completion);
    }

    public async Task<bool> CancelActiveTurnAsync(
        CancellationToken cancellationToken = default)
    {
        string turnId;
        PiAgentConversationSnapshot requestedSnapshot;
        lock (gate)
        {
            if (activeTurnId is null)
            {
                return false;
            }
            MutableTurn turn = FindTurnLocked(activeTurnId);
            if (turn.CancelRequested)
            {
                return false;
            }
            turn.CancelRequested = true;
            turnId = activeTurnId;
            requestedSnapshot = AdvanceSnapshotLocked();
        }
        Publish(requestedSnapshot);

        try
        {
            int requestSequence = Interlocked.Increment(
                ref abortRequestSequence);
            await controller.AbortTurnAsync(
                turnId,
                $"desktop-abort-{requestSequence:D8}",
                cancellationToken);
            return true;
        }
        catch
        {
            PiAgentConversationSnapshot retrySnapshot;
            lock (gate)
            {
                MutableTurn turn = FindTurnLocked(turnId);
                if (!IsTerminal(turn.Status))
                {
                    turn.CancelRequested = false;
                }
                retrySnapshot = AdvanceSnapshotLocked();
            }
            Publish(retrySnapshot);
            throw;
        }
    }

    public async Task<PiAgentWorkspaceEditSnapshot>
        ApplyWorkspaceEditAsync(
            string proposalId,
            CancellationToken cancellationToken = default)
    {
        MutableWorkspaceEdit proposal;
        TaskCompletionSource<bool> decisionCompletion;
        PiAgentConversationSnapshot applyingSnapshot;
        lock (gate)
        {
            proposal = FindPendingWorkspaceEditLocked(proposalId);
            decisionCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            workspaceEditDecisionTask = decisionCompletion.Task;
            proposal.Status = PiAgentWorkspaceEditStatus.Applying;
            applyingSnapshot = AdvanceSnapshotLocked();
        }
        try
        {
            try
            {
                Publish(applyingSnapshot);
                int requestSequence = Interlocked.Increment(
                    ref workspaceEditDecisionSequence);
                PiAgentWorkspaceEditDecisionReceipt receipt =
                    await controller.CommitWorkspaceEditAsync(
                        proposal.ProposalId,
                        proposal.BeforeSha256,
                        $"desktop-apply-edit-{requestSequence:D8}",
                        cancellationToken);
                PiAgentConversationSnapshot appliedSnapshot;
                PiAgentWorkspaceEditSnapshot result;
                lock (gate)
                {
                    MutableWorkspaceEdit current =
                        FindWorkspaceEditLocked(proposalId);
                    if (
                        current.Status !=
                            PiAgentWorkspaceEditStatus.Applying ||
                        receipt.RelativePath != current.RelativePath ||
                        receipt.BeforeSha256 != current.BeforeSha256 ||
                        receipt.Status != "applied" ||
                        !receipt.MutationPerformed ||
                        receipt.AfterSha256 is null)
                    {
                        throw new InvalidOperationException(
                            "The applied workspace edit receipt diverged from conversation state.");
                    }
                    current.Status = PiAgentWorkspaceEditStatus.Applied;
                    current.AfterSha256 = receipt.AfterSha256;
                    pendingWorkspaceEditId = null;
                    appliedSnapshot = AdvanceSnapshotLocked();
                    result = ToSnapshot(current);
                }
                Publish(appliedSnapshot);
                return result;
            }
            catch (Exception exception)
            {
                PiAgentConversationSnapshot failedSnapshot;
                PiAgentWorkspaceEditSnapshot result;
                lock (gate)
                {
                    MutableWorkspaceEdit current =
                        FindWorkspaceEditLocked(proposalId);
                    string errorCode = exception is
                        PiAgentWorkspaceEditDecisionException decision
                            ? decision.ErrorCode
                            : "workspace-edit-approval-uncertain";
                    current.Status = errorCode == "workspace-edit-drifted"
                        ? PiAgentWorkspaceEditStatus.Drifted
                        : PiAgentWorkspaceEditStatus.Failed;
                    current.ErrorCode = errorCode;
                    pendingWorkspaceEditId = null;
                    if (current.Status == PiAgentWorkspaceEditStatus.Failed)
                    {
                        acceptingSubmissions = false;
                    }
                    failedSnapshot = AdvanceSnapshotLocked();
                    result = ToSnapshot(current);
                }
                Publish(failedSnapshot);
                return result;
            }
        }
        finally
        {
            decisionCompletion.TrySetResult(true);
        }
    }

    public async Task<PiAgentWorkspaceEditSnapshot>
        RejectWorkspaceEditAsync(
            string proposalId,
            CancellationToken cancellationToken = default)
    {
        MutableWorkspaceEdit proposal;
        TaskCompletionSource<bool> decisionCompletion;
        PiAgentConversationSnapshot rejectingSnapshot;
        lock (gate)
        {
            proposal = FindPendingWorkspaceEditLocked(proposalId);
            decisionCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            workspaceEditDecisionTask = decisionCompletion.Task;
            proposal.Status = PiAgentWorkspaceEditStatus.Rejecting;
            rejectingSnapshot = AdvanceSnapshotLocked();
        }
        try
        {
            try
            {
                Publish(rejectingSnapshot);
                int requestSequence = Interlocked.Increment(
                    ref workspaceEditDecisionSequence);
                PiAgentWorkspaceEditDecisionReceipt receipt =
                    await controller.DiscardWorkspaceEditAsync(
                        proposal.ProposalId,
                        proposal.BeforeSha256,
                        $"desktop-reject-edit-{requestSequence:D8}",
                        cancellationToken);
                PiAgentConversationSnapshot rejectedSnapshot;
                PiAgentWorkspaceEditSnapshot result;
                lock (gate)
                {
                    MutableWorkspaceEdit current =
                        FindWorkspaceEditLocked(proposalId);
                    if (
                        current.Status !=
                            PiAgentWorkspaceEditStatus.Rejecting ||
                        receipt.RelativePath != current.RelativePath ||
                        receipt.BeforeSha256 != current.BeforeSha256 ||
                        receipt.Status != "rejected" ||
                        receipt.MutationPerformed ||
                        receipt.AfterSha256 is not null)
                    {
                        throw new InvalidOperationException(
                            "The rejected workspace edit receipt diverged from conversation state.");
                    }
                    current.Status = PiAgentWorkspaceEditStatus.Rejected;
                    pendingWorkspaceEditId = null;
                    rejectedSnapshot = AdvanceSnapshotLocked();
                    result = ToSnapshot(current);
                }
                Publish(rejectedSnapshot);
                return result;
            }
            catch (Exception exception)
            {
                PiAgentConversationSnapshot failedSnapshot;
                PiAgentWorkspaceEditSnapshot result;
                lock (gate)
                {
                    MutableWorkspaceEdit current =
                        FindWorkspaceEditLocked(proposalId);
                    current.Status = PiAgentWorkspaceEditStatus.Failed;
                    current.ErrorCode = exception is
                        PiAgentWorkspaceEditDecisionException decision
                            ? decision.ErrorCode
                            : "workspace-edit-rejection-uncertain";
                    pendingWorkspaceEditId = null;
                    acceptingSubmissions = false;
                    failedSnapshot = AdvanceSnapshotLocked();
                    result = ToSnapshot(current);
                }
                Publish(failedSnapshot);
                return result;
            }
        }
        finally
        {
            decisionCompletion.TrySetResult(true);
        }
    }

    public async Task QuiesceAsync(
        CancellationToken cancellationToken = default)
    {
        PiAgentConversationSnapshot? quiescingSnapshot = null;
        Task idleTask;
        Task decisionTask;
        lock (gate)
        {
            bool snapshotChanged = false;
            if (acceptingSubmissions)
            {
                acceptingSubmissions = false;
                snapshotChanged = true;
            }
            if (pendingWorkspaceEditId is not null)
            {
                MutableWorkspaceEdit pending =
                    FindWorkspaceEditLocked(pendingWorkspaceEditId);
                if (pending.Status == PiAgentWorkspaceEditStatus.Pending)
                {
                    pending.Status = PiAgentWorkspaceEditStatus.Expired;
                    pendingWorkspaceEditId = null;
                    snapshotChanged = true;
                }
            }
            if (snapshotChanged)
            {
                quiescingSnapshot = AdvanceSnapshotLocked();
            }
            idleTask = idleCompletion.Task;
            decisionTask = workspaceEditDecisionTask;
        }
        if (quiescingSnapshot is not null)
        {
            Publish(quiescingSnapshot);
        }

        _ = await CancelActiveTurnAsync(cancellationToken);
        await idleTask.WaitAsync(cancellationToken);
        await decisionTask.WaitAsync(cancellationToken);
    }

    private async Task<PiAgentConversationTurnSnapshot> ConsumeTurnAsync(
        PiAgentTurnHandle handle)
    {
        try
        {
            await foreach (
                PiAgentTurnStreamEvent streamEvent in
                    handle.ReadEventsAsync())
            {
                PiAgentConversationSnapshot snapshot =
                    ApplyEvent(streamEvent);
                Publish(snapshot);
            }

            PiAgentTurnResult result = await handle.Completion;
            PiAgentConversationTurnSnapshot finalTurn;
            lock (gate)
            {
                MutableTurn turn = FindTurnLocked(handle.TurnId);
                if (
                    !IsTerminal(turn.Status) ||
                    turn.AssistantText.ToString() != result.Response)
                {
                    throw new InvalidOperationException(
                        "The desktop conversation terminal state diverged.");
                }
                finalTurn = ToSnapshot(turn);
                idleCompletion.TrySetResult(true);
            }
            PublishTerminalCheckpoint(ExportCheckpoint());
            return finalTurn;
        }
        catch
        {
            await AbortAfterConsumerFailureAsync(handle.TurnId);
            PiAgentConversationSnapshot failedSnapshot;
            PiAgentConversationTurnSnapshot failedTurn;
            lock (gate)
            {
                MutableTurn turn = FindTurnLocked(handle.TurnId);
                turn.Status = PiAgentConversationTurnStatus.Failed;
                turn.ErrorCode = "conversation-event-stream-failed";
                activeTurnId = null;
                idleCompletion.TrySetResult(true);
                failedSnapshot = AdvanceSnapshotLocked();
                failedTurn = ToSnapshot(turn);
            }
            Publish(failedSnapshot);
            return failedTurn;
        }
    }

    private async Task AbortAfterConsumerFailureAsync(string turnId)
    {
        try
        {
            using CancellationTokenSource timeout =
                new(TimeSpan.FromSeconds(2));
            int requestSequence = Interlocked.Increment(
                ref abortRequestSequence);
            await controller.AbortTurnAsync(
                turnId,
                $"desktop-fail-closed-{requestSequence:D8}",
                timeout.Token);
        }
        catch
        {
        }
    }

    private PiAgentConversationSnapshot ApplyEvent(
        PiAgentTurnStreamEvent streamEvent)
    {
        lock (gate)
        {
            MutableTurn turn = FindTurnLocked(streamEvent.TurnId);
            if (
                IsTerminal(turn.Status) ||
                streamEvent.Sequence != turn.LastEventSequence + 1)
            {
                throw new InvalidOperationException(
                    "The desktop conversation event order was invalid.");
            }

            switch (streamEvent)
            {
                case PiAgentAssistantTextDelta text:
                    if (
                        turn.AssistantText.Length + text.Delta.Length >
                            MaximumAssistantCharacters)
                    {
                        throw new InvalidOperationException(
                            "The desktop conversation response exceeded " +
                            "its retained character limit.");
                    }
                    turn.AssistantText.Append(text.Delta);
                    break;

                case PiAgentToolExecutionStarted started:
                    if (turn.Tools.Any(tool =>
                            tool.ToolCallId == started.ToolCallId))
                    {
                        throw new InvalidOperationException(
                            "The desktop conversation tool id was duplicated.");
                    }
                    turn.Tools.Add(
                        new MutableTool
                        {
                            ToolCallId = started.ToolCallId,
                            ToolName = started.ToolName,
                            StartedSequence = started.Sequence,
                        });
                    break;

                case PiAgentToolExecutionCompleted completed:
                    MutableTool tool = turn.Tools.SingleOrDefault(
                        candidate =>
                            candidate.ToolCallId ==
                                completed.ToolCallId &&
                            candidate.ToolName ==
                                completed.ToolName &&
                            candidate.Status ==
                                PiAgentConversationToolStatus.Running) ??
                        throw new InvalidOperationException(
                            "The desktop conversation tool completion " +
                            "was unmatched.");
                    tool.Status = completed.IsError
                        ? PiAgentConversationToolStatus.Failed
                        : PiAgentConversationToolStatus.Completed;
                    tool.CompletedSequence = completed.Sequence;
                    break;

                case PiAgentWorkspaceEditProposed proposed:
                    if (
                        pendingWorkspaceEditId is not null ||
                        turns.SelectMany(candidate =>
                                candidate.WorkspaceEdits)
                            .Any(edit =>
                                edit.ProposalId == proposed.ProposalId) ||
                        !turn.Tools.Any(tool =>
                            tool.ToolName == "propose_edit" &&
                            tool.Status ==
                                PiAgentConversationToolStatus.Completed))
                    {
                        throw new InvalidOperationException(
                            "The desktop conversation workspace edit proposal was unmatched.");
                    }
                    MutableWorkspaceEdit workspaceEdit = new()
                    {
                        SchemaVersion = proposed.SchemaVersion,
                        ProposalId = proposed.ProposalId,
                        RelativePath = proposed.RelativePath,
                        BeforeSha256 = proposed.BeforeSha256,
                        OldText = proposed.OldText,
                        NewText = proposed.NewText,
                    };
                    if (!acceptingSubmissions)
                    {
                        workspaceEdit.Status =
                            PiAgentWorkspaceEditStatus.Expired;
                    }
                    turn.WorkspaceEdits.Add(workspaceEdit);
                    if (acceptingSubmissions)
                    {
                        pendingWorkspaceEditId = proposed.ProposalId;
                    }
                    break;

                case PiAgentTurnCompleted terminal:
                    if (turn.Tools.Any(tool =>
                            tool.Status ==
                                PiAgentConversationToolStatus.Running) ||
                        turn.AssistantText.ToString() !=
                            terminal.Result.Response)
                    {
                        throw new InvalidOperationException(
                            "The desktop conversation terminal payload " +
                            "was inconsistent.");
                    }
                    turn.Status = terminal.Result.Status switch
                    {
                        "completed" when terminal.Result.Success =>
                            PiAgentConversationTurnStatus.Completed,
                        "aborted" when !terminal.Result.Success =>
                            PiAgentConversationTurnStatus.Aborted,
                        "failed" when !terminal.Result.Success =>
                            PiAgentConversationTurnStatus.Failed,
                        _ => throw new InvalidOperationException(
                            "The desktop conversation terminal status " +
                            "was invalid."),
                    };
                    turn.ErrorCode = terminal.Result.ErrorCode;
                    activeTurnId = null;
                    break;

                default:
                    throw new InvalidOperationException(
                        "The desktop conversation event type was invalid.");
            }

            turn.LastEventSequence = streamEvent.Sequence;
            return AdvanceSnapshotLocked();
        }
    }

    private void TrimTerminalTurnsLocked()
    {
        while (turns.Count >= MaximumRetainedTurns)
        {
            int terminalIndex = turns.FindIndex(turn =>
                IsTerminal(turn.Status));
            if (terminalIndex < 0)
            {
                throw new InvalidOperationException(
                    "The desktop conversation retention limit is active.");
            }
            turns.RemoveAt(terminalIndex);
        }
    }

    private MutableTurn FindTurnLocked(string turnId) =>
        turns.SingleOrDefault(turn => turn.TurnId == turnId) ??
        throw new InvalidOperationException(
            "The desktop conversation turn was not retained.");

    private MutableWorkspaceEdit FindPendingWorkspaceEditLocked(
        string proposalId)
    {
        if (
            !acceptingSubmissions ||
            activeTurnId is not null ||
            pendingWorkspaceEditId != proposalId)
        {
            throw new InvalidOperationException(
                "The workspace edit is not ready for owner review.");
        }
        MutableWorkspaceEdit proposal =
            FindWorkspaceEditLocked(proposalId);
        if (proposal.Status != PiAgentWorkspaceEditStatus.Pending)
        {
            throw new InvalidOperationException(
                "The workspace edit proposal was already decided.");
        }
        return proposal;
    }

    private MutableWorkspaceEdit FindWorkspaceEditLocked(
        string proposalId) =>
        turns.SelectMany(turn => turn.WorkspaceEdits)
            .SingleOrDefault(proposal =>
                proposal.ProposalId == proposalId) ??
        throw new InvalidOperationException(
            "The workspace edit proposal was not retained.");

    private PiAgentConversationSnapshot AdvanceSnapshotLocked()
    {
        revision++;
        return BuildSnapshotLocked();
    }

    private PiAgentConversationSnapshot BuildSnapshotLocked()
    {
        MutableTurn? active = activeTurnId is null
            ? null
            : FindTurnLocked(activeTurnId);
        return new PiAgentConversationSnapshot(
            revision,
            activeTurnId,
            acceptingSubmissions &&
                activeTurnId is null &&
                pendingWorkspaceEditId is null,
            active is not null && !active.CancelRequested,
            turns.Select(ToSnapshot).ToArray());
    }

    private static PiAgentConversationTurnSnapshot ToSnapshot(
        MutableTurn turn) =>
        new(
            turn.TurnId,
            turn.UserText,
            turn.AssistantText.ToString(),
            turn.Status,
            turn.LastEventSequence,
            turn.CancelRequested,
            turn.Tools.Select(tool =>
                new PiAgentConversationToolSnapshot(
                    tool.ToolCallId,
                    tool.ToolName,
                    tool.Status,
                    tool.StartedSequence,
                    tool.CompletedSequence)).ToArray(),
            turn.WorkspaceEdits.Select(ToSnapshot).ToArray(),
            turn.ErrorCode);

    private static PiAgentWorkspaceEditSnapshot ToSnapshot(
        MutableWorkspaceEdit proposal) =>
        new(
            proposal.SchemaVersion,
            proposal.ProposalId,
            proposal.RelativePath,
            proposal.BeforeSha256,
            proposal.OldText,
            proposal.NewText,
            proposal.Status,
            proposal.AfterSha256,
            proposal.ErrorCode);

    private void Publish(PiAgentConversationSnapshot snapshot)
    {
        EventHandler<PiAgentConversationSnapshotChangedEventArgs>? handlers =
            SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        void Raise()
        {
            PiAgentConversationSnapshotChangedEventArgs eventArgs =
                new(snapshot);
            foreach (EventHandler<
                         PiAgentConversationSnapshotChangedEventArgs> handler
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

        if (
            notificationContext is null ||
            ReferenceEquals(
                SynchronizationContext.Current,
                notificationContext))
        {
            Raise();
            return;
        }
        notificationContext.Post(
            static state => ((Action)state!).Invoke(),
            (Action)Raise);
    }

    private void PublishTerminalCheckpoint(
        PiAgentConversationCheckpoint checkpoint)
    {
        Action<PiAgentConversationCheckpoint>? handlers =
            TerminalCheckpointAvailable;
        if (handlers is null)
        {
            return;
        }
        foreach (Action<PiAgentConversationCheckpoint> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(checkpoint);
            }
            catch
            {
            }
        }
    }

    private static bool IsTerminal(
        PiAgentConversationTurnStatus status) =>
        status is
            PiAgentConversationTurnStatus.Completed or
            PiAgentConversationTurnStatus.Aborted or
            PiAgentConversationTurnStatus.Failed;

    private static TaskCompletionSource<bool> CreateCompletedIdleSource()
    {
        TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(true);
        return completion;
    }

    private static void ValidateText(string text)
    {
        if (
            string.IsNullOrWhiteSpace(text) ||
            Encoding.UTF8.GetByteCount(text) >
                MaximumCheckpointTextBytes)
        {
            throw new ArgumentException(
                "Conversation text must be 1-16384 UTF-8 bytes.",
                nameof(text));
        }
    }

    private static void ValidateTurnId(string turnId)
    {
        if (!TurnIdPattern.IsMatch(turnId))
        {
            throw new ArgumentException(
                "Conversation turn ids must use the admitted 1-128 " +
                "character identifier grammar.",
                nameof(turnId));
        }
    }

    private static bool IsValidCheckpointText(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Encoding.UTF8.GetByteCount(text) <=
            MaximumCheckpointTextBytes;

    private static int GetCheckpointByteCount(
        PiAgentConversationCheckpoint checkpoint) =>
        JsonSerializer.SerializeToUtf8Bytes(
            checkpoint,
            CheckpointSerializerOptions).Length;

    private void RestoreGeneratedTurnSequence(string turnId)
    {
        const string prefix = "desktop-turn-";
        if (
            turnId.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(
                turnId.AsSpan(prefix.Length),
                out int sequence))
        {
            generatedTurnSequence = Math.Max(
                generatedTurnSequence,
                sequence);
        }
    }
}
