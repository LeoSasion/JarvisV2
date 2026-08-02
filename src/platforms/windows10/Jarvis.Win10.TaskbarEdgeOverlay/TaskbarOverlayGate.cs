using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

internal sealed record TaskbarOverlayGateInputs(
    bool SurfaceProbePassed,
    bool ExactlyOnePrimaryTaskbar,
    bool ExactHandleMatched,
    bool DesktopShellProcessMatched,
    bool RootClassMatched,
    bool RootVisible,
    bool BottomHorizontalGeometry,
    bool OverlayCapabilityGranted);

internal sealed record TaskbarOverlayGateResult(
    ShellSurfaceProbeReceipt SurfaceProbe,
    bool Passed,
    nint WindowHandle,
    TaskbarTargetIdentity? Target,
    IReadOnlyList<string> Failures);

internal static class TaskbarOverlayGate
{
    public static TaskbarOverlayGateResult Inspect(
        string expectedWindowHandle)
    {
        nint expected = ParseWindowHandle(expectedWindowHandle);
        ShellSurfaceProbeReceipt probe = ShellSurfaceInspector.Inspect();
        ShellSurfaceInventory? inventory = probe.Inventory;
        SurfaceTreeObservation[] taskbars =
            inventory?.PrimaryTaskbars.ToArray() ?? [];
        SurfaceTreeObservation? selected = taskbars.SingleOrDefault(
            taskbar => ParseWindowHandle(taskbar.RootWindow) == expected);
        WindowNodeObservation? root = selected?.Nodes.SingleOrDefault(
            node => node.NodeKey == "root");
        NativeRectangle? rootRectangle = root is null
            ? null
            : NativeRectangle.From(root.Rectangle);
        TaskbarOverlayGateInputs inputs = new(
            probe.Result == "passed-read-only-inventory",
            taskbars.Length == 1,
            selected is not null,
            selected is not null &&
                inventory is not null &&
                selected.RootProcessId == inventory.DesktopShellProcessId,
            selected?.RootClass == "Shell_TrayWnd",
            root?.Visible == true,
            rootRectangle is NativeRectangle rectangle &&
                NativeTaskbarTarget.IsSupportedBottomHorizontalGeometry(
                    rectangle),
            probe.Admission.Profile?.AllowedCapabilities.Contains(
                TaskbarOverlayPolicy.RequiredCapability,
                StringComparer.Ordinal) == true);
        IReadOnlyList<string> failures = Evaluate(inputs);
        TaskbarTargetIdentity? target = selected is null || root is null
            ? null
            : new(
                selected.RootWindow,
                selected.RootProcessId,
                selected.RootThreadId,
                selected.RootClass,
                root.Rectangle,
                selected.TopologySha256);
        return new(
            probe,
            failures.Count == 0,
            failures.Count == 0 ? expected : nint.Zero,
            target,
            failures);
    }

    public static IReadOnlyList<string> Evaluate(
        TaskbarOverlayGateInputs inputs)
    {
        List<string> failures = [];
        AddFailure(
            failures,
            inputs.SurfaceProbePassed,
            "shell-surface-read-gate-failed");
        AddFailure(
            failures,
            inputs.ExactlyOnePrimaryTaskbar,
            "exactly-one-primary-taskbar-required");
        AddFailure(
            failures,
            inputs.ExactHandleMatched,
            "expected-taskbar-handle-not-matched");
        AddFailure(
            failures,
            inputs.DesktopShellProcessMatched,
            "taskbar-not-owned-by-desktop-shell");
        AddFailure(
            failures,
            inputs.RootClassMatched,
            "taskbar-root-class-mismatch");
        AddFailure(
            failures,
            inputs.RootVisible,
            "taskbar-root-not-visible");
        AddFailure(
            failures,
            inputs.BottomHorizontalGeometry,
            "bottom-horizontal-taskbar-required");
        AddFailure(
            failures,
            inputs.OverlayCapabilityGranted,
            "owned-taskbar-overlay-capability-not-granted");
        return failures;
    }

    internal static nint ParseWindowHandle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Window handle is required.");
        }

        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
        return ulong.TryParse(
                digits,
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out ulong parsed) &&
            parsed != 0 &&
            (nuint)parsed == parsed
                ? unchecked((nint)(nuint)parsed)
                : throw new ArgumentException(
                    $"Invalid window handle '{value}'.");
    }

    private static void AddFailure(
        ICollection<string> failures,
        bool condition,
        string failure)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }
}
