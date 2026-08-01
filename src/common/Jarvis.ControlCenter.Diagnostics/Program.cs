using System.Text.Json;
using Jarvis.ControlCenter;

if (args.Length != 1 ||
    !string.Equals(
        args[0],
        "--provider-probe",
        StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: --provider-probe");
    return 2;
}

LocalDiagnosticProviderProbeReceipt receipt =
    await LocalDiagnosticProviderProbe.RunAsync();
Console.WriteLine(JsonSerializer.Serialize(
    receipt,
    new JsonSerializerOptions { WriteIndented = true }));
return string.Equals(receipt.Result, "passed", StringComparison.Ordinal)
    ? 0
    : 1;
