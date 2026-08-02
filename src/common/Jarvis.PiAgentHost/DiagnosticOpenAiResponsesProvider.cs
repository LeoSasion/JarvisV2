using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Jarvis.PiAgentHost;

public sealed record OpenAiResponsesProviderProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    string Model,
    bool RequestContractPassed,
    bool TextStreamPassed,
    bool ToolStreamPassed,
    bool UsageMappingPassed,
    bool CredentialHeaderOnly,
    bool CredentialStoreRoundTripPassed,
    bool CredentialCiphertextPassed,
    bool CredentialCorruptionRejected,
    bool HttpFailureRedacted,
    bool MalformedStreamRejected,
    bool OversizedToolArgumentsRejected,
    bool CancellationPassed,
    bool LiveModelNetworkCalled,
    bool CredentialTransportToSidecar,
    bool MutationPerformed,
    IReadOnlyList<string> Failures);

public static class OpenAiResponsesProviderProbe
{
    private const string ProbeApiKey =
        "jarvis2-offline-probe-credential-never-for-network";
    private const string PreviousProbeApiKey =
        "jarvis2-offline-previous-credential-never-for-network";

    public static async Task<OpenAiResponsesProviderProbeReceipt> RunAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> failures = [];
        using JsonDocument userRequest = CreateRequestContext(
            includeToolResult: false);
        using JsonDocument toolRequest = CreateRequestContext(
            includeToolResult: true);
        using JsonDocument requestOptions = JsonDocument.Parse(
            "{\"maxTokens\":777}");

        ScriptedHttpMessageHandler textHandler = new(
            HttpStatusCode.OK,
            TextResponseStream());
        List<DesktopModelStreamEvent> textEvents = await CollectAsync(
            textHandler,
            userRequest.RootElement,
            requestOptions.RootElement,
            cancellationToken);
        bool requestContractPassed = ValidateRequestContract(
            textHandler.RequestBody);
        bool textStreamPassed =
            string.Concat(textEvents
                .OfType<DesktopModelTextDelta>()
                .Select(item => item.Delta)) == "JARVIS online." &&
            textEvents.LastOrDefault() is DesktopModelCompleted
            {
                Reason: "stop",
            };
        bool credentialHeaderOnly =
            textHandler.AuthorizationScheme == "Bearer" &&
            textHandler.AuthorizationParameter == ProbeApiKey &&
            !(textHandler.RequestBody ?? string.Empty).Contains(
                ProbeApiKey,
                StringComparison.Ordinal);
        AddFailure(
            failures,
            requestContractPassed,
            "The synthetic request did not preserve the reviewed Responses contract.");
        AddFailure(
            failures,
            textStreamPassed,
            "The synthetic text stream did not produce the expected terminal response.");
        AddFailure(
            failures,
            credentialHeaderOnly,
            "The API key was not isolated to the desktop Authorization header.");

        ScriptedHttpMessageHandler toolHandler = new(
            HttpStatusCode.OK,
            ToolResponseStream());
        List<DesktopModelStreamEvent> toolEvents = await CollectAsync(
            toolHandler,
            toolRequest.RootElement,
            requestOptions.RootElement,
            cancellationToken);
        string toolArguments = string.Concat(toolEvents
            .OfType<DesktopModelToolCallDelta>()
            .Select(item => item.Delta));
        bool toolStreamPassed =
            toolEvents.Count == 5 &&
            toolEvents[0] is DesktopModelToolCallStarted
            {
                ToolCallId: "call_read_1|fc_item_1",
                Name: "read",
            } &&
            toolArguments == "{\"path\":\"README.md\",\"offset\":1,\"limit\":5}" &&
            toolEvents[^2] is DesktopModelToolCallCompleted
            {
                ToolCallId: "call_read_1|fc_item_1",
            } &&
            toolEvents[^1] is DesktopModelCompleted
            {
                Reason: "toolUse",
            };
        bool usageMappingPassed = toolEvents[^1] is DesktopModelCompleted
        {
            Usage.Input: 70,
            Usage.Output: 12,
            Usage.CacheRead: 25,
            Usage.CacheWrite: 5,
        };
        AddFailure(
            failures,
            toolStreamPassed,
            "The synthetic function-call stream did not preserve identity or arguments.");
        AddFailure(
            failures,
            usageMappingPassed,
            "The synthetic Responses usage was not mapped without double counting cache tokens.");

