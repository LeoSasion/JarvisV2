using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        List<string> failures = [];
        string fullOutputPath = Path.GetFullPath(outputPath);
        try
        {
            RgbFrame frame =
                RgbEffectEngine.Sample(
                    hueDegrees,
                    1.0,
                    1.0,
                    effectId,
                    phase);
            NeuralVoidPreviewSurface surface = new();
            surface.ApplyFrame(frame);
            surface.Measure(new Size(Width, Height));
            surface.Arrange(new Rect(0, 0, Width, Height));
            surface.UpdateLayout();

            RenderTargetBitmap bitmap =
                new(
                    Width,
                    Height,
                    96.0,
                    96.0,
                    PixelFormats.Pbgra32);
            bitmap.Render(surface);
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
