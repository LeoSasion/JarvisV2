using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentDesktopRuntimeOptions(
    PiAgentSidecarOptions Sidecar,
    string WorkspaceRoot,
    PiAgentConversationCheckpoint? ConversationCheckpoint = null,
    PiAgentConversationCheckpointStore? ConversationCheckpointStore = null);

public sealed class PiAgentDesktopRuntime : IAsyncDisposable
{
    public const string OwnershipModel =
        "desktop-owned-broker-sidecar-session-conversation";
    public const string ShutdownModel =
        "quiesce-cancel-checkpoint-save-sidecar-shutdown-broker-dispose";

    private readonly DesktopModelBrokerServer broker;
    private readonly PiAgentSidecarController controller;
    private readonly PiAgentConversationCheckpointStore? checkpointStore;
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private readonly int shutdownTimeoutMilliseconds;
    private int shutdownCompleted;
    private int disposeStarted;

    private PiAgentDesktopRuntime(
        DesktopModelBrokerServer broker,
        PiAgentSidecarController controller,
        PiAgentConversationState conversation,
        string workspaceRoot,
        PiAgentConversationCheckpointStore? checkpointStore,
        int restoredCheckpointTurnCount,
        int shutdownTimeoutMilliseconds)
    {
        this.broker = broker;
        this.controller = controller;
        Conversation = conversation;
        WorkspaceRoot = workspaceRoot;
        this.checkpointStore = checkpointStore;
        RestoredCheckpointTurnCount = restoredCheckpointTurnCount;
        this.shutdownTimeoutMilliseconds = shutdownTimeoutMilliseconds;
    }

    public PiAgentConversationState Conversation { get; }
    public string WorkspaceRoot { get; }
    public bool CredentialEnvironmentClean =>
        controller.CredentialEnvironmentClean;
    public int BrokerRequestCount => broker.RequestCount;
    public int BrokerFaultCount => broker.FaultCount;
    public int RestoredCheckpointTurnCount { get; }
    public PiAgentConversationCheckpointStoreReceipt?
        LastCheckpointStoreReceipt { get; private set; }
    public bool IsShutdown =>
        Volatile.Read(ref shutdownCompleted) != 0;

    public static async Task<PiAgentDesktopRuntime> StartAsync(
        PiAgentDesktopRuntimeOptions options,
        IDesktopModelProvider provider,
        SynchronizationContext? notificationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Sidecar);
        ArgumentNullException.ThrowIfNull(provider);
        ValidateOptions(options);
        PiAgentConversationCheckpoint? checkpoint =
            PiAgentConversationState.AdmitCheckpoint(
                options.ConversationCheckpoint);
        if (
            checkpoint is null &&
            options.ConversationCheckpointStore is not null)
        {
            checkpoint =
                await options.ConversationCheckpointStore.LoadAsync(
                    options.WorkspaceRoot,
                    cancellationToken);
        }

