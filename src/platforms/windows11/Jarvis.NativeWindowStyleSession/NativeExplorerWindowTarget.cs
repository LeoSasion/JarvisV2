using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.NativeWindowStyleSession;

internal sealed record NativeWindowIdentity(
    string WindowHandle,
    string ClassName,
    string Title,
    bool Visible,
    uint ProcessId,
    uint ThreadId);

internal sealed record NativeExplorerWindowTarget(
    nint WindowHandle,
    NativeWindowIdentity Identity)
{
    public static NativeExplorerWindowTarget Bind(
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle)
    {
        nint handle = ParseWindowHandle(windowHandle);
        if (!IsWindow(handle))
        {
            throw new InvalidOperationException(
                $"The exact window {windowHandle} no longer exists.");
        }

        string className = GetClassNameText(handle);
        if (!string.Equals(
                className,
                "CabinetWClass",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected CabinetWClass; observed '{className}'.");
        }

        string title = GetWindowTitle(handle);
        if (!string.Equals(
                title,
                expectedTitle,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Window title mismatch. Expected '{expectedTitle}'; " +
                $"observed '{title}'.");
        }

        uint threadId = GetWindowThreadProcessId(
            handle,
            out uint processId);
        if (processId != expectedProcessId)
        {
            throw new InvalidOperationException(
                $"Window PID mismatch. Expected {expectedProcessId}; " +
                $"observed {processId}.");
        }

        bool visible = IsWindowVisible(handle);
        if (!visible)
        {
            throw new InvalidOperationException(
                "The exact Explorer window is not visible.");
        }

        return new NativeExplorerWindowTarget(
            handle,
            new NativeWindowIdentity(
                ToHex(handle),
                className,
                title,
                visible,
                processId,
                threadId));
    }

    public static bool IsSameTarget(
        NativeExplorerWindowTarget current,
        NativeWindowIdentity expected) =>
        current.Identity == expected;

    private static nint ParseWindowHandle(string value)
    {
        if (!value.StartsWith("0x", StringComparison.Ordinal) ||
            !ulong.TryParse(
                value.AsSpan(2),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out ulong parsed) ||
            parsed == 0 ||
            parsed > long.MaxValue)
        {
            throw new ArgumentException(
                "Window handle must be a nonzero hexadecimal value.",
                nameof(value));
        }

        return new nint(unchecked((long)parsed));
    }

    private static string GetClassNameText(nint handle)
    {
        StringBuilder buffer = new(256);
        int length = GetClassName(handle, buffer, buffer.Capacity);
        return length <= 0 ? "<unavailable>" : buffer.ToString(0, length);
    }

    private static string GetWindowTitle(nint handle)
    {
        StringBuilder buffer = new(1024);
        int length = GetWindowText(handle, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString(0, length);
    }

    private static string ToHex(nint handle) =>
        $"0x{unchecked((ulong)handle.ToInt64()):X}";

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

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

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowTextW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetWindowText(
        nint windowHandle,
        StringBuilder windowText,
        int maximumCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);
}
