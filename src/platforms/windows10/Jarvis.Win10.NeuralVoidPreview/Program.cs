using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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
        if (args.Length == 0 ||
            (args.Length == 1 && args[0] == "show"))
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose,
            };
            application.Run(new MainWindow());
            return 0;
        }

        if ((args.Length == 5 && args[0] is
                "render" or
                "render-explorer-preview" or
                "render-task-manager") ||
            (args.Length == 6 && args[0] == "render-owned-layout"))
        {
            LayoutPreset? layout = null;
            if (args[0] == "render-owned-layout")
            {
                if (!Enum.TryParse(args[5], out LayoutPreset parsed) || !Enum.IsDefined(parsed))
                {
                    WriteUsage();
                    return 2;
                }

                layout = parsed;
            }

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
                    phase,
                    args[0] switch
                    {
                        "render-explorer-preview" =>
                            PreviewScene.ExplorerPreview,
                        "render-task-manager" => PreviewScene.TaskManager,
                        "render-owned-layout" => PreviewScene.OwnedWindowLayout,
                        _ => PreviewScene.Desktop,
                    },
                    layout);
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result ==
                    "rendered-own-process-preview"
                ? 0
                : 12;
        }

        if (args.Length == 1 &&
            args[0] == "test-vector-adapter")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            WpfVectorAdapterTestReceipt receipt =
                WpfVectorAdapterScenarios.Run();
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 &&
            args[0] == "test-edge-bars")
        {
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            EdgeBarTestReceipt receipt = EdgeBarScenarios.Run();
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 &&
            args[0] == "test-owned-window-layout")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            OwnedWindowLayoutTestReceipt receipt =
                OwnedWindowLayoutScenarios.Run();
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 &&
            args[0] == "test-explorer-workspace")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            ExplorerWorkspaceTestReceipt receipt =
                ExplorerWorkspaceScenarios.Run();
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 &&
            args[0] == "test-task-manager-surface")
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            TaskManagerSurfaceTestReceipt receipt =
                TaskManagerSurfaceScenarios.Run();
            Console.WriteLine(
                JsonSerializer.Serialize(receipt, JsonOptions));
            return receipt.Result == "passed" ? 0 : 12;
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
            "or render <png-path> <hue> <effect> <phase> " +
            "or render-explorer-preview <png-path> <hue> <effect> <phase> " +
            "or render-task-manager <png-path> <hue> <effect> <phase> " +
            "or render-owned-layout <png-path> <hue> <effect> <phase> <preset> " +
            "or test-vector-adapter " +
            "or test-edge-bars " +
            "or test-owned-window-layout " +
            "or test-explorer-workspace " +
            "or test-task-manager-surface");
}
