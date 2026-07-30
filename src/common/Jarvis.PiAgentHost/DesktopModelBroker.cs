using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record DesktopModelBrokerRequest(
    string RequestId,
    string ProviderId,
    string ModelId,
    JsonElement Context,
    JsonElement Options);

public sealed record DesktopModelUsage(
    long Input,
    long Output,
    long CacheRead,
    long CacheWrite);

public abstract record DesktopModelStreamEvent;

public sealed record DesktopModelTextDelta(
    string Delta) : DesktopModelStreamEvent;

public sealed record DesktopModelToolCallStarted(
    string ToolCallId,
    string Name) : DesktopModelStreamEvent;

public sealed record DesktopModelToolCallDelta(
    string ToolCallId,
    string Delta) : DesktopModelStreamEvent;

public sealed record DesktopModelToolCallCompleted(
    string ToolCallId) : DesktopModelStreamEvent;

public sealed record DesktopModelCompleted(
    string Reason,
    DesktopModelUsage Usage) : DesktopModelStreamEvent;

public interface IDesktopModelProvider
{
    IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
        DesktopModelBrokerRequest request,
        CancellationToken cancellationToken);
}

public sealed class DesktopModelBrokerServer : IAsyncDisposable
{
    public const string Protocol = "jarvisv2-pi-model-broker-v1";
    public const string ProviderId = "jarvis-desktop-broker";
    public const string ModelId = "desktop-default";
    public const int MaximumFrameBytes = 1_048_576;
    public const int MaximumConcurrentConnections = 4;

    private static readonly IReadOnlySet<string> AllowedToolNames =
        new HashSet<string>(
            ["read", "grep", "find", "ls"],
            StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenCredentialFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "apikey",
            "authorization",
            "credential",
            "credentials",
            "password",
            "secret",
            "token",
            "accesstoken",
            "refreshtoken",
        };

    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<int, Task> clients = new();
    private readonly SemaphoreSlim clientSlots = new(
        MaximumConcurrentConnections,
        MaximumConcurrentConnections);
    private readonly IDesktopModelProvider provider;
    private readonly Task acceptTask;
    private int clientSequence;
    private int faultCount;
    private int requestCount;
    private NamedPipeServerStream? pendingAcceptPipe;

    public string PipePath { get; }
    public int FaultCount => Volatile.Read(ref faultCount);
    public int RequestCount => Volatile.Read(ref requestCount);

    private DesktopModelBrokerServer(
        IDesktopModelProvider provider)
    {
        this.provider = provider;
        string pipeName = $"jarvis2-pi-model-{Guid.NewGuid():N}";
        PipePath = $@"\\.\pipe\{pipeName}";
        acceptTask = AcceptClientsAsync(
            pipeName,
            cancellation.Token);
    }

    public static DesktopModelBrokerServer Start(
        IDesktopModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new DesktopModelBrokerServer(provider);
    }

