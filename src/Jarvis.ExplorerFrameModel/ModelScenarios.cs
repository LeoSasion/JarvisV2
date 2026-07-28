namespace Jarvis.ExplorerFrameModel;

internal static class ModelScenarios
{
    private const string Generation = "offline-fixture-generation-1";

    public static ModelTestReceipt Run()
    {
        List<ModelScenarioResult> scenarios = [];

        Add(scenarios, "identity-valid", () =>
            FrameAdmission.Validate(CreateTarget()).Count == 0);
        Add(scenarios, "identity-process-invalid", () =>
            FrameAdmission.Validate(
                CreateTarget() with { ProcessId = 0 })
                .Contains("target-process-id-invalid"));
        Add(scenarios, "identity-desktop-shell-collision", () =>
            FrameAdmission.Validate(
                CreateTarget() with { ProcessId = 1111 })
                .Contains("desktop-shell-target-forbidden"));
        Add(scenarios, "identity-thread-invalid", () =>
            FrameAdmission.Validate(
                CreateTarget() with { ThreadId = 0 })
                .Contains("target-thread-id-invalid"));
        Add(scenarios, "identity-window-handle-invalid", () =>
            FrameAdmission.Validate(
                CreateTarget() with { WindowHandle = "0x0" })
                .Contains("target-window-handle-invalid"));
        Add(scenarios, "identity-window-class-invalid", () =>
            FrameAdmission.Validate(
                CreateTarget() with { WindowClass = "Progman" })
                .Contains("target-window-class-not-cabinet"));
        Add(scenarios, "identity-window-title-not-exact", () =>
            FrameAdmission.Validate(
                CreateTarget() with { WindowTitle = "Home" })
                .Contains("target-window-title-not-exact"));
        Add(scenarios, "identity-separate-process-required", () =>
            FrameAdmission.Validate(
                CreateTarget() with { SeparateProcess = false })
                .Contains("separate-explorer-process-required"));
        Add(scenarios, "identity-start-time-must-be-utc", () =>
            FrameAdmission.Validate(
                CreateTarget() with
                {
                    ProcessStartTimeUtc = DateTime.SpecifyKind(
                        new DateTime(2026, 7, 27, 5, 0, 0),
                        DateTimeKind.Unspecified),
                })
                .Contains("process-start-time-not-utc"));

        Add(scenarios, "selectors-exact-role-set", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            return transaction.TryDiscover(out _) &&
                transaction.State == FrameTransactionState.Discovered;
        });
        Add(scenarios, "selectors-missing-role-blocked", () =>
        {
            PropertyTransaction transaction = CreateTransaction(
                selectors: CreateSelectors().Take(2).ToArray());
            return !transaction.TryDiscover(out string error) &&
                error == "selector-role-set-not-exact" &&
                transaction.State == FrameTransactionState.Blocked;
        });
        Add(scenarios, "selectors-duplicate-role-blocked", () =>
        {
            SelectorSpec[] selectors = CreateSelectors();
            selectors[2] = selectors[0];
            PropertyTransaction transaction =
                CreateTransaction(selectors: selectors);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-role-set-not-exact";
        });
        Add(scenarios, "selectors-expected-count-one", () =>
        {
            SelectorSpec[] selectors = CreateSelectors();
            selectors[0] = selectors[0] with { ExpectedMatchCount = 2 };
            PropertyTransaction transaction =
                CreateTransaction(selectors: selectors);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-expected-count-not-one:tab-strip";
        });
        Add(scenarios, "selectors-offline-origin-required", () =>
        {
            SelectorSpec[] selectors = CreateSelectors();
            selectors[0] = selectors[0] with { Origin = "live-unverified" };
            PropertyTransaction transaction =
                CreateTransaction(selectors: selectors);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-origin-not-offline-candidate:tab-strip";
        });
        Add(scenarios, "selectors-missing-match-blocked", () =>
        {
            SelectorSpec[] selectors = CreateSelectors();
            selectors[0] = selectors[0] with { Name = "Missing" };
            PropertyTransaction transaction =
                CreateTransaction(selectors: selectors);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-match-missing:tab-strip";
        });
        Add(scenarios, "selectors-duplicate-match-blocked", () =>
        {
            VisualTreeFixture tree = CreateTree(includeDuplicateTab: true);
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-match-not-unique:tab-strip";
        });
        Add(scenarios, "selectors-ancestor-must-match", () =>
        {
            SelectorSpec[] selectors = CreateSelectors();
            selectors[0] = selectors[0] with
            {
                AncestorRuntimeClass = "OfflineFixture.MissingAncestor",
            };
            PropertyTransaction transaction =
                CreateTransaction(selectors: selectors);
            return !transaction.TryDiscover(out string error) &&
                error == "selector-match-missing:tab-strip";
        });

