using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Jarvis.DesktopPresence;

public enum JarvisPresenceSignal
{
    Ready,
    Working,
    OwnerActionRequired,
    Faulted,
}

public static class JarvisPresenceIcon
{
    public static Icon Create() => Create(JarvisPresenceSignal.Ready);

    public static Icon Create(JarvisPresenceSignal signal)
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(8, 16, 20));
        Color accent = signal switch
        {
            JarvisPresenceSignal.OwnerActionRequired =>
                Color.FromArgb(255, 240, 185, 88),
            JarvisPresenceSignal.Faulted =>
                Color.FromArgb(255, 240, 113, 103),
            _ => Color.FromArgb(255, 85, 222, 211),
        };
        using Pen outer = new(Color.FromArgb(160, accent), 1.4f);
        using Pen inner = new(accent, 2.2f);
        using Pen signalPen = new(accent, 2.2f)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square,
        };
        using SolidBrush signalBrush = new(accent);
        graphics.DrawEllipse(outer, 3.5f, 3.5f, 24, 24);
        graphics.DrawEllipse(inner, 9, 9, 14, 14);
        DrawSignal(graphics, signalPen, signalBrush, signal);

        nint iconHandle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(iconHandle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    private static void DrawSignal(
        Graphics graphics,
        Pen signalPen,
        Brush signalBrush,
        JarvisPresenceSignal signal)
    {
        switch (signal)
        {
            case JarvisPresenceSignal.Working:
                graphics.DrawLine(signalPen, 12, 16, 20, 16);
                graphics.FillRectangle(signalBrush, 18, 14, 3, 4);
                break;
            case JarvisPresenceSignal.OwnerActionRequired:
                graphics.FillPolygon(
                    signalBrush,
                    [
                        new PointF(16, 11.5f),
                        new PointF(20.5f, 16),
                        new PointF(16, 20.5f),
                        new PointF(11.5f, 16),
                    ]);
                break;
            case JarvisPresenceSignal.Faulted:
                graphics.DrawLine(signalPen, 12.5f, 12.5f, 19.5f, 19.5f);
                graphics.DrawLine(signalPen, 19.5f, 12.5f, 12.5f, 19.5f);
                break;
            default:
                graphics.FillRectangle(signalBrush, 14, 14, 4, 4);
                break;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
