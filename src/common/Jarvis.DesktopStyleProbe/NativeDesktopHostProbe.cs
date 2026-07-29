using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.DesktopStyleProbe;

internal sealed record DesktopHostCandidate(
    string TopLevelClass,
    string TopLevelWindow,
    string ShellDefViewWindow,
    string? FolderViewWindow,
    bool ShellDefViewVisible,
    uint ProcessId,
    uint ThreadId);

internal static class NativeDesktopHostProbe
{
    public static IReadOnlyList<DesktopHostCandidate> Inspect()
    {
        List<DesktopHostCandidate> candidates = [];
        EnumWindows(
            (topLevelWindow, _) =>
            {
                nint shellDefView = FindWindowEx(
                    topLevelWindow,
                    nint.Zero,
                    "SHELLDLL_DefView",
                    null);
                if (shellDefView == nint.Zero)
                {
                    return true;
                }

                nint folderView = FindWindowEx(
                    shellDefView,
                    nint.Zero,
                    "SysListView32",
                    "FolderView");
                if (folderView == nint.Zero)
                {
                    folderView = FindWindowEx(
                        shellDefView,
                        nint.Zero,
                        "SysListView32",
                        null);
                }

                uint threadId = GetWindowThreadProcessId(
                    shellDefView,
                    out uint processId);
                candidates.Add(
                    new DesktopHostCandidate(
                        GetClassNameText(topLevelWindow),
                        ToHex(topLevelWindow),
                        ToHex(shellDefView),
                        folderView == nint.Zero ? null : ToHex(folderView),
                        IsWindowVisible(shellDefView),
                        processId,
                        threadId));
                return true;
            },
            nint.Zero);

        return candidates;
    }

    private static string GetClassNameText(nint windowHandle)
    {
        StringBuilder buffer = new(256);
        int length = GetClassName(windowHandle, buffer, buffer.Capacity);
        return length <= 0 ? "<unavailable>" : buffer.ToString(0, length);
    }

    private static string ToHex(nint windowHandle) =>
        $"0x{unchecked((ulong)windowHandle.ToInt64()):X}";

    private delegate bool EnumWindowsCallback(
        nint windowHandle,
        nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        nint parameter);

    [DllImport(
        "user32.dll",
        EntryPoint = "FindWindowExW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern nint FindWindowEx(
        nint parentWindow,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);
}
