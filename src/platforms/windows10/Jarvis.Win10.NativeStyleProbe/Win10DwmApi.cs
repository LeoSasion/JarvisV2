using System.Runtime.InteropServices;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class Win10DwmApi
{
    public const int UseImmersiveDarkMode = 20;

    public static SystemVisualIdentity InspectSystemVisuals()
    {
        int compositionHResult =
            DwmIsCompositionEnabled(out bool compositionEnabled);
        int colorizationHResult =
            DwmGetColorizationColor(
                out uint colorizationColor,
                out bool opaqueBlend);

        return new SystemVisualIdentity(
            compositionHResult >= 0 && compositionEnabled,
            compositionHResult,
            $"#{colorizationColor:X8}",
            opaqueBlend,
            colorizationHResult,
            System.Windows.SystemParameters.HighContrast,
            System.Windows.SystemParameters.ClientAreaAnimation);
    }

    public static int SetOwnedWindowDarkCaption(
        nint ownedWindowHandle,
        bool enabled)
    {
        if (ownedWindowHandle == nint.Zero)
        {
            throw new ArgumentException(
                "An owned non-zero HWND is required.",
                nameof(ownedWindowHandle));
        }

        int value = enabled ? 1 : 0;
        return DwmSetWindowAttribute(
            ownedWindowHandle,
            UseImmersiveDarkMode,
            ref value,
            sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmIsCompositionEnabled(
        [MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetColorizationColor(
        out uint colorizationColor,
        [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
