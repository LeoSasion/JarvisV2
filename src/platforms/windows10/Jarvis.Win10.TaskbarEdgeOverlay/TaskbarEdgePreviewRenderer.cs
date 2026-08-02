using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

internal sealed record TaskbarEdgePreviewMetrics(
    int Width,
    int Height,
    int ChangedPixelCount,
    int DistinctChangedColorCount,
    int MinimumChangedX,
    int MaximumChangedX,
    int MinimumChangedY,
    int MaximumChangedY);

internal static class TaskbarEdgePreviewRenderer
{
    public const int PreviewWidth = 1600;
    public const int PreviewHeight = 48;
    public const int PreviewFrameIndex = 7;
    private const int BytesPerPixel = 4;
    private const byte BackgroundRed = 0x14;
    private const byte BackgroundGreen = 0x19;
    private const byte BackgroundBlue = 0x1D;

    public static TaskbarEdgePreviewMetrics Render(string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Taskbar edge preview output must be a PNG.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                "Preview output directory is unavailable."));

        IReadOnlyList<RgbFrame> frames = OverlayWindow.BuildSignalFrames();
        RgbFrame frame = frames[PreviewFrameIndex];
        byte[] pixels = CreateBackground();
        RenderEdgeLine(pixels, frame);
        RenderSignalLines(pixels, frame);
        RenderPulseGlow(pixels, frame);
        RenderPulseCore(pixels, frame);
        RenderPulsePoint(pixels, frame);

        int stride = PreviewWidth * BytesPerPixel;
        BitmapSource bitmap = BitmapSource.Create(
            PreviewWidth,
            PreviewHeight,
            96.0,
            96.0,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        TaskbarEdgePreviewMetrics metrics = Measure(pixels);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = new(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        encoder.Save(stream);
        return metrics;
    }

    private static byte[] CreateBackground()
    {
        byte[] pixels =
            new byte[PreviewWidth * PreviewHeight * BytesPerPixel];
        for (int offset = 0; offset < pixels.Length; offset += BytesPerPixel)
        {
            pixels[offset] = BackgroundBlue;
            pixels[offset + 1] = BackgroundGreen;
            pixels[offset + 2] = BackgroundRed;
            pixels[offset + 3] = 0xFF;
        }

        return pixels;
    }

    private static void RenderEdgeLine(byte[] pixels, RgbFrame frame)
    {
        double opacity = (74.0 / 255.0) * 0.28;
        for (int x = 0; x < PreviewWidth; x++)
        {
            Composite(pixels, x, 0, frame, opacity);
        }
    }

    private static void RenderSignalLines(byte[] pixels, RgbFrame frame)
    {
        int originX = (int)((PreviewWidth - TaskbarEdgeVectorModel.RailWidth) / 2.0);
        for (int y = 0; y <= 1; y++)
        {
            for (int localX = 0;
                 localX < (int)TaskbarEdgeVectorModel.RailWidth;
                 localX++)
            {
                double first = TaskbarEdgeVectorModel.SampleSegmentCoverage(
                    localX,
                    y,
                    0.0,
                    1.0,
                    94.0,
                    1.0,
                    1.0);
                double second = TaskbarEdgeVectorModel.SampleSegmentCoverage(
                    localX,
                    y,
                    142.0,
                    1.0,
                    236.0,
                    1.0,
                    1.0);
                Composite(
                    pixels,
                    originX + localX,
                    y,
                    frame,
                    (142.0 / 255.0) * Math.Max(first, second));
            }
        }
    }

    private static void RenderPulseGlow(byte[] pixels, RgbFrame frame)
    {
        int width = (int)TaskbarEdgeVectorModel.RailWidth;
        int height = (int)TaskbarEdgeVectorModel.RailHeight;
        double[] source = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                source[(y * width) + x] =
                    TaskbarEdgeVectorModel.SampleStrokeCoverage(x, y, 2.0);
            }
        }

        double[] kernel = TaskbarEdgeVectorModel.GaussianKernel();
        double[] horizontal = Convolve(source, width, height, kernel, true);
        double[] blurred = Convolve(horizontal, width, height, kernel, false);
        int originX = (PreviewWidth - width) / 2;
        double opacity = (196.0 / 255.0) * 0.48;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Composite(
                    pixels,
                    originX + x,
                    y,
                    frame,
                    opacity * blurred[(y * width) + x]);
            }
        }
    }

    private static void RenderPulseCore(byte[] pixels, RgbFrame frame)
    {
        int width = (int)TaskbarEdgeVectorModel.RailWidth;
        int height = (int)TaskbarEdgeVectorModel.RailHeight;
        int originX = (PreviewWidth - width) / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double coverage =
                    TaskbarEdgeVectorModel.SampleStrokeCoverage(x, y, 1.0);
                Composite(
                    pixels,
                    originX + x,
                    y,
                    frame,
                    (242.0 / 255.0) * coverage);
            }
        }
    }

    private static void RenderPulsePoint(byte[] pixels, RgbFrame frame)
    {
        int width = (int)TaskbarEdgeVectorModel.RailWidth;
        int height = (int)TaskbarEdgeVectorModel.RailHeight;
        int originX = (PreviewWidth - width) / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double coverage =
                    TaskbarEdgeVectorModel.SamplePointCoverage(x, y);
                Composite(
                    pixels,
                    originX + x,
                    y,
                    frame,
                    (242.0 / 255.0) * coverage);
            }
        }
    }

    private static double[] Convolve(
        double[] source,
        int width,
        int height,
        double[] kernel,
        bool horizontal)
    {
        double[] result = new double[source.Length];
        int radius = TaskbarEdgeVectorModel.GaussianBlurRadius;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double value = 0.0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    int sampleX = horizontal ? x + offset : x;
                    int sampleY = horizontal ? y : y + offset;
                    if (sampleX >= 0 && sampleX < width &&
                        sampleY >= 0 && sampleY < height)
                    {
                        value +=
                            source[(sampleY * width) + sampleX] *
                            kernel[offset + radius];
                    }
                }

                result[(y * width) + x] = value;
            }
        }

        return result;
    }

    private static void Composite(
        byte[] pixels,
        int x,
        int y,
        RgbFrame frame,
        double opacity)
    {
        if (x < 0 || x >= PreviewWidth ||
            y < 0 || y >= PreviewHeight ||
            opacity <= 0.0)
        {
            return;
        }

        double alpha = Math.Clamp(opacity, 0.0, 1.0);
        int offset = ((y * PreviewWidth) + x) * BytesPerPixel;
        pixels[offset] = Blend(pixels[offset], frame.Blue, alpha);
        pixels[offset + 1] = Blend(pixels[offset + 1], frame.Green, alpha);
        pixels[offset + 2] = Blend(pixels[offset + 2], frame.Red, alpha);
    }

    private static byte Blend(byte destination, byte source, double alpha) =>
        (byte)Math.Clamp(
            (int)Math.Round(
                (destination * (1.0 - alpha)) + (source * alpha)),
            0,
            255);

    private static TaskbarEdgePreviewMetrics Measure(byte[] pixels)
    {
        int changed = 0;
        int minimumX = PreviewWidth;
        int maximumX = -1;
        int minimumY = PreviewHeight;
        int maximumY = -1;
        HashSet<int> changedColors = [];
        for (int y = 0; y < PreviewHeight; y++)
        {
            for (int x = 0; x < PreviewWidth; x++)
            {
                int offset = ((y * PreviewWidth) + x) * BytesPerPixel;
                byte blue = pixels[offset];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                if (blue == BackgroundBlue &&
                    green == BackgroundGreen &&
                    red == BackgroundRed &&
                    alpha == 0xFF)
                {
                    continue;
                }

                changed++;
                minimumX = Math.Min(minimumX, x);
                maximumX = Math.Max(maximumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumY = Math.Max(maximumY, y);
                changedColors.Add(
                    (alpha << 24) |
                    (red << 16) |
                    (green << 8) |
                    blue);
            }
        }

        return new(
            PreviewWidth,
            PreviewHeight,
            changed,
            changedColors.Count,
            minimumX,
            maximumX,
            minimumY,
            maximumY);
    }
}
