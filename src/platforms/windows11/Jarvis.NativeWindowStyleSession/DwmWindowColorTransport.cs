using System.Runtime.InteropServices;

namespace Jarvis.NativeWindowStyleSession;

internal sealed record DwmColorOperation(
    IReadOnlyDictionary<string, int> HResults)
{
    public bool Passed => HResults.Values.All(result => result >= 0);

    public bool ColorMutationMayHaveOccurred =>
        HResults
            .Where(result => result.Key != "DwmFlush")
            .Any(result => result.Value >= 0);
}

internal static class DwmWindowColorTransport
{
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const uint ColorDefault = 0xFFFFFFFFU;

    public static DwmColorOperation Apply(
        nint windowHandle,
        NativeWindowColorPreset preset) =>
        SetColors(
            windowHandle,
            preset.BorderColorRef,
            preset.CaptionColorRef,
            preset.TextColorRef);

    public static DwmColorOperation ResetSystemDefault(
        nint windowHandle) =>
        SetColors(
            windowHandle,
            ColorDefault,
            ColorDefault,
            ColorDefault);

    private static DwmColorOperation SetColors(
        nint windowHandle,
        uint borderColor,
        uint captionColor,
        uint textColor)
    {
        Dictionary<string, int> results = new(StringComparer.Ordinal)
        {
            ["DWMWA_BORDER_COLOR"] = SetColor(
                windowHandle,
                BorderColor,
                borderColor),
            ["DWMWA_CAPTION_COLOR"] = SetColor(
                windowHandle,
                CaptionColor,
                captionColor),
            ["DWMWA_TEXT_COLOR"] = SetColor(
                windowHandle,
                TextColor,
                textColor),
        };
        results["DwmFlush"] = DwmFlush();
        return new DwmColorOperation(results);
    }

    private static int SetColor(
        nint windowHandle,
        int attribute,
        uint color) =>
        DwmSetWindowAttribute(
            windowHandle,
            attribute,
            ref color,
            sizeof(uint));

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint value,
        int valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmFlush();
}
