using System.Text.Json;
using Jarvis.PiAgentHost;

const int TimeoutMilliseconds = 90_000;
JsonSerializerOptions serializerOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
};

bool probeCommand =
    args.Length == 5 &&
    string.Equals(args[0], "probe", StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--sidecar", StringComparison.Ordinal);
bool brokerProbeCommand =
    args.Length == 5 &&
    string.Equals(args[0], "broker-probe", StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--sidecar", StringComparison.Ordinal);
bool conversationProbeCommand =
    args.Length == 5 &&
    string.Equals(
        args[0],
        "conversation-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--sidecar", StringComparison.Ordinal);
bool runtimeProbeCommand =
    args.Length == 5 &&
    string.Equals(
        args[0],
        "runtime-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--sidecar", StringComparison.Ordinal);
bool faultCommand =
    args.Length == 5 &&
    string.Equals(args[0], "fault-tests", StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--fixtures", StringComparison.Ordinal);
bool openAiProviderProbeCommand =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "openai-provider-probe",
        StringComparison.Ordinal);
bool reviewedIterationProbeCommand =
    args.Length == 7 &&
    string.Equals(
        args[0],
        "reviewed-iteration-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--sidecar", StringComparison.Ordinal) &&
    string.Equals(args[5], "--git", StringComparison.Ordinal);
if (
    !probeCommand &&
    !brokerProbeCommand &&
    !conversationProbeCommand &&
    !runtimeProbeCommand &&
    !openAiProviderProbeCommand &&
    !reviewedIterationProbeCommand &&
    !faultCommand)
{
    Console.Error.WriteLine(
        "Usage: jarvis-pi-agent-desktop-bridge " +
        "<probe --node <absolute-node.exe> --sidecar <absolute-host.mjs> | " +
        "broker-probe --node <absolute-node.exe> " +
        "--sidecar <absolute-host.mjs> | " +
        "conversation-probe --node <absolute-node.exe> " +
        "--sidecar <absolute-host.mjs> | " +
        "runtime-probe --node <absolute-node.exe> " +
        "--sidecar <absolute-host.mjs> | " +
        "reviewed-iteration-probe --node <absolute-node.exe> " +
        "--sidecar <absolute-host.mjs> --git <absolute-git.exe> | " +
        "openai-provider-probe | " +
        "fault-tests --node <absolute-node.exe> " +
        "--fixtures <absolute-fixture-root>>");
    return 2;
}

try
{
    using CancellationTokenSource timeout = new(TimeoutMilliseconds);
    if (openAiProviderProbeCommand)
    {
        OpenAiResponsesProviderProbeReceipt receipt =
            await OpenAiResponsesProviderProbe.RunAsync(timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }
    if (probeCommand)
    {
        PiAgentSidecarOptions options = new(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]));
        PiAgentDesktopProbeReceipt receipt =
            await PiAgentDesktopProbe.RunAsync(options, timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }
    if (brokerProbeCommand)
    {
        PiAgentSidecarOptions options = new(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]));
        PiAgentDesktopBrokerProbeReceipt receipt =
            await PiAgentDesktopBrokerProbe.RunAsync(
                options,
                timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }
    if (conversationProbeCommand)
    {
        PiAgentSidecarOptions options = new(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]));
        PiAgentConversationProbeReceipt receipt =
            await PiAgentConversationProbe.RunAsync(
                options,
                timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }
    if (runtimeProbeCommand)
    {
        PiAgentSidecarOptions options = new(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]),
            RequestTimeoutMilliseconds: 15_000);
        PiAgentDesktopRuntimeProbeReceipt receipt =
            await PiAgentDesktopRuntimeProbe.RunAsync(
                options,
                timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }
    if (reviewedIterationProbeCommand)
    {
        PiAgentSidecarOptions options = new(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]));
        PiAgentReviewedIterationProbeReceipt receipt =
            await PiAgentReviewedIterationProbe.RunAsync(
                options,
                Path.GetFullPath(args[6]),
                timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt, serializerOptions));
        return receipt.Result == "passed" ? 0 : 1;
    }

    PiAgentBridgeFaultReceipt faultReceipt =
        await PiAgentBridgeFaultProbe.RunAsync(
            Path.GetFullPath(args[2]),
            Path.GetFullPath(args[4]),
            timeout.Token);
    Console.WriteLine(
        JsonSerializer.Serialize(faultReceipt, serializerOptions));
    return faultReceipt.Result == "passed" ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
