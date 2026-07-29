using System.Windows;
using System.Windows.Interop;

namespace Jarvis.Win10.NativeStyleProbe;

internal enum NativeStylePreset
{
    SystemDefault,
    JarvisGraphite,
    NativeAccent,
}

internal sealed record OwnedWindowStyleResult(
    NativeStylePreset Preset,
    nint WindowHandle,
    IReadOnlyList<DwmStyleCall> Calls)
{
    public bool Passed => Calls.All(call => call.HResult >= 0);
}

internal static class OwnedWindowStyler
{
    public static OwnedWindowStyleResult Apply(
        Window ownedWindow,
        NativeStylePreset preset)
    {
        nint windowHandle = new WindowInteropHelper(ownedWindow).Handle;
        if (windowHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                "The Win10 style probe could not obtain its own HWND.");
        }

        int value = preset == NativeStylePreset.SystemDefault ? 0 : 1;
        int hResult =
            Win10DwmApi.SetOwnedWindowDarkCaption(
                windowHandle,
                value != 0);

        return new OwnedWindowStyleResult(
            preset,
            windowHandle,
            [
                new DwmStyleCall(
                    "DWMWA_USE_IMMERSIVE_DARK_MODE",
                    Win10DwmApi.UseImmersiveDarkMode,
                    value,
                    hResult),
            ]);
    }
}
