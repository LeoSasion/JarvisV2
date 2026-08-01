using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Jarvis.Win10.ExplorerCaptionSession;

internal static class DwmCaptionWriter
{
    private const int UseImmersiveDarkMode = 20;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawNoChildren = 0x0040;
    private const uint RedrawFrame = 0x0400;

    public static void SetDarkCaption(
        nint windowHandle,
        bool enabled)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A non-zero Explorer HWND is required.",
                nameof(windowHandle));
        }

        int value = enabled ? 1 : 0;
        int hResult = DwmSetWindowAttribute(
            windowHandle,
            UseImmersiveDarkMode,
            ref value,
            sizeof(int));
        if (hResult < 0)
        {
            throw new ExternalException(
                "DwmSetWindowAttribute rejected the exact Explorer HWND.",
                hResult);
        }
    }

    public static void Flush()
    {
        int hResult = DwmFlush();
        if (hResult < 0)
        {
            throw new ExternalException(
                "DwmFlush failed after the exact caption update.",
                hResult);
        }
    }

    public static void RequestNonClientRefresh(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A non-zero Explorer HWND is required.",
                nameof(windowHandle));
        }

        if (!RedrawWindow(
                windowHandle,
                nint.Zero,
                nint.Zero,
                RedrawInvalidate |
                RedrawNoChildren |
                RedrawFrame))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "RedrawWindow rejected the exact Explorer HWND.");
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();

    [DllImport(
        "user32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        nint windowHandle,
        nint updateRectangle,
        nint updateRegion,
        uint flags);
}
