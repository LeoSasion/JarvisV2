using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.ExplorerSurfaceProbe;

internal static class ExactExplorerTarget
{
    public static bool TryInspect(
        ExactTargetRequest request,
        out ExactTargetObservation? observation,
        out IReadOnlyList<string> failures)
    {
        List<string> errors = [];
        observation = null;

        if (request.WindowHandle == nint.Zero ||
            !IsWindow(request.WindowHandle))
        {
            errors.Add("target-window-invalid");
            failures = errors;
            return false;
        }

        uint actualThreadId = GetWindowThreadProcessId(
            request.WindowHandle,
            out uint actualProcessId);
        Require(
            actualProcessId == request.ProcessId &&
            actualProcessId > 0,
            "target-process-id-mismatch",
            errors);
        Require(
            actualThreadId == request.ThreadId &&
            actualThreadId > 0,
            "target-thread-id-mismatch",
            errors);

        string windowClass = GetClassNameText(request.WindowHandle);
        string windowTitle = GetWindowTextValue(request.WindowHandle);
        Require(
            windowClass == "CabinetWClass",
            "target-window-class-not-cabinet",
            errors);
        Require(
            windowTitle == request.ExpectedTitle &&
            !string.IsNullOrWhiteSpace(windowTitle),
            "target-window-title-not-exact",
            errors);

        nint shellWindow = GetShellWindow();
        uint shellProcessId = 0;
        if (shellWindow != nint.Zero)
        {
            _ = GetWindowThreadProcessId(
                shellWindow,
                out shellProcessId);
        }

        Require(
            shellProcessId > 0 &&
            shellProcessId ==
                request.ExpectedDesktopShellProcessId,
            "desktop-shell-process-id-mismatch",
            errors);
        Require(
            actualProcessId != shellProcessId,
            "desktop-shell-target-forbidden",
            errors);

        string processName = string.Empty;
        DateTime processStartUtc = default;
        try
        {
            using Process process =
                Process.GetProcessById(checked((int)actualProcessId));
            processName = process.ProcessName;
            processStartUtc = process.StartTime.ToUniversalTime();
        }
        catch (
            Exception exception) when (
            exception is ArgumentException ||
            exception is InvalidOperationException ||
            exception is System.ComponentModel.Win32Exception)
        {
            errors.Add("target-process-identity-unavailable");
        }

        Require(
            string.Equals(
                processName,
                "explorer",
                StringComparison.OrdinalIgnoreCase),
            "target-process-not-explorer",
            errors);
        Require(
            processStartUtc.Kind == DateTimeKind.Utc &&
            request.ExpectedProcessStartTimeUtc.Kind == DateTimeKind.Utc &&
            Math.Abs(
                (processStartUtc -
                    request.ExpectedProcessStartTimeUtc)
                .TotalMilliseconds) <= 2,
            "target-process-start-time-mismatch",
            errors);

        observation = new ExactTargetObservation(
            WindowHandle: ToHex(request.WindowHandle),
            WindowClass: windowClass,
            WindowTitle: windowTitle,
            WindowVisible: IsWindowVisible(request.WindowHandle),
            ProcessId: actualProcessId,
            ThreadId: actualThreadId,
            DesktopShellProcessId: shellProcessId,
            ProcessName: processName,
            ProcessStartTimeUtc: processStartUtc,
            SeparateProcess: actualProcessId != shellProcessId);
        failures = errors;
        return errors.Count == 0;
    }

    private static void Require(
        bool condition,
        string failure,
        ICollection<string> failures)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }

    private static string GetClassNameText(nint windowHandle)
    {
        StringBuilder buffer = new(256);
        int length = GetClassName(
            windowHandle,
            buffer,
            buffer.Capacity);
        return length <= 0
            ? string.Empty
            : buffer.ToString(0, length);
    }

    private static string GetWindowTextValue(nint windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder buffer = new(length + 1);
        int copied = GetWindowText(
            windowHandle,
            buffer,
            buffer.Capacity);
        return copied <= 0
            ? string.Empty
            : buffer.ToString(0, copied);
    }

    private static string ToHex(nint windowHandle)
    {
        return $"0x{unchecked((ulong)windowHandle.ToInt64()):X}";
    }

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
        EntryPoint = "GetWindowTextLengthW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetWindowTextLength(
        nint windowHandle);

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

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetShellWindow();
}
