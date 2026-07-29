namespace Jarvis.Win10.SurfaceSelectorModel;

internal static class ModelScenarios
{
    public static SelectorModelTestReceipt Run(EmbeddedModelInputs inputs)
    {
        List<ModelScenarioResult> scenarios = [];

        Add(scenarios, "compile-exact-eight-role-candidate", () =>
        {
            SelectorCompilationReceipt receipt = Compile(
                inputs,
                inputs.Candidate,
                inputs.Evidence);
            return receipt.Result ==
                    "compiled-offline-selector-candidates" &&
                receipt.Resolutions.Count == 8 &&
                receipt.Failures.Count == 0;
        });
        Add(scenarios, "block-candidate-schema-drift", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { SchemaVersion = 2 },
                inputs.Evidence,
                "candidate-schema-version-invalid"));
        Add(scenarios, "block-platform-drift", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { Platform = "windows11" },
                inputs.Evidence,
                "candidate-platform-invalid"));
        Add(scenarios, "block-profile-drift", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { ProfileId = "other" },
                inputs.Evidence,
                "candidate-profile-id-invalid"));
        Add(scenarios, "block-live-status", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { Status = "live" },
                inputs.Evidence,
                "candidate-status-invalid"));
        Add(scenarios, "block-style-values", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { StyleValuesDefined = true },
                inputs.Evidence,
                "candidate-style-values-must-be-absent"));
        Add(scenarios, "block-execution-capability", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { ExecutionSupported = true },
                inputs.Evidence,
                "candidate-offline-boundary-invalid"));
        Add(scenarios, "block-mutation-capability", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { MutationSupported = true },
                inputs.Evidence,
                "candidate-offline-boundary-invalid"));
        Add(scenarios, "block-activation-capability", () =>
            HasFailure(
                inputs,
                inputs.Candidate with { ActivationPermitted = true },
                inputs.Evidence,
                "candidate-offline-boundary-invalid"));
        Add(scenarios, "block-missing-role", () =>
            HasFailure(
                inputs,
                inputs.Candidate with
                {
                    Selectors = inputs.Candidate.Selectors
                        .Skip(1)
                        .ToArray(),
                },
                inputs.Evidence,
                "selector-role-set-not-exact"));
        Add(scenarios, "block-duplicate-selector-id", () =>
        {
            SurfaceSelectorCandidate[] selectors =
                CloneSelectors(inputs.Candidate.Selectors);
            selectors[1] =
                selectors[1] with { Id = selectors[0].Id };
            return HasFailure(
                inputs,
                inputs.Candidate with { Selectors = selectors },
                inputs.Evidence,
                "selector-id-duplicated");
        });
        Add(scenarios, "block-nonunique-expectation", () =>
        {
            SurfaceSelectorCandidate[] selectors =
                CloneSelectors(inputs.Candidate.Selectors);
            selectors[0] =
                selectors[0] with { ExpectedMatchCount = 2 };
            return HasFailure(
                inputs,
                inputs.Candidate with { Selectors = selectors },
                inputs.Evidence,
                "selector-match-count-not-one:desktop-icon-list");
        });
        Add(scenarios, "block-missing-observed-match", () =>
        {
            SurfaceSelectorCandidate[] selectors =
                CloneSelectors(inputs.Candidate.Selectors);
            selectors[0] =
                selectors[0] with
                {
                    ClassPath =
                        ["Progman", "SHELLDLL_DefView", "Missing"],
                };
            return HasFailure(
                inputs,
                inputs.Candidate with { Selectors = selectors },
                inputs.Evidence,
                "selector-observed-match-count-invalid:" +
                "desktop-icon-list:0");
        });
        Add(scenarios, "block-duplicate-observed-match", () =>
        {
            TopologyFixtureDocument evidence = CloneEvidence(
                inputs.Evidence);
            SurfaceFixture desktop = evidence.Surfaces.Single(
                surface => surface.SurfaceKind == "desktop-host");
            FixtureNode[] nodes =
            [
                .. desktop.Nodes,
                new FixtureNode(
                    "root/0/9",
                    "root/0",
                    "SysListView32",
                    true),
            ];
            ReplaceSurface(
                evidence,
                desktop with { Nodes = nodes });
            return HasFailure(
                inputs,
                inputs.Candidate,
                evidence,
                "selector-observed-match-count-invalid:" +
                "desktop-icon-list:2");
        });
        Add(scenarios, "block-wrong-fixture-root", () =>
        {
            TopologyFixtureDocument evidence = CloneEvidence(
                inputs.Evidence);
            SurfaceFixture desktop = evidence.Surfaces.Single(
                surface => surface.SurfaceKind == "desktop-host");
            ReplaceSurface(
                evidence,
                desktop with { RootClass = "WorkerW" });
            return HasFailure(
                inputs,
                inputs.Candidate,
                evidence,
                "fixture-root-invalid:desktop-host");
        });
        Add(scenarios, "block-missing-fixture-parent", () =>
        {
            TopologyFixtureDocument evidence = CloneEvidence(
                inputs.Evidence);
            SurfaceFixture desktop = evidence.Surfaces.Single(
                surface => surface.SurfaceKind == "desktop-host");
            FixtureNode[] nodes = desktop.Nodes
                .Select(node =>
                    node.NodeKey == "root/0/0"
                        ? node with { ParentKey = "missing" }
                        : node)
                .ToArray();
            ReplaceSurface(
                evidence,
                desktop with { Nodes = nodes });
            return HasFailure(
                inputs,
                inputs.Candidate,
                evidence,
                "fixture-node-parent-missing:" +
                "desktop-host:root/0/0");
        });
        Add(scenarios, "block-resolved-node-reuse", () =>
        {
            SurfaceSelectorCandidate[] selectors =
                CloneSelectors(inputs.Candidate.Selectors);
            int clockIndex = Array.FindIndex(
                selectors,
                selector => selector.Role == "taskbar-clock");
            selectors[clockIndex] = selectors[clockIndex] with
            {
                SurfaceKind = "primary-taskbar",
                ClassPath = ["Shell_TrayWnd", "TrayNotifyWnd"],
                RequiredVisible = true,
            };
            return HasFailure(
                inputs,
                inputs.Candidate with { Selectors = selectors },
                inputs.Evidence,
                "resolved-node-reused:taskbar-notification-area") ||
                HasFailure(
                    inputs,
                    inputs.Candidate with { Selectors = selectors },
                    inputs.Evidence,
                    "resolved-node-reused:taskbar-clock");
        });

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new SelectorModelTestReceipt(
            1,
            "jarvisv2-win10-surface-selector-model-test",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            false,
            false,
            false,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static SelectorCompilationReceipt Compile(
        EmbeddedModelInputs inputs,
        SelectorCandidateDocument candidate,
        TopologyFixtureDocument evidence) =>
        SelectorCompiler.Compile(
            candidate,
            evidence,
            inputs.CandidateSha256,
            inputs.EvidenceSha256);

    private static bool HasFailure(
        EmbeddedModelInputs inputs,
        SelectorCandidateDocument candidate,
        TopologyFixtureDocument evidence,
        string expectedFailure) =>
        Compile(inputs, candidate, evidence)
            .Failures.Contains(expectedFailure, StringComparer.Ordinal);

    private static SurfaceSelectorCandidate[] CloneSelectors(
        IEnumerable<SurfaceSelectorCandidate> selectors) =>
        selectors
            .Select(selector =>
                selector with
                {
                    ClassPath = [.. selector.ClassPath],
                })
            .ToArray();

    private static TopologyFixtureDocument CloneEvidence(
        TopologyFixtureDocument evidence) =>
        evidence with
        {
            Surfaces = evidence.Surfaces
                .Select(surface =>
                    surface with
                    {
                        Nodes = surface.Nodes
                            .Select(node => node with { })
                            .ToArray(),
                    })
                .ToArray(),
        };

    private static void ReplaceSurface(
        TopologyFixtureDocument evidence,
        SurfaceFixture replacement)
    {
        int index = Array.FindIndex(
            evidence.Surfaces,
            surface =>
                surface.SurfaceKind == replacement.SurfaceKind);
        evidence.Surfaces[index] = replacement;
    }

    private static void Add(
        ICollection<ModelScenarioResult> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    passed,
                    passed ? "passed" : "assertion returned false"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new ModelScenarioResult(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }
}
