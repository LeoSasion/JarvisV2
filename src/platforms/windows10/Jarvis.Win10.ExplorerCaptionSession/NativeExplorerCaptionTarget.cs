using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionSession;

internal static class NativeExplorerCaptionTarget
{
    private const int MaximumClassNameCharacters = 256;

    public static bool TryValidateExact(
        ExplorerCaptionTargetIdentity expected,
        out nint windowHandle,
        out string detail)
    {
        if (!TryParseWindowHandle(
                expected.WindowHandle,
                out windowHandle) ||
            !IsWindow(windowHandle))
        {
            detail = "The recorded Explorer HWND no longer exists.";
            return false;
        }

        StringBuilder className =
            new(MaximumClassNameCharacters);
        int classLength = GetClassName(
            windowHandle,
            className,
            className.Capacity);
        if (classLength <= 0 ||
            !string.Equals(
                className.ToString(),
                expected.RootClass,
                StringComparison.Ordinal))
        {
            detail =
                "The recorded HWND no longer has the expected root class.";
            return false;
        }

        uint threadId = GetWindowThreadProcessId(
            windowHandle,
            out uint processId);
        if (threadId == 0 ||
            processId != expected.ProcessId ||
            threadId != expected.ThreadId)
        {
            detail =
                "The recorded Explorer HWND PID/TID identity changed.";
            return false;
        }

        detail = "Exact HWND/PID/TID/class identity verified.";
        return true;
    }

    public static bool IsSameTarget(
        ExplorerCaptionTargetIdentity left,
        ExplorerCaptionTargetIdentity right) =>
        string.Equals(
            left.RootClass,
            right.RootClass,
            StringComparison.Ordinal) &&
        string.Equals(
            left.WindowHandle,
            right.WindowHandle,
            StringComparison.OrdinalIgnoreCase) &&
        left.ProcessId == right.ProcessId &&
        left.ThreadId == right.ThreadId;

    private static bool TryParseWindowHandle(
        string text,
        out nint windowHandle)
    {
        windowHandle = nint.Zero;
        if (!text.StartsWith("0x", StringComparison.Ordinal) ||
            !ulong.TryParse(
                text.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong raw) ||
            raw == 0 ||
            raw > long.MaxValue)
        {
            return false;
        }

        windowHandle = unchecked((nint)(long)raw);
        return true;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport(
        "user32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);
}