        (bool credentialRoundTrip, bool credentialCiphertext, bool corruptionRejected) =
            await ProbeCredentialStoreAsync(cancellationToken);
        AddFailure(
            failures,
            credentialRoundTrip,
            "The CurrentUser DPAPI credential store did not round-trip.");
        AddFailure(
            failures,
            credentialCiphertext,
            "The credential envelope exposed plaintext or left a temporary file.");
        AddFailure(
            failures,
            corruptionRejected,
            "A corrupted credential envelope was not rejected.");

        ScriptedHttpMessageHandler httpFailureHandler = new(
            HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"synthetic\"}}",
            "application/json");
        bool httpFailureRedacted = await ProbeFailureAsync(
            httpFailureHandler,
            userRequest.RootElement,
            requestOptions.RootElement,
            exception =>
                exception is HttpRequestException httpException &&
                httpException.StatusCode == HttpStatusCode.Unauthorized &&
                !exception.ToString().Contains(ProbeApiKey, StringComparison.Ordinal),
            cancellationToken);
        AddFailure(
            failures,
            httpFailureRedacted,
            "The HTTP failure path did not remain status-only and credential-redacted.");

        ScriptedHttpMessageHandler malformedHandler = new(
            HttpStatusCode.OK,
            "data: {not-json}\n\n");
        bool malformedStreamRejected = await ProbeFailureAsync(
            malformedHandler,
            userRequest.RootElement,
            requestOptions.RootElement,
            exception => exception is InvalidDataException,
            cancellationToken);
        AddFailure(
            failures,
            malformedStreamRejected,
            "Malformed SSE JSON was not rejected.");

        ScriptedHttpMessageHandler oversizedArgumentsHandler = new(
            HttpStatusCode.OK,
            OversizedToolArgumentsStream());
        bool oversizedToolArgumentsRejected = await ProbeFailureAsync(
            oversizedArgumentsHandler,
            userRequest.RootElement,
            requestOptions.RootElement,
            exception => exception is InvalidDataException,
            cancellationToken);
        AddFailure(
            failures,
            oversizedToolArgumentsRejected,
            "Oversized streamed function arguments were not rejected.");

        bool cancellationPassed = await ProbeCancellationAsync(
            userRequest.RootElement,
            requestOptions.RootElement);
        AddFailure(
            failures,
            cancellationPassed,
            "Provider cancellation did not reach the desktop HTTP transport.");

