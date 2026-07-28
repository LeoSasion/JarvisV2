using System.Globalization;

namespace Jarvis.ExplorerHostModel;

internal static class HostAdmissionPlanner
{
    private const string ExpectedEvidenceKind = "offline-fixture";
    private const string ExpectedSelectionMode = "shell-window-exact";
    private const string ExpectedProcessName = "explorer.exe";
    private const string ExpectedArchitecture = "amd64";
    private const string ExpectedModuleId = "jarvis-explorer-bridge";
    private const string ExpectedModuleContract = "standalone-explicit-init-v1";
    private const string CandidateMethod =
        "thread-specific-window-hook-review-candidate";

    public static HostPlanReceipt Evaluate(HostSnapshot snapshot)
    {
        List<string> failures = new();

        Require(snapshot.SchemaVersion == 1, "snapshot-schema-unsupported", failures);
        Require(
            string.Equals(
                snapshot.EvidenceKind,
                ExpectedEvidenceKind,
                StringComparison.Ordinal),
            "evidence-kind-not-offline-fixture",
            failures);
        Require(!snapshot.LiveSystemTouched, "fixture-claims-live-system-touch", failures);
        Require(snapshot.CurrentSessionId >= 0, "current-session-invalid", failures);
        Require(
            string.Equals(snapshot.KillSwitchState, "armed", StringComparison.Ordinal),
            "kill-switch-not-armed",
            failures);
        Require(
            string.Equals(
                snapshot.ActiveModulePermitState,
                "absent",
                StringComparison.Ordinal),
            "active-module-permit-present",
            failures);

        ValidateLegacyHost(snapshot.LegacyHost, failures);
        ValidateSelection(snapshot.Selection, failures);
        ValidateTarget(snapshot, failures);
        ValidateModule(snapshot.Module, failures);
        ValidateMappings(snapshot.ExistingMappings, failures);

        string[] uniqueFailures = failures
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        HostPlanCandidate? candidate = null;
        if (uniqueFailures.Length == 0)
        {
            candidate = new HostPlanCandidate
            {
                Method = CandidateMethod,
                ProcessId = snapshot.Target.ProcessId,
                ThreadId = snapshot.Selection.ShellWindowThreadId,
                HookScope = "single-thread",
                ModuleId = snapshot.Module.ModuleId,
                ModuleSha256 = snapshot.Module.Sha256.ToUpperInvariant(),
                RequiresLiveImplementationReview = true,
                RequiresExactUserApproval = true,
            };
        }

        return new HostPlanReceipt
        {
            SchemaVersion = 1,
            ReceiptType = "jarvisv2-explorer-host-offline-plan",
            Result = uniqueFailures.Length == 0 ? "passed-offline-plan" : "blocked",
            EvaluationMode = "offline-fixture-only",
            ExecutionSupported = false,
            ActivationPermitted = false,
            LiveExplorer = "not-run",
            MutationPerformed = false,
            Candidate = candidate,
            FailureCount = uniqueFailures.Length,
            Failures = uniqueFailures,
        };
    }

    private static void ValidateLegacyHost(
        LegacyHostSnapshot host,
        ICollection<string> failures)
    {
        Require(host.Quarantined, "legacy-host-not-quarantined", failures);
        Require(
            string.Equals(host.ServiceState, "Stopped", StringComparison.Ordinal),
            "legacy-host-service-not-stopped",
            failures);
        Require(host.ServiceProcessId == 0, "legacy-host-service-pid-present", failures);
        Require(
            host.BaseRuntimeMappingCount == 0,
            "legacy-host-base-runtime-mapped",
            failures);
    }

    private static void ValidateSelection(
        TargetSelectionSnapshot selection,
        ICollection<string> failures)
    {
        Require(
            string.Equals(
                selection.Mode,
                ExpectedSelectionMode,
                StringComparison.Ordinal),
            "selection-mode-not-shell-window-exact",
            failures);
        Require(
            !selection.ProcessEnumerationPerformed,
            "process-enumeration-forbidden",
            failures);
        Require(selection.ShellWindowPresent, "shell-window-absent", failures);
        Require(
            selection.ShellWindowProcessId > 0,
            "shell-window-process-id-invalid",
            failures);
        Require(
            selection.ShellWindowThreadId > 0,
            "shell-window-thread-id-invalid",
            failures);
        Require(
            selection.DesktopShellCandidateCount == 1,
            "desktop-shell-candidate-count-not-one",
            failures);
    }

