using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.ExplorerPreviewModel;

internal static partial class PreviewSessionPlanner
{
    private static readonly string[] ApplyOrder =
        ["tab-strip", "command-bar", "navigation-pane"];

    public static PreviewReviewPlanReceipt Create(
        CandidateProfileDocument profile,
        CandidateCompilationReceipt compilation,
        ReadOnlyDiscoveryEvidence discovery,
        DateTime nowUtc)
    {
        List<string> failures = [];

        Require(
            compilation.Result == "compiled-offline-candidate" &&
            compilation.Failures.Count == 0 &&
            compilation.ReadyForReadOnlyDiscovery,
            "candidate-compilation-not-passed",
            failures);
        Require(
            discovery.SchemaVersion == 1 &&
            discovery.ReceiptType ==
                "jarvisv2-explorer-surface-readonly-discovery" &&
            discovery.Result == "passed-read-only",
            "discovery-receipt-invalid",
            failures);
        Require(
            discovery.ProfileId == profile.ProfileId &&
            discovery.ProfileSha256 == compilation.ProfileSha256,
            "discovery-profile-binding-mismatch",
            failures);
        Require(
            discovery.LiveExplorer == "read-only-inspection" &&
            !discovery.MutationPerformed,
            "discovery-not-readonly",
            failures);
        Require(
            nowUtc.Kind == DateTimeKind.Utc &&
            discovery.ObservedAtUtc.Kind == DateTimeKind.Utc &&
            nowUtc >= discovery.ObservedAtUtc &&
            nowUtc - discovery.ObservedAtUtc <= TimeSpan.FromMinutes(2),
            "discovery-evidence-stale-or-time-invalid",
            failures);

        ValidateTarget(discovery.Target, failures);
        ValidateSurfaces(
            profile,
            compilation,
            discovery.Surfaces,
            failures);

        bool passed = failures.Count == 0;
        IReadOnlyList<PreviewPlanStep> steps = passed
            ? BuildSteps()
            : [];
        string? planId = passed
            ? CreatePlanId(compilation.ProfileSha256, discovery)
            : null;

        return new PreviewReviewPlanReceipt(
            SchemaVersion: 1,
            ReceiptType: "jarvisv2-explorer-preview-offline-review-plan",
            Result: passed ? "passed-offline-review-plan" : "blocked",
            PlanId: planId,
            ExpiresAtUtc: passed
                ? discovery.ObservedAtUtc.AddMinutes(2)
                : null,
            PreviewDurationSeconds:
                profile.PreviewPolicy.DurationSeconds,
            Steps: steps,
            ReadyForExactApproval: false,
            ExecutionSupported: false,
            ActivationPermitted: false,
            LiveExplorer: "not-run",
            MutationPerformed: false,
            Failures: failures);
    }

    private static void ValidateTarget(
        ObservedTarget target,
        ICollection<string> failures)
    {
        Require(
            target.ProcessId > 0,
            "target-process-id-invalid",
            failures);
        Require(
            target.DesktopShellProcessId > 0 &&
            target.ProcessId != target.DesktopShellProcessId,
            "desktop-shell-target-forbidden",
            failures);
        Require(
            target.ThreadId > 0,
            "target-thread-id-invalid",
            failures);
        Require(
            WindowHandlePattern().IsMatch(target.WindowHandle) &&
            Convert.ToUInt64(target.WindowHandle[2..], 16) != 0,
            "target-window-handle-invalid",
            failures);
        Require(
            target.WindowClass == "CabinetWClass",
            "target-window-class-invalid",
            failures);
        Require(
            !string.IsNullOrWhiteSpace(target.WindowTitle) &&
            target.WindowTitle == target.ExpectedWindowTitle,
            "target-window-title-not-exact",
            failures);
        Require(
            target.SeparateProcess,
            "target-separate-process-required",
            failures);
        Require(
            target.ProcessStartTimeUtc.Kind == DateTimeKind.Utc,
            "target-process-start-not-utc",
            failures);
        Require(
            !string.IsNullOrWhiteSpace(target.VisualTreeGeneration),
            "target-visual-tree-generation-missing",
            failures);
    }

