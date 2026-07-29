namespace Jarvis.ExplorerPreviewModel;

internal static class PreviewModelScenarios
{
    private const string ProfileHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string CompatibilityHash =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    public static PreviewModelTestReceipt Run()
    {
        List<ModelScenarioResult> scenarios = [];

        Add(scenarios, "compile-valid-candidate", () =>
            Compile(CreateProfile()).Result ==
                "compiled-offline-candidate");
        Add(scenarios, "compile-schema-version", () =>
            HasCompileFailure(
                CreateProfile() with { SchemaVersion = 2 },
                "profile-schema-version-invalid"));
        Add(scenarios, "compile-profile-id", () =>
            HasCompileFailure(
                CreateProfile() with { ProfileId = "other" },
                "profile-id-invalid"));
        Add(scenarios, "compile-lifecycle-state", () =>
            HasCompileFailure(
                CreateProfile() with { LifecycleState = "live" },
                "profile-lifecycle-state-invalid"));
        Add(scenarios, "compile-host-profile", () =>
            HasCompileFailure(
                CreateProfile() with { HostProfileId = "other" },
                "host-profile-id-invalid"));
        Add(scenarios, "compile-host-profile-unique", () =>
        {
            CompatibilityDocument compatibility =
                CreateCompatibility();
            CompatibilityHost host =
                compatibility.ValidatedHosts[0];
            CandidateCompilationReceipt receipt =
                CandidateProfileCompiler.Compile(
                    CreateProfile(),
                    new CompatibilityDocument([host, host]),
                    ProfileHash,
                    CompatibilityHash);
            return receipt.Failures.Contains(
                "compatibility-host-profile-not-unique");
        });
        Add(scenarios, "compile-host-build", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    HostFingerprint =
                        CreateProfile().HostFingerprint with
                        {
                            WindowsBuild = 99999,
                        },
                },
                "host-windows-build-drift"));
        Add(scenarios, "compile-host-ubr", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    HostFingerprint =
                        CreateProfile().HostFingerprint with { Ubr = 1 },
                },
                "host-ubr-drift"));
        Add(scenarios, "compile-host-architecture", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    HostFingerprint =
                        CreateProfile().HostFingerprint with
                        {
                            Architecture = "ARM64",
                        },
                },
                "host-architecture-drift"));
        Add(scenarios, "compile-explorer-sha", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    HostFingerprint =
                        CreateProfile().HostFingerprint with
                        {
                            ExplorerSha256 = "00",
                        },
                },
                "host-explorer-sha256-drift"));
        Add(scenarios, "compile-upstream-commit", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    UpstreamIdentity =
                        CreateProfile().UpstreamIdentity with
                        {
                            Commit = "drift",
                        },
                },
                "upstream-identity-drift"));
        Add(scenarios, "compile-upstream-hash", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    UpstreamIdentity =
                        CreateProfile().UpstreamIdentity with
                        {
                            SourceSha256 = "drift",
                        },
                },
                "upstream-identity-drift"));
        Add(scenarios, "compile-surface-role-set", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    Surfaces = CreateProfile().Surfaces.Take(2).ToArray(),
                },
                "surface-role-set-not-exact"));
        Add(scenarios, "compile-match-count-one", () =>
        {
            CandidateProfileDocument profile = CreateProfile();
            SurfaceCandidate[] surfaces = (SurfaceCandidate[])profile.Surfaces.Clone();
            surfaces[0] = surfaces[0] with { ExpectedMatchCount = 2 };
            return HasCompileFailure(
                profile with { Surfaces = surfaces },
                "surface-match-count-not-one:tab-strip");
        });
        Add(scenarios, "compile-selector-property-filter-forbidden", () =>
        {
            CandidateProfileDocument profile = CreateProfile();
            SurfaceCandidate[] surfaces = (SurfaceCandidate[])profile.Surfaces.Clone();
            surfaces[0] = surfaces[0] with
            {
                Selector =
                    "Microsoft.UI.Xaml.Controls.Grid[Opacity=1]",
            };
            return HasCompileFailure(
                profile with { Surfaces = surfaces },
                "selector-contains-forbidden-syntax:tab-strip");
        });
        Add(scenarios, "compile-selector-edge-wildcard", () =>
        {
            CandidateProfileDocument profile = CreateProfile();
            SurfaceCandidate[] surfaces = (SurfaceCandidate[])profile.Surfaces.Clone();
            surfaces[0] = surfaces[0] with
            {
                Selector = "* > Microsoft.UI.Xaml.Controls.Grid",
            };
            return HasCompileFailure(
                profile with { Surfaces = surfaces },
                "selector-wildcard-edge-forbidden:tab-strip");
        });
        Add(scenarios, "compile-property-set", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    AllowedProperties = ["Background"],
                },
                "allowed-property-set-not-exact"));
        Add(scenarios, "compile-duration", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    PreviewPolicy = CreateProfile().PreviewPolicy with
                    {
                        DurationSeconds = 30,
                    },
                },
                "preview-duration-not-60"));
        Add(scenarios, "compile-reverse-restore", () =>
            HasCompileFailure(
                CreateProfile() with
                {
                    PreviewPolicy = CreateProfile().PreviewPolicy with
                    {
                        RestoreOrder = "forward",
                    },
                },
                "preview-restore-order-invalid"));
        Add(scenarios, "compile-offline-flags", () =>
            HasCompileFailure(
                CreateProfile() with { ExecutionSupported = true },
                "profile-offline-boundary-invalid"));

        Add(scenarios, "plan-valid-synthetic-discovery", () =>
            CreatePlan().Result == "passed-offline-review-plan");
        Add(scenarios, "plan-compilation-required", () =>
        {
            CandidateCompilationReceipt compilation =
                Compile(CreateProfile()) with { Result = "blocked" };
            return HasPlanFailure(
                CreateProfile(),
                compilation,
                CreateDiscovery(),
                "candidate-compilation-not-passed");
        });
        Add(scenarios, "plan-profile-hash-binding", () =>
            HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                CreateDiscovery() with { ProfileSha256 = "drift" },
                "discovery-profile-binding-mismatch"));
        Add(scenarios, "plan-freshness", () =>
        {
            ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
            return PreviewSessionPlanner.Create(
                    CreateProfile(),
                    Compile(CreateProfile()),
                    discovery,
                    discovery.ObservedAtUtc.AddMinutes(3))
                .Failures.Contains(
                    "discovery-evidence-stale-or-time-invalid");
        });
        Add(scenarios, "plan-process-id", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { ProcessId = 0 },
                "target-process-id-invalid"));
        Add(scenarios, "plan-desktop-shell-collision", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { ProcessId = 1111 },
                "desktop-shell-target-forbidden"));
        Add(scenarios, "plan-thread-id", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { ThreadId = 0 },
                "target-thread-id-invalid"));
        Add(scenarios, "plan-window-handle", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { WindowHandle = "0x0" },
                "target-window-handle-invalid"));
        Add(scenarios, "plan-window-class", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { WindowClass = "Progman" },
                "target-window-class-invalid"));
        Add(scenarios, "plan-window-title", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { WindowTitle = "Home" },
                "target-window-title-not-exact"));
        Add(scenarios, "plan-separate-process", () =>
            HasTargetFailure(
                CreateDiscovery().Target with { SeparateProcess = false },
                "target-separate-process-required"));
        Add(scenarios, "plan-process-start-utc", () =>
            HasTargetFailure(
                CreateDiscovery().Target with
                {
                    ProcessStartTimeUtc = DateTime.SpecifyKind(
                        new DateTime(2026, 7, 28, 3, 59, 0),
                        DateTimeKind.Unspecified),
                },
                "target-process-start-not-utc"));
        Add(scenarios, "plan-generation", () =>
            HasTargetFailure(
                CreateDiscovery().Target with
                {
                    VisualTreeGeneration = string.Empty,
                },
                "target-visual-tree-generation-missing"));
        Add(scenarios, "plan-surface-role-set", () =>
            HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                CreateDiscovery() with
                {
                    Surfaces = CreateDiscovery().Surfaces.Take(2).ToArray(),
                },
                "discovery-surface-role-set-not-exact"));
        Add(scenarios, "plan-instance-unique", () =>
        {
            ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
            ObservedSurface[] surfaces =
                (ObservedSurface[])discovery.Surfaces.Clone();
            surfaces[1] = surfaces[1] with
            {
                InstanceId = surfaces[0].InstanceId,
            };
            return HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                discovery with { Surfaces = surfaces },
                "discovery-instance-reused");
        });
        Add(scenarios, "plan-match-count", () =>
        {
            ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
            ObservedSurface[] surfaces =
                (ObservedSurface[])discovery.Surfaces.Clone();
            surfaces[0] = surfaces[0] with { MatchCount = 2 };
            return HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                discovery with { Surfaces = surfaces },
                "discovery-match-count-not-one:tab-strip");
        });
        Add(scenarios, "plan-selector-binding", () =>
        {
            ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
            ObservedSurface[] surfaces =
                (ObservedSurface[])discovery.Surfaces.Clone();
            surfaces[0] = surfaces[0] with { Selector = "drift" };
            return HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                discovery with { Surfaces = surfaces },
                "discovery-selector-mismatch:tab-strip");
        });
        Add(scenarios, "plan-original-snapshot", () =>
        {
            ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
            ObservedSurface[] surfaces =
                (ObservedSurface[])discovery.Surfaces.Clone();
            surfaces[0] = surfaces[0] with
            {
                OriginalValues = new Dictionary<string, string>
                {
                    ["Background"] = "#FF000000",
                },
            };
            return HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                discovery with { Surfaces = surfaces },
                "discovery-original-snapshot-incomplete:tab-strip");
        });
        Add(scenarios, "plan-discovery-mutation-forbidden", () =>
            HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                CreateDiscovery() with { MutationPerformed = true },
                "discovery-not-readonly"));
        Add(scenarios, "plan-discovery-live-label", () =>
            HasPlanFailure(
                CreateProfile(),
                Compile(CreateProfile()),
                CreateDiscovery() with { LiveExplorer = "controlled-session" },
                "discovery-not-readonly"));
        Add(scenarios, "plan-apply-and-restore-order", () =>
        {
            PreviewReviewPlanReceipt plan = CreatePlan();
            string[] apply = plan.Steps
                .Where(step => step.Action == "apply-surface-style")
                .Select(step => step.Role!)
                .ToArray();
            string[] restore = plan.Steps
                .Where(step => step.Action == "restore-surface-originals")
                .Select(step => step.Role!)
                .ToArray();
            return apply.SequenceEqual(
                    ["tab-strip", "command-bar", "navigation-pane"],
                    StringComparer.Ordinal) &&
                restore.SequenceEqual(
                    apply.Reverse(),
                    StringComparer.Ordinal);
        });
        Add(scenarios, "plan-duration-60", () =>
            CreatePlan().PreviewDurationSeconds == 60);
        Add(scenarios, "plan-never-self-approves", () =>
        {
            PreviewReviewPlanReceipt plan = CreatePlan();
            return !plan.ReadyForExactApproval &&
                !plan.ExecutionSupported &&
                !plan.ActivationPermitted &&
                plan.LiveExplorer == "not-run" &&
                !plan.MutationPerformed;
        });

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new PreviewModelTestReceipt(
            SchemaVersion: 1,
            ReceiptType:
                "jarvisv2-explorer-preview-offline-model-test",
            Result: passedCount == scenarios.Count ? "passed" : "failed",
            ScenarioCount: scenarios.Count,
            PassedCount: passedCount,
            ExecutionSupported: false,
            ActivationPermitted: false,
            LiveExplorer: "not-run",
            MutationPerformed: false,
            Scenarios: scenarios);
    }

    private static void Add(
        ICollection<ModelScenarioResult> scenarios,
        string name,
        Func<bool> scenario)
    {
        try
        {
            bool passed = scenario();
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    passed,
                    passed ? "passed" : "assertion-failed"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    false,
                    $"{exception.GetType().Name}:{exception.Message}"));
        }
    }

    private static CandidateCompilationReceipt Compile(
        CandidateProfileDocument profile)
    {
        return CandidateProfileCompiler.Compile(
            profile,
            CreateCompatibility(),
            ProfileHash,
            CompatibilityHash);
    }

    private static bool HasCompileFailure(
        CandidateProfileDocument profile,
        string expectedFailure)
    {
        CandidateCompilationReceipt receipt = Compile(profile);
        return receipt.Result == "blocked" &&
            receipt.Failures.Contains(expectedFailure) &&
            !receipt.ReadyForPreview &&
            !receipt.ReadyForExactApproval;
    }

    private static PreviewReviewPlanReceipt CreatePlan()
    {
        ReadOnlyDiscoveryEvidence discovery = CreateDiscovery();
        return PreviewSessionPlanner.Create(
            CreateProfile(),
            Compile(CreateProfile()),
            discovery,
            discovery.ObservedAtUtc.AddSeconds(10));
    }

    private static bool HasTargetFailure(
        ObservedTarget target,
        string expectedFailure)
    {
        ReadOnlyDiscoveryEvidence discovery =
            CreateDiscovery() with { Target = target };
        return HasPlanFailure(
            CreateProfile(),
            Compile(CreateProfile()),
            discovery,
            expectedFailure);
    }

    private static bool HasPlanFailure(
        CandidateProfileDocument profile,
        CandidateCompilationReceipt compilation,
        ReadOnlyDiscoveryEvidence discovery,
        string expectedFailure)
    {
        PreviewReviewPlanReceipt plan = PreviewSessionPlanner.Create(
            profile,
            compilation,
            discovery,
            discovery.ObservedAtUtc.AddSeconds(10));
        return plan.Result == "blocked" &&
            plan.Failures.Contains(expectedFailure) &&
            plan.Steps.Count == 0 &&
            !plan.ReadyForExactApproval &&
            !plan.ExecutionSupported &&
            !plan.ActivationPermitted &&
            !plan.MutationPerformed;
    }

    private static CandidateProfileDocument CreateProfile()
    {
        return new CandidateProfileDocument(
            SchemaVersion: 1,
            ProfileId: PreviewContract.ProfileId,
            LifecycleState: "offline-candidate",
            HostProfileId: PreviewContract.HostProfileId,
            HostFingerprint: new HostFingerprint(
                26200,
                8875,
                "AMD64",
                "10.0.26100.8875",
                3385624,
                "80B21E6F70524EFD84037A4EDA479DDC4BC55C0D6C1A33439B85A554E740F30C"),
            UpstreamIdentity: new UpstreamIdentity(
                PreviewContract.UpstreamName,
                PreviewContract.UpstreamVersion,
                "https://github.com/ramensoftware/windhawk-mods",
                PreviewContract.UpstreamCommit,
                "mods/windows-11-file-explorer-styler.wh.cpp",
                PreviewContract.UpstreamGitBlob,
                PreviewContract.UpstreamSourceSize,
                PreviewContract.UpstreamSourceSha256,
                "GPL-3.0"),
            Surfaces:
            [
                new(
                    "tab-strip",
                    "FileExplorerExtensions.FileExplorerTabControl > * > Microsoft.UI.Xaml.Controls.Grid#TabContainerGrid",
                    1,
                    "upstream-theme-candidate"),
                new(
                    "command-bar",
                    "FileExplorerExtensions.CommandBarControl > * > Microsoft.UI.Xaml.Controls.Grid#CommandBarControlRootGrid",
                    1,
                    "upstream-theme-candidate"),
                new(
                    "navigation-pane",
                    "Microsoft.UI.Xaml.Controls.NavigationView",
                    1,
                    "inferred-candidate-requires-readonly-discovery"),
            ],
            AllowedProperties:
                ["Background", "Foreground", "BorderBrush"],
            PreviewPolicy: new PreviewPolicy(
                60,
                true,
                true,
                "strict-reverse",
                ["before", "during", "after"],
                true),
            LiveEvidence: "not-run",
            ExecutionSupported: false,
            ActivationPermitted: false,
            MutationPerformed: false);
    }

    private static CompatibilityDocument CreateCompatibility()
    {
        return new CompatibilityDocument(
            [
                new CompatibilityHost(
                    PreviewContract.HostProfileId,
                    26200,
                    8875,
                    "AMD64",
                    new CompatibilityExplorer(
                        "10.0.26100.8875",
                        3385624,
                        "80B21E6F70524EFD84037A4EDA479DDC4BC55C0D6C1A33439B85A554E740F30C")),
            ]);
    }

    private static ReadOnlyDiscoveryEvidence CreateDiscovery()
    {
        CandidateCompilationReceipt compilation = Compile(CreateProfile());
        Dictionary<string, string> originals =
            new(StringComparer.Ordinal)
            {
                ["Background"] = "#FF202020",
                ["Foreground"] = "#FFF0F0F0",
                ["BorderBrush"] = "#FF404040",
            };
        return new ReadOnlyDiscoveryEvidence(
            SchemaVersion: 1,
            ReceiptType:
                "jarvisv2-explorer-surface-readonly-discovery",
            Result: "passed-read-only",
            ProfileId: PreviewContract.ProfileId,
            ProfileSha256: ProfileHash,
            ObservedAtUtc:
                new DateTime(2026, 7, 28, 4, 0, 0, DateTimeKind.Utc),
            Target: new ObservedTarget(
                ProcessId: 4242,
                DesktopShellProcessId: 1111,
                ThreadId: 5151,
                WindowHandle: "0x0000000000012345",
                WindowClass: "CabinetWClass",
                WindowTitle: "C:\\",
                ExpectedWindowTitle: "C:\\",
                SeparateProcess: true,
                ProcessStartTimeUtc:
                    new DateTime(
                        2026,
                        7,
                        28,
                        3,
                        59,
                        0,
                        DateTimeKind.Utc),
                VisualTreeGeneration: "fixture-generation-1"),
            Surfaces: compilation.Surfaces
                .Select(
                    (surface, index) =>
                        new ObservedSurface(
                            surface.Role,
                            surface.Selector,
                            1,
                            $"fixture-instance-{index + 1}",
                            new Dictionary<string, string>(
                                originals,
                                StringComparer.Ordinal)))
                .ToArray(),
            LiveExplorer: "read-only-inspection",
            MutationPerformed: false);
    }
}
