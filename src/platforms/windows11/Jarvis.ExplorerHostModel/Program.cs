using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.ExplorerHostModel;

internal static class Program
{
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2 ||
            !string.Equals(
                args[0],
                "evaluate-fixture",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-explorer-host-model evaluate-fixture <snapshot.json>");
            return 2;
        }

        try
        {
            string json = File.ReadAllText(args[1]);
            HostSnapshot? snapshot =
                JsonSerializer.Deserialize<HostSnapshot>(json, InputOptions);
            if (snapshot is null)
            {
                throw new JsonException("The fixture root is null.");
            }

            HostPlanReceipt receipt = HostAdmissionPlanner.Evaluate(snapshot);
            Console.Out.WriteLine(
                JsonSerializer.Serialize(receipt, OutputOptions));
            return receipt.Result == "passed-offline-plan" ? 0 : 12;
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is JsonException)
        {
            Console.Error.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        error = "invalid-offline-fixture",
                        message = exception.Message,
                        executionSupported = false,
                        activationPermitted = false,
                    },
                    OutputOptions));
            return 2;
        }
    }
}
