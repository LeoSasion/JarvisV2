using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentDesktopRuntimeProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string OwnershipModel,
    string ShutdownModel,
    bool RuntimeCompositionPassed,
    bool MultiTurnPassed,
    bool ToolRoundTripPassed,
    bool WorkspaceEditProposalPassed,
    bool WorkspaceEditApprovalPassed,
    bool WorkspaceEditReplayRejected,
    bool WorkspaceEditDriftRejected,
    bool WorkspaceEditRejectionPassed,
    bool WorkspaceEditShutdownExpirationPassed,
    bool WorkspaceEditFixtureMutationPerformed,
    bool CheckpointExportPassed,
    bool CheckpointContextRestorePassed,
    bool CheckpointAdmissionPassed,
    bool CheckpointStoreRoundTripPassed,
    bool CheckpointStoreCiphertextPassed,
    bool CheckpointStoreBindingPassed,
    bool CheckpointStoreCorruptionRejected,
    bool CheckpointStoreFailureShutdownPassed,
    bool CheckpointTerminalAutosavePassed,
    bool QuiesceClosedSubmission,
    bool ShutdownCancelledActiveTurn,
    bool OrderlyShutdownPassed,
    bool StartupRollbackPassed,
    bool CredentialEnvironmentClean,
    int NormalBrokerRequestCount,
    int ResumeBrokerRequestCount,
    int AbortBrokerRequestCount,
    int WorkspaceEditBrokerRequestCount,
    int ExportedCheckpointTurnCount,
    int RestoredCheckpointTurnCount,
    int PersistedCheckpointTurnCount,
    int NormalCheckpointSaveCount,
    int ResumeCheckpointSaveCount,
    int BrokerFaultCount,
    bool CredentialTransportAllowed,
    bool PiSidecarModelNetworkAllowed,
    string LiveModelNetwork,
    string LiveExplorer,
    bool MutationPerformed);

