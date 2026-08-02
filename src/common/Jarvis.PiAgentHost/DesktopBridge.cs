using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentSidecarOptions(
    string NodeExecutablePath,
    string HostScriptPath,
    int MaximumFrameBytes = 131_072,
    int RequestTimeoutMilliseconds = 10_000,
    int ShutdownTimeoutMilliseconds = 3_000,
    string? ModelBrokerPipePath = null);

public sealed record PiAgentPromptResult(
    string Response,
    int DeltaCount,
    int ToolExecutionCount);

public sealed record PiAgentTurnResult(
    string TurnId,
    bool Success,
    string Status,
    string Response,
    int DeltaCount,
    int ToolExecutionCount,
    string? ErrorCode);

public abstract record PiAgentTurnStreamEvent(
    string TurnId,
    int Sequence);

public sealed record PiAgentAssistantTextDelta(
    string TurnId,
    int Sequence,
    string Delta) : PiAgentTurnStreamEvent(TurnId, Sequence);

public sealed record PiAgentToolExecutionStarted(
    string TurnId,
    int Sequence,
    string ToolCallId,
    string ToolName) : PiAgentTurnStreamEvent(TurnId, Sequence);

public sealed record PiAgentToolExecutionCompleted(
    string TurnId,
    int Sequence,
    string ToolCallId,
    string ToolName,
    bool IsError) : PiAgentTurnStreamEvent(TurnId, Sequence);

public sealed record PiAgentWorkspaceEditProposed(
    string TurnId,
    int Sequence,
    int SchemaVersion,
    string ProposalId,
    string Operation,
    string RelativePath,
    string BeforeSha256,
    string OldText,
    string NewText,
    IReadOnlyList<PiAgentWorkspacePatchHunk> PatchHunks) :
    PiAgentTurnStreamEvent(TurnId, Sequence)
{
    public IReadOnlyList<PiAgentWorkspaceFileChange> FileChanges
        { get; init; } = [];
}

public sealed record PiAgentWorkspacePatchHunk(
    int Ordinal,
    string OldText,
    string NewText);

public sealed record PiAgentWorkspaceFileChange(
    int Ordinal,
    string Operation,
    string RelativePath,
    string BeforeSha256,
    string OldText,
    string NewText,
    IReadOnlyList<PiAgentWorkspacePatchHunk> PatchHunks);

public sealed record PiAgentTurnCompleted(
    string TurnId,
    int Sequence,
    PiAgentTurnResult Result) : PiAgentTurnStreamEvent(TurnId, Sequence);

public sealed record PiAgentWorkspaceEditDecisionReceipt(
    int SchemaVersion,
    string ProposalId,
    string Operation,
    string RelativePath,
    string BeforeSha256,
    string? AfterSha256,
    string Status,
    bool MutationPerformed)
{
    public string? TransactionModel { get; init; }
    public IReadOnlyList<PiAgentWorkspaceFileDecisionReceipt> Files
        { get; init; } = [];
}

public sealed record PiAgentWorkspaceFileDecisionReceipt(
    int Ordinal,
    string Operation,
    string RelativePath,
    string BeforeSha256,
    string? AfterSha256);

public sealed class PiAgentWorkspaceEditDecisionException(
    string errorCode,
    string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class PiAgentTurnHandle
{
    private readonly ChannelReader<PiAgentTurnStreamEvent> events;
    private int readerClaimed;

    internal PiAgentTurnHandle(
        string turnId,
        Task<PiAgentTurnResult> completion,
        ChannelReader<PiAgentTurnStreamEvent> events)
    {
        TurnId = turnId;
        Completion = completion;
        this.events = events;
    }

    public string TurnId { get; }
    public Task<PiAgentTurnResult> Completion { get; }

    public IAsyncEnumerable<PiAgentTurnStreamEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref readerClaimed, 1) != 0)
        {
            throw new InvalidOperationException(
                "A Pi Agent turn event stream has one desktop consumer.");
        }
        return events.ReadAllAsync(cancellationToken);
    }
}

public sealed record PiAgentDesktopProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Protocol,
    string Package,
    string InstalledVersion,
    string IntegrationMode,
    bool DesktopLaunchImplemented,
    bool ReadyObserved,
    bool HelloPassed,
    bool CapabilitiesPassed,
    bool SessionCreationPassed,
    bool WorkspaceBound,
    bool ShutdownPassed,
    bool PiOffline,
    bool CredentialEnvironmentScrubbed,
    IReadOnlyList<string> InitialTools,
    IReadOnlyList<string> DeniedTools,
    bool SessionCreationEnabled,
    bool PromptingEnabled,
    bool SessionPersisted,
    bool CredentialTransportAllowed,
    bool WorkspacePatchSupported,
    int WorkspacePatchMinimumHunks,
    int WorkspacePatchMaximumHunks,
    int WorkspacePatchMaximumPreviewBytes,
    bool WorkspaceChangeSetSupported,
    int WorkspaceChangeSetMinimumFiles,
    int WorkspaceChangeSetMaximumFiles,
    int WorkspaceChangeSetMaximumPreviewBytes,
    bool WorkspaceChangeSetRecoveryBeforeToolsPassed,
    bool WorkspaceChangeSetRecoveryAvailableToModel,
    bool SimultaneousMultiPathVisibilityClaimed,
    bool ShellMutationSupported,
    bool ExplorerMutationSupported,
    bool SystemMutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed);

public sealed record PiAgentBridgeFaultScenario(
    string Name,
    bool Passed,
    string ObservedFailure);

public sealed record PiAgentBridgeFaultReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    IReadOnlyList<PiAgentBridgeFaultScenario> Scenarios,
    bool SessionCreationEnabled,
    bool ShellMutationSupported,
    bool ExplorerMutationSupported,
    bool SystemMutationSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed);

public sealed class PiAgentSidecarController : IAsyncDisposable
{
    public const int TurnEventBufferCapacity = 512;

    private sealed class PendingTurn
    {
        public Channel<PiAgentTurnStreamEvent> Events { get; } =
            Channel.CreateBounded<PiAgentTurnStreamEvent>(
                new BoundedChannelOptions(TurnEventBufferCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });
        public Dictionary<string, string> ActiveTools { get; } =
            new(StringComparer.Ordinal);
        public StringBuilder Response { get; } = new();
        public int DeltaCount { get; set; }
        public int EventSequence { get; set; }
        public int ToolExecutionCount { get; set; }
        public int AwaitingWorkspaceEditProposalCount { get; set; }
        public TaskCompletionSource<PiAgentTurnResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public const string ContractId = "jarvisv2-pi-agent-desktop-host-v1";
    public const string PackageName = "@earendil-works/pi-coding-agent";
    public const string ExpectedVersion = "0.82.1";

    private static readonly IReadOnlySet<string> AllowedTurnToolNames =
        new HashSet<string>(
            [
                "read",
                "grep",
                "find",
                "ls",
                "propose_edit",
                "propose_patch",
                "propose_create_file",
                "propose_change_set",
            ],
            StringComparer.Ordinal);