    private static void ValidateSurfaces(
        CandidateProfileDocument profile,
        CandidateCompilationReceipt compilation,
        IReadOnlyList<ObservedSurface> surfaces,
        ICollection<string> failures)
    {
        if (surfaces.Count != PreviewContract.RequiredRoles.Count ||
            !surfaces.Select(surface => surface.Role)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(PreviewContract.RequiredRoles))
        {
            failures.Add("discovery-surface-role-set-not-exact");
            return;
        }

        if (surfaces.GroupBy(
                    surface => surface.Role,
                    StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            failures.Add("discovery-surface-role-duplicated");
            return;
        }

        if (surfaces.Select(surface => surface.InstanceId)
            .Distinct(StringComparer.Ordinal).Count() != surfaces.Count)
        {
            failures.Add("discovery-instance-reused");
        }

        foreach (ObservedSurface surface in surfaces)
        {
            CompiledSurfaceCandidate? compiled =
                compilation.Surfaces.SingleOrDefault(
                    candidate => candidate.Role == surface.Role);
            SurfaceCandidate? candidate = profile.Surfaces.SingleOrDefault(
                item => item.Role == surface.Role);
            if (compiled is null || candidate is null)
            {
                failures.Add(
                    $"discovery-surface-not-compiled:{surface.Role}");
                continue;
            }

            Require(
                surface.MatchCount == 1,
                $"discovery-match-count-not-one:{surface.Role}",
                failures);
            Require(
                surface.Selector == compiled.Selector,
                $"discovery-selector-mismatch:{surface.Role}",
                failures);
            Require(
                !string.IsNullOrWhiteSpace(surface.InstanceId),
                $"discovery-instance-id-missing:{surface.Role}",
                failures);
            Require(
                surface.OriginalValues.Count ==
                    PreviewContract.AllowedProperties.Count &&
                surface.OriginalValues.Keys
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(PreviewContract.AllowedProperties) &&
                surface.OriginalValues.Values.All(
                    value => !string.IsNullOrWhiteSpace(value)),
                $"discovery-original-snapshot-incomplete:{surface.Role}",
                failures);
        }
    }

    private static IReadOnlyList<PreviewPlanStep> BuildSteps()
    {
        List<PreviewPlanStep> steps =
        [
            new(0, "verify-exact-target-identity", null, false, false),
            new(1, "capture-before-screenshot", null, false, false),
            new(2, "journal-all-original-properties", null, false, true),
        ];

        foreach (string role in ApplyOrder)
        {
            steps.Add(
                new(
                    steps.Count,
                    "apply-surface-style",
                    role,
                    true,
                    true));
        }

        steps.Add(
            new(
                steps.Count,
                "capture-during-screenshot",
                null,
                false,
                true));
        steps.Add(
            new(
                steps.Count,
                "wait-until-60-second-deadline",
                null,
                false,
                true));

        foreach (string role in ApplyOrder.Reverse())
        {
            steps.Add(
                new(
                    steps.Count,
                    "restore-surface-originals",
                    role,
                    true,
                    true));
        }

        steps.Add(
            new(
                steps.Count,
                "verify-all-originals-restored",
                null,
                false,
                true));
        steps.Add(
            new(
                steps.Count,
                "capture-after-screenshot",
                null,
                false,
                true));
        steps.Add(
            new(
                steps.Count,
                "close-temporary-window",
                null,
                true,
                true));
        return steps;
    }

    private static string CreatePlanId(
        string profileSha256,
        ReadOnlyDiscoveryEvidence discovery)
    {
        string material = string.Join(
            "\n",
            profileSha256,
            discovery.ObservedAtUtc.ToString("O"),
            discovery.Target.ProcessId,
            discovery.Target.ThreadId,
            discovery.Target.WindowHandle,
            discovery.Target.ProcessStartTimeUtc.ToString("O"),
            discovery.Target.VisualTreeGeneration);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
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

    [GeneratedRegex(
        "^0x[0-9A-F]{1,16}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowHandlePattern();
}
