using System.Text.Json;
using Jarvis.Win10.HostAdmission;
using Jarvis.Win10.ShellSurfaceProbe;

namespace Jarvis.Win10.DesktopStyleSession;

internal sealed record Win10DesktopSafetyState(
    string StateRoot,
    string KillSwitchPath,
    bool KillSwitchArmed,
    string ActiveModulePath,
    bool ActiveModulePresent,
    string? Error);

internal sealed record Win10DesktopStyleGateReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    DateTimeOffset ObservedAtUtc,
    Windows10HostAdmissionReceipt Admission,
    ShellSurfaceProbeReceipt SurfaceProbe,
    Win10DesktopSafetyState SafetyState,
    string RequiredCapability,
    string? HostProfileId,
    uint? ExplorerProcessId,
    bool PreviewExecutionSupported,
    bool ModuleActivationPermitted,
    bool MutationPerformed,
    string LiveExplorer,
    IReadOnlyList<string> Failures)
{
    public bool Passed =>
        string.Equals(
            Result,
            "passed-exact-win10-desktop-style-gate",
            StringComparison.Ordinal) &&
        HostProfileId is not null &&
        ExplorerProcessId is not null;
}

internal sealed record Win10DesktopStyleGateInputs(
    bool AdmissionPassed,
    bool CapabilityGranted,
    bool ProfileActivationDenied,
    bool ProfileLiveExplorerNotRun,
    bool SurfaceProbePassed,
    bool ExactDesktopObserved,
    bool ExactTaskbarObserved,
    bool SurfaceProbeReadOnly,
    bool KillSwitchArmed,
    bool ActiveModuleAbsent,
    bool SafetyStateKnown);

internal static class Win10DesktopStyleGate
{
    public const string RequiredCapability =
        "run-bounded-desktop-text-color-preview";

    private static readonly HashSet<string> AllowedPresetIds =
        new(StringComparer.Ordinal)
        {
            "orbital-cyan",
            "reactor-amber",
            "neural-emerald",
        };

    public static Win10DesktopStyleGateReceipt Inspect()
    {
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        ShellSurfaceProbeReceipt surfaceProbe =
            ShellSurfaceInspector.Inspect();
        Windows10HostAdmissionReceipt admission = surfaceProbe.Admission;
        Win10DesktopSafetyState safetyState = ReadSafetyState();
        Windows10HostProfile? profile = admission.Profile;
        ShellSurfaceInventory? inventory = surfaceProbe.Inventory;

        Win10DesktopStyleGateInputs inputs = new(
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
            !surfaceProbe.WindowTextCollected &&
                !surfaceProbe.ExecutionSupported &&
                !surfaceProbe.MutationSupported &&
                !surfaceProbe.ActivationPermitted &&
                !surfaceProbe.MutationPerformed,
            safetyState.KillSwitchArmed,
            !safetyState.ActiveModulePresent,
            safetyState.Error is null);
        IReadOnlyList<string> failures = Evaluate(inputs);

        return new Win10DesktopStyleGateReceipt(
            1,
            "jarvisv2-win10-desktop-style-session-gate",
            failures.Count == 0
                ? "passed-exact-win10-desktop-style-gate"
                : "blocked",
            observedAtUtc,
            admission,
            surfaceProbe,
            safetyState,
            RequiredCapability,
            profile?.ProfileId,
            inventory?.DesktopShellProcessId is > 0
                ? inventory.DesktopShellProcessId
                : null,
            failures.Count == 0,
            false,
            false,
            "read-only-admission",
            failures);
    }

