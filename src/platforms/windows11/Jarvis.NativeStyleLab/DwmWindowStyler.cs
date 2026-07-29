using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Jarvis.NativeStyleLab;

internal enum NativeStylePreset
{
    SystemDefault,
    GraphiteMica,
    NightAcrylic,
    MicaAlt,
}

internal sealed record DwmStyleResult(
    NativeStylePreset Preset,
    nint WindowHandle,
    IReadOnlyDictionary<string, int> HResults)
{
    public bool Passed => HResults.Values.All(value => value >= 0);
}

internal static class DwmWindowStyler
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int SystemBackdropType = 38;

    private const int CornerDefault = 0;
    private const int CornerRound = 2;
    private const int CornerRoundSmall = 3;

    private const int BackdropAuto = 0;
    private const int BackdropMainWindow = 2;
    private const int BackdropTransientWindow = 3;
    private const int BackdropTabbedWindow = 4;

    private const uint ColorDefault = 0xFFFFFFFFU;

    public static DwmStyleResult Apply(
        Window ownedWindow,
        NativeStylePreset preset)
    {
        nint windowHandle = new WindowInteropHelper(ownedWindow).Handle;
        if (windowHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                "The style lab could not obtain its own HWND.");
        }

        Dictionary<string, int> results = new(StringComparer.Ordinal);
        StyleValues values = GetValues(preset);
        SetAttribute(
            windowHandle,
            UseImmersiveDarkMode,
            values.DarkMode,
            "DWMWA_USE_IMMERSIVE_DARK_MODE",
            results);
        SetAttribute(
            windowHandle,
            WindowCornerPreference,
            values.Corner,
            "DWMWA_WINDOW_CORNER_PREFERENCE",
            results);
        SetAttribute(
            windowHandle,
            BorderColor,
            values.BorderColor,
            "DWMWA_BORDER_COLOR",
            results);
        SetAttribute(
            windowHandle,
            CaptionColor,
            values.CaptionColor,
            "DWMWA_CAPTION_COLOR",
            results);
        SetAttribute(
            windowHandle,
            TextColor,
            values.TextColor,
            "DWMWA_TEXT_COLOR",
            results);
        SetAttribute(
            windowHandle,
            SystemBackdropType,
            values.Backdrop,
            "DWMWA_SYSTEMBACKDROP_TYPE",
            results);

        return new DwmStyleResult(preset, windowHandle, results);
    }

    private static StyleValues GetValues(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault => new StyleValues(
                0,
                CornerDefault,
                ColorDefault,
                ColorDefault,
                ColorDefault,
                BackdropAuto),
            NativeStylePreset.GraphiteMica => new StyleValues(
                1,
                CornerRound,
                ToColorRef(Color.FromRgb(0x35, 0x72, 0x76)),
                ToColorRef(Color.FromRgb(0x0A, 0x12, 0x16)),
                ToColorRef(Color.FromRgb(0xE7, 0xF5, 0xF4)),
                BackdropMainWindow),
            NativeStylePreset.NightAcrylic => new StyleValues(
                1,
                CornerRoundSmall,
                ToColorRef(Color.FromRgb(0x8A, 0x68, 0x32)),
                ToColorRef(Color.FromRgb(0x14, 0x12, 0x0E)),
                ToColorRef(Color.FromRgb(0xF6, 0xE5, 0xC4)),
                BackdropTransientWindow),
            NativeStylePreset.MicaAlt => new StyleValues(
                1,
                CornerRound,
                ToColorRef(Color.FromRgb(0x4E, 0x5D, 0x82)),
                ToColorRef(Color.FromRgb(0x0D, 0x11, 0x1A)),
                ToColorRef(Color.FromRgb(0xEA, 0xED, 0xF8)),
                BackdropTabbedWindow),
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Unknown native style preset."),
        };

    private static uint ToColorRef(Color color) =>
        color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    private static void SetAttribute(
        nint windowHandle,
        int attribute,
        int value,
        string name,
        IDictionary<string, int> results)
    {
        results.Add(
            name,
            DwmSetWindowAttribute(
                windowHandle,
                attribute,
                ref value,
                sizeof(int)));
    }

    private static void SetAttribute(
        nint windowHandle,
        int attribute,
        uint value,
        string name,
        IDictionary<string, int> results)
    {
        results.Add(
            name,
            DwmSetWindowAttribute(
                windowHandle,
                attribute,
                ref value,
                sizeof(uint)));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint value,
        int valueSize);

    private sealed record StyleValues(
        int DarkMode,
        int Corner,
        uint BorderColor,
        uint CaptionColor,
        uint TextColor,
        int Backdrop);
}
