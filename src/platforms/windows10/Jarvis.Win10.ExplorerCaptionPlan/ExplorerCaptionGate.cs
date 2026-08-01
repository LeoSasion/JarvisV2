using System.Globalization;
using System.Text.Json;
using Jarvis.Win10.HostAdmission;
using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.ExplorerCaptionPlan;

public sealed record ExplorerCaptionSafetyState(
    string StateRoot,
    string KillSwitchPath,
    bool KillSwitchArmed,
    string ActiveModulePath,
    bool ActiveModulePresent,
    string? Error);

public sealed record ExplorerCaptionTargetIdentity(
    string RootClass,
    string WindowHandle,
    uint ProcessId,
    uint ThreadId,
    WindowRectangle Rectangle,
    string TopologySha256);

public sealed record ExplorerCaptionGateReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    Windows10HostAdmissionReceipt Admission,
    ShellSurfaceProbeReceipt SurfaceProbe,
    ExplorerCaptionSafetyState SafetyState,
    string RequiredCapability,
    string? HostProfileId,
    int ExplorerCandidateCount,
    string? RequestedWindowHandle,
    ExplorerCaptionTargetIdentity? Target,
    DwmCaptionObservation? CurrentCaption,
    bool InspectionSupported,
    bool PreviewPlanningSupported,
    bool PreviewExecutionSupported,
    bool MutationSupported,
    bool ActivationPermitted,
    bool MutationPerformed,
    string LiveExplorer,
    IReadOnlyList<string> Failures)
{
    public bool Passed =>
        string.Equals(
            Result,
            "passed-single-explorer-caption-read",
            StringComparison.Ordinal) &&
        Target is not null &&
        CurrentCaption?.Passed == true;
}

public sealed record ExplorerCaptionGateResult(
    ExplorerCaptionGateReceipt Receipt,
    nint WindowHandle);

internal sealed record ExplorerCaptionGateInputs(
    bool AdmissionPassed,
    bool CapabilityGranted,
    bool ProfileActivationDenied,
    bool ProfileLiveExplorerNotRun,
    bool SurfaceProbePassed,
    bool ExactDesktopObserved,
    bool ExactTaskbarObserved,
    bool TargetSelectionExact,
    bool ExplorerRootExact,
    bool ExplorerMatchesDesktopShell,
    bool DwmReadPassed,
    bool DwmValueBoolean,
    bool KillSwitchArmed,
    bool ActiveModuleAbsent,
    bool SafetyStateKnown);

public static class ExplorerCaptionGate
{
    public const string RequiredCapability =
        "read-single-explorer-caption-state";

    public static ExplorerCaptionGateResult Inspect(
        string? expectedWindowHandle)
    {
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        ShellSurfaceProbeReceipt surfaceProbe =
            ShellSurfaceInspector.Inspect();
        Windows10HostAdmissionReceipt admission = surfaceProbe.Admission;
        ExplorerCaptionSafetyState safetyState = ReadSafetyState();
        Windows10HostProfile? profile = admission.Profile;
        ShellSurfaceInventory? inventory = surfaceProbe.Inventory;
        SurfaceTreeObservation[] explorerWindows =
            inventory?.ExplorerWindows.ToArray() ?? [];
        SurfaceTreeObservation? explorerWindow = SelectExplorerWindow(
            explorerWindows,
            expectedWindowHandle);
        WindowNodeObservation? rootNode = explorerWindow?.Nodes
            .SingleOrDefault(node => node.ParentKey is null);

        nint windowHandle = nint.Zero;
        ExplorerCaptionTargetIdentity? target = null;
        DwmCaptionObservation? currentCaption = null;
        if (explorerWindow is not null &&
            rootNode is not null &&
            TryParseWindowHandle(
                explorerWindow.RootWindow,
                out windowHandle))
        {
            target = new ExplorerCaptionTargetIdentity(
                explorerWindow.RootClass,
                explorerWindow.RootWindow,
                explorerWindow.RootProcessId,
                explorerWindow.RootThreadId,
                rootNode.Rectangle,
                explorerWindow.TopologySha256);
            currentCaption = DwmCaptionReader.Read(windowHandle);
        }

        ExplorerCaptionGateInputs inputs = new(
            admission.Passed,
            profile?.AllowedCapabilities.Contains(
                RequiredCapability,
                StringComparer.Ordinal) == true,
            profile is not null && !profile.ActivationPermitted,
            string.Equals(
                profile?.LiveExplorer,
                "not-run",
                StringComparison.Ordinal),
            string.Equals(
                surfaceProbe.Result,
                "passed-read-only-inventory",
                StringComparison.Ordinal),
            inventory?.ExactDesktopHostObserved == true,
            inventory?.ExactPrimaryTaskbarObserved == true,
            explorerWindow is not null,
            explorerWindow is not null &&
                rootNode is not null &&
                string.Equals(
                    explorerWindow.RootClass,
                    "CabinetWClass",
                    StringComparison.Ordinal) &&
                rootNode.Visible &&
                rootNode.ProcessId == explorerWindow.RootProcessId &&
                rootNode.ThreadId == explorerWindow.RootThreadId &&
                !explorerWindow.Truncated &&
                windowHandle != nint.Zero,
            explorerWindow is not null &&
                inventory is not null &&
                explorerWindow.RootProcessId ==
                    inventory.DesktopShellProcessId,
            currentCaption?.HResult >= 0,
            currentCaption?.Value is 0 or 1,
            safetyState.KillSwitchArmed,
            !safetyState.ActiveModulePresent,
            safetyState.Error is null);
        IReadOnlyList<string> failures = Evaluate(inputs);

        ExplorerCaptionGateReceipt receipt = new(
            1,
            "jarvisv2-win10-explorer-caption-read-gate",
            failures.Count == 0
                ? "passed-single-explorer-caption-read"
                : "blocked",
            observedAtUtc,
            admission,
            surfaceProbe,
            safetyState,
            RequiredCapability,
            profile?.ProfileId,
            explorerWindows.Length,
            expectedWindowHandle,
            target,
            currentCaption,
            failures.Count == 0,
            failures.Count == 0,
            false,
            false,
            false,
            false,
            "read-only-inspection",
            failures);
        return new ExplorerCaptionGateResult(receipt, windowHandle);
    }

