using System.Text.Json;
using Jarvis.PiAgentHost;

const int TimeoutMilliseconds = 15_000;
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
bool faultCommand =
    args.Length == 5 &&
    string.Equals(args[0], "fault-tests", StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--fixtures", StringComparison.Ordinal);
if (!probeCommand && !brokerProbeCommand && !faultCommand)
{
    Console.Error.WriteLine(
        "Usage: jarvis-pi-agent-desktop-bridge " +
        "<probe --node <absolute-node.exe> --sidecar <absolute-host.mjs> | " +
        "broker-probe --node <absolute-node.exe> " +
        "--sidecar <absolute-host.mjs> | " +
        "fault-tests --node <absolute-node.exe> " +
        "--fixtures <absolute-fixture-root>>");
    return 2;
}

try
{
    using CancellationTokenSource timeout = new(TimeoutMilliseconds);
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