        DesktopModelBrokerServer broker =
            DesktopModelBrokerServer.Start(provider);
        PiAgentSidecarController? controller = null;
        try
        {
            controller = await PiAgentSidecarController.StartAsync(
                options.Sidecar with
                {
                    ModelBrokerPipePath = broker.PipePath,
                },
                cancellationToken);
            string sessionRequestId =
                $"desktop-session-{Guid.NewGuid():N}";
            using JsonDocument session =
                await controller.StartReadOnlySessionAsync(
                    options.WorkspaceRoot,
                    sessionRequestId,
                    checkpoint,
                    cancellationToken);
            string canonicalWorkspaceRoot =
                ValidateSessionReceipt(
                    session.RootElement,
                    checkpoint?.Turns.Count ?? 0);
            PiAgentConversationState conversation = new(
                controller,
                notificationContext,
                checkpoint);
            return new PiAgentDesktopRuntime(
                broker,
                controller,
                conversation,
                canonicalWorkspaceRoot,
                options.ConversationCheckpointStore,
                checkpoint?.Turns.Count ?? 0,
                options.Sidecar.ShutdownTimeoutMilliseconds);
        }
        catch
        {
            try
            {
                if (controller is not null)
                {
                    await controller.DisposeAsync();
                }
            }
            catch
            {
            }
            try
            {
                await broker.DisposeAsync();
            }
            catch
            {
            }
            throw;
        }
    }

    public async Task ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsShutdown)
        {
            return;
        }

        await shutdownGate.WaitAsync(cancellationToken);
        try
        {
            if (IsShutdown)
            {
                return;
            }
            await Conversation.QuiesceAsync(cancellationToken);
            try
            {
                if (checkpointStore is not null)
                {
                    PiAgentConversationCheckpoint checkpoint =
                        Conversation.ExportCheckpoint();
                    if (checkpoint.Turns.Count != 0)
                    {
                        LastCheckpointStoreReceipt =
                            await checkpointStore.SaveAsync(
                                WorkspaceRoot,
                                checkpoint,
                                cancellationToken);
                    }
                }
            }
            finally
            {
                await controller.ShutdownAsync(cancellationToken);
                Volatile.Write(ref shutdownCompleted, 1);
            }
        }
        finally
        {
            shutdownGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = new(
                TimeSpan.FromMilliseconds(
                    shutdownTimeoutMilliseconds + 5_000));
            try
            {
                await ShutdownAsync(timeout.Token);
            }
            catch (Exception exception)
                when (exception is
                    OperationCanceledException or
                    InvalidOperationException or
                    IOException)
            {
            }
        }
        finally
        {
            try
            {
                await controller.DisposeAsync();
            }
            finally
            {
                await broker.DisposeAsync();
            }
        }
    }

    private static void ValidateOptions(
        PiAgentDesktopRuntimeOptions options)
    {
        if (options.Sidecar.ModelBrokerPipePath is not null)
        {
            throw new ArgumentException(
                "The desktop runtime must own the model broker pipe.",
                nameof(options));
        }
        if (
            !Path.IsPathFullyQualified(options.WorkspaceRoot) ||
            !Directory.Exists(options.WorkspaceRoot))
        {
            throw new ArgumentException(
                "WorkspaceRoot must name an existing absolute directory.",
                nameof(options));
        }
    }

    private static string ValidateSessionReceipt(
        JsonElement root,
        int expectedRestoredTurnCount)
    {
        if (!root.GetProperty("success").GetBoolean())
        {
            string errorCode = root
                .GetProperty("error")
                .GetProperty("code")
                .GetString() ?? "session-admission-failed";
            throw new InvalidOperationException(
                $"The desktop runtime session failed closed: {errorCode}.");
        }

        JsonElement data = root.GetProperty("data");
        string canonicalWorkspaceRoot =
            data.GetProperty("workspaceRoot").GetString() ??
            throw new InvalidOperationException(
                "The desktop runtime session omitted its workspace root.");
        string[] activeTools = data
            .GetProperty("activeTools")
            .EnumerateArray()
            .Select(tool => tool.GetString() ?? string.Empty)
            .ToArray();
        bool valid =
            Path.IsPathFullyQualified(canonicalWorkspaceRoot) &&
            Directory.Exists(canonicalWorkspaceRoot) &&
            activeTools.SequenceEqual(["read", "grep", "find", "ls"]) &&
            !data.GetProperty("sessionPersisted").GetBoolean() &&
            data.GetProperty("modelSelected").GetBoolean() &&
            data.GetProperty("promptingEnabled").GetBoolean() &&
            data.GetProperty("modelProvider").GetString() ==
                DesktopModelBrokerServer.ProviderId &&
            data.GetProperty("modelId").GetString() ==
                DesktopModelBrokerServer.ModelId &&
            data.GetProperty("restoredTurnCount").GetInt32() ==
                expectedRestoredTurnCount &&
            data.GetProperty(
                "restoredContextMessageCount").GetInt32() ==
                expectedRestoredTurnCount * 2 &&
            !data.GetProperty("resourceDiscoveryEnabled").GetBoolean() &&
            !data.GetProperty("modelNetworkAllowed").GetBoolean();
        if (!valid)
        {
            throw new InvalidOperationException(
                "The desktop runtime session receipt failed admission.");
        }
        return canonicalWorkspaceRoot;
    }
}
