using System.Runtime.InteropServices;
using System.Text;
using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public static NativeRectangle From(WindowRectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
}

internal readonly record struct NativeTaskbarSnapshot(
    NativeRectangle Rectangle,
    uint Dpi,
    bool Visible,
    bool EdgeOccludedByFullscreen);

internal static class NativeTaskbarTarget
{
    private const int MaximumClassNameCharacters = 256;
    private const int ExtendedFrameBoundsAttribute = 9;

    public static bool TryReadExact(
        nint windowHandle,
        TaskbarTargetIdentity expected,
        out NativeTaskbarSnapshot snapshot,
        out string detail)
    {
        snapshot = default;
        if (windowHandle == nint.Zero || !IsWindow(windowHandle))
        {
            detail = "The exact taskbar target retired.";
            return false;
        }

        StringBuilder className = new(MaximumClassNameCharacters);
        int classLength = GetClassName(
            windowHandle,
            className,
            className.Capacity);
        uint threadId = GetWindowThreadProcessId(
            windowHandle,
            out uint processId);
        if (classLength <= 0 ||
            processId != expected.ProcessId ||
            threadId != expected.ThreadId ||
            !string.Equals(
                className.ToString(),
                expected.RootClass,
                StringComparison.Ordinal))
        {
            detail = "The exact taskbar HWND/PID/TID/class changed.";
            return false;
        }

        if (!GetWindowRect(windowHandle, out NativeRectangle rectangle) ||
            !IsSupportedBottomHorizontalGeometry(rectangle))
        {
            detail =
                "The taskbar is no longer a supported bottom horizontal target.";
            return false;
        }

        uint dpi = GetDpiForWindow(windowHandle);
        if (dpi is < 96 or > 768)
        {
            detail = $"The exact taskbar DPI is invalid: {dpi}.";
            return false;
        }

        bool edgeOccluded = false;
        nint foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != nint.Zero &&
            foregroundWindow != windowHandle &&
            TryReadVisibleFrameBounds(
                foregroundWindow,
                out NativeRectangle foregroundRectangle))
        {
            edgeOccluded = OccludesTaskbarEdge(
                foregroundRectangle,
                rectangle);
        }

        snapshot = new(
            rectangle,
            dpi,
            IsWindowVisible(windowHandle),
            edgeOccluded);
        detail = "Exact taskbar target remains valid.";
        return true;
    }

    private static bool TryReadVisibleFrameBounds(
        nint windowHandle,
        out NativeRectangle rectangle)
    {
        int size = Marshal.SizeOf<NativeRectangle>();
        int hResult = DwmGetWindowAttribute(
            windowHandle,
            ExtendedFrameBoundsAttribute,
            out rectangle,
            size);
        return hResult >= 0 || GetWindowRect(windowHandle, out rectangle);
    }

    internal static bool IsSupportedBottomHorizontalGeometry(
        NativeRectangle rectangle) =>
        rectangle.Top > 0 &&
        rectangle.Width > rectangle.Height &&
        rectangle.Height is >= 24 and <= 256;

    internal static bool OccludesTaskbarEdge(
        NativeRectangle foreground,
        NativeRectangle taskbar) =>
        foreground.Left <= taskbar.Left &&
        foreground.Right >= taskbar.Right &&
        foreground.Top <= taskbar.Top &&
        foreground.Bottom > taskbar.Top + 1;

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint windowHandle,
        out NativeRectangle rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out NativeRectangle value,
        int valueSize);
}
