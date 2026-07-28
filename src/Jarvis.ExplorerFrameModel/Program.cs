using System.Text.Json;

namespace Jarvis.ExplorerFrameModel;

internal static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static int Main(string[] args)
    {
        if (args.Length != 1 ||
            !string.Equals(
                args[0],
                "model-test",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-explorer-frame-model model-test");
            return 2;
        }

        ModelTestReceipt receipt = ModelScenarios.Run();
        Console.Out.WriteLine(
            JsonSerializer.Serialize(receipt, OutputOptions));
        return receipt.Result == "passed" ? 0 : 12;
    }
}
