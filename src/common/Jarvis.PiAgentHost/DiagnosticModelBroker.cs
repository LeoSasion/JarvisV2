using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentDesktopBrokerProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Protocol,
    bool ReadyObserved,
    bool CapabilitiesPassed,
    bool SessionCreationPassed,
    bool PromptPassed,
    bool MultiTurnPassed,
    bool ToolRoundTripPassed,
    int ToolExecutionCount,
    int CompletedTurnCount,
    bool AbortPassed,
    string AbortStatus,
    bool InvalidToolRejected,
    int ProviderFaultCount,
    bool ConcurrentResponsePump,
    string Response,
    int DeltaCount,
    int BrokerRequestCount,
    int BrokerFaultCount,
    bool NamedPipeOnly,
    bool CredentialTransportAllowed,
    bool PiSidecarModelNetworkAllowed,
    string LiveModelNetwork,
    string LiveExplorer,
    bool MutationPerformed);

internal sealed class DiagnosticDesktopModelBroker : IAsyncDisposable
{
    public const string Protocol = DesktopModelBrokerServer.Protocol;

    private readonly DiagnosticDesktopModelProvider provider;
    private readonly DesktopModelBrokerServer server;

    public string PipePath => server.PipePath;
    public int FaultCount => server.FaultCount;
    public int RequestCount => server.RequestCount;

    private DiagnosticDesktopModelBroker(bool holdResponse)
    {
        provider = new DiagnosticDesktopModelProvider(holdResponse);
        server = DesktopModelBrokerServer.Start(provider);
    }

    public static DiagnosticDesktopModelBroker Start(
        bool holdResponse = false)
    {
        return new DiagnosticDesktopModelBroker(holdResponse);
    }

    public async Task WaitForRequestAsync(
        CancellationToken cancellationToken)
    {
        await provider.WaitForRequestAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
    }
}

public static class PiAgentDesktopBrokerProbe
{
    public static async Task<PiAgentDesktopBrokerProbeReceipt> RunAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        await using DiagnosticDesktopModelBroker broker =
            DiagnosticDesktopModelBroker.Start();
        PiAgentSidecarOptions brokerOptions = options with
        {
            ModelBrokerPipePath = broker.PipePath,
        };
        await using PiAgentSidecarController controller =
            await PiAgentSidecarController.StartAsync(
                brokerOptions,
                cancellationToken);

        using JsonDocument capabilities = await controller.RequestAsync(
            "capabilities",
            "broker-capabilities",
            cancellationToken);
        bool capabilitiesPassed =
            capabilities.RootElement.GetProperty("success").GetBoolean() &&
            capabilities.RootElement
                .GetProperty("data")
                .GetProperty("promptingEnabled")
                .GetBoolean();

        string workspaceRoot =
            Directory.GetParent(options.HostScriptPath)?
                .Parent?.FullName
            ?? throw new InvalidOperationException(
                "The broker probe workspace could not be resolved.");
        using JsonDocument admittedSession =
            await controller.StartReadOnlySessionAsync(
                workspaceRoot,
                "broker-session",
                cancellationToken);
        JsonElement sessionData =
            admittedSession.RootElement.GetProperty("data");
        bool sessionCreationPassed =
            admittedSession.RootElement
                .GetProperty("success")
                .GetBoolean() &&
            sessionData
                .GetProperty("promptingEnabled")
                .GetBoolean() &&
            sessionData
                .GetProperty("modelProvider")
                .GetString() == DesktopModelBrokerServer.ProviderId &&
            sessionData
                .GetProperty("modelId")
                .GetString() == DesktopModelBrokerServer.ModelId;

        PiAgentPromptResult firstPrompt = await controller.PromptAsync(
            "Confirm the desktop-owned model broker is online.",
            "broker-prompt-one",
            cancellationToken);
        bool promptPassed =
            firstPrompt.Response == "JARVIS desktop broker online." &&
            firstPrompt.DeltaCount == 2 &&
            firstPrompt.ToolExecutionCount == 0;

        PiAgentPromptResult secondPrompt = await controller.PromptAsync(
            "Confirm that the same broker accepts another turn.",
            "broker-prompt-two",
            cancellationToken);
        bool multiTurnPassed =
            secondPrompt.Response == "JARVIS desktop broker online." &&
            secondPrompt.DeltaCount == 2 &&
            secondPrompt.ToolExecutionCount == 0 &&
            broker.RequestCount == 2 &&
            broker.FaultCount == 0;