        Add(scenarios, "prepare-snapshots-all-originals", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            return transaction.TryDiscover(out _) &&
                transaction.TryPrepare(CreateIntents(), out _) &&
                transaction.State == FrameTransactionState.Prepared &&
                transaction.Snapshots.Count == 9 &&
                transaction.Snapshots.All(
                    snapshot =>
                        !string.IsNullOrWhiteSpace(snapshot.OriginalValue));
        });
        Add(scenarios, "prepare-disallowed-property-blocked", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            StyleIntent[] intents =
            [
                new(
                    SurfaceRoles.TabStrip,
                    "Opacity",
                    "0.5"),
            ];
            return transaction.TryDiscover(out _) &&
                !transaction.TryPrepare(intents, out string error) &&
                error == "style-property-not-allowed:Opacity";
        });
        Add(scenarios, "prepare-duplicate-property-blocked", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            StyleIntent duplicate = new(
                SurfaceRoles.TabStrip,
                StyleProperties.Background,
                "#FF101820");
            return transaction.TryDiscover(out _) &&
                !transaction.TryPrepare(
                    [duplicate, duplicate],
                    out string error) &&
                error ==
                    "style-property-duplicated:tab-strip:Background";
        });
        Add(scenarios, "prepare-missing-original-blocked", () =>
        {
            VisualTreeFixture tree = CreateTree();
            tree.GetRequiredNode("tab-strip")
                .Properties.Remove(StyleProperties.Background);
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            return transaction.TryDiscover(out _) &&
                !transaction.TryPrepare(
                    [
                        new(
                            SurfaceRoles.TabStrip,
                            StyleProperties.Background,
                            "#FF101820"),
                    ],
                    out string error) &&
                error ==
                    "original-property-missing:tab-strip:Background";
        });

        Add(scenarios, "apply-and-reverse-restore", () =>
        {
            VisualTreeFixture tree = CreateTree();
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            if (!Prepare(transaction) ||
                !transaction.TryApply(new FaultProfile(), out _) ||
                !transaction.TryRestore(new FaultProfile(), out _))
            {
                return false;
            }

            AuditEvent[] applies =
                transaction.Audit.Where(item => item.Action == "apply")
                    .ToArray();
            AuditEvent[] restores =
                transaction.Audit.Where(item => item.Action == "restore")
                    .ToArray();
            return transaction.State == FrameTransactionState.Restored &&
                applies.Length == 9 &&
                restores.Length == 9 &&
                applies.Select(Key).Reverse().SequenceEqual(
                    restores.Select(Key),
                    StringComparer.Ordinal) &&
                TreeHasOriginalValues(tree);
        });
        Add(scenarios, "apply-generation-drift-blocked", () =>
        {
            VisualTreeFixture tree = CreateTree();
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            if (!Prepare(transaction))
            {
                return false;
            }

            tree.Generation = "drifted";
            return !transaction.TryApply(
                    new FaultProfile(),
                    out string error) &&
                error == "visual-tree-generation-drift" &&
                transaction.State == FrameTransactionState.Blocked &&
                transaction.Audit.Count == 0;
        });
        Add(scenarios, "partial-apply-auto-restores", () =>
        {
            VisualTreeFixture tree = CreateTree();
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            return Prepare(transaction) &&
                !transaction.TryApply(
                    new FaultProfile(FailApplyAtIndex: 4),
                    out string error) &&
                error == "apply-fault-simulated" &&
                transaction.State == FrameTransactionState.Restored &&
                transaction.Audit.Count(item => item.Action == "apply") == 4 &&
                transaction.Audit.Count(item => item.Action == "restore") == 4 &&
                TreeHasOriginalValues(tree);
        });
        Add(scenarios, "partial-restore-never-claims-restored", () =>
        {
            VisualTreeFixture tree = CreateTree();
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            return Prepare(transaction) &&
                !transaction.TryApply(
                    new FaultProfile(
                        FailApplyAtIndex: 4,
                        FailRestoreAtIndex: 1),
                    out string error) &&
                error.Contains(
                    "restore-fault-simulated",
                    StringComparison.Ordinal) &&
                transaction.State == FrameTransactionState.RestoreRequired;
        });
        Add(scenarios, "restore-generation-drift-stays-required", () =>
        {
            VisualTreeFixture tree = CreateTree();
            PropertyTransaction transaction = CreateTransaction(tree: tree);
            if (!Prepare(transaction) ||
                !transaction.TryApply(new FaultProfile(), out _))
            {
                return false;
            }

            tree.Generation = "drifted";
            return !transaction.TryRestore(
                    new FaultProfile(),
                    out string error) &&
                error == "visual-tree-generation-drift" &&
                transaction.State == FrameTransactionState.RestoreRequired;
        });
        Add(scenarios, "duplicate-apply-rejected", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            if (!Prepare(transaction) ||
                !transaction.TryApply(new FaultProfile(), out _))
            {
                return false;
            }

            int auditCount = transaction.Audit.Count;
            return !transaction.TryApply(
                    new FaultProfile(),
                    out string error) &&
                error == "apply-state-invalid" &&
                transaction.State == FrameTransactionState.Applied &&
                transaction.Audit.Count == auditCount;
        });
        Add(scenarios, "duplicate-restore-idempotent", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            if (!Prepare(transaction) ||
                !transaction.TryApply(new FaultProfile(), out _) ||
                !transaction.TryRestore(new FaultProfile(), out _))
            {
                return false;
            }

            int auditCount = transaction.Audit.Count;
            return transaction.TryRestore(new FaultProfile(), out _) &&
                transaction.State == FrameTransactionState.Restored &&
                transaction.Audit.Count == auditCount;
        });
        Add(scenarios, "mutation-order-deterministic", () =>
        {
            PropertyTransaction transaction = CreateTransaction();
            StyleIntent[] reversed = CreateIntents().Reverse().ToArray();
            if (!transaction.TryDiscover(out _) ||
                !transaction.TryPrepare(reversed, out _))
            {
                return false;
            }

            string[] expected =
            [
                "tab-strip:Background",
                "tab-strip:Foreground",
                "tab-strip:BorderBrush",
                "command-bar:Background",
                "command-bar:Foreground",
                "command-bar:BorderBrush",
                "navigation-pane:Background",
                "navigation-pane:Foreground",
                "navigation-pane:BorderBrush",
            ];
            return transaction.Snapshots
                .Select(
                    snapshot =>
                        $"{snapshot.Role}:{snapshot.Property}")
                .SequenceEqual(expected, StringComparer.Ordinal);
        });

        int passedCount = scenarios.Count(item => item.Passed);
        return new ModelTestReceipt(
            SchemaVersion: 1,
            ReceiptType: "jarvisv2-explorer-frame-offline-model-test",
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

    private static bool Prepare(PropertyTransaction transaction)
    {
        return transaction.TryDiscover(out _) &&
            transaction.TryPrepare(CreateIntents(), out _);
    }

    private static string Key(AuditEvent item)
    {
        return $"{item.Role}:{item.NodeId}:{item.Property}";
    }

    private static TargetIdentity CreateTarget()
    {
        return new TargetIdentity(
            ProcessId: 4242,
            DesktopShellProcessId: 1111,
            ThreadId: 5151,
            WindowHandle: "0x0000000000012345",
            WindowClass: "CabinetWClass",
            WindowTitle: "C:\\",
            ExpectedWindowTitle: "C:\\",
            SeparateProcess: true,
            ProcessStartTimeUtc:
                new DateTime(2026, 7, 27, 5, 0, 0, DateTimeKind.Utc),
            VisualTreeGeneration: Generation);
    }

    private static PropertyTransaction CreateTransaction(
        TargetIdentity? target = null,
        VisualTreeFixture? tree = null,
        IReadOnlyList<SelectorSpec>? selectors = null)
    {
        return new PropertyTransaction(
            target ?? CreateTarget(),
            tree ?? CreateTree(),
            selectors ?? CreateSelectors());
    }

    private static VisualTreeFixture CreateTree(
        bool includeDuplicateTab = false)
    {
        List<VisualNode> nodes =
        [
            new(
                "root",
                null,
                "OfflineFixture.ExplorerFrameRoot",
                "ExplorerFrameFixture",
                "fixture-root",
                Properties("#FF202020", "#FFF0F0F0", "#FF404040")),
            new(
                "tab-strip",
                "root",
                "OfflineFixture.TabStripPresenter",
                "TabStripFixture",
                SurfaceRoles.TabStrip,
                Properties("#FF111111", "#FFF1F1F1", "#FF333333")),
            new(
                "command-bar",
                "root",
                "OfflineFixture.CommandBarPresenter",
                "CommandBarFixture",
                SurfaceRoles.CommandBar,
                Properties("#FF181818", "#FFE8E8E8", "#FF3C3C3C")),
            new(
                "navigation-pane",
                "root",
                "OfflineFixture.NavigationPanePresenter",
                "NavigationPaneFixture",
                SurfaceRoles.NavigationPane,
                Properties("#FF151515", "#FFECECEC", "#FF383838")),
        ];

        if (includeDuplicateTab)
        {
            nodes.Add(
                new VisualNode(
                    "tab-strip-duplicate",
                    "root",
                    "OfflineFixture.TabStripPresenter",
                    "TabStripFixture",
                    SurfaceRoles.TabStrip,
                    Properties("#FF111111", "#FFF1F1F1", "#FF333333")));
        }

        return new VisualTreeFixture(Generation, nodes);
    }

    private static Dictionary<string, string> Properties(
        string background,
        string foreground,
        string border)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StyleProperties.Background] = background,
            [StyleProperties.Foreground] = foreground,
            [StyleProperties.BorderBrush] = border,
        };
    }

    private static SelectorSpec[] CreateSelectors()
    {
        return
        [
            new(
                SurfaceRoles.TabStrip,
                "OfflineFixture.TabStripPresenter",
                "TabStripFixture",
                "OfflineFixture.ExplorerFrameRoot",
                1,
                SelectorEngine.OfflineCandidateOrigin),
            new(
                SurfaceRoles.CommandBar,
                "OfflineFixture.CommandBarPresenter",
                "CommandBarFixture",
                "OfflineFixture.ExplorerFrameRoot",
                1,
                SelectorEngine.OfflineCandidateOrigin),
            new(
                SurfaceRoles.NavigationPane,
                "OfflineFixture.NavigationPanePresenter",
                "NavigationPaneFixture",
                "OfflineFixture.ExplorerFrameRoot",
                1,
                SelectorEngine.OfflineCandidateOrigin),
        ];
    }

    private static StyleIntent[] CreateIntents()
    {
        return SurfaceRoles.RequiredRoles
            .SelectMany(
                role => new[]
                {
                    new StyleIntent(
                        role,
                        StyleProperties.Background,
                        "#FF0B1118"),
                    new StyleIntent(
                        role,
                        StyleProperties.Foreground,
                        "#FFFFB547"),
                    new StyleIntent(
                        role,
                        StyleProperties.BorderBrush,
                        "#FF2E465A"),
                })
            .ToArray();
    }

    private static bool TreeHasOriginalValues(VisualTreeFixture tree)
    {
        return
            tree.GetRequiredNode("tab-strip")
                .Properties[StyleProperties.Background] == "#FF111111" &&
            tree.GetRequiredNode("command-bar")
                .Properties[StyleProperties.Foreground] == "#FFE8E8E8" &&
            tree.GetRequiredNode("navigation-pane")
                .Properties[StyleProperties.BorderBrush] == "#FF383838";
    }
}
