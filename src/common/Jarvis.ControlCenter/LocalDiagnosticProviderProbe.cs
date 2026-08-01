using System.Text.Json;
using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

public sealed record LocalDiagnosticProviderProbeReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    bool RequestedOnlyLs,
    bool StreamedText,
    bool ProductionAuthenticationConfigured,
    bool MutationPerformed,
    IReadOnlyList<string> EventSequence,
    string Response,
    IReadOnlyList<string> Failures);

public static class LocalDiagnosticProviderProbe
{
    public static async Task<LocalDiagnosticProviderProbeReceipt> RunAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> failures = [];
        LocalDiagnosticModelProvider provider = new();

        using JsonDocument userContext = JsonDocument.Parse(
            """
            {"messages":[{"role":"user","content":[{"type":"text","text":"Inspect the workspace boundary."}]}]}
            """);
        using JsonDocument options = JsonDocument.Parse("{}");
        List<DesktopModelStreamEvent> first = await CollectAsync(
            provider,
            new DesktopModelBrokerRequest(
                "probe-user",
                DesktopModelBrokerServer.ProviderId,
                DesktopModelBrokerServer.ModelId,
                userContext.RootElement,
                options.RootElement),
            cancellationToken).ConfigureAwait(false);

        bool requestedOnlyLs =
            first.Count == 4 &&
            first[0] is DesktopModelToolCallStarted
            {
                ToolCallId: "local-ls-probeuser",
                Name: "ls",
            } &&
            first[1] is DesktopModelToolCallDelta
            {
                ToolCallId: "local-ls-probeuser",
                Delta: "{\"path\":\".\",\"limit\":40}",
            } &&
            first[2] is DesktopModelToolCallCompleted
            {
                ToolCallId: "local-ls-probeuser",
            } &&
            first[3] is DesktopModelCompleted { Reason: "toolUse" };
        if (!requestedOnlyLs)
        {
            failures.Add("The first pass did not request exactly one ls tool call.");
        }

        using JsonDocument toolContext = JsonDocument.Parse(
            """
            {"messages":[{"role":"toolResult","toolCallId":"local-ls-probeuser","toolName":"ls","content":[{"type":"text","text":"README.md\nsrc\ndocs\nconfig"}],"isError":false}]}
            """);
        List<DesktopModelStreamEvent> second = await CollectAsync(
            provider,
            new DesktopModelBrokerRequest(
                "probe-tool-result",
                DesktopModelBrokerServer.ProviderId,
                DesktopModelBrokerServer.ModelId,
                toolContext.RootElement,
                options.RootElement),
            cancellationToken).ConfigureAwait(false);
        string response = string.Concat(
            second.OfType<DesktopModelTextDelta>().Select(item => item.Delta));
        bool streamedText =
            second.OfType<DesktopModelTextDelta>().Count() >= 2 &&
            second.LastOrDefault() is DesktopModelCompleted { Reason: "stop" };
        if (!streamedText)
        {
            failures.Add("The tool-result pass did not stream text before stop.");
        }
        if (!response.Contains(
                "production model authentication is not configured",
                StringComparison.Ordinal))
        {
            failures.Add("The response did not disclose the missing production authentication.");
        }
        if (!response.Contains("README.md", StringComparison.Ordinal) ||
            !response.Contains("src", StringComparison.Ordinal))
        {
            failures.Add("The response did not summarize the synthetic ls result.");
        }

        IReadOnlyList<string> sequence = first
            .Concat(second)
            .Select(item => item.GetType().Name)
            .ToArray();
        bool passed = failures.Count == 0;
        return new LocalDiagnosticProviderProbeReceipt(
            1,
            "jarvisv2-local-diagnostic-provider-probe",
            passed ? "passed" : "failed",
            requestedOnlyLs,
            streamedText,
            false,
            false,
            sequence,
            response,
            failures);
    }

    private static async Task<List<DesktopModelStreamEvent>> CollectAsync(
        LocalDiagnosticModelProvider provider,
        DesktopModelBrokerRequest request,
        CancellationToken cancellationToken)
    {
        List<DesktopModelStreamEvent> events = [];
        await foreach (DesktopModelStreamEvent item in provider
                           .StreamAsync(request, cancellationToken)
                           .ConfigureAwait(false))
        {
            events.Add(item);
        }
        return events;
    }
}