    public static int RunModelTests()
    {
        (string Name, Win10DesktopStyleGateInputs Inputs, bool Passed)[]
            scenarios =
            [
                (
                    "all-gates-pass",
                    AllPassed(),
                    true
                ),
                (
                    "host-admission-fails",
                    AllPassed() with { AdmissionPassed = false },
                    false
                ),
                (
                    "capability-missing",
                    AllPassed() with { CapabilityGranted = false },
                    false
                ),
                (
                    "surface-probe-fails",
                    AllPassed() with { SurfaceProbePassed = false },
                    false
                ),
                (
                    "desktop-shape-missing",
                    AllPassed() with { ExactDesktopObserved = false },
                    false
                ),
                (
                    "kill-switch-missing",
                    AllPassed() with { KillSwitchArmed = false },
                    false
                ),
                (
                    "one-shot-permit-present",
                    AllPassed() with { ActiveModuleAbsent = false },
                    false
                ),
                (
                    "safety-state-unknown",
                    AllPassed() with { SafetyStateKnown = false },
                    false
                ),
            ];

        List<object> results = scenarios
            .Select(scenario =>
            {
                IReadOnlyList<string> failures = Evaluate(scenario.Inputs);
                bool actualPassed = failures.Count == 0;
                return new
                {
                    scenario.Name,
                    Passed = actualPassed == scenario.Passed,
                    ExpectedGatePassed = scenario.Passed,
                    ActualGatePassed = actualPassed,
                    Failures = failures,
                };
            })
            .Cast<object>()
            .ToList();
        foreach ((string presetId, bool expectedAllowed) in new[]
                 {
                     ("orbital-cyan", true),
                     ("reactor-amber", true),
                     ("neural-emerald", true),
                     ("graphite", false),
                 })
        {
            bool actualAllowed = AllowedPresetIds.Contains(presetId);
            results.Add(
                new
                {
                    Name = $"preset-{presetId}",
                    Passed = actualAllowed == expectedAllowed,
                    ExpectedAllowed = expectedAllowed,
                    ActualAllowed = actualAllowed,
                    Failures = Array.Empty<string>(),
                });
        }

        int passedCount = results.Count(result =>
            (bool)(result.GetType().GetProperty("Passed")?.GetValue(result) ??
                false));
        bool passed = passedCount == results.Count;
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-win10-desktop-style-session-model-tests",
                result = passed ? "passed" : "failed",
                scenarioCount = results.Count,
                passedCount,
                scenarios = results,
                moduleActivationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "not-run",
            });
        return passed ? 0 : 1;
    }

    public static void ValidatePreset(string presetId)
    {
        if (!AllowedPresetIds.Contains(presetId))
        {
            throw new ArgumentException(
                $"Unsupported Win10 preset '{presetId}'. Expected " +
                "orbital-cyan, reactor-amber or neural-emerald.",
                nameof(presetId));
        }
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

    private static Win10DesktopStyleGateInputs AllPassed() =>
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
            true);

    private static IReadOnlyList<string> Evaluate(
        Win10DesktopStyleGateInputs inputs)
    {
        List<string> failures = [];
        AddFailure(
            failures,
            inputs.AdmissionPassed,
            "exact-win10-host-admission-failed");
        AddFailure(
            failures,
            inputs.CapabilityGranted,
            "bounded-desktop-preview-capability-not-granted");
        AddFailure(
            failures,
            inputs.ProfileActivationDenied,
            "host-profile-does-not-deny-module-activation");
        AddFailure(
            failures,
            inputs.ProfileLiveExplorerNotRun,
            "host-profile-live-explorer-state-invalid");
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
            inputs.SurfaceProbeReadOnly,
            "surface-probe-readonly-contract-invalid");
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

    private static Win10DesktopSafetyState ReadSafetyState()
    {
        string stateRoot = Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "JARVIS2"));
        string killSwitchPath =
            Path.Combine(stateRoot, "disabled.flag");
        string activeModulePath =
            Path.Combine(stateRoot, "active-module.txt");

        try
        {
            EnsureOrdinaryPath(stateRoot);
            EnsureOrdinaryPath(killSwitchPath);
            EnsureOrdinaryPath(activeModulePath);
            return new Win10DesktopSafetyState(
                stateRoot,
                killSwitchPath,
                File.Exists(killSwitchPath),
                activeModulePath,
                File.Exists(activeModulePath),
                null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            return new Win10DesktopSafetyState(
                stateRoot,
                killSwitchPath,
                false,
                activeModulePath,
                true,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void EnsureOrdinaryPath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Safety path is a reparse point: {path}");
        }
    }
}
