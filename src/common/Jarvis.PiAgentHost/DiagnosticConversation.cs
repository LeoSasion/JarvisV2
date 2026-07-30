using System.Collections.Concurrent;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentConversationProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool NormalTurnPassed,
    bool DeltaSnapshotsObserved,
    bool RevisionOrderPassed,
    bool ToolLifecyclePassed,
    bool SingleActiveTurnEnforced,
    bool CancelRequestObserved,
    bool AbortTurnPassed,
    bool NotificationContextUsed,
    int RetainedTurnLimit,
    int AssistantCharacterLimit,
    int CompletedTurnCount,
    int ObservedSnapshotCount,
    bool CanSubmitAfterTerminal,
    bool CanCancelAfterTerminal,
    bool CredentialTransportAllowed,
    bool PiSidecarModelNetworkAllowed,
    string LiveModelNetwork,
    string LiveExplorer,
    bool MutationPerformed);

public static class PiAgentConversationProbe
{
    public static async Task<PiAgentConversationProbeReceipt> RunAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        string workspaceRoot = ResolveWorkspaceRoot(options);
        RecordingSynchronizationContext notificationContext = new();
        ConcurrentQueue<PiAgentConversationSnapshot> snapshots = new();

        await using DiagnosticDesktopModelBroker broker =
            DiagnosticDesktopModelBroker.Start();
        await using PiAgentSidecarController controller =
            await PiAgentSidecarController.StartAsync(
                options with
                {
                    ModelBrokerPipePath = broker.PipePath,
                },
                cancellationToken);
        using JsonDocument session =
            await controller.StartReadOnlySessionAsync(
                workspaceRoot,
                "conversation-session",
                cancellationToken);
        if (!session.RootElement
                .GetProperty("success")
                .GetBoolean())
        {
            throw new InvalidOperationException(
                "The conversation probe session was not admitted.");
        }

        PiAgentConversationState conversation =
            new(controller, notificationContext);
        conversation.SnapshotChanged += (_, eventArgs) =>
            snapshots.Enqueue(eventArgs.Snapshot);

        PiAgentConversationTurn first =
            await conversation.SubmitAsync(
                "Begin the desktop conversation.",
                "conversation-turn-1",
                cancellationToken);
        PiAgentConversationTurnSnapshot firstFinal =
            await first.Completion.WaitAsync(cancellationToken);

        PiAgentConversationTurn second =
            await conversation.SubmitAsync(
                "Continue the desktop conversation.",
                "conversation-turn-2",
                cancellationToken);
        PiAgentConversationTurnSnapshot secondFinal =
            await second.Completion.WaitAsync(cancellationToken);

        PiAgentConversationTurn toolTurn =
            await conversation.SubmitAsync(
                "Read the admitted package manifest.",
                "conversation-tool-turn",
                cancellationToken);
        PiAgentConversationTurnSnapshot toolFinal =
            await toolTurn.Completion.WaitAsync(cancellationToken);
        PiAgentConversationSnapshot normalFinal = conversation.Snapshot;

        PiAgentConversationSnapshot[] observed = snapshots.ToArray();
        bool normalTurnPassed =
            firstFinal.Status ==
                PiAgentConversationTurnStatus.Completed &&
            secondFinal.Status ==
                PiAgentConversationTurnStatus.Completed &&
            firstFinal.AssistantText ==
                "JARVIS desktop broker online." &&
            secondFinal.AssistantText ==
                "JARVIS desktop broker online." &&
            normalFinal.Turns.Count == 3 &&
            normalFinal.ActiveTurnId is null;
        bool deltaSnapshotsObserved =
            observed.Any(snapshot =>
                snapshot.Turns.Any(turn =>
                    turn.TurnId == first.TurnId &&
                    turn.AssistantText == "JARVIS ")) &&
            observed.Any(snapshot =>
                snapshot.Turns.Any(turn =>
                    turn.TurnId == first.TurnId &&
                    turn.AssistantText ==
                        "JARVIS desktop broker online."));
        bool revisionOrderPassed =
            observed.Length > 0 &&
            observed
                .Select(snapshot => snapshot.Revision)
                .SequenceEqual(observed
                    .Select(snapshot => snapshot.Revision)
                    .Order()) &&
            observed
                .Select(snapshot => snapshot.Revision)
                .Distinct()
                .Count() == observed.Length;
        bool toolLifecyclePassed =
            toolFinal.Status ==
                PiAgentConversationTurnStatus.Completed &&
            toolFinal.AssistantText ==
                "JARVIS workspace tool online." &&
            toolFinal.Tools.Count == 1 &&
            toolFinal.Tools[0].ToolName == "read" &&
            toolFinal.Tools[0].Status ==
                PiAgentConversationToolStatus.Completed &&
            toolFinal.Tools[0].StartedSequence == 1 &&
            toolFinal.Tools[0].CompletedSequence == 2 &&
            observed.Any(snapshot =>
                snapshot.Turns.Any(turn =>
                    turn.TurnId == toolTurn.TurnId &&
                    turn.Tools.Any(tool =>
                        tool.Status ==
                            PiAgentConversationToolStatus.Running)));

