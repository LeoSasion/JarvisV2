using System.ComponentModel;
using System.Diagnostics;
using Jarvis.Win10.HostAdmission;

namespace Jarvis.Win10.ShellSurfaceProbe;

public static class ShellSurfaceInspector
{
    private static readonly HashSet<string> DesktopRootClasses =
        new(StringComparer.Ordinal)
        {
            "Progman",
            "WorkerW",
        };

    public static ShellSurfaceProbeReceipt Inspect()
    {
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        Windows10HostAdmissionReceipt admission =
            ExactWindows10HostInspector.Inspect();
        if (!admission.Passed ||
            admission.Profile is null ||
            !admission.Profile.AllowedCapabilities.Contains(
                "read-shell-window-topology",
                StringComparer.Ordinal))
        {
            IReadOnlyList<string> admissionFailures =
                admission.Passed
                    ? ["profile-capability-not-granted"]
                    : admission.Failures;
            return Blocked(
                observedAtUtc,
                admission,
                null,
                admissionFailures);
        }

        List<string> failures = [];
        try
        {
            IReadOnlyList<ExplorerProcessObservation> processes =
                ReadExplorerProcesses(failures);
            HashSet<uint> explorerProcessIds =
                processes.Select(process => process.ProcessId).ToHashSet();
            nint shellWindow =
                NativeWindowTopologyReader.GetDesktopShellWindow();
            uint shellProcessId = shellWindow == nint.Zero
                ? 0
                : NativeWindowTopologyReader.GetWindowProcessId(
                    shellWindow,
                    out _);
            if (shellProcessId == 0 ||
                !explorerProcessIds.Contains(shellProcessId))
            {
                failures.Add("desktop-shell-process-not-exact-explorer");
            }

            IReadOnlyList<nint> topLevelWindows =
                NativeWindowTopologyReader.EnumerateTopLevelWindows();
            List<SurfaceTreeObservation> desktopSurfaces = [];
            List<SurfaceTreeObservation> explorerWindows = [];
            List<SurfaceTreeObservation> primaryTaskbars = [];
            List<SurfaceTreeObservation> secondaryTaskbars = [];

            foreach (nint windowHandle in topLevelWindows)
            {
                string className =
                    NativeWindowTopologyReader.GetWindowClass(windowHandle);
                SurfaceTreeObservation? tree = className switch
                {
                    "CabinetWClass" =>
                        NativeWindowTopologyReader.ReadSurface(
                            "explorer-folder-window",
                            windowHandle),
                    "Shell_TrayWnd" =>
                        NativeWindowTopologyReader.ReadSurface(
                            "primary-taskbar",
                            windowHandle),
                    "Shell_SecondaryTrayWnd" =>
                        NativeWindowTopologyReader.ReadSurface(
                            "secondary-taskbar",
                            windowHandle),
                    _ when DesktopRootClasses.Contains(className) =>
                        NativeWindowTopologyReader.ReadSurface(
                            "desktop-root-candidate",
                            windowHandle),
                    _ => null,
                };
                if (tree is null)
                {
                    continue;
                }

                if (!explorerProcessIds.Contains(tree.RootProcessId))
                {
                    if (!string.Equals(
                            tree.SurfaceKind,
                            "desktop-root-candidate",
                            StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"surface-root-not-explorer:{tree.SurfaceKind}");
                    }

                    continue;
                }

                switch (tree.SurfaceKind)
                {
                    case "explorer-folder-window":
                        explorerWindows.Add(tree);
                        break;
                    case "primary-taskbar":
                        primaryTaskbars.Add(tree);
                        break;
                    case "secondary-taskbar":
                        secondaryTaskbars.Add(tree);
                        break;
                    case "desktop-root-candidate"
                        when tree.ClassHistogram.ContainsKey(
                            "SHELLDLL_DefView"):
                        desktopSurfaces.Add(
                            tree with
                            {
                                SurfaceKind = "desktop-host",
                            });
                        break;
                }
            }

            bool exactDesktop =
                desktopSurfaces.Count == 1 &&
                desktopSurfaces[0].RootProcessId == shellProcessId &&
                desktopSurfaces[0].ClassHistogram.ContainsKey(
                    "SHELLDLL_DefView") &&
                desktopSurfaces[0].ClassHistogram.ContainsKey(
                    "SysListView32");
            bool exactTaskbar =
                primaryTaskbars.Count == 1 &&
                primaryTaskbars[0].RootProcessId == shellProcessId;
            if (!exactDesktop)
            {
                failures.Add("exact-desktop-host-count-or-shape-invalid");
            }

            if (!exactTaskbar)
            {
                failures.Add("exact-primary-taskbar-count-or-pid-invalid");
            }

            IReadOnlyList<SurfaceTreeObservation> allSurfaces =
            [
                .. desktopSurfaces,
                .. explorerWindows,
                .. primaryTaskbars,
                .. secondaryTaskbars,
            ];
            foreach (SurfaceTreeObservation tree in allSurfaces)
            {
                if (tree.Truncated)
                {
                    failures.Add(
                        $"surface-tree-truncated:{tree.SurfaceKind}");
                }
            }

            bool explorerObserved = explorerWindows.Count > 0;
            ShellSurfaceInventory inventory = new(
                shellProcessId,
                processes
                    .Select(process =>
                        process with
                        {
                            DesktopShell =
                                process.ProcessId == shellProcessId,
                        })
                    .ToArray(),
                desktopSurfaces,
                explorerWindows,
                primaryTaskbars,
                secondaryTaskbars,
                exactDesktop,
                exactTaskbar,
                explorerObserved,
                exactDesktop && exactTaskbar && explorerObserved);

            return new ShellSurfaceProbeReceipt(
                1,
                "jarvisv2-win10-shell-surface-readonly-probe",
                failures.Count == 0
                    ? "passed-read-only-inventory"
                    : "blocked-incomplete-inventory",
                observedAtUtc,
                admission,
                inventory,
                "bounded-read-only-window-topology",
                false,
                false,
                false,
                false,
                "read-only-inspection",
                false,
                failures);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception)
        {
            failures.Add(
                $"inventory-exception:{exception.GetType().Name}");
            return Blocked(
                observedAtUtc,
                admission,
                null,
                failures);
        }
    }

    private static IReadOnlyList<ExplorerProcessObservation>
        ReadExplorerProcesses(ICollection<string> failures)
    {
        List<ExplorerProcessObservation> processes = [];
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    processes.Add(
                        new ExplorerProcessObservation(
                            checked((uint)process.Id),
                            process.StartTime.ToUniversalTime(),
                            false));
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    Win32Exception)
                {
                    failures.Add(
                        "explorer-process-identity-unavailable");
                }
            }
        }

        if (processes.Count == 0)
        {
            failures.Add("explorer-process-set-empty");
        }

        return processes;
    }

    private static ShellSurfaceProbeReceipt Blocked(
        DateTimeOffset observedAtUtc,
        Windows10HostAdmissionReceipt admission,
        ShellSurfaceInventory? inventory,
        IEnumerable<string> failures) =>
        new(
            1,
            "jarvisv2-win10-shell-surface-readonly-probe",
            "blocked",
            observedAtUtc,
            admission,
            inventory,
            "bounded-read-only-window-topology",
            false,
            false,
            false,
            false,
            "read-only-inspection",
            false,
            failures.ToArray());
}
