using Jarvis.Win10.ExplorerCaptionPlan;
using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.ExplorerCaptionOverlay;

internal sealed record OwnedOverlayGateResult(
    ExplorerCaptionGateResult CaptionGate,
    bool Passed,
    bool SeparateExplorerProcessAccepted,
    IReadOnlyList<string> AcceptedCaptionGateFailures,
    IReadOnlyList<string> Failures);

internal sealed record OwnedOverlayGateInputs(
    bool CaptionGatePassed,
    bool OnlySeparateProcessFailure,
    bool TargetPresent,
    bool TargetInObservedExplorerProcessSet,
    bool OverlayCapabilityGranted);

internal static class OwnedOverlayGate
{
    private const string SeparateProcessFailure =
        "explorer-root-pid-not-desktop-shell";

    public static OwnedOverlayGateResult Inspect(
        string expectedWindowHandle)
    {
        ExplorerCaptionGateResult captionGate =
            ExplorerCaptionGate.Inspect(expectedWindowHandle);
        ExplorerCaptionGateReceipt receipt = captionGate.Receipt;
        ExplorerCaptionTargetIdentity? target = receipt.Target;
        ShellSurfaceInventory? inventory = receipt.SurfaceProbe.Inventory;
        bool onlySeparateProcessFailure =
            receipt.Failures.Count == 1 &&
            string.Equals(
                receipt.Failures[0],
                SeparateProcessFailure,
                StringComparison.Ordinal);
        bool targetInExplorerProcessSet =
            target is not null &&
            inventory?.ExplorerProcesses.Any(process =>
                process.ProcessId == target.ProcessId) == true;
        OwnedOverlayGateInputs inputs = new(
            receipt.Passed,
            onlySeparateProcessFailure,
            target is not null && captionGate.WindowHandle != nint.Zero,
            targetInExplorerProcessSet,
            receipt.Admission.Profile?.AllowedCapabilities.Contains(
                OverlayPolicy.RequiredCapability,
                StringComparer.Ordinal) == true);
        IReadOnlyList<string> failures = Evaluate(inputs);
        bool acceptedSeparateProcess =
            failures.Count == 0 && onlySeparateProcessFailure;
        return new OwnedOverlayGateResult(
            captionGate,
            failures.Count == 0,
            acceptedSeparateProcess,
            acceptedSeparateProcess ? [SeparateProcessFailure] : [],
            failures);
    }

    public static IReadOnlyList<string> Evaluate(
        OwnedOverlayGateInputs inputs)
    {
        List<string> failures = [];
        AddFailure(
            failures,
            inputs.CaptionGatePassed || inputs.OnlySeparateProcessFailure,
            "caption-read-gate-not-overlay-compatible");
        AddFailure(
            failures,
            inputs.TargetPresent,
            "exact-explorer-target-missing");
        AddFailure(
            failures,
            inputs.TargetInObservedExplorerProcessSet,
            "target-not-in-observed-explorer-process-set");
        AddFailure(
            failures,
            inputs.OverlayCapabilityGranted,
            "owned-overlay-capability-not-granted");
        return failures;
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
