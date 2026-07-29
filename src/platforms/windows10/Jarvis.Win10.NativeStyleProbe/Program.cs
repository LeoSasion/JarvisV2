using System.Text.Json;
using System.Windows;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            WriteUsage();
            return 2;
        }

        HostProbeReceipt hostReceipt = Win10HostInspector.Inspect();
        switch (args[0])
        {
            case "inspect":
                WriteJson(hostReceipt);
                return hostReceipt.Passed ? 0 : 12;

            case "verify-owned-window":
                if (!hostReceipt.Passed)
                {
                    WriteJson(hostReceipt);
                    return 12;
                }

                OwnedWindowVerificationReceipt verification =
                    OwnedWindowVerifier.Verify(hostReceipt);
                WriteJson(verification);
                return string.Equals(
                    verification.Result,
                    "passed-own-window-only",
                    StringComparison.Ordinal)
                    ? 0
                    : 13;

            case "show":
                if (!hostReceipt.Passed)
                {
                    WriteJson(hostReceipt);
                    return 12;
                }

                Application application = new()
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose,
                };
                MainWindow window = new(hostReceipt);
                application.Run(window);
                return 0;

            default:
                WriteUsage();
                return 2;
        }
    }

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteUsage() =>
        Console.Error.WriteLine(
            "Usage: jarvis-win10-native-style-probe <inspect|verify-owned-window|show>");
}
