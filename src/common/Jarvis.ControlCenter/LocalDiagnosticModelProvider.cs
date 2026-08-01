using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

public sealed class LocalDiagnosticModelProvider : IDesktopModelProvider
{
    public const string DisplayName = "LOCAL DIAGNOSTIC";
    public const string Boundary =
        "deterministic-local-provider-read-only-pi-tool-roundtrip";

    public async IAsyncEnumerable<DesktopModelStreamEvent> StreamAsync(
        DesktopModelBrokerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        JsonElement lastMessage = GetLastMessage(request.Context);
        string role = lastMessage.GetProperty("role").GetString() ??
            throw new InvalidOperationException(
                "The diagnostic provider message role was missing.");

        if (string.Equals(role, "toolResult", StringComparison.Ordinal))
        {
            string response = BuildToolResultResponse(lastMessage);
            foreach (string chunk in SplitForStreaming(response, 36))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(24, cancellationToken).ConfigureAwait(false);
                yield return new DesktopModelTextDelta(chunk);
            }

            yield return new DesktopModelCompleted(
                "stop",
                EstimateUsage(request.Context, response));
            yield break;
        }

        if (!string.Equals(role, "user", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The diagnostic provider expected a user or tool result " +
                "message.");
        }

        string toolCallId =
            $"local-ls-{request.RequestId.Replace("-", string.Empty, StringComparison.Ordinal)}";
        yield return new DesktopModelToolCallStarted(toolCallId, "ls");
        yield return new DesktopModelToolCallDelta(
            toolCallId,
            "{\"path\":\".\",\"limit\":40}");
        yield return new DesktopModelToolCallCompleted(toolCallId);
        yield return new DesktopModelCompleted(
            "toolUse",
            EstimateUsage(request.Context, string.Empty));
    }

    private static JsonElement GetLastMessage(JsonElement context)
    {
        if (!context.TryGetProperty("messages", out JsonElement messages) ||
            messages.ValueKind != JsonValueKind.Array ||
            messages.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "The diagnostic provider context contained no messages.");
        }

        return messages.EnumerateArray().Last();
    }

    private static string BuildToolResultResponse(JsonElement toolResult)
    {
        bool isError =
            toolResult.TryGetProperty("isError", out JsonElement error) &&
            error.ValueKind == JsonValueKind.True;
        string text = ExtractText(toolResult);
        if (isError)
        {
            return "The root-confined Pi read tool rejected the workspace " +
                "inspection safely. No mutation was attempted. Production " +
                "model authentication remains unconfigured.";
        }

        string[] entries = text
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(entry => !entry.StartsWith("[", StringComparison.Ordinal))
            .Take(8)
            .ToArray();
        string sample = entries.Length == 0
            ? "no visible entries"
            : string.Join(", ", entries);
        return "Workspace boundary admitted. Pi's root-confined ls tool " +
            $"returned visible top-level entries including: {sample}. " +
            "This provider is local and deterministic; production model " +
            "authentication is not configured.";
    }

    private static string ExtractText(JsonElement toolResult)
    {
        if (!toolResult.TryGetProperty(
                "content",
                out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            content
                .EnumerateArray()
                .Where(block =>
                    block.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out _))
                .Select(block =>
                    block.GetProperty("text").GetString() ?? string.Empty));
    }

    private static IEnumerable<string> SplitForStreaming(
        string value,
        int maximumCharacters)
    {
        for (int index = 0; index < value.Length; index += maximumCharacters)
        {
            yield return value.Substring(
                index,
                Math.Min(maximumCharacters, value.Length - index));
        }
    }

    private static DesktopModelUsage EstimateUsage(
        JsonElement context,
        string output)
    {
        long input = Math.Max(
            1,
            Encoding.UTF8.GetByteCount(context.GetRawText()) / 4);
        long outputTokens = output.Length == 0
            ? 1
            : Math.Max(1, Encoding.UTF8.GetByteCount(output) / 4);
        return new DesktopModelUsage(input, outputTokens, 0, 0);
    }
}
