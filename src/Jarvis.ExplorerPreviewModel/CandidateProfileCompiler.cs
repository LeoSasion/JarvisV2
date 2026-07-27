namespace Jarvis.ExplorerPreviewModel;

internal static class CandidateProfileCompiler
{
    public static CandidateCompilationReceipt Compile(
        CandidateProfileDocument profile,
        CompatibilityDocument compatibility,
        string profileSha256,
        string compatibilitySha256)
    {
        List<string> failures = [];
        List<CompiledSurfaceCandidate> compiledSurfaces = [];

        Require(
            profile.SchemaVersion == 1,
            "profile-schema-version-invalid",
            failures);
        Require(
            profile.ProfileId == PreviewContract.ProfileId,
            "profile-id-invalid",
            failures);
        Require(
            profile.LifecycleState == "offline-candidate",
            "profile-lifecycle-state-invalid",
            failures);
        Require(
            profile.HostProfileId == PreviewContract.HostProfileId,
            "host-profile-id-invalid",
            failures);

        CompatibilityHost[] matchingHosts = compatibility.ValidatedHosts
            .Where(
                candidate =>
                    candidate.ProfileId == profile.HostProfileId)
            .ToArray();
        if (matchingHosts.Length != 1)
        {
            failures.Add(
                matchingHosts.Length == 0
                    ? "compatibility-host-profile-missing"
                    : "compatibility-host-profile-not-unique");
        }
        else
        {
            ValidateHostFingerprint(
                profile.HostFingerprint,
                matchingHosts[0],
                failures);
        }

        ValidateUpstream(profile.UpstreamIdentity, failures);

        if (profile.Surfaces.Length !=
                PreviewContract.RequiredRoles.Count ||
            !profile.Surfaces
                .Select(surface => surface.Role)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(PreviewContract.RequiredRoles))
        {
            failures.Add("surface-role-set-not-exact");
        }
        else if (profile.Surfaces
                     .GroupBy(surface => surface.Role, StringComparer.Ordinal)
                     .Any(group => group.Count() != 1))
        {
            failures.Add("surface-role-duplicated");
        }

        foreach (SurfaceCandidate surface in profile.Surfaces
                     .OrderBy(surface => RoleOrder(surface.Role)))
        {
            if (!PreviewContract.RequiredRoles.Contains(surface.Role))
            {
                continue;
            }

            if (surface.ExpectedMatchCount != 1)
            {
                failures.Add(
                    $"surface-match-count-not-one:{surface.Role}");
                continue;
            }

            string requiredEvidence = surface.Role == "navigation-pane"
                ? "inferred-candidate-requires-readonly-discovery"
                : "upstream-theme-candidate";
            if (!string.Equals(
                    surface.EvidenceState,
                    requiredEvidence,
                    StringComparison.Ordinal))
            {
                failures.Add(
                    $"surface-evidence-state-invalid:{surface.Role}");
                continue;
            }

            if (!SelectorGrammar.TryNormalize(
                    surface.Selector,
                    out string normalized,
                    out string selectorError))
            {
                failures.Add($"{selectorError}:{surface.Role}");
                continue;
            }

            compiledSurfaces.Add(
                new CompiledSurfaceCandidate(
                    surface.Role,
                    normalized,
                    SelectorGrammar.Fingerprint(normalized),
                    surface.ExpectedMatchCount,
                    surface.EvidenceState));
        }

        Require(
            profile.AllowedProperties.Length ==
                PreviewContract.AllowedProperties.Count &&
            profile.AllowedProperties
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(PreviewContract.AllowedProperties),
            "allowed-property-set-not-exact",
            failures);

        ValidatePreviewPolicy(profile.PreviewPolicy, failures);
        Require(
            profile.LiveEvidence == "not-run",
            "profile-live-evidence-not-locked",
            failures);
        Require(
            !profile.ExecutionSupported &&
            !profile.ActivationPermitted &&
            !profile.MutationPerformed,
            "profile-offline-boundary-invalid",
            failures);

        bool passed = failures.Count == 0;
        return new CandidateCompilationReceipt(
            SchemaVersion: 1,
            ReceiptType:
                "jarvisv2-explorer-selector-candidate-compilation",
            Result: passed
                ? "compiled-offline-candidate"
                : "blocked",
            ProfileId: profile.ProfileId,
            ProfileSha256: profileSha256,
            CompatibilitySha256: compatibilitySha256,
            Surfaces: compiledSurfaces,
            ReadyForReadOnlyDiscovery: passed,
            ReadyForPreview: false,
            ReadyForExactApproval: false,
            ExecutionSupported: false,
            ActivationPermitted: false,
            LiveExplorer: "not-run",
            MutationPerformed: false,
            Failures: failures);
    }

    private static void ValidateHostFingerprint(
        HostFingerprint fingerprint,
        CompatibilityHost host,
        ICollection<string> failures)
    {
        Require(
            fingerprint.WindowsBuild == host.WindowsBuild,
            "host-windows-build-drift",
            failures);
        Require(
            fingerprint.Ubr == host.Ubr,
            "host-ubr-drift",
            failures);
        Require(
            fingerprint.Architecture == host.Architecture,
            "host-architecture-drift",
            failures);
        Require(
            fingerprint.ExplorerProductVersion ==
                host.Explorer.ProductVersion,
            "host-explorer-version-drift",
            failures);
        Require(
            fingerprint.ExplorerSize == host.Explorer.Size,
            "host-explorer-size-drift",
            failures);
        Require(
            fingerprint.ExplorerSha256 == host.Explorer.Sha256,
            "host-explorer-sha256-drift",
            failures);
    }

    private static void ValidateUpstream(
        UpstreamIdentity upstream,
        ICollection<string> failures)
    {
        Require(
            upstream.Name == PreviewContract.UpstreamName &&
            upstream.Version == PreviewContract.UpstreamVersion &&
            upstream.Commit == PreviewContract.UpstreamCommit &&
            upstream.GitBlob == PreviewContract.UpstreamGitBlob &&
            upstream.SourceSize == PreviewContract.UpstreamSourceSize &&
            upstream.SourceSha256 ==
                PreviewContract.UpstreamSourceSha256 &&
            upstream.Repository ==
                "https://github.com/ramensoftware/windhawk-mods" &&
            upstream.SourcePath ==
                "mods/windows-11-file-explorer-styler.wh.cpp" &&
            upstream.License == "GPL-3.0",
            "upstream-identity-drift",
            failures);
    }

    private static void ValidatePreviewPolicy(
        PreviewPolicy policy,
        ICollection<string> failures)
    {
        Require(
            policy.DurationSeconds == 60,
            "preview-duration-not-60",
            failures);
        Require(
            policy.RequireSeparateExplorerProcess,
            "preview-separate-process-not-required",
            failures);
        Require(
            policy.RequireCompleteOriginalSnapshot,
            "preview-original-snapshot-not-required",
            failures);
        Require(
            policy.RestoreOrder == "strict-reverse",
            "preview-restore-order-invalid",
            failures);
        Require(
            policy.ScreenshotCheckpoints.SequenceEqual(
                ["before", "during", "after"],
                StringComparer.Ordinal),
            "preview-screenshot-checkpoints-invalid",
            failures);
        Require(
            policy.CloseTemporaryWindowAfterRestore,
            "preview-close-window-not-required",
            failures);
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

    private static int RoleOrder(string role)
    {
        return role switch
        {
            "tab-strip" => 0,
            "command-bar" => 1,
            "navigation-pane" => 2,
            _ => int.MaxValue,
        };
    }
}
