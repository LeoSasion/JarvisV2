using System.IO.Pipes;
using System.Text;
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
    string Response,
    int DeltaCount,
    int BrokerRequestCount,
    bool NamedPipeOnly,
    bool CredentialTransportAllowed,
    bool PiSidecarModelNetworkAllowed,
    string LiveModelNetwork,
    string LiveExplorer,
    bool MutationPerformed);

internal sealed class DiagnosticDesktopModelBroker : IAsyncDisposable
{
    public const string Protocol = "jarvisv2-pi-model-broker-v1";
    private const int MaximumFrameBytes = 1_048_576;

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
    private readonly NamedPipeServerStream server;
    private readonly Task serverTask;
    private int requestCount;

    public string PipePath { get; }
    public int RequestCount => Volatile.Read(ref requestCount);

    private DiagnosticDesktopModelBroker()
    {
        string pipeName = $"jarvis2-pi-model-{Guid.NewGuid():N}";
        PipePath = $@"\\.\pipe\{pipeName}";
        server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            4_096,
            4_096);
        serverTask = RunAsync(cancellation.Token);
    }

    public static DiagnosticDesktopModelBroker Start()
    {
        return new DiagnosticDesktopModelBroker();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        using StreamReader reader = new(
            server,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4_096,
            leaveOpen: true);
        using StreamWriter writer = new(
            server,
            new UTF8Encoding(false, true),
            bufferSize: 4_096,
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
                "The Pi model broker hello failed admission.");
        }
        await WriteFrameAsync(
            writer,
            new
            {
                type = "broker_ready",
                protocol = Protocol,
            },
            cancellationToken);

        using JsonDocument request = await ReadFrameAsync(
            reader,
            cancellationToken);
        JsonElement requestRoot = request.RootElement;
        if (
            requestRoot.GetProperty("type").GetString() != "model_request" ||
            requestRoot.GetProperty("protocol").GetString() != Protocol ||
            requestRoot
                .GetProperty("model")
                .GetProperty("provider")
                .GetString() != "jarvis-desktop-broker" ||
            requestRoot
                .GetProperty("model")
                .GetProperty("id")
                .GetString() != "desktop-default" ||
            ContainsCredentialField(requestRoot))
        {
            throw new InvalidOperationException(
                "The Pi model request failed desktop admission.");
        }
        string requestId =
            requestRoot.GetProperty("id").GetString()
            ?? throw new InvalidOperationException(
                "The Pi model request id was missing.");
        Interlocked.Increment(ref requestCount);

        await WriteFrameAsync(
            writer,
            new
            {
                type = "model_delta",
                id = requestId,
                delta = "JARVIS ",
            },
            cancellationToken);
        await WriteFrameAsync(
            writer,
            new
            {
                type = "model_delta",
                id = requestId,
                delta = "desktop broker online.",
            },
            cancellationToken);
        await WriteFrameAsync(
            writer,
            new
            {
                type = "model_done",
                id = requestId,
                reason = "stop",
                usage = new
                {
                    input = 7,
                    output = 4,
                    cacheRead = 0,
                    cacheWrite = 0,
                },
            },
            cancellationToken);
    }

    private static async Task<JsonDocument> ReadFrameAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        string? line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new InvalidOperationException(
                "The Pi model broker peer closed before a complete frame.");
        }
        if (Encoding.UTF8.GetByteCount(line) > MaximumFrameBytes)
        {
            throw new InvalidOperationException(
                "The Pi model broker input exceeded its frame limit.");
        }
        return JsonDocument.Parse(line);
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
                "The Pi model broker output exceeded its frame limit.");
        }
        await writer.WriteLineAsync(
            payload.AsMemory(),
            cancellationToken);
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
        if (!serverTask.IsCompleted)
        {
            await server.DisposeAsync();
        }
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        server.Dispose();
        cancellation.Dispose();
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
                .GetString() == "jarvis-desktop-broker" &&
            sessionData
                .GetProperty("modelId")
                .GetString() == "desktop-default";

        PiAgentPromptResult prompt = await controller.PromptAsync(
            "Confirm the desktop-owned model broker is online.",
            "broker-prompt",
            cancellationToken);
        bool promptPassed =
            prompt.Response == "JARVIS desktop broker online." &&
            prompt.DeltaCount == 2 &&
            prompt.ToolExecutionCount == 0;

        await controller.ShutdownAsync(cancellationToken);
        bool passed =
            capabilitiesPassed &&
            sessionCreationPassed &&
            promptPassed &&
            broker.RequestCount == 1;
        return new PiAgentDesktopBrokerProbeReceipt(
            1,
            "jarvisv2-pi-desktop-broker-bridge-probe",
            passed ? "passed" : "failed",
            DiagnosticDesktopModelBroker.Protocol,
            true,
            capabilitiesPassed,
            sessionCreationPassed,
            promptPassed,
            prompt.Response,
            prompt.DeltaCount,
            broker.RequestCount,
            true,
            false,
            false,
            "diagnostic-only",
            "not-run",
            false);
    }
}