    private static void ValidateTarget(
        HostSnapshot snapshot,
        ICollection<string> failures)
    {
        TargetProcessSnapshot target = snapshot.Target;
        Require(target.ProcessId > 0, "target-process-id-invalid", failures);
        Require(
            target.ProcessId == snapshot.Selection.ShellWindowProcessId,
            "target-does-not-own-shell-window",
            failures);
        Require(
            target.SessionId == snapshot.CurrentSessionId,
            "target-session-mismatch",
            failures);
        Require(
            string.Equals(
                target.ProcessName,
                ExpectedProcessName,
                StringComparison.OrdinalIgnoreCase),
            "target-is-not-explorer",
            failures);
        Require(
            string.Equals(
                target.ImagePath,
                target.ExpectedImagePath,
                StringComparison.OrdinalIgnoreCase),
            "target-image-path-mismatch",
            failures);
        Require(
            IsSha256(target.ImageSha256) &&
            string.Equals(
                target.ImageSha256,
                target.ExpectedImageSha256,
                StringComparison.OrdinalIgnoreCase),
            "target-image-sha256-mismatch",
            failures);
        Require(
            !string.IsNullOrWhiteSpace(target.ProductVersion) &&
            string.Equals(
                target.ProductVersion,
                target.ExpectedProductVersion,
                StringComparison.Ordinal),
            "target-product-version-mismatch",
            failures);
        Require(
            string.Equals(
                target.SignatureState,
                "trusted",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(target.SignerSubject) &&
            string.Equals(
                target.SignerSubject,
                target.ExpectedSignerSubject,
                StringComparison.Ordinal),
            "target-signature-mismatch",
            failures);
        Require(
            string.Equals(
                target.Architecture,
                ExpectedArchitecture,
                StringComparison.Ordinal),
            "target-architecture-mismatch",
            failures);
        bool startTimeValid = DateTimeOffset.TryParse(
            target.StartTimeUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset startTime);
        Require(
            startTimeValid && startTime.Offset == TimeSpan.Zero,
            "target-start-time-invalid",
            failures);
    }

    private static void ValidateModule(
        CandidateModuleSnapshot module,
        ICollection<string> failures)
    {
        Require(
            string.Equals(
                module.ModuleId,
                ExpectedModuleId,
                StringComparison.Ordinal),
            "module-id-not-standalone-bridge",
            failures);
        Require(
            string.Equals(
                module.Contract,
                ExpectedModuleContract,
                StringComparison.Ordinal),
            "module-contract-not-standalone",
            failures);
        Require(
            module.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            !module.Path.Contains("windhawk", StringComparison.OrdinalIgnoreCase),
            "module-path-not-standalone-dll",
            failures);
        Require(
            IsSha256(module.Sha256) &&
            string.Equals(
                module.Sha256,
                module.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase),
            "module-sha256-mismatch",
            failures);
        Require(
            string.Equals(
                module.SignatureState,
                "trusted",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(module.SignerSubject) &&
            string.Equals(
                module.SignerSubject,
                module.ExpectedSignerSubject,
                StringComparison.Ordinal),
            "module-signature-mismatch",
            failures);
        Require(
            string.Equals(
                module.Architecture,
                ExpectedArchitecture,
                StringComparison.Ordinal),
            "module-architecture-mismatch",
            failures);
    }

    private static void ValidateMappings(
        IReadOnlyList<ExistingMappingSnapshot> mappings,
        ICollection<string> failures)
    {
        foreach (ExistingMappingSnapshot mapping in mappings)
        {
            if (mapping.ProcessId <= 0)
            {
                failures.Add("mapping-process-id-invalid");
            }

            if (mapping.ModuleName.Contains("windhawk", StringComparison.OrdinalIgnoreCase) ||
                mapping.ModuleName.Contains("jarvis", StringComparison.OrdinalIgnoreCase) ||
                mapping.Path.Contains("windhawk", StringComparison.OrdinalIgnoreCase) ||
                mapping.Path.Contains("jarvis", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("legacy-or-target-runtime-already-mapped");
            }
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(static character =>
            character is >= '0' and <= '9' ||
            character is >= 'A' and <= 'F' ||
            character is >= 'a' and <= 'f');

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
}

internal sealed class HostPlanReceipt
{
    public int SchemaVersion { get; init; }
    public string ReceiptType { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string EvaluationMode { get; init; } = string.Empty;
    public bool ExecutionSupported { get; init; }
    public bool ActivationPermitted { get; init; }
    public string LiveExplorer { get; init; } = string.Empty;
    public bool MutationPerformed { get; init; }
    public HostPlanCandidate? Candidate { get; init; }
    public int FailureCount { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
}

internal sealed class HostPlanCandidate
{
    public string Method { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public string HookScope { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleSha256 { get; init; } = string.Empty;
    public bool RequiresLiveImplementationReview { get; init; }
    public bool RequiresExactUserApproval { get; init; }
}