    private async Task AcceptClientsAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await clientSlots.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                clientSlots.Release();
                return;
            }

            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(pipeName);
            }
            catch
            {
                clientSlots.Release();
                throw;
            }
            Volatile.Write(ref pendingAcceptPipe, pipe);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                clientSlots.Release();
                return;
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
                clientSlots.Release();
                return;
            }
            catch (IOException)
                when (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                clientSlots.Release();
                return;
            }
            catch
            {
                await pipe.DisposeAsync();
                clientSlots.Release();
                throw;
            }
            finally
            {
                Interlocked.CompareExchange(
                    ref pendingAcceptPipe,
                    null,
                    pipe);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync();
                clientSlots.Release();
                return;
            }

            int clientId = Interlocked.Increment(
                ref clientSequence);
            Task clientTask = HandleClientSafelyAsync(
                pipe,
                cancellationToken);
            if (!clients.TryAdd(clientId, clientTask))
            {
                await pipe.DisposeAsync();
                clientSlots.Release();
                throw new InvalidOperationException(
                    "The model broker client id collided.");
            }
            _ = RemoveCompletedClientAsync(
                clientId,
                clientTask);
        }
    }

    private static NamedPipeServerStream CreatePipe(
        string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            MaximumConcurrentConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            8_192,
            8_192);
    }

    private async Task RemoveCompletedClientAsync(
        int clientId,
        Task clientTask)
    {
        try
        {
            await clientTask;
        }
        finally
        {
            clients.TryRemove(clientId, out _);
            clientSlots.Release();
        }
    }

    private async Task HandleClientSafelyAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                await HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                Interlocked.Increment(ref faultCount);
            }
            catch
            {
                Interlocked.Increment(ref faultCount);
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(
            pipe,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8_192,
            leaveOpen: true);
        using StreamWriter writer = new(
            pipe,
            new UTF8Encoding(false, true),
            bufferSize: 8_192,
            leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        using JsonDocument hello = await ReadFrameAsync(
            reader,
            cancellationToken);
        JsonElement helloRoot = hello.RootElement;
        if (
            helloRoot.GetProperty("type").GetString() != "broker_hello" ||
            helloRoot.GetProperty("protocol").GetString() != Protocol)
        {
            throw new InvalidOperationException(
                "The model broker hello failed admission.");
        }
        await WriteFrameAsync(
            writer,
            new
            {
                type = "broker_ready",
                protocol = Protocol,
            },
            cancellationToken);

        using JsonDocument requestFrame = await ReadFrameAsync(
            reader,
            cancellationToken);
        DesktopModelBrokerRequest request = ParseRequest(
            requestFrame.RootElement);
        Interlocked.Increment(ref requestCount);

        using CancellationTokenSource providerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        using CancellationTokenSource monitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        Task disconnectMonitor = MonitorDisconnectAsync(
            reader,
            providerCancellation,
            monitorCancellation.Token);

        bool completed = false;
        HashSet<string> activeToolCalls =
            new(StringComparer.Ordinal);
        try
        {
            await foreach (
                DesktopModelStreamEvent streamEvent in provider
                    .StreamAsync(
                        request,
                        providerCancellation.Token)
                    .WithCancellation(providerCancellation.Token))
            {
                if (completed)
                {
                    throw new InvalidOperationException(
                        "The model provider emitted data after completion.");
                }
                completed = await WriteProviderEventAsync(
                    writer,
                    request.RequestId,
                    streamEvent,
                    activeToolCalls,
                    providerCancellation.Token);
            }
            if (!completed && !providerCancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "The model provider ended without completion.");
            }
        }
        catch (OperationCanceledException)
            when (providerCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            Interlocked.Increment(ref faultCount);
            await TryWriteProviderErrorAsync(
                writer,
                request.RequestId,
                cancellationToken);
        }
        finally
        {
            monitorCancellation.Cancel();
            try
            {
                await disconnectMonitor;
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static DesktopModelBrokerRequest ParseRequest(
        JsonElement root)
    {
        if (
            root.GetProperty("type").GetString() != "model_request" ||
            root.GetProperty("protocol").GetString() != Protocol ||
            ContainsCredentialField(root))
        {
            throw new InvalidOperationException(
                "The model request failed desktop admission.");
        }
        string requestId =
            root.GetProperty("id").GetString()
            ?? throw new InvalidOperationException(
                "The model request id was missing.");
        if (
            !Guid.TryParseExact(requestId, "D", out _) ||
            requestId.Length != 36)
        {
            throw new InvalidOperationException(
                "The model request id was invalid.");
        }

        JsonElement model = root.GetProperty("model");
        string providerId =
            model.GetProperty("provider").GetString()
            ?? string.Empty;
        string modelId =
            model.GetProperty("id").GetString()
            ?? string.Empty;
        if (providerId != ProviderId || modelId != ModelId)
        {
            throw new InvalidOperationException(
                "The model request target was not admitted.");
        }
        JsonElement context = root.GetProperty("context");
        JsonElement options = root.GetProperty("options");
        if (
            context.ValueKind != JsonValueKind.Object ||
            options.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The model request payload shape was invalid.");
        }
        return new DesktopModelBrokerRequest(
            requestId,
            providerId,
            modelId,
            context.Clone(),
            options.Clone());
    }

    private static async Task MonitorDisconnectAsync(
        StreamReader reader,
        CancellationTokenSource providerCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            string? unexpected = await reader.ReadLineAsync(
                cancellationToken);
            providerCancellation.Cancel();
            if (unexpected is not null)
            {
                throw new InvalidOperationException(
                    "The model broker peer sent an unexpected frame.");
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<bool> WriteProviderEventAsync(
        StreamWriter writer,
        string requestId,
        DesktopModelStreamEvent streamEvent,
        ISet<string> activeToolCalls,
        CancellationToken cancellationToken)
    {
        switch (streamEvent)
        {
            case DesktopModelTextDelta text:
                if (text.Delta.Length == 0)
                {
                    return false;
                }
                await WriteFrameAsync(
                    writer,
                    new
                    {
                        type = "model_delta",
                        id = requestId,
                        delta = text.Delta,
                    },
                    cancellationToken);
                return false;
            case DesktopModelToolCallStarted toolStart:
                ValidateToolIdentity(
                    toolStart.ToolCallId,
                    toolStart.Name);
                if (
                    !AllowedToolNames.Contains(toolStart.Name) ||
                    !activeToolCalls.Add(toolStart.ToolCallId))
                {
                    throw new InvalidOperationException(
                        "The model provider emitted an unadmitted tool start.");
                }
                await WriteFrameAsync(
                    writer,
                    new
                    {
                        type = "model_tool_call_start",
                        id = requestId,
                        toolCallId = toolStart.ToolCallId,
                        name = toolStart.Name,
                    },
                    cancellationToken);
                return false;
            case DesktopModelToolCallDelta toolDelta:
                ValidateToolIdentity(
                    toolDelta.ToolCallId,
                    name: null);
                if (!activeToolCalls.Contains(toolDelta.ToolCallId))
                {
                    throw new InvalidOperationException(
                        "The model provider emitted a tool delta without a start.");
                }
                await WriteFrameAsync(
                    writer,
                    new
                    {
                        type = "model_tool_call_delta",
                        id = requestId,
                        toolCallId = toolDelta.ToolCallId,
                        delta = toolDelta.Delta,
                    },
                    cancellationToken);
                return false;
            case DesktopModelToolCallCompleted toolEnd:
                ValidateToolIdentity(
                    toolEnd.ToolCallId,
                    name: null);
                if (!activeToolCalls.Remove(toolEnd.ToolCallId))
                {
                    throw new InvalidOperationException(
                        "The model provider ended an inactive tool call.");
                }
                await WriteFrameAsync(
                    writer,
                    new
                    {
                        type = "model_tool_call_end",
                        id = requestId,
                        toolCallId = toolEnd.ToolCallId,
                    },
                    cancellationToken);
                return false;
            case DesktopModelCompleted completed:
                if (activeToolCalls.Count != 0)
                {
                    throw new InvalidOperationException(
                        "The model provider completed with active tool calls.");
                }
                string reason = completed.Reason is
                    "stop" or "length" or "toolUse"
                    ? completed.Reason
                    : throw new InvalidOperationException(
                        "The model completion reason was invalid.");
                ValidateUsage(completed.Usage);
                await WriteFrameAsync(
                    writer,
                    new
                    {
                        type = "model_done",
                        id = requestId,
                        reason,
                        usage = new
                        {
                            input = completed.Usage.Input,
                            output = completed.Usage.Output,
                            cacheRead = completed.Usage.CacheRead,
                            cacheWrite = completed.Usage.CacheWrite,
                        },
                    },
                    cancellationToken);
                return true;
            default:
                throw new InvalidOperationException(
                    "The model provider event was not admitted.");
        }
    }

    private static void ValidateToolIdentity(
        string toolCallId,
        string? name)
    {
        if (
            string.IsNullOrWhiteSpace(toolCallId) ||
            toolCallId.Length > 256 ||
            (name is not null &&
                (string.IsNullOrWhiteSpace(name) ||
                    name.Length > 128)))
        {
            throw new InvalidOperationException(
                "The model tool identity was invalid.");
        }
    }

    private static void ValidateUsage(DesktopModelUsage usage)
    {
        if (
            usage.Input < 0 ||
            usage.Output < 0 ||
            usage.CacheRead < 0 ||
            usage.CacheWrite < 0)
        {
            throw new InvalidOperationException(
                "The model usage values were invalid.");
        }
    }

    private static async Task<JsonDocument> ReadFrameAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        string? line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new IOException(
                "The model broker peer closed before a complete frame.");
        }
        if (Encoding.UTF8.GetByteCount(line) > MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The model broker input exceeded its frame limit.");
        }
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The model broker input was invalid JSON.",
                exception);
        }
    }

    private static async Task WriteFrameAsync<T>(
        StreamWriter writer,
        T value,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(value);
        if (Encoding.UTF8.GetByteCount(payload) > MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The model broker output exceeded its frame limit.");
        }
        await writer.WriteLineAsync(
            payload.AsMemory(),
            cancellationToken);
    }

    private static async Task TryWriteProviderErrorAsync(
        StreamWriter writer,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteFrameAsync(
                writer,
                new
                {
                    type = "model_error",
                    id = requestId,
                    message =
                        "The desktop model provider failed closed.",
                },
                cancellationToken);
        }
        catch
        {
        }
    }

    private static bool ContainsCredentialField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element
                .EnumerateArray()
                .Any(ContainsCredentialField);
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string normalized = property.Name
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (
                ForbiddenCredentialFields.Contains(normalized) ||
                ContainsCredentialField(property.Value))
            {
                return true;
            }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        NamedPipeServerStream? pendingPipe =
            Interlocked.Exchange(
                ref pendingAcceptPipe,
                null);
        if (pendingPipe is not null)
        {
            await pendingPipe.DisposeAsync();
        }
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        Task[] activeClients = clients.Values.ToArray();
        if (activeClients.Length != 0)
        {
            await Task.WhenAll(activeClients);
        }
        if (provider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        cancellation.Dispose();
        clientSlots.Dispose();
    }
}

