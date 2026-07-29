using Jarvis.Win10.HostAdmission;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class Win10HostInspector
{
    public static HostProbeReceipt Inspect()
    {
        Windows10HostAdmissionReceipt admission =
            ExactWindows10HostInspector.Inspect();
        if (!admission.Passed ||
            admission.Host is null ||
            admission.Profile is null ||
            !admission.Profile.AllowedCapabilities.Contains(
                "read-system-dwm-state",
                StringComparer.Ordinal) ||
            !admission.Profile.AllowedCapabilities.Contains(
                "set-owned-window-dark-caption",
                StringComparer.Ordinal))
        {
            string failure = admission.Passed
                ? "The exact profile does not grant both native-style probe capabilities."
                : string.Join(" ", admission.Failures);
            return new HostProbeReceipt(
                1,
                "jarvisv2-win10-native-style-host-probe",
                admission.Result,
                admission.ObservedAtUtc,
                admission.Profile?.ProfileId,
                admission.Host,
                null,
                "own-process-hwnd-only",
                false,
                false,
                false,
                false,
                "not-run",
                failure);
        }

        SystemVisualIdentity visuals =
            Win10DwmApi.InspectSystemVisuals();
        return new HostProbeReceipt(
            1,
            "jarvisv2-win10-native-style-host-probe",
            "passed-exact-own-process-candidate",
            admission.ObservedAtUtc,
            admission.Profile.ProfileId,
            admission.Host,
            visuals,
            "own-process-hwnd-only",
            true,
            false,
            false,
            false,
            "not-run",
            null);
    }
}
