using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.DesktopStyleSession;

internal sealed record DesktopHostIdentity(
    string TopLevelClass,
    string TopLevelWindow,
    string ShellDefViewWindow,
    string FolderViewWindow,
    bool ShellDefViewVisible,
    uint ProcessId,
    uint ThreadId);

internal sealed record DesktopHostTarget(
    nint TopLevelWindow,
    nint ShellDefViewWindow,
    nint FolderViewWindow,
    DesktopHostIdentity Identity);

internal static class NativeDesktopHost
{
    public static DesktopHostTarget LocateExact(uint expectedExplorerProcessId)
    {
        if (expectedExplorerProcessId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedExplorerProcessId));
        }

        List<DesktopHostTarget> candidates = [];
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
                    return true;
                }

                uint threadId = GetWindowThreadProcessId(
                    folderView,
                    out uint processId);
                string topLevelClass = GetClassNameText(topLevelWindow);
                DesktopHostIdentity identity = new(
                    topLevelClass,
                    ToHex(topLevelWindow),
                    ToHex(shellDefView),
                    ToHex(folderView),
                    IsWindowVisible(shellDefView),
                    processId,
                    threadId);
                candidates.Add(
                    new DesktopHostTarget(
                        topLevelWindow,
                        shellDefView,
                        folderView,
                        identity));
                return true;
            },
            nint.Zero);

        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one Explorer desktop FolderView; observed " +
                $"{candidates.Count}.");
        }

        DesktopHostTarget target = candidates[0];
        if (target.Identity.TopLevelClass is not ("Progman" or "WorkerW"))
        {
            throw new InvalidOperationException(
                "The exact desktop host is not a Progman or WorkerW window.");
        }

        if (!target.Identity.ShellDefViewVisible)
        {
            throw new InvalidOperationException(
                "The exact desktop SHELLDLL_DefView is not visible.");
        }

        if (target.Identity.ProcessId != expectedExplorerProcessId)
        {
            throw new InvalidOperationException(
                "Explorer PID mismatch. Expected " +
                $"{expectedExplorerProcessId}; observed " +
                $"{target.Identity.ProcessId}.");
        }

        return target;
    }

    public static bool IsSameTarget(
        DesktopHostTarget current,
        DesktopHostIdentity expected) =>
        current.Identity == expected;

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