    public const string WorkspaceFileAbsentSha256 =
        "679ac4df69d6bb3057107f0831a8a336ab25fc6b07a1679eb5ee97773ec0eaa3";

    private static readonly Regex WorkspaceEditProposalIdPattern = new(
        @"\Aworkspace-edit-[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant);

    private static readonly string[] RequiredChildEnvironmentVariables =
    [
        "APPDATA",
        "HOMEDRIVE",
        "HOMEPATH",
        "LOCALAPPDATA",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PiAgentSidecarOptions options;
    private readonly Process process;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<
        string,
        TaskCompletionSource<JsonDocument>> pendingResponses = new(
            StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingTurn> pendingTurns =
        new(StringComparer.Ordinal);
    private readonly CancellationTokenSource outputPumpCancellation = new();
    private readonly Task<string> stderrTask;
    private Task outputPumpTask = Task.CompletedTask;
    private bool admitted;
    private bool shutdownRequested;
    private bool shutdownCompleted;

    public bool CredentialEnvironmentClean { get; private set; }

    private PiAgentSidecarController(
        PiAgentSidecarOptions options,
        Process process)
    {
        this.options = options;
        this.process = process;
        stderrTask = process.StandardError.ReadToEndAsync();
    }

    public static async Task<PiAgentSidecarController> StartAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        ProcessStartInfo startInfo = new()
        {
            FileName = options.NodeExecutablePath,
            WorkingDirectory =
                Directory.GetParent(options.HostScriptPath)?.Parent?.FullName
                ?? throw new InvalidOperationException(
                    "The sidecar project root could not be resolved."),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add(options.HostScriptPath);
        startInfo.ArgumentList.Add("serve");
        startInfo.Environment.Clear();
        foreach (string variable in RequiredChildEnvironmentVariables)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                startInfo.Environment[variable] = value;
            }
        }
        startInfo.Environment["PI_OFFLINE"] = "1";
        if (options.ModelBrokerPipePath is not null)
        {
            startInfo.Environment["JARVIS_MODEL_BROKER_PIPE"] =
                options.ModelBrokerPipePath;
        }

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                "The Pi Agent sidecar process did not start.");
        }

        PiAgentSidecarController controller = new(options, process);
        try
        {
            using CancellationTokenSource timeout =
                CreateTimeout(
                    cancellationToken,
                    options.RequestTimeoutMilliseconds);
            using JsonDocument ready = await controller.ReadFrameAsync(
                timeout.Token);
            ValidateReady(
                ready.RootElement,
                options.ModelBrokerPipePath is not null);
            controller.CredentialEnvironmentClean = ready.RootElement
                .GetProperty("credentialEnvironmentClean")
                .GetBoolean();
            controller.admitted = true;
            controller.outputPumpTask = controller.PumpOutputAsync(
                controller.outputPumpCancellation.Token);
            return controller;
        }
        catch
        {
            await controller.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonDocument> RequestAsync(
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        return await SendRequestAsync(
            new { type, id },
            type,
            id,
            cancellationToken);
    }

    public async Task<JsonDocument> StartReadOnlySessionAsync(
        string workspaceRoot,
        string id,
        CancellationToken cancellationToken)
    {
        return await StartReadOnlySessionAsync(
            workspaceRoot,
            id,
            conversationCheckpoint: null,
            cancellationToken);
    }

    public async Task<JsonDocument> StartReadOnlySessionAsync(
        string workspaceRoot,
        string id,
        PiAgentConversationCheckpoint? conversationCheckpoint,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "workspaceRoot must name an existing absolute directory.",
                nameof(workspaceRoot));
        }
        PiAgentConversationCheckpoint? admittedCheckpoint =
            PiAgentConversationState.AdmitCheckpoint(
                conversationCheckpoint);
        return await SendRequestAsync(
            new
            {
                type = "start_session",
                id,
                workspaceRoot,
                conversationCheckpoint = admittedCheckpoint,
            },
            "start_session",
            id,
            cancellationToken);
    }

