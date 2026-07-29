using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace Jarvis.Win10.NeuralVoidPreview;

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
        if (args.Length == 1 && args[0] == "show")
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose,
            };
            application.Run(new MainWindow());
            return 0;
        }

        if (args.Length == 5 && args[0] == "render")
        {
            if (!TryDouble(args[2], out double hue) ||
                !TryDouble(args[4], out double phase))
            {
                WriteUsage();
                return 2;
            }

            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            PreviewRenderReceipt receipt =
                PreviewRenderer.Render(
                    args[1],
                    hue,
                    args[3],
                    phase);
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result ==
                    "rendered-own-process-preview"
                ? 0
                : 12;
        }

        WriteUsage();
        return 2;
    }

    private static bool TryDouble(
        string value,
        out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);

    private static void WriteUsage() =>
        Console.Error.WriteLine(
            "Usage: jarvis-win10-neural-void-preview show " +
            "or render <png-path> <hue> <effect> <phase>");
}