    public static int RunModelTests()
    {
        List<object> scenarios = [];
        RunScenario(scenarios, "all-gates-pass", AllPassed(), true);
        RunScenario(
            scenarios,
            "host-admission-fails",
            AllPassed() with { AdmissionPassed = false },
            false);
        RunScenario(
            scenarios,
            "capability-missing",
            AllPassed() with { CapabilityGranted = false },
            false);
        RunScenario(
            scenarios,
            "profile-activation-not-denied",
            AllPassed() with { ProfileActivationDenied = false },
            false);
        RunScenario(
            scenarios,
            "profile-live-state-drifted",
            AllPassed() with { ProfileLiveExplorerNotRun = false },
            false);
        RunScenario(
            scenarios,
            "surface-probe-fails",
            AllPassed() with { SurfaceProbePassed = false },
            false);
        RunScenario(
            scenarios,
            "desktop-shape-missing",
            AllPassed() with { ExactDesktopObserved = false },
            false);
        RunScenario(
            scenarios,
            "taskbar-shape-missing",
            AllPassed() with { ExactTaskbarObserved = false },
            false);
        RunScenario(
            scenarios,
            "explorer-count-not-one",
            AllPassed() with { TargetSelectionExact = false },
            false);
        RunScenario(
            scenarios,
            "explorer-root-invalid",
            AllPassed() with { ExplorerRootExact = false },
            false);
        RunScenario(
            scenarios,
            "explorer-pid-not-shell",
            AllPassed() with { ExplorerMatchesDesktopShell = false },
            false);
        RunScenario(
            scenarios,
            "dwm-read-fails",
            AllPassed() with { DwmReadPassed = false },
            false);
        RunScenario(
            scenarios,
            "dwm-value-not-boolean",
            AllPassed() with { DwmValueBoolean = false },
            false);
        RunScenario(
            scenarios,
            "kill-switch-missing",
            AllPassed() with { KillSwitchArmed = false },
            false);
        RunScenario(
            scenarios,
            "one-shot-permit-present",
            AllPassed() with { ActiveModuleAbsent = false },
            false);
        RunScenario(
            scenarios,
            "safety-state-unknown",
            AllPassed() with { SafetyStateKnown = false },
            false);

        int passedCount = scenarios.Count(
            scenario =>
                (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                    scenario) ?? false));
        bool passed = passedCount == scenarios.Count;
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-win10-explorer-caption-gate-model-tests",
                result = passed ? "passed" : "failed",
                scenarioCount = scenarios.Count,
                passedCount,
                scenarios,
                previewExecutionSupported = false,
                mutationSupported = false,
                activationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "not-run",
            });
        return passed ? 0 : 1;
    }

    public static void WriteJson(object value) =>
        Console.WriteLine(
            JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));

    private static ExplorerCaptionGateInputs AllPassed() =>
        new(
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true);

    private static IReadOnlyList<string> Evaluate(
        ExplorerCaptionGateInputs inputs)
    {
        List<string> failures = [];
        AddFailure(
            failures,
            inputs.AdmissionPassed,
            "exact-win10-host-admission-failed");
        AddFailure(
            failures,
            inputs.CapabilityGranted,
            "profile-capability-not-granted");
        AddFailure(
            failures,
            inputs.ProfileActivationDenied,
            "profile-activation-not-denied");
        AddFailure(
            failures,
            inputs.ProfileLiveExplorerNotRun,
            "profile-live-explorer-state-drifted");
        AddFailure(
            failures,
            inputs.SurfaceProbePassed,
            "win10-shell-surface-probe-failed");
        AddFailure(
            failures,
            inputs.ExactDesktopObserved,
            "exact-desktop-host-not-observed");
        AddFailure(
            failures,
            inputs.ExactTaskbarObserved,
            "exact-primary-taskbar-not-observed");
        AddFailure(
            failures,
            inputs.TargetSelectionExact,
            "expected-explorer-window-not-exactly-selected");
        AddFailure(
            failures,
            inputs.ExplorerRootExact,
            "explorer-root-identity-invalid");
        AddFailure(
            failures,
            inputs.ExplorerMatchesDesktopShell,
            "explorer-root-pid-not-desktop-shell");
        AddFailure(
            failures,
            inputs.DwmReadPassed,
            "dwm-caption-attribute-read-failed");
        AddFailure(
            failures,
            inputs.DwmValueBoolean,
            "dwm-caption-attribute-not-boolean");
        AddFailure(
            failures,
            inputs.KillSwitchArmed,
            "kill-switch-not-armed");
        AddFailure(
            failures,
            inputs.ActiveModuleAbsent,
            "one-shot-module-permit-present");
        AddFailure(
            failures,
            inputs.SafetyStateKnown,
            "safety-state-unknown");
        return failures;
    }

    private static void AddFailure(
        ICollection<string> failures,
        bool passed,
        string failure)
    {
        if (!passed)
        {
            failures.Add(failure);
        }
    }

    private static void RunScenario(
        ICollection<object> scenarios,
        string name,
        ExplorerCaptionGateInputs inputs,
        bool expectedPassed)
    {
        IReadOnlyList<string> failures = Evaluate(inputs);
        bool actualPassed = failures.Count == 0;
        scenarios.Add(
            new
            {
                Name = name,
                Passed = actualPassed == expectedPassed,
                ExpectedGatePassed = expectedPassed,
                ActualGatePassed = actualPassed,
                Failures = failures,
            });
    }

    private static bool TryParseWindowHandle(
        string text,
        out nint windowHandle)
    {
        windowHandle = nint.Zero;
        if (!text.StartsWith("0x", StringComparison.Ordinal) ||
            !ulong.TryParse(
                text.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong raw) ||
            raw == 0 ||
            raw > long.MaxValue)
        {
            return false;
        }

        windowHandle = unchecked((nint)(long)raw);
        return true;
    }

    private static SurfaceTreeObservation? SelectExplorerWindow(
        IReadOnlyList<SurfaceTreeObservation> explorerWindows,
        string? expectedWindowHandle)
    {
        if (string.IsNullOrWhiteSpace(expectedWindowHandle))
        {
            return explorerWindows.Count == 1
                ? explorerWindows[0]
                : null;
        }

        SurfaceTreeObservation[] matches = explorerWindows
            .Where(candidate =>
                string.Equals(
                    candidate.RootWindow,
                    expectedWindowHandle,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ExplorerCaptionSafetyState ReadSafetyState()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        string stateRoot = Path.GetFullPath(
            Path.Combine(localAppData, "JARVIS2"));
        string killSwitchPath =
            Path.Combine(stateRoot, "disabled.flag");
        string activeModulePath =
            Path.Combine(stateRoot, "active-module.txt");
        try
        {
            bool killSwitchArmed =
                File.Exists(killSwitchPath) &&
                EnsureOrdinaryPath(killSwitchPath);
            bool activeModulePresent = File.Exists(activeModulePath);
            if (activeModulePresent)
            {
                EnsureOrdinaryPath(activeModulePath);
            }

            return new ExplorerCaptionSafetyState(
                stateRoot,
                killSwitchPath,
                killSwitchArmed,
                activeModulePath,
                activeModulePresent,
                null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            return new ExplorerCaptionSafetyState(
                stateRoot,
                killSwitchPath,
                false,
                activeModulePath,
                false,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool EnsureOrdinaryPath(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Safety path is not an ordinary file: {path}");
        }

        return true;
    }
}
