using System.Text.Json;
using Jarvis.ControlCenter;

bool providerProbe =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "--provider-probe",
        StringComparison.Ordinal);
bool bootstrapProbe =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "--bootstrap-probe",
        StringComparison.Ordinal);
if (!providerProbe && !bootstrapProbe)
{
    Console.Error.WriteLine(
        "Usage: <--provider-probe | --bootstrap-probe>");
    return 2;
}

object receipt = providerProbe
    ? await LocalDiagnosticProviderProbe.RunAsync()
    : DesktopRuntimeBootstrapProbe.Run();
Console.WriteLine(JsonSerializer.Serialize(
    receipt,
    receipt.GetType(),
    new JsonSerializerOptions { WriteIndented = true }));
string result = (string)(receipt.GetType()
    .GetProperty("Result")?
    .GetValue(receipt) ?? string.Empty);
return string.Equals(result, "passed", StringComparison.Ordinal) ? 0 : 1;