    public async Task<PiAgentPromptResult> PromptAsync(
        string text,
        string id,
        CancellationToken cancellationToken)
    {
        PiAgentTurnHandle handle = await StartTurnAsync(
            text,
            id,
            cancellationToken);
        Task drainTask = DrainTurnEventsAsync(handle);
        PiAgentTurnResult result;
        try
        {
            result = await WaitForTurnAsync(
                handle,
                cancellationToken);
        }
        catch
        {
            _ = drainTask.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
        await drainTask;
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"The Pi Agent turn failed closed: {result.ErrorCode}.");
        }
        return new PiAgentPromptResult(
            result.Response,
            result.DeltaCount,
            result.ToolExecutionCount);
    }

    public async Task<PiAgentTurnHandle> StartTurnAsync(
        string text,
        string id,
        CancellationToken cancellationToken)
    {
        if (options.ModelBrokerPipePath is null)
        {
            throw new InvalidOperationException(
                "Prompting requires a desktop-owned model broker.");
        }
        if (string.IsNullOrWhiteSpace(text) ||
            Encoding.UTF8.GetByteCount(text) > 16_384 ||
            string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "Prompt text and id do not match the reviewed limits.");
        }

        PendingTurn pendingTurn = new();
        if (!pendingTurns.TryAdd(id, pendingTurn))
        {
            throw new InvalidOperationException(
                "The Pi Agent turn id is already in use.");
        }
        try
        {
            using JsonDocument response = await SendRequestAsync(
                new
                {
                    type = "start_turn",
                    id,
                    text,
                },
                "start_turn",
                id,
                cancellationToken);
            JsonElement root = response.RootElement;
            if (!root.GetProperty("success").GetBoolean())
            {
                string code = root
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetString() ?? "turn-start-failed";
                throw new InvalidOperationException(
                    $"The Pi Agent turn failed to start: {code}.");
            }
            JsonElement data = root.GetProperty("data");
            if (
                data.GetProperty("turnId").GetString() != id ||
                data.GetProperty("status").GetString() != "started")
            {
                throw new InvalidOperationException(
                    "The Pi Agent turn start receipt was invalid.");
            }
            return new PiAgentTurnHandle(
                id,
                pendingTurn.Completion.Task,
                pendingTurn.Events.Reader);
        }
        catch (Exception exception)
        {
            pendingTurns.TryRemove(id, out _);
            pendingTurn.Events.Writer.TryComplete(exception);
            throw;
        }
    }

    public async Task<PiAgentTurnResult> WaitForTurnAsync(
        PiAgentTurnHandle handle,
        CancellationToken cancellationToken)
    {
        return await handle.Completion.WaitAsync(cancellationToken);
    }

    private static async Task DrainTurnEventsAsync(
        PiAgentTurnHandle handle)
    {
        await foreach (
            PiAgentTurnStreamEvent _ in handle.ReadEventsAsync())
        {
        }
    }

    public async Task AbortTurnAsync(
        string turnId,
        string id,
        CancellationToken cancellationToken)
    {
        if (!pendingTurns.ContainsKey(turnId))
        {
            throw new InvalidOperationException(
                "The requested Pi Agent turn is not pending.");
        }
        using JsonDocument response = await SendRequestAsync(
            new
            {
                type = "abort_turn",
                id,
                turnId,
            },
            "abort_turn",
            id,
            cancellationToken);
        JsonElement root = response.RootElement;
        if (!root.GetProperty("success").GetBoolean())
        {
            string code = root
                .GetProperty("error")
                .GetProperty("code")
                .GetString() ?? "abort-failed";
            throw new InvalidOperationException(
                $"The Pi Agent abort failed closed: {code}.");
        }
        JsonElement data = root.GetProperty("data");
        if (
            data.GetProperty("turnId").GetString() != turnId ||
            data.GetProperty("status").GetString() != "abort-requested")
        {
            throw new InvalidOperationException(
                "The Pi Agent abort receipt was invalid.");
        }
    }

    public async Task<PiAgentWorkspaceEditDecisionReceipt>
        CommitWorkspaceEditAsync(
            string proposalId,
            string beforeSha256,
            string id,
            CancellationToken cancellationToken)
    {
        ValidateWorkspaceEditDecisionRequest(
            proposalId,
            beforeSha256);
        using JsonDocument response = await SendRequestAsync(
            new
            {
                type = "commit_workspace_edit",
                id,
                proposalId,
                beforeSha256,
            },
            "commit_workspace_edit",
            id,
            cancellationToken);
        return ParseWorkspaceEditDecisionResponse(
            response.RootElement,
            proposalId,
            beforeSha256,
            "applied",
            mutationExpected: true);
    }

    public async Task<PiAgentWorkspaceEditDecisionReceipt>
        DiscardWorkspaceEditAsync(
            string proposalId,
            string beforeSha256,
            string id,
            CancellationToken cancellationToken)
    {
        ValidateWorkspaceEditDecisionRequest(
            proposalId,
            beforeSha256);
        using JsonDocument response = await SendRequestAsync(
            new
            {
                type = "discard_workspace_edit",
                id,
                proposalId,
                beforeSha256,
            },
            "discard_workspace_edit",
            id,
            cancellationToken);
        return ParseWorkspaceEditDecisionResponse(
            response.RootElement,
            proposalId,
            beforeSha256,
            "rejected",
            mutationExpected: false);
    }

    private async Task<JsonDocument> SendRequestAsync<TRequest>(
        TRequest request,
        string type,
        string id,
        CancellationToken cancellationToken)
    {
        if (shutdownCompleted)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar has already shut down.");
        }
        if (string.IsNullOrWhiteSpace(type) ||
            string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "Request type and id must be non-empty.");
        }

        TaskCompletionSource<JsonDocument> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingResponses.TryAdd(id, completion))
        {
            throw new InvalidOperationException(
                "The Pi Agent request id is already in use.");
        }
        try
        {
            using CancellationTokenSource timeout =
                CreateTimeout(
                    cancellationToken,
                    options.RequestTimeoutMilliseconds);
            await WriteRequestAsync(request, timeout.Token);
            JsonDocument response = await completion.Task.WaitAsync(
                timeout.Token);
            try
            {
                ValidateResponseEnvelope(response.RootElement, type, id);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        finally
        {
            pendingResponses.TryRemove(id, out _);
        }
    }

    private async Task WriteRequestAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken)
    {
        if (shutdownCompleted)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar has already shut down.");
        }
        string payload = JsonSerializer.Serialize(
            request,
            SerializerOptions);
        if (Encoding.UTF8.GetByteCount(payload) > options.MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The outgoing Pi Agent frame exceeds the contract limit.");
        }

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteLineAsync(
                payload.AsMemory(),
                cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task PumpOutputAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                JsonDocument frame = await ReadFrameAsync(
                    cancellationToken);
                bool transferred = false;
                try
                {
                    JsonElement root = frame.RootElement;
                    string? type = root.GetProperty("type").GetString();
                    if (type == "response")
                    {
                        if (
                            root.GetProperty("command").GetString() ==
                                "shutdown" &&
                            root.GetProperty("success").GetBoolean())
                        {
                            shutdownRequested = true;
                        }
                        string id =
                            root.GetProperty("id").GetString()
                            ?? throw new InvalidOperationException(
                                "The Pi Agent response id was missing.");
                        if (!pendingResponses.TryRemove(id, out
                            TaskCompletionSource<JsonDocument>? pending))
                        {
                            throw new InvalidOperationException(
                                "The Pi Agent response id was not pending.");
                        }
                        transferred = pending.TrySetResult(frame);
                        if (!transferred)
                        {
                            throw new InvalidOperationException(
                                "The Pi Agent response was already completed.");
                        }
                    }
                    else if (type == "event")
                    {
                        await RouteTurnEventAsync(
                            root,
                            cancellationToken);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "The Pi Agent output frame type was not admitted.");
                    }
                }
                finally
                {
                    if (!transferred)
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (shutdownRequested)
        {
            FailPendingOperations(exception);
        }
        catch (Exception exception)
        {
            FailPendingOperations(exception);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private async Task RouteTurnEventAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        string turnId =
            root.GetProperty("requestId").GetString()
            ?? throw new InvalidOperationException(
                "The Pi Agent event turn id was missing.");
        if (!pendingTurns.TryGetValue(
            turnId,
            out PendingTurn? pending))
        {
            throw new InvalidOperationException(
                "The Pi Agent event turn id was not pending.");
        }

        string? eventName = root.GetProperty("event").GetString();
        if (eventName == "assistant_text_delta")
        {
            string delta =
                root.GetProperty("delta").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent emitted an invalid text delta.");
            pending.Response.Append(delta);
            pending.DeltaCount++;
            await WriteTurnEventAsync(
                pending,
                new PiAgentAssistantTextDelta(
                    turnId,
                    NextEventSequence(pending),
                    delta),
                cancellationToken);
            return;
        }
        if (eventName == "tool_execution_start")
        {
            string toolCallId =
                root.GetProperty("toolCallId").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent tool call id was missing.");
            string toolName =
                root.GetProperty("toolName").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent tool name was missing.");
            if (
                string.IsNullOrWhiteSpace(toolCallId) ||
                string.IsNullOrWhiteSpace(toolName) ||
                !AllowedTurnToolNames.Contains(toolName) ||
                !pending.ActiveTools.TryAdd(toolCallId, toolName))
            {
                throw new InvalidOperationException(
                    "The Pi Agent emitted an invalid tool start.");
            }
            pending.ToolExecutionCount++;
            await WriteTurnEventAsync(
                pending,
                new PiAgentToolExecutionStarted(
                    turnId,
                    NextEventSequence(pending),
                    toolCallId,
                    toolName),
                cancellationToken);
            return;
        }
        if (eventName == "tool_execution_end")
        {
            string toolCallId =
                root.GetProperty("toolCallId").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent tool call id was missing.");
            string toolName =
                root.GetProperty("toolName").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent tool name was missing.");
            bool isError = root.GetProperty("isError").GetBoolean();
            if (
                !pending.ActiveTools.Remove(
                    toolCallId,
                    out string? activeToolName) ||
                activeToolName != toolName)
            {
                throw new InvalidOperationException(
                    "The Pi Agent ended an inactive tool call.");
            }
            if (
                (toolName is
                    "propose_edit" or
                    "propose_patch" or
                    "propose_create_file" or
                    "propose_change_set") &&
                !isError)
            {
                pending.AwaitingWorkspaceEditProposalCount++;
            }
            await WriteTurnEventAsync(
                pending,
                new PiAgentToolExecutionCompleted(
                    turnId,
                    NextEventSequence(pending),
                    toolCallId,
                    toolName,
                    isError),
                cancellationToken);
            return;
        }
        if (eventName == "workspace_edit_proposed")
        {
            int schemaVersion =
                root.GetProperty("schemaVersion").GetInt32();
            string proposalId =
                root.GetProperty("proposalId").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent workspace edit proposal id was missing.");
            string operation =
                root.GetProperty("operation").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent workspace proposal operation was missing.");
            string beforeSha256 =
                root.GetProperty("beforeSha256").GetString()
                ?? throw new InvalidOperationException(
                    "The Pi Agent workspace edit hash was missing.");
            string relativePath = string.Empty;
            string oldText = string.Empty;
            string newText = string.Empty;
            PiAgentWorkspacePatchHunk[] patchHunks = [];
            PiAgentWorkspaceFileChange[] fileChanges = [];
            bool proposalValid;
            if (schemaVersion == 4 && operation == "change-set")
            {
                fileChanges = root
                    .GetProperty("changes")
                    .EnumerateArray()
                    .Select(ParseWorkspaceFileChange)
                    .ToArray();
                proposalValid =
                    IsValidWorkspaceChangeSet(
                        fileChanges,
                        beforeSha256);
            }
            else
            {
                relativePath =
                    root.GetProperty("relativePath").GetString()
                    ?? throw new InvalidOperationException(
                        "The Pi Agent workspace edit path was missing.");
                oldText =
                    root.GetProperty("oldText").GetString()
                    ?? throw new InvalidOperationException(
                        "The Pi Agent workspace edit old text was missing.");
                newText =
                    root.GetProperty("newText").GetString()
                    ?? throw new InvalidOperationException(
                        "The Pi Agent workspace edit new text was missing.");
                patchHunks = ParseWorkspacePatchHunks(
                    root.GetProperty("patchHunks"));
                proposalValid =
                    schemaVersion == 3 &&
                    operation is "replace" or "patch" or "create" &&
                    IsValidWorkspaceRelativePath(relativePath) &&
                    IsValidWorkspaceProposalText(
                        operation,
                        beforeSha256,
                        oldText,
                        newText,
                        patchHunks);
            }
            if (
                !WorkspaceEditProposalIdPattern.IsMatch(proposalId) ||
                !Sha256Pattern.IsMatch(beforeSha256) ||
                !proposalValid ||
                pending.AwaitingWorkspaceEditProposalCount != 1)
            {
                throw new InvalidOperationException(
                    "The Pi Agent emitted an invalid workspace edit proposal.");
            }
            pending.AwaitingWorkspaceEditProposalCount = 0;
            await WriteTurnEventAsync(
                pending,
                new PiAgentWorkspaceEditProposed(
                    turnId,
                    NextEventSequence(pending),
                    schemaVersion,
                    proposalId,
                    operation,
                    relativePath,
                    beforeSha256,
                    oldText,
                    newText,
                    patchHunks)
                {
                    FileChanges = fileChanges,
                },
                cancellationToken);
            return;
        }
        if (eventName != "turn_completed")
        {
            throw new InvalidOperationException(
                "The Pi Agent emitted an unsupported event.");
        }

        bool success = root.GetProperty("success").GetBoolean();
        string status =
            root.GetProperty("status").GetString()
            ?? throw new InvalidOperationException(
                "The Pi Agent turn status was missing.");
        int deltaCount = root.GetProperty("deltaCount").GetInt32();
        int toolExecutionCount =
            root.GetProperty("toolExecutionCount").GetInt32();
        if (
            deltaCount != pending.DeltaCount ||
            toolExecutionCount != pending.ToolExecutionCount ||
            pending.ActiveTools.Count != 0 ||
            pending.AwaitingWorkspaceEditProposalCount != 0 ||
            (success && status != "completed") ||
            (!success && status is not ("aborted" or "failed")))
        {
            throw new InvalidOperationException(
                "The Pi Agent turn receipt did not match its events.");
        }
        string? errorCode = success
            ? null
            : root
                .GetProperty("error")
                .GetProperty("code")
                .GetString();
        PiAgentTurnResult result = new(
            turnId,
            success,
            status,
            pending.Response.ToString(),
            deltaCount,
            toolExecutionCount,
            errorCode);
        await WriteTurnEventAsync(
            pending,
            new PiAgentTurnCompleted(
                turnId,
                NextEventSequence(pending),
                result),
            cancellationToken);
        pending.Events.Writer.TryComplete();
        pendingTurns.TryRemove(turnId, out _);
        if (!pending.Completion.TrySetResult(result))
        {
            throw new InvalidOperationException(
                "The Pi Agent turn completed more than once.");
        }
    }

    private static int NextEventSequence(PendingTurn pending)
    {
        return ++pending.EventSequence;
    }

    private async ValueTask WriteTurnEventAsync(
        PendingTurn pending,
        PiAgentTurnStreamEvent streamEvent,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CreateTimeout(
                cancellationToken,
                options.RequestTimeoutMilliseconds);
        try
        {
            await pending.Events.Writer.WriteAsync(
                streamEvent,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The desktop turn event consumer exceeded its " +
                "backpressure deadline.");
        }
    }

    private void FailPendingOperations(Exception exception)
    {
        foreach (KeyValuePair<
            string,
            TaskCompletionSource<JsonDocument>> entry in pendingResponses)
        {
            if (pendingResponses.TryRemove(entry.Key, out
                TaskCompletionSource<JsonDocument>? pending))
            {
                pending.TrySetException(exception);
            }
        }
        foreach (KeyValuePair<string, PendingTurn> entry in pendingTurns)
        {
            if (pendingTurns.TryRemove(
                entry.Key,
                out PendingTurn? pending))
            {
                pending.Events.Writer.TryComplete(exception);
                pending.Completion.TrySetException(exception);
            }
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (shutdownCompleted)
        {
            return;
        }

        using JsonDocument response = await RequestAsync(
            "shutdown",
            "desktop-shutdown",
            cancellationToken);
        if (!response.RootElement.GetProperty("success").GetBoolean())
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar rejected orderly shutdown.");
        }

        shutdownRequested = true;
        process.StandardInput.Close();
        using CancellationTokenSource timeout =
            CreateTimeout(
                cancellationToken,
                options.ShutdownTimeoutMilliseconds);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            string stderr = await stderrTask;
            throw new InvalidOperationException(
                $"The Pi Agent sidecar exited with {process.ExitCode}: " +
                stderr.Trim());
        }
        await outputPumpTask.WaitAsync(timeout.Token);
        shutdownCompleted = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            using CancellationTokenSource timeout = new(
                options.ShutdownTimeoutMilliseconds);
            if (admitted)
            {
                try
                {
                    await ShutdownAsync(timeout.Token);
                }
                catch (Exception exception)
                    when (exception is OperationCanceledException or
                        InvalidOperationException or IOException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
            }
            else
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        shutdownRequested = true;
        outputPumpCancellation.Cancel();
        try
        {
            await outputPumpTask;
        }
        catch (OperationCanceledException)
        {
        }
        FailPendingOperations(
            new ObjectDisposedException(
                nameof(PiAgentSidecarController)));
        outputPumpCancellation.Dispose();
        writeGate.Dispose();
        process.Dispose();
    }

    private async Task<JsonDocument> ReadFrameAsync(
        CancellationToken cancellationToken)
    {
        string? line = await process.StandardOutput.ReadLineAsync(
            cancellationToken);
        if (line is null)
        {
            string stderr = process.HasExited
                ? await stderrTask
                : string.Empty;
            throw new InvalidOperationException(
                "The Pi Agent sidecar closed its output before a complete " +
                $"frame was received. {stderr.Trim()}");
        }
        if (Encoding.UTF8.GetByteCount(line) > options.MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The incoming Pi Agent frame exceeds the contract limit.");
        }

        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar returned invalid JSON.",
                exception);
        }
    }

    private static void ValidateOptions(PiAgentSidecarOptions options)
    {
        if (!Path.IsPathFullyQualified(options.NodeExecutablePath) ||
            !File.Exists(options.NodeExecutablePath) ||
            !string.Equals(
                Path.GetFileName(options.NodeExecutablePath),
                "node.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "NodeExecutablePath must name an existing absolute node.exe.");
        }
        if (!Path.IsPathFullyQualified(options.HostScriptPath) ||
            !File.Exists(options.HostScriptPath) ||
            !string.Equals(
                Path.GetFileName(options.HostScriptPath),
                "host.mjs",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "HostScriptPath must name an existing absolute host.mjs.");
        }
        if (options.MaximumFrameBytes != 131_072 ||
            options.RequestTimeoutMilliseconds is < 1_000 or > 15_000 ||
            options.ShutdownTimeoutMilliseconds is < 1_000 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sidecar limits do not match the reviewed desktop policy.");
        }
        if (
            options.ModelBrokerPipePath is not null &&
            !Regex.IsMatch(
                options.ModelBrokerPipePath,
                @"^\\\\\.\\pipe\\jarvis2-pi-model-[0-9a-f]{32}$",
                RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "ModelBrokerPipePath failed local named-pipe admission.");
        }
    }

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        int timeoutMilliseconds)
    {
        CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(timeoutMilliseconds);
        return timeout;
    }

    private static void ValidateReady(
        JsonElement ready,
        bool promptingExpected)
    {
        bool valid =
            ready.GetProperty("type").GetString() == "ready" &&
            ready.GetProperty("protocol").GetString() == ContractId &&
            ready.GetProperty("package").GetString() == PackageName &&
            ready.GetProperty("version").GetString() == ExpectedVersion &&
            ready.GetProperty("credentialEnvironmentClean").GetBoolean() &&
            ready.GetProperty("sessionCreationEnabled").GetBoolean() &&
            ready.GetProperty("promptingEnabled").GetBoolean() ==
                promptingExpected;
        if (!valid)
        {
            throw new InvalidOperationException(
                "The Pi Agent sidecar ready frame failed admission.");
        }
    }

    private static void ValidateResponseEnvelope(
        JsonElement response,
        string expectedCommand,
        string expectedId)
    {
        bool valid =
            response.GetProperty("type").GetString() == "response" &&
            response.GetProperty("command").GetString() == expectedCommand &&
            response.GetProperty("id").GetString() == expectedId;
        if (!valid)
        {
            throw new InvalidOperationException(
                "The Pi Agent response envelope did not match its request.");
        }
    }

    private static void ValidateWorkspaceEditDecisionRequest(
        string proposalId,
        string beforeSha256)
    {
        if (
            !WorkspaceEditProposalIdPattern.IsMatch(proposalId) ||
            !Sha256Pattern.IsMatch(beforeSha256))
        {
            throw new ArgumentException(
                "A workspace edit decision requires the exact proposal id and lowercase SHA-256.");
        }
    }

    private static PiAgentWorkspaceEditDecisionReceipt
        ParseWorkspaceEditDecisionResponse(
            JsonElement root,
            string expectedProposalId,
            string expectedBeforeSha256,
            string expectedStatus,
            bool mutationExpected)
    {
        if (!root.GetProperty("success").GetBoolean())
        {
            JsonElement error = root.GetProperty("error");
            string code = error.GetProperty("code").GetString()
                ?? "workspace-edit-decision-failed";
            string message = error.GetProperty("message").GetString()
                ?? "The workspace edit decision failed closed.";
            throw new PiAgentWorkspaceEditDecisionException(
                code,
                message);
        }

        JsonElement data = root.GetProperty("data");
        int schemaVersion =
            data.GetProperty("schemaVersion").GetInt32();
        string proposalId =
            data.GetProperty("proposalId").GetString() ?? string.Empty;
        string operation =
            data.GetProperty("operation").GetString() ?? string.Empty;
        string beforeSha256 =
            data.GetProperty("beforeSha256").GetString() ?? string.Empty;
        JsonElement afterElement = data.GetProperty("afterSha256");
        string? afterSha256 = afterElement.ValueKind == JsonValueKind.Null
            ? null
            : afterElement.GetString();
        string status =
            data.GetProperty("status").GetString() ?? string.Empty;
        bool mutationPerformed =
            data.GetProperty("mutationPerformed").GetBoolean();
        bool commonValid =
            proposalId == expectedProposalId &&
            beforeSha256 == expectedBeforeSha256 &&
            status == expectedStatus &&
            mutationPerformed == mutationExpected &&
            (mutationExpected
                ? afterSha256 is not null &&
                    Sha256Pattern.IsMatch(afterSha256) &&
                    afterSha256 != beforeSha256
                : afterSha256 is null);
        if (schemaVersion == 4 && operation == "change-set")
        {
            string transactionModel = data
                .GetProperty("transactionModel")
                .GetString() ?? string.Empty;
            PiAgentWorkspaceFileDecisionReceipt[] files = data
                .GetProperty("files")
                .EnumerateArray()
                .Select(file =>
                {
                    JsonElement fileAfterElement =
                        file.GetProperty("afterSha256");
                    return new PiAgentWorkspaceFileDecisionReceipt(
                        file.GetProperty("ordinal").GetInt32(),
                        file.GetProperty("operation").GetString()
                            ?? string.Empty,
                        file.GetProperty("relativePath").GetString()
                            ?? string.Empty,
                        file.GetProperty("beforeSha256").GetString()
                            ?? string.Empty,
                        fileAfterElement.ValueKind == JsonValueKind.Null
                            ? null
                            : fileAfterElement.GetString());
                })
                .ToArray();
            bool changeSetValid =
                commonValid &&
                transactionModel ==
                    "durable-before-or-after-convergence-no-simultaneous-visibility-claim" &&
                IsValidWorkspaceChangeSetDecision(
                    files,
                    afterSha256,
                    mutationExpected);
            if (!changeSetValid)
            {
                throw new InvalidOperationException(
                    "The Pi Agent workspace change-set decision receipt was invalid.");
            }
            return new PiAgentWorkspaceEditDecisionReceipt(
                schemaVersion,
                proposalId,
                operation,
                string.Empty,
                beforeSha256,
                afterSha256,
                status,
                mutationPerformed)
            {
                TransactionModel = transactionModel,
                Files = files,
            };
        }

        string relativePath =
            data.GetProperty("relativePath").GetString() ?? string.Empty;
        bool valid =
            commonValid &&
            schemaVersion == 3 &&
            operation is "replace" or "patch" or "create" &&
            IsValidWorkspaceRelativePath(relativePath);
        if (!valid)
        {
            throw new InvalidOperationException(
                "The Pi Agent workspace edit decision receipt was invalid.");
        }
        return new PiAgentWorkspaceEditDecisionReceipt(
            schemaVersion,
            proposalId,
            operation,
            relativePath,
            beforeSha256,
            afterSha256,
            status,
            mutationPerformed);
    }

    private static bool IsValidWorkspaceChangeSetDecision(
        IReadOnlyList<PiAgentWorkspaceFileDecisionReceipt> files,
        string? afterSha256,
        bool mutationExpected)
    {
        if (files.Count is < 2 or > 4)
        {
            return false;
        }
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < files.Count; index++)
        {
            PiAgentWorkspaceFileDecisionReceipt file = files[index];
            if (
                file.Ordinal != index + 1 ||
                file.Operation is not
                    ("replace" or "patch" or "create") ||
                !IsValidWorkspaceRelativePath(file.RelativePath) ||
                !paths.Add(file.RelativePath) ||
                !Sha256Pattern.IsMatch(file.BeforeSha256) ||
                (mutationExpected
                    ? file.AfterSha256 is null ||
                        !Sha256Pattern.IsMatch(file.AfterSha256) ||
                        file.AfterSha256 == file.BeforeSha256
                    : file.AfterSha256 is not null))
            {
                return false;
            }
        }
        return !mutationExpected || string.Equals(
            ComputeWorkspaceChangeSetAfterDigest(files),
            afterSha256,
            StringComparison.Ordinal);
    }

    private static string ComputeWorkspaceChangeSetAfterDigest(
        IReadOnlyList<PiAgentWorkspaceFileDecisionReceipt> files)
    {
        StringBuilder material = new(
            "JARVIS2/workspace-change-set-after/v1");
        foreach (PiAgentWorkspaceFileDecisionReceipt file in files)
        {
            material
                .Append('\0')
                .Append(file.Ordinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append('\0')
                .Append(file.RelativePath)
                .Append('\0')
                .Append(file.AfterSha256);
        }
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(material.ToString())))
            .ToLowerInvariant();
    }

    private static bool IsValidWorkspaceRelativePath(
        string relativePath)
    {
        if (
            string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Length > 512 ||
            relativePath.Contains('\\') ||
            relativePath.Contains(':') ||
            relativePath.Contains("//", StringComparison.Ordinal) ||
            relativePath.StartsWith('/') ||
            relativePath.EndsWith('/') ||
            relativePath.Any(char.IsControl) ||
            Path.IsPathFullyQualified(relativePath))
        {
            return false;
        }
        return relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment =>
                segment is not "." and not ".." &&
                !segment.Equals(
                    ".jarvis2-workspace-transaction.json",
                    StringComparison.OrdinalIgnoreCase) &&
                !segment.StartsWith(
                    ".jarvis2-workspace-transaction-",
                    StringComparison.OrdinalIgnoreCase) &&
                !segment.Equals(
                    ".git",
                    StringComparison.OrdinalIgnoreCase) &&
                !segment.Equals(
                    ".hg",
                    StringComparison.OrdinalIgnoreCase) &&
                !segment.Equals(
                    ".svn",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static PiAgentWorkspacePatchHunk[]
        ParseWorkspacePatchHunks(JsonElement element) => element
            .EnumerateArray()
            .Select(hunk => new PiAgentWorkspacePatchHunk(
                hunk.GetProperty("ordinal").GetInt32(),
                hunk.GetProperty("oldText").GetString()
                    ?? throw new InvalidOperationException(
                        "A Pi Agent workspace patch old text was missing."),
                hunk.GetProperty("newText").GetString()
                    ?? throw new InvalidOperationException(
                        "A Pi Agent workspace patch new text was missing.")))
            .ToArray();

    private static PiAgentWorkspaceFileChange
        ParseWorkspaceFileChange(JsonElement element) => new(
            element.GetProperty("ordinal").GetInt32(),
            element.GetProperty("operation").GetString()
                ?? throw new InvalidOperationException(
                    "A Pi Agent change-set operation was missing."),
            element.GetProperty("relativePath").GetString()
                ?? throw new InvalidOperationException(
                    "A Pi Agent change-set path was missing."),
            element.GetProperty("beforeSha256").GetString()
                ?? throw new InvalidOperationException(
                    "A Pi Agent change-set before hash was missing."),
            element.GetProperty("oldText").GetString()
                ?? throw new InvalidOperationException(
                    "A Pi Agent change-set old text was missing."),
            element.GetProperty("newText").GetString()
                ?? throw new InvalidOperationException(
                    "A Pi Agent change-set new text was missing."),
            ParseWorkspacePatchHunks(
                element.GetProperty("patchHunks")));

    private static bool IsValidWorkspaceChangeSet(
        IReadOnlyList<PiAgentWorkspaceFileChange> changes,
        string reviewDigest)
    {
        if (
            changes.Count is < 2 or > 4 ||
            !Sha256Pattern.IsMatch(reviewDigest))
        {
            return false;
        }
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        int previewBytes = 0;
        for (int index = 0; index < changes.Count; index++)
        {
            PiAgentWorkspaceFileChange change = changes[index];
            if (
                change.Ordinal != index + 1 ||
                change.Operation is not
                    ("replace" or "patch" or "create") ||
                !IsValidWorkspaceRelativePath(change.RelativePath) ||
                !paths.Add(change.RelativePath) ||
                !Sha256Pattern.IsMatch(change.BeforeSha256) ||
                !IsValidWorkspaceProposalText(
                    change.Operation,
                    change.BeforeSha256,
                    change.OldText,
                    change.NewText,
                    change.PatchHunks))
            {
                return false;
            }
            if (change.Operation == "patch")
            {
                previewBytes += change.PatchHunks.Sum(hunk =>
                    Encoding.UTF8.GetByteCount(hunk.OldText) +
                    Encoding.UTF8.GetByteCount(hunk.NewText));
            }
            else
            {
                previewBytes +=
                    Encoding.UTF8.GetByteCount(change.OldText) +
                    Encoding.UTF8.GetByteCount(change.NewText);
            }
        }
        return
            previewBytes <= 32_768 &&
            string.Equals(
                ComputeWorkspaceChangeSetReviewDigest(changes),
                reviewDigest,
                StringComparison.Ordinal);
    }

    private static string ComputeWorkspaceChangeSetReviewDigest(
        IReadOnlyList<PiAgentWorkspaceFileChange> changes)
    {
        StringBuilder material = new(
            "JARVIS2/workspace-change-set-review/v1");
        foreach (PiAgentWorkspaceFileChange change in changes)
        {
            material
                .Append('\0')
                .Append(change.Ordinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append('\0')
                .Append(change.Operation)
                .Append('\0')
                .Append(change.RelativePath)
                .Append('\0')
                .Append(change.BeforeSha256)
                .Append('\0')
                .Append(change.OldText)
                .Append('\0')
                .Append(change.NewText)
                .Append('\0')
                .Append(change.PatchHunks.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            foreach (PiAgentWorkspacePatchHunk hunk in change.PatchHunks)
            {
                material
                    .Append('\0')
                    .Append(hunk.Ordinal.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\0')
                    .Append(hunk.OldText)
                    .Append('\0')
                    .Append(hunk.NewText);
            }
        }
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(material.ToString())))
            .ToLowerInvariant();
    }

    private static bool IsValidWorkspaceProposalText(
        string operation,
        string beforeSha256,
        string oldText,
        string newText,
        IReadOnlyList<PiAgentWorkspacePatchHunk> patchHunks)
    {
        if (
            oldText.Contains('\0') ||
            newText.Contains('\0') ||
            !IsStrictUtf16(oldText) ||
            !IsStrictUtf16(newText))
        {
            return false;
        }
        if (operation == "replace")
        {
            return
                patchHunks.Count == 0 &&
                oldText.Length != 0 &&
                Encoding.UTF8.GetByteCount(oldText) <= 4_096 &&
                Encoding.UTF8.GetByteCount(newText) <= 4_096 &&
                oldText != newText;
        }
        if (operation == "create")
        {
            return
                patchHunks.Count == 0 &&
                beforeSha256 == WorkspaceFileAbsentSha256 &&
                oldText.Length == 0 &&
                Encoding.UTF8.GetByteCount(newText) is > 0 and <= 16_384 &&
                !ContainsBinaryControlCharacters(newText);
        }
        if (
            oldText.Length != 0 ||
            newText.Length != 0 ||
            patchHunks.Count is < 2 or > 8)
        {
            return false;
        }
        HashSet<string> oldTexts = new(StringComparer.Ordinal);
        int previewBytes = 0;
        for (int index = 0; index < patchHunks.Count; index++)
        {
            PiAgentWorkspacePatchHunk hunk = patchHunks[index];
            if (
                hunk.Ordinal != index + 1 ||
                hunk.OldText.Length == 0 ||
                !IsStrictUtf16(hunk.OldText) ||
                !IsStrictUtf16(hunk.NewText) ||
                hunk.OldText.Contains('\0') ||
                hunk.NewText.Contains('\0') ||
                ContainsBinaryControlCharacters(hunk.OldText) ||
                ContainsBinaryControlCharacters(hunk.NewText) ||
                Encoding.UTF8.GetByteCount(hunk.OldText) > 4_096 ||
                Encoding.UTF8.GetByteCount(hunk.NewText) > 4_096 ||
                hunk.OldText == hunk.NewText ||
                !oldTexts.Add(hunk.OldText))
            {
                return false;
            }
            previewBytes +=
                Encoding.UTF8.GetByteCount(hunk.OldText) +
                Encoding.UTF8.GetByteCount(hunk.NewText);
        }
        return previewBytes <= 16_384;
    }

    private static bool ContainsBinaryControlCharacters(string value) =>
        value.Any(character =>
            character is >= '\u0001' and <= '\u0008' or
                '\u000b' or
                '\u000c' or
                >= '\u000e' and <= '\u001f' or
                '\u007f');

    private static bool IsStrictUtf16(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (
                    index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return false;
            }
        }
        return true;
    }
}

public static class PiAgentDesktopProbe
{
    public static async Task<PiAgentDesktopProbeReceipt> RunAsync(
        PiAgentSidecarOptions options,
        CancellationToken cancellationToken)
    {
        await using PiAgentSidecarController controller =
            await PiAgentSidecarController.StartAsync(
                options,
                cancellationToken);

        using JsonDocument hello = await controller.RequestAsync(
            "hello",
            "desktop-hello",
            cancellationToken);
        bool helloPassed =
            hello.RootElement.GetProperty("success").GetBoolean() &&
            hello.RootElement.GetProperty("protocol").GetString() ==
                PiAgentSidecarController.ContractId &&
            hello.RootElement.GetProperty("runtime").GetString() ==
                PiAgentSidecarController.ExpectedVersion;

        using JsonDocument capabilities = await controller.RequestAsync(
            "capabilities",
            "desktop-capabilities",
            cancellationToken);
        JsonElement capabilityData =
            capabilities.RootElement.GetProperty("data");
        string[] initialTools = capabilityData
            .GetProperty("initialTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        string[] deniedTools = capabilityData
            .GetProperty("deniedTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        bool workspaceChangeSetSupported = capabilityData
            .GetProperty("workspaceChangeSetSupported")
            .GetBoolean();
        int workspaceChangeSetMinimumFiles = capabilityData
            .GetProperty("workspaceChangeSetMinimumFiles")
            .GetInt32();
        int workspaceChangeSetMaximumFiles = capabilityData
            .GetProperty("workspaceChangeSetMaximumFiles")
            .GetInt32();
        int workspaceChangeSetMaximumPreviewBytes = capabilityData
            .GetProperty("workspaceChangeSetMaximumPreviewBytes")
            .GetInt32();
        bool workspaceChangeSetRecoveryAvailableToModel = capabilityData
            .GetProperty("workspaceChangeSetRecoveryAvailableToModel")
            .GetBoolean();
        bool capabilitiesPassed =
            capabilities.RootElement.GetProperty("success").GetBoolean() &&
            initialTools.SequenceEqual(
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
            deniedTools.SequenceEqual(["bash", "edit", "write"]) &&
            capabilityData
                .GetProperty("sessionCreationEnabled")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("promptingEnabled")
                .GetBoolean() &&
            capabilityData
                .GetProperty("sessionPersistence")
                .GetString() == "in-memory" &&
            capabilityData
                .GetProperty("conversationCheckpoint")
                .GetString() ==
                    "bounded-completed-text-context-restore" &&
            capabilityData
                .GetProperty("conversationCheckpointMaxTurns")
                .GetInt32() == 32 &&
            capabilityData
                .GetProperty("conversationCheckpointMaxBytes")
                .GetInt32() == 32_768 &&
            capabilityData
                .GetProperty("conversationCheckpointMaxTextBytes")
                .GetInt32() == 16_384 &&
            capabilityData
                .GetProperty("conversationCheckpointPersistence")
                .GetString() == "desktop-owned-external" &&
            capabilityData
                .GetProperty("workspaceBinding")
                .GetString() == "single-explicit-root" &&
            !capabilityData
                .GetProperty("resourceDiscoveryEnabled")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("modelNetworkAllowed")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("credentialTransportAllowed")
                .GetBoolean() &&
            capabilityData
                .GetProperty("workspacePatchSupported")
                .GetBoolean() &&
            capabilityData
                .GetProperty("workspacePatchMinimumHunks")
                .GetInt32() == 2 &&
            capabilityData
                .GetProperty("workspacePatchMaximumHunks")
                .GetInt32() == 8 &&
            capabilityData
                .GetProperty("workspacePatchMaximumPreviewBytes")
                .GetInt32() == 16_384 &&
            capabilityData
                .GetProperty("workspacePatchCommitMode")
                .GetString() ==
                    "single-file-atomic-replace-and-post-verify" &&
            workspaceChangeSetSupported &&
            workspaceChangeSetMinimumFiles == 2 &&
            workspaceChangeSetMaximumFiles == 4 &&
            workspaceChangeSetMaximumPreviewBytes == 32_768 &&
            capabilityData
                .GetProperty("workspaceChangeSetCommitMode")
                .GetString() ==
                    "durable-before-or-after-convergence-no-simultaneous-visibility-claim" &&
            capabilityData
                .GetProperty("workspaceChangeSetRecovery")
                .GetString() ==
                    "strict-journal-before-tools-rollback-or-complete" &&
            !workspaceChangeSetRecoveryAvailableToModel &&
            !capabilityData
                .GetProperty("shellMutationSupported")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("explorerMutationSupported")
                .GetBoolean() &&
            !capabilityData
                .GetProperty("activationPermitted")
                .GetBoolean();

        string workspaceRoot =
            Directory.GetParent(options.HostScriptPath)?
                .Parent?.FullName
            ?? throw new InvalidOperationException(
                "The desktop probe workspace could not be resolved.");
        using JsonDocument admittedSession =
            await controller.StartReadOnlySessionAsync(
                workspaceRoot,
                "desktop-session-admission",
                cancellationToken);
        JsonElement sessionData =
            admittedSession.RootElement.GetProperty("data");
        JsonElement recoveryData = sessionData
            .GetProperty("workspaceTransactionRecovery");
        bool workspaceChangeSetRecoveryBeforeToolsPassed =
            recoveryData.GetProperty("schemaVersion").GetInt32() == 1 &&
            recoveryData.GetProperty("receiptType").GetString() ==
                "jarvis2-workspace-change-set-recovery" &&
            recoveryData.GetProperty("result").GetString() == "none" &&
            recoveryData.GetProperty("proposalId").ValueKind ==
                JsonValueKind.Null &&
            recoveryData.GetProperty("fileCount").GetInt32() == 0 &&
            !recoveryData.GetProperty("mutationPerformed").GetBoolean();
        string[] activeTools = sessionData
            .GetProperty("activeTools")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        bool sessionCreationPassed =
            admittedSession.RootElement
                .GetProperty("success")
                .GetBoolean() &&
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
            workspaceChangeSetRecoveryBeforeToolsPassed &&
            !sessionData
                .GetProperty("sessionPersisted")
                .GetBoolean() &&
            sessionData
                .GetProperty("restoredTurnCount")
                .GetInt32() == 0 &&
            sessionData
                .GetProperty("restoredContextMessageCount")
                .GetInt32() == 0 &&
            !sessionData
                .GetProperty("promptingEnabled")
                .GetBoolean() &&
            !sessionData
                .GetProperty("resourceDiscoveryEnabled")
                .GetBoolean() &&
            !sessionData
                .GetProperty("modelNetworkAllowed")
                .GetBoolean();
        bool workspaceBound = string.Equals(
            Path.GetFullPath(workspaceRoot),
            sessionData.GetProperty("workspaceRoot").GetString(),
            StringComparison.OrdinalIgnoreCase);

        await controller.ShutdownAsync(cancellationToken);
        bool passed =
            helloPassed &&
            capabilitiesPassed &&
            sessionCreationPassed &&
            workspaceBound;

        return new PiAgentDesktopProbeReceipt(
            1,
            "jarvisv2-pi-agent-read-only-session-probe",
            passed ? "passed" : "failed",
            PiAgentSidecarController.ContractId,
            PiAgentSidecarController.PackageName,
            PiAgentSidecarController.ExpectedVersion,
            "sdk-sidecar-jsonl",
            true,
            true,
            helloPassed,
            capabilitiesPassed,
            sessionCreationPassed,
            workspaceBound,
            true,
            true,
            controller.CredentialEnvironmentClean,
            initialTools,
            deniedTools,
            true,
            false,
            false,
            false,
            true,
            2,
            8,
            16_384,
            workspaceChangeSetSupported,
            workspaceChangeSetMinimumFiles,
            workspaceChangeSetMaximumFiles,
            workspaceChangeSetMaximumPreviewBytes,
            workspaceChangeSetRecoveryBeforeToolsPassed,
            workspaceChangeSetRecoveryAvailableToModel,
            false,
            false,
            false,
            false,
            false,
            "not-run",
            false);
    }
}

public static class PiAgentBridgeFaultProbe
{
    public static async Task<PiAgentBridgeFaultReceipt> RunAsync(
        string nodeExecutablePath,
        string fixtureRoot,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(fixtureRoot);
        List<PiAgentBridgeFaultScenario> scenarios =
        [
            await ExpectAdmissionFailureAsync(
                "wrong-ready-rejected",
                nodeExecutablePath,
                Path.Combine(root, "wrong-ready", "host.mjs"),
                exception =>
                    exception is InvalidOperationException &&
                    exception.Message.Contains(
                        "ready frame failed admission",
                        StringComparison.Ordinal),
                cancellationToken),
            await ExpectAdmissionFailureAsync(
                "oversized-ready-rejected",
                nodeExecutablePath,
                Path.Combine(root, "oversized-ready", "host.mjs"),
                exception =>
                    exception is InvalidOperationException &&
                    exception.Message.Contains(
                        "frame exceeds the contract limit",
                        StringComparison.Ordinal),
                cancellationToken),
            await ExpectAdmissionFailureAsync(
                "hung-ready-times-out",
                nodeExecutablePath,
                Path.Combine(root, "hung-ready", "host.mjs"),
                exception => exception is OperationCanceledException,
                cancellationToken),
        ];
        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new PiAgentBridgeFaultReceipt(
            1,
            "jarvisv2-pi-agent-desktop-bridge-fault-probe",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            scenarios,
            true,
            false,
            false,
            false,
            false,
            "not-run",
            false);
    }

    private static async Task<PiAgentBridgeFaultScenario>
        ExpectAdmissionFailureAsync(
            string name,
            string nodeExecutablePath,
            string hostScriptPath,
            Func<Exception, bool> expected,
            CancellationToken cancellationToken)
    {
        try
        {
            PiAgentSidecarOptions options = new(
                Path.GetFullPath(nodeExecutablePath),
                Path.GetFullPath(hostScriptPath),
                RequestTimeoutMilliseconds: 1_000,
                ShutdownTimeoutMilliseconds: 1_000);
            await using PiAgentSidecarController controller =
                await PiAgentSidecarController.StartAsync(
                    options,
                    cancellationToken);
            return new PiAgentBridgeFaultScenario(
                name,
                false,
                "sidecar unexpectedly passed admission");
        }
        catch (Exception exception)
        {
            return new PiAgentBridgeFaultScenario(
                name,
                expected(exception),
                exception.GetType().Name);
        }
    }
}
