using System.Text.Json;
using Jarvis.ControlCenter;
using Jarvis.DesktopPresence;

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
bool recentSessionStoreProbe =
    args.Length == 3 &&
    string.Equals(
        args[0],
        "--recent-session-store-probe",
        StringComparison.Ordinal) &&
    string.Equals(args[1], "--workspace", StringComparison.Ordinal);
bool handoffVfxProbe =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "--handoff-vfx-probe",
        StringComparison.Ordinal);
bool uiLanguageProbe =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "--ui-language-probe",
        StringComparison.Ordinal);
bool desktopPresenceProbe =
    args.Length == 1 &&
    string.Equals(
        args[0],
        "--desktop-presence-probe",
        StringComparison.Ordinal);
if (
    !providerProbe &&
    !bootstrapProbe &&
    !sessionLaunchProbe &&
    !sessionLaunchLifecycleProbe &&
    !recentSessionStoreProbe &&
    !handoffVfxProbe &&
    !uiLanguageProbe &&
    !desktopPresenceProbe)
{
    Console.Error.WriteLine(
        "Usage: <--provider-probe | --bootstrap-probe | " +
        "--session-launch-probe --node <absolute-node.exe> " +
        "--session-launch-lifecycle-probe --node <absolute-node.exe> " +
        "--recent-session-store-probe " +
        "--handoff-vfx-probe " +
        "--ui-language-probe " +
        "--desktop-presence-probe " +
        "--workspace <absolute-workspace>>");
    return 2;
}

object receipt = providerProbe
    ? await LocalDiagnosticProviderProbe.RunAsync()
    : desktopPresenceProbe
        ? await DesktopPresenceProbe.RunAsync()
    : uiLanguageProbe
        ? UiLanguageProbe.Run()
        : bootstrapProbe
            ? DesktopRuntimeBootstrapProbe.Run()
            : handoffVfxProbe
                ? HandoffConstellationProbe.Run()
                : recentSessionStoreProbe
                    ? await DesktopRecentSessionStoreProbe.RunAsync(
                        Path.GetFullPath(args[2]))
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
