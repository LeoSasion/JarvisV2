using System.Text;
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

public sealed record PiAgentConversationToolSnapshot(
    string ToolCallId,
    string ToolName,
    PiAgentConversationToolStatus Status,
    int StartedSequence,
    int? CompletedSequence);

public sealed record PiAgentConversationTurnSnapshot(
    string TurnId,
    string UserText,
    string AssistantText,
    PiAgentConversationTurnStatus Status,
    int LastEventSequence,
    bool CancelRequested,
    IReadOnlyList<PiAgentConversationToolSnapshot> Tools,
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

public sealed class PiAgentConversationState
{
    public const int MaximumRetainedTurns = 128;
    public const int MaximumAssistantCharacters = 262_144;

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
        public PiAgentConversationTurnStatus Status { get; set; } =
            PiAgentConversationTurnStatus.Starting;
        public int LastEventSequence { get; set; }
        public bool CancelRequested { get; set; }
        public string? ErrorCode { get; set; }
    }

    private static readonly Regex TurnIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\z",
        RegexOptions.CultureInvariant);

    private readonly object gate = new();
    private readonly PiAgentSidecarController controller;
    private readonly SynchronizationContext? notificationContext;
    private readonly List<MutableTurn> turns = [];
    private string? activeTurnId;
    private int revision;
    private int generatedTurnSequence;
    private int abortRequestSequence;
    private bool acceptingSubmissions = true;
    private TaskCompletionSource<bool> idleCompletion =
        CreateCompletedIdleSource();

    public PiAgentConversationState(
        PiAgentSidecarController controller,
        SynchronizationContext? notificationContext = null)
    {
        this.controller = controller ??
            throw new ArgumentNullException(nameof(controller));
        this.notificationContext = notificationContext;
    }

    public event EventHandler<
        PiAgentConversationSnapshotChangedEventArgs>? SnapshotChanged;

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

    public async Task QuiesceAsync(
        CancellationToken cancellationToken = default)
    {
        PiAgentConversationSnapshot? quiescingSnapshot = null;
        Task idleTask;
        lock (gate)
        {
            if (acceptingSubmissions)
            {
                acceptingSubmissions = false;
                quiescingSnapshot = AdvanceSnapshotLocked();
            }
            idleTask = idleCompletion.Task;
        }
        if (quiescingSnapshot is not null)
        {
            Publish(quiescingSnapshot);
        }

        _ = await CancelActiveTurnAsync(cancellationToken);
        await idleTask.WaitAsync(cancellationToken);
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
                PiAgentConversationTurnSnapshot finalTurn =
                    ToSnapshot(turn);
                idleCompletion.TrySetResult(true);
                return finalTurn;
            }
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
            acceptingSubmissions && activeTurnId is null,
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
            turn.ErrorCode);

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
            Encoding.UTF8.GetByteCount(text) > 16_384)
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
}
