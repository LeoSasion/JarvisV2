using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentDesktopRuntimeOptions(
    PiAgentSidecarOptions Sidecar,
    string WorkspaceRoot,
    PiAgentConversationCheckpoint? ConversationCheckpoint = null,
    PiAgentConversationCheckpointStore? ConversationCheckpointStore = null);

public sealed record PiAgentWorkspaceTransactionRecoveryReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string? ProposalId,
    int FileCount,
    bool MutationPerformed);

public sealed class PiAgentDesktopRuntime : IAsyncDisposable
{
    public const string OwnershipModel =
        "desktop-owned-broker-sidecar-session-conversation";
    public const string ShutdownModel =
        "quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose";
    public const string CheckpointPersistenceModel =
        "ordered-terminal-autosave-fail-closed";
    public const int CheckpointSaveTimeoutMilliseconds = 5_000;

    private readonly DesktopModelBrokerServer broker;
    private readonly PiAgentSidecarController controller;
    private readonly PiAgentConversationCheckpointStore? checkpointStore;
    private readonly object checkpointPersistenceGate = new();
    private readonly SemaphoreSlim shutdownGate = new(1, 1);
    private readonly int shutdownTimeoutMilliseconds;
    private Task checkpointPersistenceTask = Task.CompletedTask;
    private PiAgentConversationCheckpoint? lastQueuedCheckpoint;
    private PiAgentConversationCheckpointStoreReceipt?
        lastCheckpointStoreReceipt;
    private Exception? checkpointPersistenceFailure;
    private bool checkpointPersistenceClosed;
    private int checkpointSaveCount;
    private int shutdownCompleted;
    private int disposeStarted;

    private PiAgentDesktopRuntime(
        DesktopModelBrokerServer broker,
        PiAgentSidecarController controller,
        PiAgentConversationState conversation,
        string workspaceRoot,
        PiAgentConversationCheckpointStore? checkpointStore,
        PiAgentConversationCheckpoint? restoredCheckpoint,
        bool checkpointLoadedFromStore,
        int restoredCheckpointTurnCount,
        PiAgentWorkspaceTransactionRecoveryReceipt transactionRecovery,
        int shutdownTimeoutMilliseconds)
    {
        this.broker = broker;
        this.controller = controller;
        Conversation = conversation;
        WorkspaceRoot = workspaceRoot;
        this.checkpointStore = checkpointStore;
        if (checkpointLoadedFromStore)
        {
            lastQueuedCheckpoint = restoredCheckpoint;
        }
        RestoredCheckpointTurnCount = restoredCheckpointTurnCount;
        WorkspaceTransactionRecovery = transactionRecovery;
        this.shutdownTimeoutMilliseconds = shutdownTimeoutMilliseconds;
        Conversation.TerminalCheckpointAvailable +=
            QueueCheckpointPersistence;
    }

