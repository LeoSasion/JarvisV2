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
bool sessionLaunchProbe =
    args.Length == 5 &&
    string.Equals(
        args[0],
        "--session-launch-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--workspace", StringComparison.Ordinal);
bool sessionLaunchLifecycleProbe =
    args.Length == 5 &&
    string.Equals(
        args[0],
        "--session-launch-lifecycle-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--node", StringComparison.Ordinal) &&
    string.Equals(args[3], "--workspace", StringComparison.Ordinal);
if (
    !providerProbe &&
    !bootstrapProbe &&
    !sessionLaunchProbe &&
    !sessionLaunchLifecycleProbe)
{
    Console.Error.WriteLine(
        "Usage: <--provider-probe | --bootstrap-probe | " +
        "--session-launch-probe --node <absolute-node.exe> " +
        "--session-launch-lifecycle-probe --node <absolute-node.exe> " +
        "--workspace <absolute-workspace>>");
    return 2;
}

object receipt = providerProbe
    ? await LocalDiagnosticProviderProbe.RunAsync()
    : bootstrapProbe
        ? DesktopRuntimeBootstrapProbe.Run()
        : sessionLaunchProbe
            ? DesktopSessionLaunchAdmissionProbe.Run(
                Path.GetFullPath(args[4]),
                Path.GetFullPath(args[2]))
            : await DesktopSessionLaunchAdmissionProbe.RunLifecycleAsync(
                Path.GetFullPath(args[4]),
                Path.GetFullPath(args[2]));
Console.WriteLine(JsonSerializer.Serialize(
    receipt,
    receipt.GetType(),
    new JsonSerializerOptions { WriteIndented = true }));
string result = (string)(receipt.GetType()
    .GetProperty("Result")?
    .GetValue(receipt) ?? string.Empty);
return string.Equals(result, "passed", StringComparison.Ordinal) ? 0 : 1;
