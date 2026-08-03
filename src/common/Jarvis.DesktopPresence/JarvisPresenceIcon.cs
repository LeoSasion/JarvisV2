using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Jarvis.DesktopPresence;

public static class JarvisPresenceIcon
{
    public static Icon Create()
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(8, 16, 20));
        using Pen outer = new(Color.FromArgb(160, 85, 222, 211), 1.4f);
        using Pen inner = new(Color.FromArgb(255, 85, 222, 211), 2.2f);
        using SolidBrush signal = new(Color.FromArgb(255, 85, 222, 211));
        graphics.DrawEllipse(outer, 3.5f, 3.5f, 24, 24);
        graphics.DrawEllipse(inner, 9, 9, 14, 14);
        graphics.FillRectangle(signal, 14, 14, 4, 4);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