internal sealed class DiagnosticDesktopModelProvider :
    IDesktopModelProvider
{
    private readonly bool holdResponse;
    private readonly TaskCompletionSource<bool> requestObserved = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<JsonElement> requestContexts = new();
    private int requestSequence;

    public DiagnosticDesktopModelProvider(bool holdResponse)
    {
        this.holdResponse = holdResponse;
    }

    public IReadOnlyList<JsonElement> RequestContexts =>
        requestContexts.ToArray();

    public async Task WaitForRequestAsync(
        CancellationToken cancellationToken)
    {
        await requestObserved.Task.WaitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
        DesktopModelBrokerRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        requestObserved.TrySetResult(true);
        requestContexts.Enqueue(request.Context.Clone());
        int sequence = Interlocked.Increment(
            ref requestSequence);
        if (holdResponse)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            yield break;
        }

        if (sequence == 3)
        {
            yield return new DesktopModelToolCallStarted(
                "diagnostic-read-1",
                "read");
            yield return new DesktopModelToolCallDelta(
                "diagnostic-read-1",
                "{\"path\":\"package.json\",\"offset\":1,\"limit\":5}");
            yield return new DesktopModelToolCallCompleted(
                "diagnostic-read-1");
            yield return new DesktopModelCompleted(
                "toolUse",
                new DesktopModelUsage(
                    Input: 12,
                    Output: 8,
                    CacheRead: 0,
                    CacheWrite: 0));
            yield break;
        }
        if (sequence == 4)
        {
            yield return new DesktopModelTextDelta(
                "JARVIS workspace tool online.");
            yield return new DesktopModelCompleted(
                "stop",
                new DesktopModelUsage(
                    Input: 18,
                    Output: 5,
                    CacheRead: 0,
                    CacheWrite: 0));
            yield break;
        }

        yield return new DesktopModelTextDelta("JARVIS ");
        yield return new DesktopModelTextDelta(
            "desktop broker online.");
        yield return new DesktopModelCompleted(
            "stop",
            new DesktopModelUsage(
                Input: 7,
                Output: 4,
                CacheRead: 0,
                CacheWrite: 0));
    }
}