        bool passed = failures.Count == 0;
        return new OpenAiResponsesProviderProbeReceipt(
            1,
            "jarvisv2-openai-responses-provider-probe",
            passed ? "passed" : "failed",
            "gpt-5.6-sol",
            requestContractPassed,
            textStreamPassed,
            toolStreamPassed,
            usageMappingPassed,
            credentialHeaderOnly,
            credentialRoundTrip,
            credentialCiphertext,
            corruptionRejected,
            httpFailureRedacted,
            malformedStreamRejected,
            oversizedToolArgumentsRejected,
            cancellationPassed,
            false,
            false,
            false,
            failures);
    }

    private static async Task<List<DesktopModelStreamEvent>> CollectAsync(
        ScriptedHttpMessageHandler handler,
        JsonElement context,
        JsonElement options,
        CancellationToken cancellationToken)
    {
        using HttpClient client = new(handler);
        using OpenAiResponsesModelProvider provider = new(
            new StaticApiKeySource(ProbeApiKey),
            httpClient: client);
        List<DesktopModelStreamEvent> events = [];
        await foreach (DesktopModelStreamEvent item in provider.StreamAsync(
            CreateBrokerRequest(context, options),
            cancellationToken))
        {
            events.Add(item);
        }
        return events;
    }

    private static async Task<bool> ProbeFailureAsync(
        ScriptedHttpMessageHandler handler,
        JsonElement context,
        JsonElement options,
        Func<Exception, bool> predicate,
        CancellationToken cancellationToken)
    {
        using HttpClient client = new(handler);
        using OpenAiResponsesModelProvider provider = new(
            new StaticApiKeySource(ProbeApiKey),
            httpClient: client);
        try
        {
            await foreach (DesktopModelStreamEvent _ in provider.StreamAsync(
                CreateBrokerRequest(context, options),
                cancellationToken))
            {
            }
            return false;
        }
        catch (Exception exception)
        {
            return predicate(exception);
        }
    }

    private static async Task<bool> ProbeCancellationAsync(
        JsonElement context,
        JsonElement options)
    {
        CancellationHttpMessageHandler handler = new();
        using HttpClient client = new(handler);
        using OpenAiResponsesModelProvider provider = new(
            new StaticApiKeySource(ProbeApiKey),
            httpClient: client);
        using CancellationTokenSource cancellation = new(250);
        try
        {
            await foreach (DesktopModelStreamEvent _ in provider.StreamAsync(
                CreateBrokerRequest(context, options),
                cancellation.Token))
            {
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            return handler.CancellationObserved;
        }
    }

    private static async Task<(bool RoundTrip, bool Ciphertext, bool CorruptionRejected)>
        ProbeCredentialStoreAsync(CancellationToken cancellationToken)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"jarvisv2-openai-credential-{Guid.NewGuid():N}");
        try
        {
            OpenAiApiKeyCredentialStore store = new(root);
            _ = await store.SaveAsync(
                PreviousProbeApiKey,
                cancellationToken);
            OpenAiApiKeyStoreReceipt receipt = await store.SaveAsync(
                ProbeApiKey,
                cancellationToken);
            string? loaded = await store.GetApiKeyAsync(cancellationToken);
            string envelopeText = await File.ReadAllTextAsync(
                receipt.CredentialPath,
                cancellationToken);
            bool roundTrip = loaded == ProbeApiKey;
            bool ciphertext =
                !envelopeText.Contains(ProbeApiKey, StringComparison.Ordinal) &&
                !envelopeText.Contains(
                    PreviousProbeApiKey,
                    StringComparison.Ordinal) &&
                !Directory.EnumerateFiles(
                        root,
                        "*.tmp",
                        SearchOption.TopDirectoryOnly)
                    .Any();

            using JsonDocument envelope = JsonDocument.Parse(envelopeText);
            JsonElement envelopeRoot = envelope.RootElement;
            string protectedPayload = envelopeRoot
                .GetProperty("protectedPayload")
                .GetString() ?? string.Empty;
            char replacement = protectedPayload[0] == 'A' ? 'B' : 'A';
            string corrupted = JsonSerializer.Serialize(new
            {
                schemaVersion = envelopeRoot
                    .GetProperty("schemaVersion")
                    .GetInt32(),
                receiptType = envelopeRoot
                    .GetProperty("receiptType")
                    .GetString(),
                savedAtUtc = envelopeRoot
                    .GetProperty("savedAtUtc")
                    .GetDateTimeOffset(),
                protectedPayload = replacement + protectedPayload[1..],
            });
            await File.WriteAllTextAsync(
                receipt.CredentialPath,
                corrupted,
                cancellationToken);
            bool corruptionRejected;
            try
            {
                _ = await store.GetApiKeyAsync(cancellationToken);
                corruptionRejected = false;
            }
            catch (InvalidDataException)
            {
                corruptionRejected = true;
            }
            return (roundTrip, ciphertext, corruptionRejected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                string temporaryRoot = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.GetTempPath()));
                string fullRoot = Path.GetFullPath(root);
                if (
                    !fullRoot.StartsWith(
                        temporaryRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(fullRoot).StartsWith(
                        "jarvisv2-openai-credential-",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The credential probe cleanup path failed admission.");
                }
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private static DesktopModelBrokerRequest CreateBrokerRequest(
        JsonElement context,
        JsonElement options) =>
        new(
            Guid.NewGuid().ToString("D"),
            DesktopModelBrokerServer.ProviderId,
            DesktopModelBrokerServer.ModelId,
            context.Clone(),
            options.Clone());

    private static JsonDocument CreateRequestContext(bool includeToolResult)
    {
        string messages = includeToolResult
            ? """
              {"role":"user","content":[{"type":"text","text":"Inspect README.md."}],"timestamp":1},
              {"role":"assistant","content":[{"type":"toolCall","id":"call_previous|fc_previous","name":"read","arguments":{"path":"README.md","offset":1,"limit":5}}],"timestamp":2},
              {"role":"toolResult","toolCallId":"call_previous|fc_previous","toolName":"read","content":[{"type":"text","text":"1: JARVIS"}],"isError":false,"timestamp":3}
              """
            : """
              {"role":"user","content":[{"type":"text","text":"Inspect README.md."}],"timestamp":1}
              """;
        return JsonDocument.Parse(
            $$"""
            {
              "systemPrompt": "JARVIS desktop read-only session.",
              "messages": [{{messages}}],
              "tools": [
                {
                  "name": "read",
                  "description": "Read a UTF-8 text file.",
                  "parameters": {
                    "type": "object",
                    "properties": {
                      "path": { "type": "string" },
                      "offset": { "type": "integer" },
                      "limit": { "type": "integer" }
                    },
                    "required": ["path"],
                    "additionalProperties": false
                  }
                }
              ]
            }
            """);
    }

    private static bool ValidateRequestContract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        JsonElement tool = root.GetProperty("tools")[0];
        return
            root.GetProperty("model").GetString() == "gpt-5.6-sol" &&
            root.GetProperty("stream").GetBoolean() &&
            !root.GetProperty("store").GetBoolean() &&
            root.GetProperty("max_output_tokens").GetInt32() == 777 &&
            root.GetProperty("reasoning")
                .GetProperty("effort")
                .GetString() == "medium" &&
            root.GetProperty("input")[0]
                .GetProperty("role")
                .GetString() == "developer" &&
            tool.GetProperty("name").GetString() == "read" &&
            !tool.GetProperty("strict").GetBoolean() &&
            !body.Contains("bash", StringComparison.Ordinal) &&
            !body.Contains("write", StringComparison.Ordinal) &&
            !body.Contains("edit", StringComparison.Ordinal);
    }

    private static string TextResponseStream() =>
        Sse("""
            {"type":"response.created","response":{"id":"resp_text"}}
            """) +
        Sse("""
            {"type":"response.output_item.added","output_index":0,"item":{"type":"message","id":"msg_1","role":"assistant","content":[]}}
            """) +
        Sse("""
            {"type":"response.output_text.delta","output_index":0,"delta":"JARVIS "}
            """) +
        Sse("""
            {"type":"response.output_text.delta","output_index":0,"delta":"online."}
            """) +
        Sse("""
            {"type":"response.output_item.done","output_index":0,"item":{"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"JARVIS online.","annotations":[]}]}}
            """) +
        Sse("""
            {"type":"response.completed","response":{"id":"resp_text","status":"completed","usage":{"input_tokens":20,"output_tokens":4,"input_tokens_details":{"cached_tokens":0,"cache_write_tokens":0}}}}
            """) +
        "data: [DONE]\n\n";

    private static string ToolResponseStream() =>
        Sse("""
            {"type":"response.output_item.added","output_index":0,"item":{"type":"function_call","id":"fc_item_1","call_id":"call_read_1","name":"read","arguments":""}}
            """) +
        Sse("""
            {"type":"response.function_call_arguments.delta","output_index":0,"delta":"{\"path\":\"README.md\","}
            """) +
        Sse("""
            {"type":"response.function_call_arguments.delta","output_index":0,"delta":"\"offset\":1,\"limit\":5}"}
            """) +
        Sse("""
            {"type":"response.function_call_arguments.done","output_index":0,"arguments":"{\"path\":\"README.md\",\"offset\":1,\"limit\":5}"}
            """) +
        Sse("""
            {"type":"response.output_item.done","output_index":0,"item":{"type":"function_call","id":"fc_item_1","call_id":"call_read_1","name":"read","arguments":"{\"path\":\"README.md\",\"offset\":1,\"limit\":5}"}}
            """) +
        Sse("""
            {"type":"response.completed","response":{"id":"resp_tool","status":"completed","usage":{"input_tokens":100,"output_tokens":12,"input_tokens_details":{"cached_tokens":25,"cache_write_tokens":5}}}}
            """);

    private static string OversizedToolArgumentsStream()
    {
        string added = JsonSerializer.Serialize(new
        {
            type = "response.output_item.added",
            output_index = 0,
            item = new
            {
                type = "function_call",
                id = "fc_oversized",
                call_id = "call_oversized",
                name = "read",
                arguments = string.Empty,
            },
        });
        string delta = JsonSerializer.Serialize(new
        {
            type = "response.function_call_arguments.delta",
            output_index = 0,
            delta = new string(
                'a',
                OpenAiResponsesModelProvider
                    .MaximumFunctionArgumentsCharacters + 1),
        });
        return Sse(added) + Sse(delta);
    }

    private static string Sse(string json) => $"data: {json}\n\n";

    private static void AddFailure(
        ICollection<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }

    private sealed class StaticApiKeySource(string apiKey) : IOpenAiApiKeySource
    {
        public ValueTask<string?> GetApiKeyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(apiKey);
        }
    }

    private sealed class ScriptedHttpMessageHandler(
        HttpStatusCode statusCode,
        string responseBody,
        string mediaType = "text/event-stream") : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthenticationHeaderValue? authorization =
                request.Headers.Authorization;
            AuthorizationScheme = authorization?.Scheme;
            AuthorizationParameter = authorization?.Parameter;
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    mediaType),
            };
        }
    }

    private sealed class CancellationHttpMessageHandler : HttpMessageHandler
    {
        private int cancellationObserved;

        public bool CancellationObserved =>
            Volatile.Read(ref cancellationObserved) != 0;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException(
                    "The cancellation probe unexpectedly resumed.");
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref cancellationObserved, 1);
                throw;
            }
        }
    }
}