        await controller.ShutdownAsync(cancellationToken);

        ConcurrentQueue<PiAgentConversationSnapshot> abortSnapshots = new();
        await using DiagnosticDesktopModelBroker abortBroker =
            DiagnosticDesktopModelBroker.Start(holdResponse: true);
        await using PiAgentSidecarController abortController =
            await PiAgentSidecarController.StartAsync(
                options with
                {
                    ModelBrokerPipePath = abortBroker.PipePath,
                },
                cancellationToken);
        using JsonDocument abortSession =
            await abortController.StartReadOnlySessionAsync(
                workspaceRoot,
                "conversation-abort-session",
                cancellationToken);
        PiAgentConversationState abortConversation =
            new(abortController, notificationContext);
        abortConversation.SnapshotChanged += (_, eventArgs) =>
            abortSnapshots.Enqueue(eventArgs.Snapshot);

        PiAgentConversationTurn abortTurn =
            await abortConversation.SubmitAsync(
                "Wait until the desktop requests cancellation.",
                "conversation-abort-turn",
                cancellationToken);
        await abortBroker.WaitForRequestAsync(cancellationToken);

        bool singleActiveTurnEnforced = false;
        try
        {
            _ = await abortConversation.SubmitAsync(
                "This concurrent turn must be rejected.",
                "conversation-rejected-turn",
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            singleActiveTurnEnforced = true;
        }

        bool cancelRequestPassed =
            await abortConversation.CancelActiveTurnAsync(
                cancellationToken);
        PiAgentConversationTurnSnapshot abortFinal =
            await abortTurn.Completion.WaitAsync(cancellationToken);
        PiAgentConversationSnapshot abortFinalSnapshot =
            abortConversation.Snapshot;
        bool cancelRequestObserved =
            cancelRequestPassed &&
            abortSnapshots.Any(snapshot =>
                snapshot.Turns.Any(turn =>
                    turn.TurnId == abortTurn.TurnId &&
                    turn.CancelRequested));
        bool abortTurnPassed =
            abortFinal.Status ==
                PiAgentConversationTurnStatus.Aborted &&
            abortFinal.ErrorCode == "turn-aborted" &&
            abortFinalSnapshot.ActiveTurnId is null &&
            !await abortConversation.CancelActiveTurnAsync(
                cancellationToken);

        await abortController.ShutdownAsync(cancellationToken);

        int completedTurnCount =
            normalFinal.Turns.Count(turn =>
                turn.Status ==
                    PiAgentConversationTurnStatus.Completed);
        bool passed =
            normalTurnPassed &&
            deltaSnapshotsObserved &&
            revisionOrderPassed &&
            toolLifecyclePassed &&
            singleActiveTurnEnforced &&
            cancelRequestObserved &&
            abortTurnPassed &&
            notificationContext.PostCount > 0 &&
            completedTurnCount == 3 &&
            normalFinal.CanSubmit &&
            !normalFinal.CanCancel &&
            broker.FaultCount == 0 &&
            abortBroker.FaultCount == 0;

        return new PiAgentConversationProbeReceipt(
            1,
            "jarvisv2-pi-agent-desktop-conversation-probe",
            passed ? "passed" : "failed",
            normalTurnPassed,
            deltaSnapshotsObserved,
            revisionOrderPassed,
            toolLifecyclePassed,
            singleActiveTurnEnforced,
            cancelRequestObserved,
            abortTurnPassed,
            notificationContext.PostCount > 0,
            PiAgentConversationState.MaximumRetainedTurns,
            PiAgentConversationState.MaximumAssistantCharacters,
            completedTurnCount,
            observed.Length,
            normalFinal.CanSubmit,
            normalFinal.CanCancel,
            false,
            false,
            "diagnostic-only",
            "not-run",
            false);
    }

    private static string ResolveWorkspaceRoot(
        PiAgentSidecarOptions options) =>
        Directory.GetParent(options.HostScriptPath)?
            .Parent?.FullName ??
        throw new InvalidOperationException(
            "The conversation probe workspace could not be resolved.");

    private sealed class RecordingSynchronizationContext :
        SynchronizationContext
    {
        private int postCount;

        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(
            SendOrPostCallback callback,
            object? state)
        {
            Interlocked.Increment(ref postCount);
            callback(state);
        }
    }
}
