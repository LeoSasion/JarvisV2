using System.Text.RegularExpressions;

namespace Jarvis.ExplorerFrameModel;

internal static partial class FrameAdmission
{
    private const string RequiredWindowClass = "CabinetWClass";

    public static IReadOnlyList<string> Validate(TargetIdentity target)
    {
        List<string> failures = [];

        if (target.ProcessId <= 0)
        {
            failures.Add("target-process-id-invalid");
        }

        if (target.DesktopShellProcessId <= 0)
        {
            failures.Add("desktop-shell-process-id-invalid");
        }
        else if (target.ProcessId == target.DesktopShellProcessId)
        {
            failures.Add("desktop-shell-target-forbidden");
        }

        if (target.ThreadId <= 0)
        {
            failures.Add("target-thread-id-invalid");
        }

        if (!WindowHandlePattern().IsMatch(target.WindowHandle) ||
            Convert.ToUInt64(target.WindowHandle[2..], 16) == 0)
        {
            failures.Add("target-window-handle-invalid");
        }

        if (!string.Equals(
                target.WindowClass,
                RequiredWindowClass,
                StringComparison.Ordinal))
        {
            failures.Add("target-window-class-not-cabinet");
        }

        if (string.IsNullOrWhiteSpace(target.WindowTitle) ||
            !string.Equals(
                target.WindowTitle,
                target.ExpectedWindowTitle,
                StringComparison.Ordinal))
        {
            failures.Add("target-window-title-not-exact");
        }

        if (!target.SeparateProcess)
        {
            failures.Add("separate-explorer-process-required");
        }

        if (target.ProcessStartTimeUtc.Kind != DateTimeKind.Utc)
        {
            failures.Add("process-start-time-not-utc");
        }

        if (string.IsNullOrWhiteSpace(target.VisualTreeGeneration))
        {
            failures.Add("visual-tree-generation-missing");
        }

        return failures;
    }

    [GeneratedRegex(
        "^0x[0-9A-F]{1,16}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowHandlePattern();
}
