using System.Text.Json;

namespace Jarvis.Win10.ShellSurfaceProbe;

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
            !string.Equals(args[0], "inspect", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-win10-shell-surface-probe inspect");
            return 2;
        }

        ShellSurfaceProbeReceipt receipt =
            ShellSurfaceInspector.Inspect();
        Console.WriteLine(
            JsonSerializer.Serialize(receipt, JsonOptions));
        return string.Equals(
            receipt.Result,
            "passed-read-only-inventory",
            StringComparison.Ordinal)
            ? 0
            : 12;
    }
}
