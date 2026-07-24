using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace Jarvis.Supervisor;

#pragma warning disable SYSLIB1054 // DllImport avoids requiring unsafe generated interop in this recovery binary.

internal static class WindowsShell
{
    private const string TaskbarWindowClass = "Shell_TrayWnd";

    public static ShellIdentity Probe(int expectedSessionId, string expectedExplorerPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ShellIdentity.Invalid("Windows shell inspection is available only on Windows.");
        }

        try
        {
            nint shellWindow = GetShellWindow();
            nint taskbarWindow = FindWindowW(TaskbarWindowClass, null);
            if (shellWindow == 0 && taskbarWindow == 0)
            {
                return ShellIdentity.Absent("Neither the desktop shell window nor Shell_TrayWnd exists.");
            }

            if (shellWindow == 0 || taskbarWindow == 0)
            {
                return ShellIdentity.Invalid(
                    "The desktop shell window and Shell_TrayWnd were not both present.");
            }

            if (!IsWindow(shellWindow) || !IsWindow(taskbarWindow))
            {
                return ShellIdentity.Invalid("A shell window became invalid during inspection.");
            }

            uint shellProcessId = GetWindowProcessId(shellWindow);
            uint taskbarProcessId = GetWindowProcessId(taskbarWindow);
            if (shellProcessId == 0 || shellProcessId != taskbarProcessId)
            {
                return ShellIdentity.Invalid(
                    $"GetShellWindow and {TaskbarWindowClass} were owned by different processes.");
            }

            int processId = checked((int)shellProcessId);
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited || process.SessionId != expectedSessionId)
            {
                return ShellIdentity.Invalid(
                    $"Shell process {processId} was not running in session {expectedSessionId}.");
            }

            string? imagePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(imagePath) ||
                !PathsEqual(imagePath, expectedExplorerPath))
            {
                return ShellIdentity.Invalid(
                    $"Shell process {processId} was not verified as {expectedExplorerPath}.");
            }

            // Re-read both owners after opening and inspecting the process. This rejects a
            // shell transition that raced the path/session verification above.
            if (!IsWindow(shellWindow) || !IsWindow(taskbarWindow) ||
                GetWindowProcessId(shellWindow) != shellProcessId ||
                GetWindowProcessId(taskbarWindow) != shellProcessId)
            {
                return ShellIdentity.Invalid("The verified shell changed during inspection.");
            }

            return ShellIdentity.Verified(
                processId,
                Path.GetFullPath(imagePath),
                shellWindow,
                taskbarWindow);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            Win32Exception or
            NotSupportedException or
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            OverflowException)
        {
            return ShellIdentity.Invalid(
                $"Shell inspection failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static uint GetWindowProcessId(nint window)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetShellWindow();

    [DllImport(
        "user32.dll",
        EntryPoint = "FindWindowW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);
}

internal enum ShellIdentityState
{
    Verified,
    Absent,
    Invalid,
}

internal sealed record ShellIdentity(
    ShellIdentityState State,
    int? ProcessId,
    string? ImagePath,
    nint ShellWindow,
    nint TaskbarWindow,
    string? Error)
{
    public bool IsVerified => State == ShellIdentityState.Verified;

    public static ShellIdentity Verified(
        int processId,
        string imagePath,
        nint shellWindow,
        nint taskbarWindow) =>
        new(
            ShellIdentityState.Verified,
            processId,
            imagePath,
            shellWindow,
            taskbarWindow,
            null);

    public static ShellIdentity Absent(string error) =>
        new(ShellIdentityState.Absent, null, null, 0, 0, error);

    public static ShellIdentity Invalid(string error) =>
        new(ShellIdentityState.Invalid, null, null, 0, 0, error);
}

#pragma warning restore SYSLIB1054
