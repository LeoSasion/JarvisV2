using System.Runtime.InteropServices;
using System.Text;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionOverlay;

internal readonly record struct NativeTargetSnapshot(
    nint WindowHandle,
    uint ProcessId,
    uint ThreadId,
    string RootClass,
    NativeRectangle Rectangle,
    uint Dpi,
    bool IsForeground);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRectangle
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal static class NativeOverlayTarget
{
    private const int MaximumClassNameCharacters = 256;

    public static bool TryReadExact(
        nint windowHandle,
        ExplorerCaptionTargetIdentity expected,
        out NativeTargetSnapshot snapshot,
        out string detail)
    {
        snapshot = default;
        if (windowHandle == nint.Zero || !IsWindow(windowHandle))
        {
            detail = "The exact Explorer target retired.";
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
            threadId == 0 ||
            processId != expected.ProcessId ||
            threadId != expected.ThreadId ||
            !string.Equals(
                className.ToString(),
                expected.RootClass,
                StringComparison.Ordinal))
        {
            detail = "The exact Explorer HWND/PID/TID/class changed.";
            return false;
        }

        if (!GetWindowRect(windowHandle, out NativeRectangle rectangle) ||
            rectangle.Right <= rectangle.Left ||
            rectangle.Bottom <= rectangle.Top)
        {
            detail = "The exact Explorer rectangle is unavailable.";
            return false;
        }

        uint dpi = GetDpiForWindow(windowHandle);
        if (dpi is < 96 or > 768)
        {
            detail = $"The exact Explorer DPI is invalid: {dpi}.";
            return false;
        }

        snapshot = new NativeTargetSnapshot(
            windowHandle,
            processId,
            threadId,
            className.ToString(),
            rectangle,
            dpi,
            GetForegroundWindow() == windowHandle);
        detail = "Exact Explorer target remains valid.";
        return true;
    }

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

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);
}
