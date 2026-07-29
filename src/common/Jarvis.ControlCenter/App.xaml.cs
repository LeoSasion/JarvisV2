using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Jarvis.ControlCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        if (eventArgs.Args.Length == 0)
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
            return;
        }

        if (eventArgs.Args.Length != 2 ||
            !string.Equals(
                eventArgs.Args[0],
                "--capture-preview",
                StringComparison.Ordinal))
        {
            Shutdown(2);
            return;
        }

        string outputPath = Path.GetFullPath(eventArgs.Args[1]);
        if (!string.Equals(
                Path.GetExtension(outputPath),
                ".png",
                StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(Path.GetDirectoryName(outputPath)))
        {
            Shutdown(2);
            return;
        }

        MainWindow preview = new()
        {
            Width = 1440,
            Height = 900,
            ResizeMode = ResizeMode.NoResize,
        };
        MainWindow = preview;
        preview.Show();

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => CaptureAndClose(preview, outputPath));
    }

    private void CaptureAndClose(MainWindow preview, string outputPath)
    {
        try
        {
            preview.UpdateLayout();
            RenderTargetBitmap bitmap = new(
                1440,
                900,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(preview);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream output = new(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            encoder.Save(output);
            preview.Close();
            Shutdown(0);
        }
        catch
        {
            preview.Close();
            Shutdown(3);
        }
    }
}
