using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jarvis.PiAgentHost;

public sealed record OpenAiResponsesProviderOptions(
    string ModelId = "gpt-5.6-sol",
    string ReasoningEffort = "medium",
    int MaximumOutputTokens = 16_384,
    int RequestTimeoutMilliseconds = 120_000);

public sealed class OpenAiResponsesModelProvider :
    IDesktopModelProvider,
    IDisposable
{
    public const string DisplayName = "OPENAI RESPONSES // GPT-5.6 SOL";
    public const string TransportBoundary =
        "desktop-only-https-no-sidecar-credential-transport";
    public const int MaximumSseEventCharacters = 1_048_576;
    public const int MaximumFunctionArgumentsCharacters = 262_144;

    private static readonly Uri ResponsesEndpoint = new(
        "https://api.openai.com/v1/responses",
        UriKind.Absolute);
    private static readonly IReadOnlySet<string> AllowedToolNames =
        new HashSet<string>(
            [
                "read",
                "grep",
                "find",
                "ls",
                "propose_edit",
                "propose_create_file",
            ],
            StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> AllowedReasoningEfforts =
        new HashSet<string>(
            ["none", "low", "medium", "high", "xhigh", "max"],
            StringComparer.Ordinal);

    private readonly IOpenAiApiKeySource apiKeySource;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly OpenAiResponsesProviderOptions options;
    private int disposed;

    public OpenAiResponsesModelProvider(
        IOpenAiApiKeySource apiKeySource,
        OpenAiResponsesProviderOptions? options = null,
        HttpClient? httpClient = null)
    {
        this.apiKeySource = apiKeySource ??
            throw new ArgumentNullException(nameof(apiKeySource));
        this.options = options ?? new OpenAiResponsesProviderOptions();
        ValidateOptions(this.options);
        this.httpClient = httpClient ?? new HttpClient();
        ownsHttpClient = httpClient is null;
    }

    public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
        DesktopModelBrokerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(request);

        string? apiKey = await apiKeySource.GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            throw new InvalidOperationException(
                "OpenAI authentication is not configured in the desktop credential store.");
        }
        OpenAiApiKeyCredentialStore.ValidateApiKey(apiKey);

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeoutMilliseconds);
        using HttpRequestMessage message = new(
            HttpMethod.Post,
            ResponsesEndpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "text/event-stream"));
        message.Headers.UserAgent.ParseAdd("JARVIS2-Desktop/1.0");
        string body = BuildRequestBody(request);
        message.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI Responses request failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "text/event-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The OpenAI Responses endpoint did not return an SSE stream.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(
            timeout.Token);
        Dictionary<int, ToolStreamState> tools = [];
        bool terminal = false;
        bool emittedToolCall = false;
        await foreach (
            string data in ReadSseDataAsync(stream, timeout.Token))
        {
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                continue;
            }
            if (terminal)
            {
                throw new InvalidDataException(
                    "The OpenAI stream emitted data after completion.");
            }

            using JsonDocument eventDocument = ParseEvent(data);
            JsonElement root = eventDocument.RootElement;
            string type = GetRequiredString(root, "type");
            switch (type)
            {
                case "response.output_text.delta":
                case "response.refusal.delta":
                {
                    string delta = GetRequiredString(root, "delta");
                    if (delta.Length != 0)
                    {
                        yield return new DesktopModelTextDelta(delta);
                    }
                    break;
                }
                case "response.output_item.added":
                {
                    if (!TryGetFunctionCall(root, out FunctionCallItem? item))
                    {
                        break;
                    }
                    ToolStreamState state = StartToolCall(item!, tools);
                    emittedToolCall = true;
                    yield return new DesktopModelToolCallStarted(
                        state.ToolCallId,
                        state.Name);
                    if (state.Arguments.Length != 0)
                    {
                        yield return new DesktopModelToolCallDelta(
                            state.ToolCallId,
                            state.Arguments.ToString());
                    }
                    break;
                }
                case "response.function_call_arguments.delta":
                {
                    int outputIndex = GetRequiredInt32(root, "output_index");
                    ToolStreamState state = GetToolState(tools, outputIndex);
                    string delta = GetRequiredString(root, "delta");
                    AppendToolArguments(state, delta);
                    if (delta.Length != 0)
                    {
                        yield return new DesktopModelToolCallDelta(
                            state.ToolCallId,
                            delta);
                    }
                    break;
                }
                case "response.function_call_arguments.done":
                {
                    int outputIndex = GetRequiredInt32(root, "output_index");
                    ToolStreamState state = GetToolState(tools, outputIndex);
                    string arguments = GetRequiredString(root, "arguments");
                    string partial = state.Arguments.ToString();
                    if (!arguments.StartsWith(partial, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The OpenAI function arguments changed after streaming.");
                    }
                    string remainder = arguments[partial.Length..];
                    AppendToolArguments(state, remainder);
                    if (remainder.Length != 0)
                    {
                        yield return new DesktopModelToolCallDelta(
                            state.ToolCallId,
                            remainder);
                    }
                    break;
                }
                case "response.output_item.done":
                {
                    if (!TryGetFunctionCall(root, out FunctionCallItem? item))
                    {
                        break;
                    }
                    if (!tools.TryGetValue(item!.OutputIndex, out ToolStreamState? state))
                    {
                        state = StartToolCall(item, tools);
                        emittedToolCall = true;
                        yield return new DesktopModelToolCallStarted(
                            state.ToolCallId,
                            state.Name);
                    }
                    string partial = state.Arguments.ToString();
                    if (!item.Arguments.StartsWith(partial, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The completed OpenAI function arguments did not match the stream.");
                    }
                    string remainder = item.Arguments[partial.Length..];
                    if (remainder.Length != 0)
                    {
                        AppendToolArguments(state, remainder);
                        yield return new DesktopModelToolCallDelta(
                            state.ToolCallId,
                            remainder);
                    }
                    ValidateJsonObject(state.Arguments.ToString());
                    tools.Remove(item.OutputIndex);
                    yield return new DesktopModelToolCallCompleted(
                        state.ToolCallId);
                    break;
                }
                case "response.completed":
                case "response.incomplete":
                {
                    if (tools.Count != 0)
                    {
                        throw new InvalidDataException(
                            "The OpenAI response completed with active tool calls.");
                    }
                    JsonElement completedResponse = root.GetProperty("response");
                    DesktopModelUsage usage = ReadUsage(completedResponse);
                    string reason = type == "response.incomplete"
                        ? "length"
                        : emittedToolCall ? "toolUse" : "stop";
                    terminal = true;
                    yield return new DesktopModelCompleted(reason, usage);
                    break;
                }
                case "response.failed":
                    throw new HttpRequestException(
                        "The OpenAI Responses stream reported a failed response.");
                case "error":
                    throw new HttpRequestException(
                        "The OpenAI Responses stream reported a protocol error.");
                default:
                    break;
            }
        }

        if (!terminal)
        {
            throw new InvalidDataException(
                "The OpenAI Responses stream ended without a terminal event.");
        }
    }

    private string BuildRequestBody(DesktopModelBrokerRequest request)
    {
        JsonArray input = ConvertInput(request.Context);
        JsonArray tools = ConvertTools(request.Context);
        int maximumOutputTokens = ReadMaximumOutputTokens(request.Options);
        JsonObject payload = new()
        {
            ["model"] = options.ModelId,
            ["input"] = input,
            ["stream"] = true,
            ["store"] = false,
            ["max_output_tokens"] = maximumOutputTokens,
            ["reasoning"] = new JsonObject
            {
                ["effort"] = options.ReasoningEffort,
            },
        };
        if (tools.Count != 0)
        {
            payload["tools"] = tools;
            payload["tool_choice"] = "auto";
        }
        return payload.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
        });
    }

    private static JsonArray ConvertInput(JsonElement context)
    {
        if (context.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "The Pi model context must be an object.");
        }
        JsonArray input = [];
        if (
            context.TryGetProperty("systemPrompt", out JsonElement system) &&
            system.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(system.GetString()))
        {
            input.Add(new JsonObject
            {
                ["role"] = "developer",
                ["content"] = system.GetString(),
            });
        }
        if (
            !context.TryGetProperty("messages", out JsonElement messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The Pi model context contained no message array.");
        }

        foreach (JsonElement message in messages.EnumerateArray())
        {
            string role = GetRequiredString(message, "role");
            switch (role)
            {
                case "user":
                    input.Add(CreateTextMessage("user", message));
                    break;
                case "assistant":
                    ConvertAssistantMessage(message, input);
                    break;
                case "toolResult":
                    input.Add(ConvertToolResult(message));
                    break;
                default:
                    throw new InvalidDataException(
                        "The Pi model context contained an unsupported role.");
            }
        }
        if (input.Count == 0)
        {
            throw new InvalidDataException(
                "The Pi model context produced no OpenAI input items.");
        }
        return input;
    }

    private static JsonObject CreateTextMessage(
        string role,
        JsonElement message)
    {
        string text = ExtractTextContent(message.GetProperty("content"));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                "A Pi text message contained no admitted text.");
        }
        return new JsonObject
        {
            ["role"] = role,
            ["content"] = text,
        };
    }

    private static void ConvertAssistantMessage(
        JsonElement message,
        JsonArray input)
    {
        JsonElement content = message.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "A Pi assistant message had invalid content.");
        }
        foreach (JsonElement block in content.EnumerateArray())
        {
            string type = GetRequiredString(block, "type");
            if (type == "text")
            {
                string text = GetRequiredString(block, "text");
                if (text.Length != 0)
                {
                    input.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = text,
                    });
                }
                continue;
            }
            if (type != "toolCall")
            {
                throw new InvalidDataException(
                    "A Pi assistant message contained unsupported content.");
            }

            string toolCallId = GetRequiredString(block, "id");
            string[] parts = toolCallId.Split('|', 2);
            string callId = parts[0];
            string name = GetRequiredString(block, "name");
            ValidateToolName(name);
            JsonElement arguments = block.GetProperty("arguments");
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "A Pi tool call contained invalid arguments.");
            }
            JsonObject item = new()
            {
                ["type"] = "function_call",
                ["call_id"] = callId,
                ["name"] = name,
                ["arguments"] = arguments.GetRawText(),
            };
            if (parts.Length == 2 && parts[1].StartsWith("fc_", StringComparison.Ordinal))
            {
                item["id"] = parts[1];
            }
            input.Add(item);
        }
    }

    private static JsonObject ConvertToolResult(JsonElement message)
    {
        string toolCallId = GetRequiredString(message, "toolCallId");
        string callId = toolCallId.Split('|', 2)[0];
        string toolName = GetRequiredString(message, "toolName");
        ValidateToolName(toolName);
        string output = ExtractTextContent(message.GetProperty("content"));
        if (
            message.TryGetProperty("isError", out JsonElement isError) &&
            isError.ValueKind == JsonValueKind.True)
        {
            output = "[tool_error]\n" + output;
        }
        return new JsonObject
        {
            ["type"] = "function_call_output",
            ["call_id"] = callId,
            ["output"] = output.Length == 0 ? "(no tool output)" : output,
        };
    }

    private static JsonArray ConvertTools(JsonElement context)
    {
        JsonArray result = [];
        if (!context.TryGetProperty("tools", out JsonElement tools))
        {
            return result;
        }
        if (tools.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The Pi model tools must be an array.");
        }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            string name = GetRequiredString(tool, "name");
            ValidateToolName(name);
            if (!names.Add(name))
            {
                throw new InvalidDataException(
                    "The Pi model tools contained a duplicate name.");
            }
            string description = GetRequiredString(tool, "description");
            JsonElement parameters = tool.GetProperty("parameters");
            if (parameters.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "A Pi model tool contained an invalid JSON schema.");
            }
            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = JsonNode.Parse(parameters.GetRawText()),
                ["strict"] = false,
            });
        }
        return result;
    }

    private int ReadMaximumOutputTokens(JsonElement requestOptions)
    {
        if (
            requestOptions.ValueKind == JsonValueKind.Object &&
            requestOptions.TryGetProperty("maxTokens", out JsonElement maximum) &&
            maximum.TryGetInt32(out int requested) &&
            requested > 0)
        {
            return Math.Clamp(requested, 16, options.MaximumOutputTokens);
        }
        return options.MaximumOutputTokens;
    }

    private static string ExtractTextContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }
        if (content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Pi message content must be text or a content array.");
        }
        StringBuilder text = new();
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (GetRequiredString(block, "type") != "text")
            {
                throw new InvalidDataException(
                    "Image and non-text Pi content is not admitted by this provider.");
            }
            if (text.Length != 0)
            {
                text.Append('\n');
            }
            text.Append(GetRequiredString(block, "text"));
        }
        return text.ToString();
    }

    private static ToolStreamState StartToolCall(
        FunctionCallItem item,
        IDictionary<int, ToolStreamState> tools)
    {
        ValidateToolName(item.Name);
        string toolCallId = string.IsNullOrWhiteSpace(item.ItemId)
            ? item.CallId
            : $"{item.CallId}|{item.ItemId}";
        if (
            string.IsNullOrWhiteSpace(toolCallId) ||
            toolCallId.Length > 256 ||
            tools.ContainsKey(item.OutputIndex))
        {
            throw new InvalidDataException(
                "The OpenAI stream emitted an invalid function call identity.");
        }
        ToolStreamState state = new(toolCallId, item.Name);
        AppendToolArguments(state, item.Arguments);
        tools.Add(item.OutputIndex, state);
        return state;
    }

    private static void AppendToolArguments(
        ToolStreamState state,
        string value)
    {
        if (value.Length >
            MaximumFunctionArgumentsCharacters - state.Arguments.Length)
        {
            throw new InvalidDataException(
                "The OpenAI function arguments exceeded their size boundary.");
        }
        state.Arguments.Append(value);
    }

    private static ToolStreamState GetToolState(
        IReadOnlyDictionary<int, ToolStreamState> tools,
        int outputIndex) =>
        tools.TryGetValue(outputIndex, out ToolStreamState? state)
            ? state
            : throw new InvalidDataException(
                "The OpenAI stream emitted function arguments before a call.");

    private static bool TryGetFunctionCall(
        JsonElement root,
        out FunctionCallItem? functionCall)
    {
        functionCall = null;
        int outputIndex = GetRequiredInt32(root, "output_index");
        JsonElement item = root.GetProperty("item");
        if (GetRequiredString(item, "type") != "function_call")
        {
            return false;
        }
        functionCall = new FunctionCallItem(
            outputIndex,
            GetRequiredString(item, "call_id"),
            item.TryGetProperty("id", out JsonElement itemId) &&
                itemId.ValueKind == JsonValueKind.String
                ? itemId.GetString()
                : null,
            GetRequiredString(item, "name"),
            item.TryGetProperty("arguments", out JsonElement arguments) &&
                arguments.ValueKind == JsonValueKind.String
                ? arguments.GetString() ?? string.Empty
                : string.Empty);
        return true;
    }

    private static DesktopModelUsage ReadUsage(JsonElement response)
    {
        if (
            !response.TryGetProperty("usage", out JsonElement usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return new DesktopModelUsage(0, 0, 0, 0);
        }
        long input = ReadNonNegativeInt64(usage, "input_tokens");
        long output = ReadNonNegativeInt64(usage, "output_tokens");
        long cacheRead = 0;
        long cacheWrite = 0;
        if (
            usage.TryGetProperty(
                "input_tokens_details",
                out JsonElement details) &&
            details.ValueKind == JsonValueKind.Object)
        {
            cacheRead = ReadNonNegativeInt64(details, "cached_tokens");
            cacheWrite = ReadNonNegativeInt64(details, "cache_write_tokens");
        }
        return new DesktopModelUsage(
            Math.Max(0, input - cacheRead - cacheWrite),
            output,
            cacheRead,
            cacheWrite);
    }

    private static long ReadNonNegativeInt64(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return 0;
        }
        if (!value.TryGetInt64(out long result) || result < 0)
        {
            throw new InvalidDataException(
                "The OpenAI response contained invalid usage values.");
        }
        return result;
    }

    private static JsonDocument ParseEvent(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The OpenAI Responses stream contained malformed JSON.",
                exception);
        }
    }

    private static async IAsyncEnumerable<string> ReadSseDataAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using StreamReader reader = new(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 8_192,
            leaveOpen: true);
        StringBuilder data = new();
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            if (line.Length == 0)
            {
                if (data.Length != 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }
                continue;
            }
            if (line[0] == ':')
            {
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }
            string value = line[5..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }
            if (data.Length != 0)
            {
                data.Append('\n');
            }
            data.Append(value);
            if (data.Length > MaximumSseEventCharacters)
            {
                throw new InvalidDataException(
                    "The OpenAI SSE event exceeded its size boundary.");
            }
        }
        if (data.Length != 0)
        {
            yield return data.ToString();
        }
    }

    private static void ValidateToolName(string name)
    {
        if (!AllowedToolNames.Contains(name))
        {
            throw new InvalidDataException(
                "The OpenAI provider received a tool outside the reviewed workspace allowlist.");
        }
    }

    private static void ValidateJsonObject(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "The OpenAI function arguments were not an object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The OpenAI function arguments were malformed.",
                exception);
        }
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            value.GetString() is not string result)
        {
            throw new InvalidDataException(
                $"The protocol field '{propertyName}' was missing or invalid.");
        }
        return result;
    }

    private static int GetRequiredInt32(
        JsonElement element,
        string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement value) ||
            !value.TryGetInt32(out int result) ||
            result < 0)
        {
            throw new InvalidDataException(
                $"The protocol field '{propertyName}' was missing or invalid.");
        }
        return result;
    }

    private static void ValidateOptions(
        OpenAiResponsesProviderOptions options)
    {
        if (
            string.IsNullOrWhiteSpace(options.ModelId) ||
            options.ModelId.Length > 128 ||
            options.ModelId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '_' and not '-') ||
            !AllowedReasoningEfforts.Contains(options.ReasoningEffort) ||
            options.MaximumOutputTokens is < 16 or > 128_000 ||
            options.RequestTimeoutMilliseconds is < 1_000 or > 600_000)
        {
            throw new ArgumentException(
                "The OpenAI Responses provider options failed admission.",
                nameof(options));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private sealed record FunctionCallItem(
        int OutputIndex,
        string CallId,
        string? ItemId,
        string Name,
        string Arguments);

    private sealed class ToolStreamState(
        string toolCallId,
        string name)
    {
        public string ToolCallId { get; } = toolCallId;
        public string Name { get; } = name;
        public StringBuilder Arguments { get; } = new();
    }
}
