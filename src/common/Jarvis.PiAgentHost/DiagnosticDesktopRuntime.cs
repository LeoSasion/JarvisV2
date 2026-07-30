using System.Runtime.CompilerServices;

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
    bool QuiesceClosedSubmission,
    bool ShutdownCancelledActiveTurn,
    bool OrderlyShutdownPassed,
    bool StartupRollbackPassed,
    bool CredentialEnvironmentClean,
    int NormalBrokerRequestCount,
    int AbortBrokerRequestCount,
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
        bool normalQuiesceClosedSubmission;
        bool normalShutdownPassed;
        bool normalCredentialEnvironmentClean;

        DiagnosticDesktopModelProvider normalProvider = new(
            holdResponse: false);
        await using (
            PiAgentDesktopRuntime runtime =
                await PiAgentDesktopRuntime.StartAsync(
                    new PiAgentDesktopRuntimeOptions(
                        sidecarOptions,
                        workspaceRoot),
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

            await runtime.ShutdownAsync(cancellationToken);
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
            normalBrokerFaultCount + abortBrokerFaultCount;
        bool startupRollbackPassed =
            await ProbeStartupRollbackAsync(
                sidecarOptions,
                cancellationToken);
        bool passed =
            runtimeCompositionPassed &&
            multiTurnPassed &&
            toolRoundTripPassed &&
            normalQuiesceClosedSubmission &&
            shutdownCancelledActiveTurn &&
            abortQuiesceClosedSubmission &&
            normalShutdownPassed &&
            abortShutdownPassed &&
            startupRollbackPassed &&
            normalCredentialEnvironmentClean &&
            abortCredentialEnvironmentClean &&
            normalBrokerRequestCount == 4 &&
            abortBrokerRequestCount == 1 &&
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
            normalQuiesceClosedSubmission &&
                abortQuiesceClosedSubmission,
            shutdownCancelledActiveTurn,
            normalShutdownPassed && abortShutdownPassed,
            startupRollbackPassed,
            normalCredentialEnvironmentClean &&
                abortCredentialEnvironmentClean,
            normalBrokerRequestCount,
            abortBrokerRequestCount,
            brokerFaultCount,
            false,
            false,
            "diagnostic-only",
            "not-run",
            false);
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
}
