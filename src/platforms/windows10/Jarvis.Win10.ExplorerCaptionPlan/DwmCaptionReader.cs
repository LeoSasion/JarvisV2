using System.Runtime.InteropServices;

namespace Jarvis.Win10.ExplorerCaptionPlan;

public sealed record DwmCaptionObservation(
    int Attribute,
    int Value,
    int HResult)
{
    public bool Passed => HResult >= 0 && Value is 0 or 1;
}

public static class DwmCaptionReader
{
    public const int UseImmersiveDarkMode = 20;

    public static DwmCaptionObservation Read(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "A non-zero Explorer HWND is required.",
                nameof(windowHandle));
        }

        int hResult = DwmGetWindowAttribute(
            windowHandle,
            UseImmersiveDarkMode,
            out int value,
            sizeof(int));
        return new DwmCaptionObservation(
            UseImmersiveDarkMode,
            value,
            hResult);
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out int value,
        int valueSize);
}
