using System.Text.Json;

namespace Jarvis.Win10.SurfaceSelectorModel;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        if (args.Length != 1 ||
            (args[0] != "compile" && args[0] != "test"))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-win10-surface-selector-model " +
                "<compile|test>");
            return 2;
        }

        EmbeddedModelInputs inputs = EmbeddedModelInputReader.Read();
        if (args[0] == "compile")
        {
            SelectorCompilationReceipt receipt =
                SelectorCompiler.Compile(
                    inputs.Candidate,
                    inputs.Evidence,
                    inputs.CandidateSha256,
                    inputs.EvidenceSha256);
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result ==
                    "compiled-offline-selector-candidates"
                ? 0
                : 12;
        }

        SelectorModelTestReceipt testReceipt =
            ModelScenarios.Run(inputs);
        Console.WriteLine(
            JsonSerializer.Serialize(testReceipt, JsonOptions));
        return testReceipt.Result == "passed" ? 0 : 12;
    }
}
