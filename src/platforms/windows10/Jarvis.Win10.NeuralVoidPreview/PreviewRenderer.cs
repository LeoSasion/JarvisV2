using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Jarvis.VisualEffects;
using Jarvis.Win10.RgbThemeModel;

namespace Jarvis.Win10.NeuralVoidPreview;

internal static class PreviewRenderer
{
    private const int Width = 1600;
    private const int Height = 900;

    public static PreviewRenderReceipt Render(
        string outputPath,
        double hueDegrees,
        string effectId,
        double phase)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        List<string> failures = [];
        string fullOutputPath = Path.GetFullPath(outputPath);
        Window? renderHost = null;
        try
        {
            RgbFrame frame =
                RgbEffectEngine.Sample(
                    hueDegrees,
                    1.0,
                    1.0,
                    effectId,
                    phase);
            DesktopShellSurface surface = new();
            surface.ApplyFrame(frame);
            renderHost = CreateRenderHost(surface);
            renderHost.Show();
            PumpDispatcher();
            surface.PrepareLayoutRailForSnapshot();
            PumpDispatcher();
            surface.SetClockForSnapshot(
                new DateTime(2026, 8, 6, 14, 39, 0));
            surface.Measure(new Size(Width, Height));
            surface.Arrange(new Rect(0, 0, Width, Height));
            surface.UpdateLayout();
            PumpDispatcher();

            RenderTargetBitmap bitmap =
                new(
                    Width,
                    Height,
                    96.0,
                    96.0,
                    PixelFormats.Pbgra32);
            bitmap.Render(surface);
            bool usefulPixels = HasUsefulPixels(bitmap);
            if (!usefulPixels)
            {
                DrawingVisual visualCopy = new();
                using DrawingContext context = visualCopy.RenderOpen();
                context.DrawRectangle(
                    new VisualBrush(surface)
                    {
                        Stretch = Stretch.Fill,
                    },
                    null,
                    new Rect(0, 0, Width, Height));
                bitmap =
                    new RenderTargetBitmap(
                        Width,
                        Height,
                        96.0,
                        96.0,
                        PixelFormats.Pbgra32);
                bitmap.Render(visualCopy);
                usefulPixels = HasUsefulPixels(bitmap);
            }

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string? directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream stream =
                new(
                    fullOutputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
            encoder.Save(stream);

            if (!usefulPixels)
            {
                failures.Add(
                    "rendered-frame-was-empty-or-transparent:" +
                    $"visible={surface.IsVisible};" +
                    $"size={surface.ActualWidth}x{surface.ActualHeight};" +
                    $"source={PresentationSource.FromVisual(surface) is not null}");
                return Receipt(
                    "blocked",
                    fullOutputPath,
                    frame,
                    failures);
            }

            return Receipt(
                "rendered-own-process-preview",
                fullOutputPath,
                frame,
                failures);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentOutOfRangeException or
            InvalidOperationException)
        {
            failures.Add(
                $"render-exception:{exception.GetType().Name}");
            return Receipt(
                "blocked",
                fullOutputPath,
                null,
                failures);
        }
        finally
        {
            if (renderHost is not null)
            {
                renderHost.Content = null;
                renderHost.Close();
            }
        }
    }

    private static Window CreateRenderHost(
        DesktopShellSurface surface)
    {
        double hostWidth =
            Math.Min(Width, SystemParameters.PrimaryScreenWidth);
        double hostHeight = hostWidth * Height / Width;
        Viewbox stage = new()
        {
            Width = hostWidth,
            Height = hostHeight,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Child = surface,
        };
        return new Window
        {
            Width = hostWidth,
            Height = hostHeight,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Background = Brushes.Black,
            Content = stage,
        };
    }

    private static void PumpDispatcher()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static bool HasUsefulPixels(BitmapSource bitmap)
    {
        int stride = Width * 4;
        byte[] pixels = new byte[stride * Height];
        bitmap.CopyPixels(pixels, stride, 0);
        for (int y = 0; y < Height; y += 16)
        {
            for (int x = 0; x < Width; x += 16)
            {
                int offset = (y * stride) + (x * 4);
                byte blue = pixels[offset];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                if (alpha >= 64 && Math.Max(red, Math.Max(green, blue)) >= 64)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static PreviewRenderReceipt Receipt(
        string result,
        string outputPath,
        RgbFrame? frame,
        IReadOnlyList<string> failures) =>
        new(
            1,
            "jarvisv2-win10-neural-void-owned-preview-render",
            result,
            "own-process-offscreen-wpf-surface",
            outputPath,
            Width,
            Height,
            frame?.Hex ?? string.Empty,
            frame?.EffectId ?? string.Empty,
            frame?.Phase ?? 0.0,
            false,
            true,
            false,
            false,
            false,
            "not-run",
            false,
            failures);
}