    public PiAgentConversationState Conversation { get; }
    public string WorkspaceRoot { get; }
    public bool CredentialEnvironmentClean =>
        controller.CredentialEnvironmentClean;
    public int BrokerRequestCount => broker.RequestCount;
    public int BrokerFaultCount => broker.FaultCount;
    public int RestoredCheckpointTurnCount { get; }
    public PiAgentWorkspaceTransactionRecoveryReceipt
        WorkspaceTransactionRecovery { get; }
    public PiAgentConversationCheckpointStoreReceipt?
        LastCheckpointStoreReceipt
    {
        get
        {
            lock (checkpointPersistenceGate)
            {
                return lastCheckpointStoreReceipt;
            }
        }
    }
    public int CheckpointSaveCount
    {
        get
        {
            lock (checkpointPersistenceGate)
            {
                return checkpointSaveCount;
            }
        }
    }
    public bool CheckpointPersistenceFaulted
    {
        get
        {
            lock (checkpointPersistenceGate)
            {
                return checkpointPersistenceFailure is not null;
            }
        }
    }
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
        bool checkpointLoadedFromStore = false;
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
            checkpointLoadedFromStore = checkpoint is not null;
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
            (string canonicalWorkspaceRoot,
                PiAgentWorkspaceTransactionRecoveryReceipt
                    transactionRecovery) =
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
                checkpoint,
                checkpointLoadedFromStore,
                checkpoint?.Turns.Count ?? 0,
                transactionRecovery,
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
                    QueueCheckpointPersistence(
                        Conversation.ExportCheckpoint());
                }
                Task persistenceTask;
                lock (checkpointPersistenceGate)
                {
                    checkpointPersistenceClosed = true;
                    persistenceTask = checkpointPersistenceTask;
                }
                await persistenceTask.WaitAsync(
                    cancellationToken);
                Exception? persistenceFailure;
                lock (checkpointPersistenceGate)
                {
                    persistenceFailure =
                        checkpointPersistenceFailure;
                }
                if (persistenceFailure is not null)
                {
                    throw new InvalidOperationException(
                        "The desktop conversation checkpoint failed " +
                        "closed during persistence.",
                        persistenceFailure);
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
            Conversation.TerminalCheckpointAvailable -=
                QueueCheckpointPersistence;
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

    private void QueueCheckpointPersistence(
        PiAgentConversationCheckpoint checkpoint)
    {
        if (
            checkpointStore is null ||
            checkpoint.Turns.Count == 0)
        {
            return;
        }
        PiAgentConversationCheckpoint admitted =
            PiAgentConversationState.AdmitCheckpoint(checkpoint) ??
            throw new InvalidOperationException(
                "The terminal conversation checkpoint was absent.");
        lock (checkpointPersistenceGate)
        {
            if (
                checkpointPersistenceClosed ||
                checkpointPersistenceFailure is not null ||
                CheckpointsEqual(
                    lastQueuedCheckpoint,
                    admitted))
            {
                return;
            }
            lastQueuedCheckpoint = admitted;
            Task previous = checkpointPersistenceTask;
            checkpointPersistenceTask =
                PersistCheckpointAfterAsync(
                    previous,
                    admitted);
        }
    }

    private async Task PersistCheckpointAfterAsync(
        Task previous,
        PiAgentConversationCheckpoint checkpoint)
    {
        await previous.ConfigureAwait(false);
        lock (checkpointPersistenceGate)
        {
            if (checkpointPersistenceFailure is not null)
            {
                return;
            }
        }
        try
        {
            using CancellationTokenSource timeout = new(
                TimeSpan.FromMilliseconds(
                    CheckpointSaveTimeoutMilliseconds));
            PiAgentConversationCheckpointStoreReceipt receipt =
                await checkpointStore!.SaveAsync(
                    WorkspaceRoot,
                    checkpoint,
                    timeout.Token).ConfigureAwait(false);
            lock (checkpointPersistenceGate)
            {
                lastCheckpointStoreReceipt = receipt;
                checkpointSaveCount++;
            }
        }
        catch (Exception exception)
        {
            lock (checkpointPersistenceGate)
            {
                checkpointPersistenceFailure ??= exception;
            }
            try
            {
                await Conversation.QuiesceAsync(
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static bool CheckpointsEqual(
        PiAgentConversationCheckpoint? left,
        PiAgentConversationCheckpoint? right) =>
        ReferenceEquals(left, right) ||
        (
            left is not null &&
            right is not null &&
            left.SchemaVersion == right.SchemaVersion &&
            left.Turns.SequenceEqual(right.Turns)
        );

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

    private static (string WorkspaceRoot,
        PiAgentWorkspaceTransactionRecoveryReceipt TransactionRecovery)
        ValidateSessionReceipt(
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
        JsonElement recoveryData = data.GetProperty(
            "workspaceTransactionRecovery");
        JsonElement recoveryProposalId = recoveryData.GetProperty(
            "proposalId");
        PiAgentWorkspaceTransactionRecoveryReceipt transactionRecovery = new(
            recoveryData.GetProperty("schemaVersion").GetInt32(),
            recoveryData.GetProperty("receiptType").GetString()
                ?? string.Empty,
            recoveryData.GetProperty("result").GetString()
                ?? string.Empty,
            recoveryProposalId.ValueKind == JsonValueKind.Null
                ? null
                : recoveryProposalId.GetString(),
            recoveryData.GetProperty("fileCount").GetInt32(),
            recoveryData.GetProperty("mutationPerformed").GetBoolean());
        bool recoveryValid =
            transactionRecovery.SchemaVersion == 1 &&
            transactionRecovery.ReceiptType ==
                "jarvis2-workspace-change-set-recovery" &&
            transactionRecovery.Result is
                "none" or "rolled-back" or "completed" &&
            (transactionRecovery.Result == "none"
                ? transactionRecovery.ProposalId is null &&
                    transactionRecovery.FileCount == 0 &&
                    !transactionRecovery.MutationPerformed
                : transactionRecovery.ProposalId is not null &&
                    transactionRecovery.FileCount is >= 2 and <= 4 &&
                    transactionRecovery.MutationPerformed ==
                        (transactionRecovery.Result == "rolled-back"));
        bool valid =
            Path.IsPathFullyQualified(canonicalWorkspaceRoot) &&
            Directory.Exists(canonicalWorkspaceRoot) &&
            activeTools.SequenceEqual(
                [
                    "read",
                    "grep",
                    "find",
                    "ls",
                    "propose_edit",
                    "propose_patch",
                    "propose_create_file",
                    "propose_change_set",
                ]) &&
            recoveryValid &&
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
        return (canonicalWorkspaceRoot, transactionRecovery);
    }
}