public static class PiAgentDesktopRuntimeProbe
{
    public static async Task<PiAgentDesktopRuntimeProbeReceipt> RunAsync(
        PiAgentSidecarOptions sidecarOptions,
        CancellationToken cancellationToken)
    {
        string workspaceRoot = ResolveWorkspaceRoot(sidecarOptions);
        int normalBrokerRequestCount;
        int normalBrokerFaultCount;
        bool runtimeCompositionPassed;
        bool multiTurnPassed;
        bool toolRoundTripPassed;
        bool checkpointExportPassed;
        bool normalQuiesceClosedSubmission;
        bool normalShutdownPassed;
        bool normalCredentialEnvironmentClean;
        PiAgentConversationCheckpoint checkpoint;
        await using TemporaryCheckpointStoreFixture storeFixture =
            new();
        PiAgentConversationCheckpointStore checkpointStore =
            storeFixture.Store;
        bool storeInitiallyEmpty =
            await checkpointStore.LoadAsync(
                workspaceRoot,
                cancellationToken) is null;
        PiAgentConversationCheckpointStoreReceipt?
            normalStoreReceipt;
        int normalCheckpointSaveCount;
        bool normalCheckpointPersistenceHealthy;

        DiagnosticDesktopModelProvider normalProvider = new(
            holdResponse: false);
        await using (
            PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        workspaceRoot,
                        ConversationCheckpointStore:
                            checkpointStore),
                    normalProvider,
                    cancellationToken: cancellationToken))
        {
            PiAgentConversationTurn first =
                await runtime.Conversation.SubmitAsync(
                    "Start the owned desktop runtime.",
                    "runtime-turn-1",
                    cancellationToken);
            PiAgentConversationTurnSnapshot firstFinal =
                await first.Completion.WaitAsync(cancellationToken);
            if (!runtime.Conversation.Snapshot.CanSubmit)
            {
                throw new InvalidOperationException(
                    $"Runtime stopped accepting after first turn: {firstFinal.Status} / {firstFinal.ErrorCode ?? "no error"}; checkpoint faulted={runtime.CheckpointPersistenceFaulted}.");
            }
            PiAgentConversationTurn second =
                await runtime.Conversation.SubmitAsync(
                    "Continue the owned desktop runtime.",
                    "runtime-turn-2",
                    cancellationToken);
            PiAgentConversationTurnSnapshot secondFinal =
                await second.Completion.WaitAsync(cancellationToken);
            PiAgentConversationTurn toolTurn =
                await runtime.Conversation.SubmitAsync(
                    "Read the admitted package manifest.",
                    "runtime-tool-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot toolFinal =
                await toolTurn.Completion.WaitAsync(cancellationToken);

            runtimeCompositionPassed =
                Path.IsPathFullyQualified(runtime.WorkspaceRoot) &&
                Directory.Exists(runtime.WorkspaceRoot) &&
                runtime.CredentialEnvironmentClean &&
                runtime.Conversation.Snapshot.Turns.Count == 3;
            multiTurnPassed =
                firstFinal.Status ==
                    PiAgentConversationTurnStatus.Completed &&
                secondFinal.Status ==
                    PiAgentConversationTurnStatus.Completed &&
                firstFinal.AssistantText ==
                    "JARVIS desktop broker online." &&
                secondFinal.AssistantText ==
                    "JARVIS desktop broker online.";
            toolRoundTripPassed =
                toolFinal.Status ==
                    PiAgentConversationTurnStatus.Completed &&
                toolFinal.AssistantText ==
                    "JARVIS workspace tool online." &&
                toolFinal.Tools.Count == 1 &&
                toolFinal.Tools[0].ToolName == "read" &&
                toolFinal.Tools[0].Status ==
                    PiAgentConversationToolStatus.Completed;
            checkpoint = runtime.Conversation.ExportCheckpoint();
            checkpointExportPassed =
                checkpoint.SchemaVersion == 1 &&
                checkpoint.Turns.Count == 3 &&
                checkpoint.Turns.Select(turn => turn.TurnId)
                    .SequenceEqual([
                        "runtime-turn-1",
                        "runtime-turn-2",
                        "runtime-tool-turn",
                    ]) &&
                checkpoint.Turns.All(turn =>
                    !string.IsNullOrWhiteSpace(turn.UserText) &&
                    !string.IsNullOrWhiteSpace(
                        turn.AssistantText));

            await runtime.ShutdownAsync(cancellationToken);
            normalStoreReceipt =
                runtime.LastCheckpointStoreReceipt;
            normalCheckpointSaveCount =
                runtime.CheckpointSaveCount;
            normalCheckpointPersistenceHealthy =
                !runtime.CheckpointPersistenceFaulted;
            normalShutdownPassed =
                runtime.IsShutdown &&
                runtime.Conversation.Snapshot.ActiveTurnId is null &&
                !runtime.Conversation.Snapshot.CanSubmit &&
                !runtime.Conversation.Snapshot.CanCancel;
            normalQuiesceClosedSubmission =
                await SubmissionIsClosedAsync(
                    runtime.Conversation,
                    "runtime-rejected-after-shutdown",
                    cancellationToken);
            normalCredentialEnvironmentClean =
                runtime.CredentialEnvironmentClean;
            normalBrokerRequestCount = runtime.BrokerRequestCount;
            normalBrokerFaultCount = runtime.BrokerFaultCount;
        }

        PiAgentConversationCheckpoint? storedCheckpoint =
            await checkpointStore.LoadAsync(
                workspaceRoot,
                cancellationToken);
        bool checkpointStoreRoundTripPassed =
            storeInitiallyEmpty &&
            normalStoreReceipt is not null &&
            normalStoreReceipt.TurnCount == 3 &&
            normalStoreReceipt.EnvelopeBytes > 0 &&
            normalStoreReceipt.EnvelopeBytes <=
                PiAgentConversationCheckpointStore
                    .MaximumEnvelopeBytes &&
            storedCheckpoint is not null &&
            storedCheckpoint.Turns.SequenceEqual(
                checkpoint.Turns);
        string encryptedCheckpointText =
            normalStoreReceipt is null
                ? string.Empty
                : await File.ReadAllTextAsync(
                    normalStoreReceipt.CheckpointPath,
                    cancellationToken);
        bool checkpointStoreCiphertextPassed =
            normalStoreReceipt is not null &&
            File.Exists(normalStoreReceipt.CheckpointPath) &&
            !encryptedCheckpointText.Contains(
                "Start the owned desktop runtime.",
                StringComparison.Ordinal) &&
            !encryptedCheckpointText.Contains(
                "JARVIS desktop broker online.",
                StringComparison.Ordinal) &&
            !encryptedCheckpointText.Contains(
                workspaceRoot,
                StringComparison.OrdinalIgnoreCase) &&
            !Directory.EnumerateFiles(
                    checkpointStore.RootDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly)
                .Any();

        int resumeBrokerRequestCount;
        int resumeBrokerFaultCount;
        int restoredCheckpointTurnCount;
        bool checkpointContextRestorePassed;
        PiAgentConversationCheckpointStoreReceipt?
            resumeStoreReceipt;
        int resumeCheckpointSaveCount;
        bool resumeCheckpointPersistenceHealthy;
        DiagnosticDesktopModelProvider resumeProvider = new(
            holdResponse: false);
        await using (
            PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        workspaceRoot,
                        ConversationCheckpointStore:
                            checkpointStore),
                    resumeProvider,
                    cancellationToken: cancellationToken))
        {
            PiAgentConversationSnapshot restoredSnapshot =
                runtime.Conversation.Snapshot;
            restoredCheckpointTurnCount =
                restoredSnapshot.Turns.Count;
            PiAgentConversationTurn resumed =
                await runtime.Conversation.SubmitAsync(
                    "Continue after checkpoint restore.",
                    "runtime-resumed-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot resumedFinal =
                await resumed.Completion.WaitAsync(
                    cancellationToken);
            checkpointContextRestorePassed =
                restoredSnapshot.Revision == 1 &&
                restoredSnapshot.ActiveTurnId is null &&
                restoredSnapshot.CanSubmit &&
                !restoredSnapshot.CanCancel &&
                restoredSnapshot.Turns.Count ==
                    checkpoint.Turns.Count &&
                runtime.RestoredCheckpointTurnCount ==
                    checkpoint.Turns.Count &&
                restoredSnapshot.Turns.All(turn =>
                    turn.Status ==
                        PiAgentConversationTurnStatus.Completed) &&
                resumedFinal.Status ==
                    PiAgentConversationTurnStatus.Completed &&
                resumeProvider.RequestContexts.Count == 1 &&
                ContextContainsCheckpoint(
                    resumeProvider.RequestContexts[0],
                    checkpoint,
                    "Continue after checkpoint restore.");
            await runtime.ShutdownAsync(cancellationToken);
            resumeStoreReceipt =
                runtime.LastCheckpointStoreReceipt;
            resumeCheckpointSaveCount =
                runtime.CheckpointSaveCount;
            resumeCheckpointPersistenceHealthy =
                !runtime.CheckpointPersistenceFaulted;
            resumeBrokerRequestCount = runtime.BrokerRequestCount;
            resumeBrokerFaultCount = runtime.BrokerFaultCount;
        }
        PiAgentConversationCheckpoint? resumedStoredCheckpoint =
            await checkpointStore.LoadAsync(
                workspaceRoot,
                cancellationToken);
        checkpointStoreRoundTripPassed =
            checkpointStoreRoundTripPassed &&
            resumeStoreReceipt is not null &&
            resumeStoreReceipt.TurnCount == 4 &&
            resumedStoredCheckpoint?.Turns.Count == 4;
        bool checkpointStoreBindingPassed =
            await ProbeCheckpointStoreBindingAsync(
                checkpointStore,
                workspaceRoot,
                storeFixture.RootDirectory,
                cancellationToken);
        bool checkpointStoreCorruptionRejected =
            await ProbeCheckpointStoreCorruptionAsync(
                checkpointStore,
                workspaceRoot,
                cancellationToken);
        CheckpointStoreFailureProbe storeFailureProbe =
            await ProbeCheckpointStoreFailureShutdownAsync(
                sidecarOptions,
                workspaceRoot,
                storeFixture.RootDirectory,
                cancellationToken);
        int persistedCheckpointTurnCount =
            resumeStoreReceipt?.TurnCount ?? 0;
        bool checkpointTerminalAutosavePassed =
            normalCheckpointPersistenceHealthy &&
            resumeCheckpointPersistenceHealthy &&
            normalCheckpointSaveCount == 3 &&
            resumeCheckpointSaveCount == 1;

        WorkspaceEditApprovalProbe workspaceEditProbe =
            await ProbeWorkspaceEditApprovalAsync(
                sidecarOptions,
                workspaceRoot,
                cancellationToken);

        int abortBrokerRequestCount;
        int abortBrokerFaultCount;
        bool shutdownCancelledActiveTurn;
        bool abortQuiesceClosedSubmission;
        bool abortShutdownPassed;
        bool abortCredentialEnvironmentClean;

        DiagnosticDesktopModelProvider abortProvider = new(
            holdResponse: true);
        await using (
            PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        workspaceRoot),
                    abortProvider,
                    cancellationToken: cancellationToken))
        {
            PiAgentConversationTurn activeTurn =
                await runtime.Conversation.SubmitAsync(
                    "Wait until the desktop runtime shuts down.",
                    "runtime-active-turn",
                    cancellationToken);
            await abortProvider.WaitForRequestAsync(cancellationToken);
            await runtime.ShutdownAsync(cancellationToken);
            PiAgentConversationTurnSnapshot activeFinal =
                await activeTurn.Completion.WaitAsync(cancellationToken);

            shutdownCancelledActiveTurn =
                activeFinal.Status ==
                    PiAgentConversationTurnStatus.Aborted &&
                activeFinal.ErrorCode == "turn-aborted" &&
                activeFinal.CancelRequested;
            abortShutdownPassed =
                runtime.IsShutdown &&
                runtime.Conversation.Snapshot.ActiveTurnId is null &&
                !runtime.Conversation.Snapshot.CanSubmit &&
                !runtime.Conversation.Snapshot.CanCancel;
            abortQuiesceClosedSubmission =
                await SubmissionIsClosedAsync(
                    runtime.Conversation,
                    "runtime-rejected-after-abort",
                    cancellationToken);
            abortCredentialEnvironmentClean =
                runtime.CredentialEnvironmentClean;
            abortBrokerRequestCount = runtime.BrokerRequestCount;
            abortBrokerFaultCount = runtime.BrokerFaultCount;
        }

        int brokerFaultCount =
            normalBrokerFaultCount +
            resumeBrokerFaultCount +
            abortBrokerFaultCount +
            storeFailureProbe.BrokerFaultCount;
        brokerFaultCount += workspaceEditProbe.BrokerFaultCount;
        bool startupRollbackPassed =
            await ProbeStartupRollbackAsync(
                sidecarOptions,
                cancellationToken);
        bool checkpointAdmissionPassed =
            ProbeCheckpointAdmission();
        bool passed =
            runtimeCompositionPassed &&
            multiTurnPassed &&
            toolRoundTripPassed &&
            workspaceEditProbe.ProposalPassed &&
            workspaceEditProbe.ApprovalPassed &&
            workspaceEditProbe.ReplayRejected &&
            workspaceEditProbe.DriftRejected &&
            workspaceEditProbe.RejectionPassed &&
            workspaceEditProbe.ShutdownExpirationPassed &&
            workspaceEditProbe.FixtureMutationPerformed &&
            checkpointExportPassed &&
            checkpointContextRestorePassed &&
            checkpointAdmissionPassed &&
            checkpointStoreRoundTripPassed &&
            checkpointStoreCiphertextPassed &&
            checkpointStoreBindingPassed &&
            checkpointStoreCorruptionRejected &&
            storeFailureProbe.Passed &&
            checkpointTerminalAutosavePassed &&
            normalQuiesceClosedSubmission &&
            shutdownCancelledActiveTurn &&
            abortQuiesceClosedSubmission &&
            normalShutdownPassed &&
            abortShutdownPassed &&
            startupRollbackPassed &&
            normalCredentialEnvironmentClean &&
            abortCredentialEnvironmentClean &&
            normalBrokerRequestCount == 4 &&
            resumeBrokerRequestCount == 1 &&
            abortBrokerRequestCount == 1 &&
            workspaceEditProbe.BrokerRequestCount == 8 &&
            checkpoint.Turns.Count == 3 &&
            restoredCheckpointTurnCount == 3 &&
            brokerFaultCount == 0;

        return new PiAgentDesktopRuntimeProbeReceipt(
            1,
            "jarvisv2-pi-agent-desktop-runtime-probe",
            passed ? "passed" : "failed",
            PiAgentDesktopRuntime.OwnershipModel,
            PiAgentDesktopRuntime.ShutdownModel,
            runtimeCompositionPassed,
            multiTurnPassed,
            toolRoundTripPassed,
            workspaceEditProbe.ProposalPassed,
            workspaceEditProbe.ApprovalPassed,
            workspaceEditProbe.ReplayRejected,
            workspaceEditProbe.DriftRejected,
            workspaceEditProbe.RejectionPassed,
            workspaceEditProbe.ShutdownExpirationPassed,
            workspaceEditProbe.FixtureMutationPerformed,
            checkpointExportPassed,
            checkpointContextRestorePassed,
            checkpointAdmissionPassed,
            checkpointStoreRoundTripPassed,
            checkpointStoreCiphertextPassed,
            checkpointStoreBindingPassed,
            checkpointStoreCorruptionRejected,
            storeFailureProbe.Passed,
            checkpointTerminalAutosavePassed,
            normalQuiesceClosedSubmission &&
                abortQuiesceClosedSubmission,
            shutdownCancelledActiveTurn,
            normalShutdownPassed && abortShutdownPassed,
            startupRollbackPassed,
            normalCredentialEnvironmentClean &&
                abortCredentialEnvironmentClean,
            normalBrokerRequestCount,
            resumeBrokerRequestCount,
            abortBrokerRequestCount,
            workspaceEditProbe.BrokerRequestCount,
            checkpoint.Turns.Count,
            restoredCheckpointTurnCount,
            persistedCheckpointTurnCount,
            normalCheckpointSaveCount,
            resumeCheckpointSaveCount,
            brokerFaultCount,
            false,
            false,
            "diagnostic-only",
            "not-run",
            false);
    }

    private sealed record WorkspaceEditApprovalProbe(
        bool ProposalPassed,
        bool ApprovalPassed,
        bool ReplayRejected,
        bool DriftRejected,
        bool RejectionPassed,
        bool ShutdownExpirationPassed,
        bool FixtureMutationPerformed,
        int BrokerRequestCount,
        int BrokerFaultCount);

    private static async Task<WorkspaceEditApprovalProbe>
        ProbeWorkspaceEditApprovalAsync(
            PiAgentSidecarOptions sidecarOptions,
            string parentWorkspaceRoot,
            CancellationToken cancellationToken)
    {
        string fixtureRoot = Path.Combine(
            parentWorkspaceRoot,
            $".jarvis-workspace-edit-{Guid.NewGuid():N}");
        string fixturePath = Path.Combine(
            fixtureRoot,
            "review.txt");
        Directory.CreateDirectory(fixtureRoot);
        await File.WriteAllTextAsync(
            fixturePath,
            "alpha\nowner-reviewed\nomega\n",
            cancellationToken);
        int brokerRequestCount = 0;
        int brokerFaultCount = 0;
        try
        {
            DiagnosticWorkspaceEditModelProvider provider = new();
            await using PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        fixtureRoot),
                    provider,
                    cancellationToken: cancellationToken);

            PiAgentConversationTurn approvalTurn =
                await runtime.Conversation.SubmitAsync(
                    "Stage the first reviewed text edit.",
                    "workspace-edit-approval-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot approvalFinal =
                await approvalTurn.Completion.WaitAsync(
                    cancellationToken);
            PiAgentWorkspaceEditSnapshot approval =
                approvalFinal.WorkspaceEdits.Single();
            bool proposalPassed =
                approval.Status == PiAgentWorkspaceEditStatus.Pending &&
                approval.RelativePath == "review.txt" &&
                !runtime.Conversation.Snapshot.CanSubmit &&
                await File.ReadAllTextAsync(
                    fixturePath,
                    cancellationToken) ==
                    "alpha\nowner-reviewed\nomega\n";

            PiAgentWorkspaceEditSnapshot applied =
                await runtime.Conversation.ApplyWorkspaceEditAsync(
                    approval.ProposalId,
                    cancellationToken);
            bool approvalPassed =
                applied.Status == PiAgentWorkspaceEditStatus.Applied &&
                applied.AfterSha256 is not null &&
                runtime.Conversation.Snapshot.CanSubmit &&
                await File.ReadAllTextAsync(
                    fixturePath,
                    cancellationToken) ==
                    "alpha\nowner-approved\nomega\n";
            if (!approvalPassed)
            {
                throw new InvalidOperationException(
                    $"Workspace edit approval probe failed: {applied.Status} / {applied.ErrorCode ?? "no error"}.");
            }
            bool replayRejected = false;
            try
            {
                _ = await runtime.Conversation.ApplyWorkspaceEditAsync(
                    approval.ProposalId,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                replayRejected = true;
            }

            PiAgentConversationTurn driftTurn =
                await runtime.Conversation.SubmitAsync(
                    "Stage an edit that will be invalidated by drift.",
                    "workspace-edit-drift-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot driftFinal =
                await driftTurn.Completion.WaitAsync(
                    cancellationToken);
            PiAgentWorkspaceEditSnapshot drift =
                driftFinal.WorkspaceEdits.Single();
            await File.WriteAllTextAsync(
                fixturePath,
                "alpha\nowner-updated-elsewhere\nomega\n",
                cancellationToken);
            PiAgentWorkspaceEditSnapshot drifted =
                await runtime.Conversation.ApplyWorkspaceEditAsync(
                    drift.ProposalId,
                    cancellationToken);
            bool driftRejected =
                drifted.Status == PiAgentWorkspaceEditStatus.Drifted &&
                drifted.ErrorCode == "workspace-edit-drifted" &&
                runtime.Conversation.Snapshot.CanSubmit &&
                await File.ReadAllTextAsync(
                    fixturePath,
                    cancellationToken) ==
                    "alpha\nowner-updated-elsewhere\nomega\n";

            PiAgentConversationTurn rejectionTurn =
                await runtime.Conversation.SubmitAsync(
                    "Stage an edit for explicit rejection.",
                    "workspace-edit-rejection-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot rejectionFinal =
                await rejectionTurn.Completion.WaitAsync(
                    cancellationToken);
            PiAgentWorkspaceEditSnapshot rejection =
                rejectionFinal.WorkspaceEdits.Single();
            PiAgentWorkspaceEditSnapshot rejected =
                await runtime.Conversation.RejectWorkspaceEditAsync(
                    rejection.ProposalId,
                    cancellationToken);
            bool rejectionPassed =
                rejected.Status == PiAgentWorkspaceEditStatus.Rejected &&
                rejected.AfterSha256 is null &&
                runtime.Conversation.Snapshot.CanSubmit &&
                await File.ReadAllTextAsync(
                    fixturePath,
                    cancellationToken) ==
                    "alpha\nowner-updated-elsewhere\nomega\n";

            PiAgentConversationTurn expirationTurn =
                await runtime.Conversation.SubmitAsync(
                    "Stage an edit that expires when the desktop closes.",
                    "workspace-edit-expiration-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot expirationFinal =
                await expirationTurn.Completion.WaitAsync(
                    cancellationToken);
            PiAgentWorkspaceEditSnapshot pendingExpiration =
                expirationFinal.WorkspaceEdits.Single();
            await runtime.ShutdownAsync(cancellationToken);
            PiAgentWorkspaceEditSnapshot expired =
                runtime.Conversation.Snapshot.Turns.Single(turn =>
                        turn.TurnId == expirationTurn.TurnId)
                    .WorkspaceEdits.Single();
            bool shutdownExpirationPassed =
                pendingExpiration.Status ==
                    PiAgentWorkspaceEditStatus.Pending &&
                expired.Status == PiAgentWorkspaceEditStatus.Expired &&
                !expired.CanDecide &&
                expired.AfterSha256 is null &&
                runtime.IsShutdown &&
                !runtime.Conversation.Snapshot.CanSubmit &&
                await File.ReadAllTextAsync(
                    fixturePath,
                    cancellationToken) ==
                    "alpha\nowner-updated-elsewhere\nomega\n";
            brokerRequestCount = runtime.BrokerRequestCount;
            brokerFaultCount = runtime.BrokerFaultCount;
            return new WorkspaceEditApprovalProbe(
                proposalPassed,
                approvalPassed,
                replayRejected,
                driftRejected,
                rejectionPassed,
                shutdownExpirationPassed,
                approvalPassed,
                brokerRequestCount,
                brokerFaultCount);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private sealed record CheckpointStoreFailureProbe(
        bool Passed,
        int BrokerFaultCount);

    private static async Task<CheckpointStoreFailureProbe>
        ProbeCheckpointStoreFailureShutdownAsync(
            PiAgentSidecarOptions sidecarOptions,
            string workspaceRoot,
            string fixtureRoot,
            CancellationToken cancellationToken)
    {
        string failureRoot = Path.Combine(
            fixtureRoot,
            "commit-failure-store");
        Directory.CreateDirectory(failureRoot);
        PiAgentConversationCheckpointStore failureStore = new(
            failureRoot);
        DiagnosticDesktopModelProvider provider = new(
            holdResponse: false);
        bool saveRejected = false;
        bool sidecarStopped = false;
        bool noReceipt = false;
        bool noTemporaryFile = false;
        int brokerFaultCount;
        await using (
            PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        workspaceRoot,
                        ConversationCheckpointStore:
                            failureStore),
                    provider,
                    cancellationToken: cancellationToken))
        {
            Directory.CreateDirectory(
                failureStore.GetCheckpointPath(workspaceRoot));
            PiAgentConversationTurn turn =
                await runtime.Conversation.SubmitAsync(
                    "Persist this turn through a forced commit failure.",
                    "runtime-store-failure-turn",
                    cancellationToken);
            PiAgentConversationTurnSnapshot final =
                await turn.Completion.WaitAsync(
                    cancellationToken);
            try
            {
                await runtime.ShutdownAsync(cancellationToken);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    UnauthorizedAccessException)
            {
                saveRejected = true;
            }
            sidecarStopped =
                final.Status ==
                    PiAgentConversationTurnStatus.Completed &&
                runtime.IsShutdown &&
                runtime.CheckpointPersistenceFaulted &&
                !runtime.Conversation.Snapshot.CanSubmit &&
                runtime.CheckpointSaveCount == 0;
            noReceipt =
                runtime.LastCheckpointStoreReceipt is null;
            noTemporaryFile =
                !Directory.EnumerateFiles(
                        failureRoot,
                        "*.tmp",
                        SearchOption.TopDirectoryOnly)
                    .Any();
            brokerFaultCount = runtime.BrokerFaultCount;
        }
        return new CheckpointStoreFailureProbe(
            saveRejected &&
                sidecarStopped &&
                noReceipt &&
                noTemporaryFile,
            brokerFaultCount);
    }

    private static async Task<bool>
        ProbeCheckpointStoreBindingAsync(
            PiAgentConversationCheckpointStore checkpointStore,
            string workspaceRoot,
            string fixtureRoot,
            CancellationToken cancellationToken)
    {
        string sourcePath =
            checkpointStore.GetCheckpointPath(workspaceRoot);
        string foreignWorkspace = Path.Combine(
            fixtureRoot,
            "foreign-workspace");
        Directory.CreateDirectory(foreignWorkspace);
        string foreignPath =
            checkpointStore.GetCheckpointPath(foreignWorkspace);
        File.Copy(sourcePath, foreignPath, overwrite: true);
        try
        {
            _ = await checkpointStore.LoadAsync(
                foreignWorkspace,
                cancellationToken);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
        finally
        {
            if (File.Exists(foreignPath))
            {
                File.Delete(foreignPath);
            }
        }
    }

    private static async Task<bool>
        ProbeCheckpointStoreCorruptionAsync(
            PiAgentConversationCheckpointStore checkpointStore,
            string workspaceRoot,
            CancellationToken cancellationToken)
    {
        string checkpointPath =
            checkpointStore.GetCheckpointPath(workspaceRoot);
        using JsonDocument envelope = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                checkpointPath,
                cancellationToken));
        JsonElement root = envelope.RootElement;
        string protectedPayload = root
            .GetProperty("protectedPayload")
            .GetString() ??
            throw new InvalidDataException(
                "The diagnostic checkpoint payload was absent.");
        char replacement =
            protectedPayload[0] == 'A' ? 'B' : 'A';
        string corruptedPayload =
            replacement + protectedPayload[1..];
        string corruptedEnvelope = JsonSerializer.Serialize(
            new
            {
                schemaVersion =
                    root.GetProperty("schemaVersion").GetInt32(),
                receiptType =
                    root.GetProperty("receiptType").GetString(),
                workspaceId =
                    root.GetProperty("workspaceId").GetString(),
                savedAtUtc =
                    root.GetProperty("savedAtUtc")
                        .GetDateTimeOffset(),
                protectedPayload = corruptedPayload,
            });
        await File.WriteAllTextAsync(
            checkpointPath,
            corruptedEnvelope,
            cancellationToken);
        try
        {
            _ = await checkpointStore.LoadAsync(
                workspaceRoot,
                cancellationToken);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool ContextContainsCheckpoint(
        JsonElement context,
        PiAgentConversationCheckpoint checkpoint,
        string currentPrompt)
    {
        if (
            !context.TryGetProperty(
                "messages",
                out JsonElement messages) ||
            messages.ValueKind != JsonValueKind.Array ||
            messages.GetArrayLength() !=
                checkpoint.Turns.Count * 2 + 1)
        {
            return false;
        }

        JsonElement[] contextMessages =
            messages.EnumerateArray().ToArray();
        for (
            int index = 0;
            index < checkpoint.Turns.Count;
            index++)
        {
            PiAgentConversationCheckpointTurn expected =
                checkpoint.Turns[index];
            JsonElement user = contextMessages[index * 2];
            JsonElement assistant =
                contextMessages[index * 2 + 1];
            if (
                user.GetProperty("role").GetString() != "user" ||
                !MessageTextEquals(
                    user.GetProperty("content"),
                    expected.UserText) ||
                assistant.GetProperty("role").GetString() !=
                    "assistant" ||
                !MessageTextEquals(
                    assistant.GetProperty("content"),
                    expected.AssistantText))
            {
                return false;
            }
        }

        JsonElement current = contextMessages[^1];
        return
            current.GetProperty("role").GetString() == "user" &&
            MessageTextEquals(
                current.GetProperty("content"),
                currentPrompt);
    }

    private static bool MessageTextEquals(
        JsonElement content,
        string expected)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() == expected;
        }
        if (
            content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        string text = string.Concat(
            content
                .EnumerateArray()
                .Where(block =>
                    block.TryGetProperty(
                        "type",
                        out JsonElement type) &&
                    type.GetString() == "text")
                .Select(block =>
                    block.GetProperty("text").GetString()));
        return text == expected;
    }

    private static bool ProbeCheckpointAdmission()
    {
        PiAgentConversationCheckpointTurn duplicate = new(
            "duplicate-checkpoint-turn",
            "Prompt.",
            "Response.");
        bool duplicateRejected;
        try
        {
            _ = PiAgentConversationState.AdmitCheckpoint(
                new PiAgentConversationCheckpoint(
                    1,
                    [duplicate, duplicate]));
            duplicateRejected = false;
        }
        catch (ArgumentException)
        {
            duplicateRejected = true;
        }

        bool oversizedRejected;
        try
        {
            _ = PiAgentConversationState.AdmitCheckpoint(
                new PiAgentConversationCheckpoint(
                    1,
                    [new PiAgentConversationCheckpointTurn(
                        "oversized-checkpoint-turn",
                        new string('u', 16_384),
                        new string('a', 16_384))]));
            oversizedRejected = false;
        }
        catch (ArgumentException)
        {
            oversizedRejected = true;
        }
        return duplicateRejected && oversizedRejected;
    }

    private static async Task<bool> ProbeStartupRollbackAsync(
        PiAgentSidecarOptions sidecarOptions,
        CancellationToken cancellationToken)
    {
        string protectedRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (
            string.IsNullOrWhiteSpace(protectedRoot) ||
            !Directory.Exists(protectedRoot))
        {
            return false;
        }

        DisposableProbeProvider provider = new();
        PiAgentDesktopRuntime? unexpectedRuntime = null;
        bool rejected = false;
        try
        {
            unexpectedRuntime = await PiAgentDesktopRuntime.StartAsync(
                new PiAgentDesktopRuntimeOptions(
                    sidecarOptions,
                    protectedRoot),
                provider,
                cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        finally
        {
            if (unexpectedRuntime is not null)
            {
                await unexpectedRuntime.DisposeAsync();
            }
        }
        return rejected && provider.IsDisposed;
    }

    private static async Task<bool> SubmissionIsClosedAsync(
        PiAgentConversationState conversation,
        string turnId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await conversation.SubmitAsync(
                "This turn must be rejected after quiesce.",
                turnId,
                cancellationToken);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static string ResolveWorkspaceRoot(
        PiAgentSidecarOptions options) =>
        Directory.GetParent(options.HostScriptPath)?
            .Parent?.FullName ??
        throw new InvalidOperationException(
            "The runtime probe workspace could not be resolved.");

    private sealed class TemporaryCheckpointStoreFixture :
        IAsyncDisposable
    {
        public TemporaryCheckpointStoreFixture()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                $"jarvisv2-pi-checkpoint-{Guid.NewGuid():N}");
            Store = new PiAgentConversationCheckpointStore(
                RootDirectory);
        }

        public string RootDirectory { get; }
        public PiAgentConversationCheckpointStore Store { get; }

        public ValueTask DisposeAsync()
        {
            if (!Directory.Exists(RootDirectory))
            {
                return ValueTask.CompletedTask;
            }
            string temporaryRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            string admittedPrefix =
                temporaryRoot + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(RootDirectory);
            if (
                !fullRoot.StartsWith(
                    admittedPrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(fullRoot).StartsWith(
                    "jarvisv2-pi-checkpoint-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The diagnostic checkpoint root failed cleanup " +
                    "admission.");
            }
            Directory.Delete(fullRoot, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableProbeProvider :
        IDesktopModelProvider,
        IAsyncDisposable
    {
        private int disposed;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
            DesktopModelBrokerRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new DesktopModelCompleted(
                "stop",
                new DesktopModelUsage(0, 0, 0, 0));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref disposed, 1);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DiagnosticWorkspaceEditModelProvider :
        IDesktopModelProvider
    {
        private int requestSequence;

        public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
            DesktopModelBrokerRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            int sequence = Interlocked.Increment(
                ref requestSequence);
            if (sequence % 2 == 0)
            {
                yield return new DesktopModelTextDelta(
                    "JARVIS staged one exact edit for owner review.");
                yield return new DesktopModelCompleted(
                    "stop",
                    new DesktopModelUsage(12, 8, 0, 0));
                yield break;
            }

            (string oldText, string newText) = sequence switch
            {
                1 => ("owner-reviewed", "owner-approved"),
                3 => ("owner-approved", "model-second"),
                5 => ("owner-updated-elsewhere", "model-third"),
                7 => ("owner-updated-elsewhere", "model-expired"),
                _ => throw new InvalidOperationException(
                    "The diagnostic workspace edit sequence exceeded its fixture."),
            };
            string toolCallId = $"diagnostic-propose-edit-{sequence}";
            yield return new DesktopModelToolCallStarted(
                toolCallId,
                "propose_edit");
            yield return new DesktopModelToolCallDelta(
                toolCallId,
                JsonSerializer.Serialize(new
                {
                    path = "review.txt",
                    oldText,
                    newText,
                }));
            yield return new DesktopModelToolCallCompleted(toolCallId);
            yield return new DesktopModelCompleted(
                "toolUse",
                new DesktopModelUsage(14, 10, 0, 0));
        }
    }
}