        PiAgentPromptResult toolPrompt = await controller.PromptAsync(
            "Read the admitted package manifest and confirm the tool path.",
            "broker-tool-turn",
            cancellationToken);
        bool toolRoundTripPassed =
            toolPrompt.Response == "JARVIS workspace tool online." &&
            toolPrompt.DeltaCount == 1 &&
            toolPrompt.ToolExecutionCount == 1 &&
            broker.RequestCount == 4 &&
            broker.FaultCount == 0;

        await controller.ShutdownAsync(cancellationToken);
        await using DiagnosticDesktopModelBroker abortBroker =
            DiagnosticDesktopModelBroker.Start(holdResponse: true);
        PiAgentSidecarOptions abortOptions = options with
        {
            ModelBrokerPipePath = abortBroker.PipePath,
        };
        await using PiAgentSidecarController abortController =
            await PiAgentSidecarController.StartAsync(
                abortOptions,
                cancellationToken);
        using JsonDocument abortSession =
            await abortController.StartReadOnlySessionAsync(
                workspaceRoot,
                "abort-session",
                cancellationToken);
        if (!abortSession.RootElement
            .GetProperty("success")
            .GetBoolean())
        {
            throw new InvalidOperationException(
                "The abort probe session failed admission.");
        }
        PiAgentTurnHandle abortHandle =
            await abortController.StartTurnAsync(
                "Wait until the desktop cancels this turn.",
                "abort-target",
                cancellationToken);
        await abortBroker.WaitForRequestAsync(cancellationToken);
        await abortController.AbortTurnAsync(
            abortHandle.TurnId,
            "abort-command",
            cancellationToken);
        PiAgentTurnResult abortResult =
            await abortController.WaitForTurnAsync(
                abortHandle,
                cancellationToken);
        bool abortPassed =
            !abortResult.Success &&
            abortResult.Status == "aborted" &&
            abortResult.ErrorCode == "turn-aborted" &&
            abortBroker.RequestCount == 1;
        await abortController.ShutdownAsync(cancellationToken);

        await using DesktopModelBrokerServer invalidToolBroker =
            DesktopModelBrokerServer.Start(
                new InvalidToolDesktopModelProvider());
        PiAgentSidecarOptions invalidToolOptions = options with
        {
            ModelBrokerPipePath = invalidToolBroker.PipePath,
        };
        await using PiAgentSidecarController invalidToolController =
            await PiAgentSidecarController.StartAsync(
                invalidToolOptions,
                cancellationToken);
        using JsonDocument invalidToolSession =
            await invalidToolController.StartReadOnlySessionAsync(
                workspaceRoot,
                "invalid-tool-session",
                cancellationToken);
        if (!invalidToolSession.RootElement
            .GetProperty("success")
            .GetBoolean())
        {
            throw new InvalidOperationException(
                "The invalid-tool probe session failed admission.");
        }
        bool invalidToolRejected = false;
        try
        {
            await invalidToolController.PromptAsync(
                "Reject a provider attempt to invoke an unadmitted tool.",
                "invalid-tool-turn",
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            invalidToolRejected =
                invalidToolBroker.RequestCount == 1 &&
                invalidToolBroker.FaultCount == 1;
        }
        await invalidToolController.ShutdownAsync(cancellationToken);

        int brokerRequestCount =
            broker.RequestCount + abortBroker.RequestCount;
        int brokerFaultCount =
            broker.FaultCount + abortBroker.FaultCount;
        bool passed =
            capabilitiesPassed &&
            sessionCreationPassed &&
            promptPassed &&
            multiTurnPassed &&
            toolRoundTripPassed &&
            abortPassed &&
            invalidToolRejected &&
            brokerRequestCount == 5 &&
            brokerFaultCount == 0;
        return new PiAgentDesktopBrokerProbeReceipt(
            1,
            "jarvisv2-pi-desktop-broker-bridge-probe",
            passed ? "passed" : "failed",
            DiagnosticDesktopModelBroker.Protocol,
            true,
            capabilitiesPassed,
            sessionCreationPassed,
            promptPassed,
            multiTurnPassed,
            toolRoundTripPassed,
            toolPrompt.ToolExecutionCount,
            3,
            abortPassed,
            abortResult.Status,
            invalidToolRejected,
            invalidToolBroker.FaultCount,
            true,
            firstPrompt.Response,
            firstPrompt.DeltaCount,
            brokerRequestCount,
            brokerFaultCount,
            true,
            false,
            false,
            "diagnostic-only",
            "not-run",
            false);
    }
}

internal sealed class InvalidToolDesktopModelProvider :
    IDesktopModelProvider
{
    public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
        DesktopModelBrokerRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new DesktopModelToolCallStarted(
            "invalid-tool-1",
            "bash");
    }
}
