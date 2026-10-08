using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record OwnedWindowLayoutScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record OwnedWindowLayoutTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool OwnProcessOnly,
    bool ThirdPartyWindowControlSupported,
    bool ShellMutationSupported,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<OwnedWindowLayoutScenario> Scenarios);

internal static class OwnedWindowLayoutScenarios
{
    private static readonly Rect WorkArea = new(148, 24, 1428, 752);
    private static readonly OwnedWindowId Alpha = new("explorer");
    private static readonly OwnedWindowId Beta = new("task-manager");
    private static readonly OwnedWindowId Gamma = new("owned-gamma");
    private static readonly OwnedWindowId Delta = new("owned-delta");

    public static OwnedWindowLayoutTestReceipt Run()
    {
        List<OwnedWindowLayoutScenario> scenarios = [];
        Add(scenarios, "availability-counts-are-1-7-15-16", AvailabilityCounts);
        Add(
            scenarios,
            "insufficient-three-and-four-pane-layouts-are-transactional-no-ops",
            InsufficientLayoutsAreNoOps);
        Add(
            scenarios,
            "all-sixteen-layouts-place-active-window-in-primary-pane",
            ActiveWindowOwnsPrimaryPane);
        Add(
            scenarios,
            "remaining-pane-assignment-follows-mru-then-registration-order",
            RemainingPanesFollowMru);
        Add(
            scenarios,
            "overfull-layout-leaves-extra-window-bounds-and-lifecycle-unchanged",
            OverfullLayoutPreservesExtras);
        Add(
            scenarios,
            "internal-gaps-are-twelve-dip-pixel-rounded-and-in-bounds",
            GapAndBounds);
        Add(
            scenarios,
            "minimize-preserves-pane-and-restore-reenables-layout",
            MinimizePreservesPane);
        Add(
            scenarios,
            "close-breaks-layout-and-reopen-restores-floating-bounds",
            CloseBreaksLayout);
        Add(
            scenarios,
            "manual-move-breaks-layout-and-replaces-floating-bounds",
            ManualMoveBreaksLayout);
        Add(
            scenarios,
            "maximize-restores-last-usable-tiled-layout",
            MaximizeRestoresUsableTiledLayout);
        Add(
            scenarios,
            "maximize-restores-freeform-when-last-tiled-layout-is-unavailable",
            MaximizeRestoresFreeformFallback);
        Add(
            scenarios,
            "unknown-window-id-is-rejected-without-state-change",
            UnknownIdIsNoOp);
        Add(
            scenarios,
            "reapplying-the-same-layout-is-idempotent",
            ReapplyIsIdempotent);
        Add(
            scenarios,
            "all-sixteen-exact-cover-catalogs-produce-complete-placement-plans",
            AllCatalogsProduceCompletePlans);
        Add(
            scenarios,
            "equal-area-primary-pane-is-stable-y-x-reading-order",
            EqualAreaPrimaryUsesReadingOrder);
        Add(
            scenarios,
            "invalid-work-area-is-a-transactional-no-op",
            InvalidWorkAreaIsNoOp);
        Add(scenarios, "hosted-starts-freeform-with-one-available-layout", HostedInitialState);
        Add(scenarios, "hosted-open-enables-exactly-seven-layouts", HostedAvailability);
        Add(scenarios, "hosted-six-splits-place-both-active-window-choices", HostedSplitBounds);
        Add(scenarios, "hosted-compact-layouts-retain-content-and-controls", HostedCompactContent);
        Add(scenarios, "hosted-disabled-layouts-preserve-bounds-and-selection", HostedDisabledNoOp);
        Add(scenarios, "hosted-minimize-restore-and-maximize-retain-pane", HostedLifecycle);
        Add(scenarios, "hosted-close-and-reopen-return-to-floating-bounds", HostedCloseReopen);
        Add(scenarios, "hosted-body-focus-and-manual-move-update-active-freeform-state", HostedFocusAndMove);
        Add(scenarios, "hosted-title-click-and-jitter-preserve-maximized-floating-restore", HostedTitleClickPreservesFloatingRestore);
        Add(scenarios, "hosted-title-clicks-preserve-tiled-layout-and-maximize-restore", HostedTitleClickPreservesTiledLayout);
        Add(scenarios, "hosted-title-drag-and-capture-loss-commit-only-real-moves", HostedTitleDragCommitsRealMove);

        int passed = scenarios.Count(scenario => scenario.Passed);
        return new(
            1,
            "jarvisv2-win10-owned-window-layout-test",
            passed == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passed,
            true,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static bool AvailabilityCounts()
    {
        int[] expected = [1, 7, 15, 16];
        for (int visibleCount = 1; visibleCount <= 4; visibleCount++)
        {
            OwnedWindowLayoutCoordinator coordinator =
                CreateCoordinator(visibleCount);
            int enabled = LayoutCatalog.All.Count(
                definition =>
                    coordinator.GetAvailability(definition.Preset).IsEnabled);
            if (enabled != expected[visibleCount - 1])
            {
                return false;
            }
        }

        return true;
    }

    private static bool InsufficientLayoutsAreNoOps()
    {
        foreach (int visibleCount in new[] { 1, 2 })
        {
            OwnedWindowLayoutCoordinator coordinator =
                CreateCoordinator(visibleCount);
            string before = Fingerprint(coordinator.CaptureSnapshot());
            foreach (LayoutDefinition definition in LayoutCatalog.All.Where(
                         definition => definition.PaneCount is 3 or 4))
            {
                OwnedLayoutApplyResult result = coordinator.TryApply(
                    definition.Preset,
                    WorkArea);
                if (result.Status !=
                        OwnedLayoutApplyStatus.InsufficientVisibleWindows ||
                    result.Placements.Count != 0 ||
                    Fingerprint(coordinator.CaptureSnapshot()) != before)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ActiveWindowOwnsPrimaryPane()
    {
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
            if (!coordinator.TryActivate(Beta))
            {
                return false;
            }

            OwnedLayoutApplyResult result = coordinator.TryApply(
                definition.Preset,
                WorkArea);
            OwnedWindowPlacement? active = result.Placements.SingleOrDefault(
                placement => placement.WindowId == Beta);
            int expectedPane =
                OwnedWindowLayoutCoordinator.PrimaryPaneIndex(definition);
            if (result.Status != OwnedLayoutApplyStatus.Applied ||
                active is null ||
                active.PaneIndex != expectedPane ||
                !active.IsActive ||
                result.ActiveWindowId != Beta)
            {
                return false;
            }
        }

        return true;
    }

    private static bool RemainingPanesFollowMru()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
        _ = coordinator.TryActivate(Gamma);
        _ = coordinator.TryActivate(Alpha);
        OwnedLayoutApplyResult result = coordinator.TryApply(
            LayoutPreset.LeftMainRightStack,
            WorkArea);
        IReadOnlyList<OwnedWindowId> participants =
            result.Placements
                .OrderByDescending(placement => placement.ZRank)
                .Select(placement => placement.WindowId)
                .ToArray();
        return
            result.Status == OwnedLayoutApplyStatus.Applied &&
            participants.SequenceEqual([Alpha, Gamma, Delta]) &&
            result.Placements.Single(placement => placement.WindowId == Alpha)
                .PaneIndex == 0 &&
            result.Placements.Single(placement => placement.WindowId == Gamma)
                .PaneIndex == 1 &&
            result.Placements.Single(placement => placement.WindowId == Delta)
                .PaneIndex == 2;
    }

    private static bool OverfullLayoutPreservesExtras()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
        _ = coordinator.TryActivate(Alpha);
        _ = coordinator.TryActivate(Beta);
        OwnedWindowState gammaBefore = State(coordinator, Gamma);
        OwnedWindowState deltaBefore = State(coordinator, Delta);
        OwnedLayoutApplyResult result = coordinator.TryApply(
            LayoutPreset.WideLeftNarrowRight,
            WorkArea);
        OwnedWindowState gammaAfter = State(coordinator, Gamma);
        OwnedWindowState deltaAfter = State(coordinator, Delta);
        return
            result.Status == OwnedLayoutApplyStatus.Applied &&
            result.Placements.Select(placement => placement.WindowId)
                .SequenceEqual([Beta, Alpha]) &&
            gammaAfter.Bounds == gammaBefore.Bounds &&
            gammaAfter.Lifecycle == gammaBefore.Lifecycle &&
            gammaAfter.AssignedPaneIndex is null &&
            deltaAfter.Bounds == deltaBefore.Bounds &&
            deltaAfter.Lifecycle == deltaBefore.Lifecycle &&
            deltaAfter.AssignedPaneIndex is null;
    }

    private static bool GapAndBounds()
    {
        Rect fractionalWorkArea = new(148.2, 24.7, 1428.6, 752.2);
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            IReadOnlyList<Rect> scaled =
                OwnedWindowLayoutCoordinator.ScaleWithInternalGap(
                    definition.Preset,
                    fractionalWorkArea);
            if (scaled.Count != definition.PaneCount ||
                scaled.Any(
                    bounds =>
                        bounds.Left < fractionalWorkArea.Left ||
                        bounds.Top < fractionalWorkArea.Top ||
                        bounds.Right > fractionalWorkArea.Right ||
                        bounds.Bottom > fractionalWorkArea.Bottom ||
                        !IsInteger(bounds.Left) ||
                        !IsInteger(bounds.Top) ||
                        !IsInteger(bounds.Right) ||
                        !IsInteger(bounds.Bottom)))
            {
                return false;
            }

            for (int first = 0; first < definition.PaneCount; first++)
            {
                for (int second = first + 1;
                     second < definition.PaneCount;
                     second++)
                {
                    LayoutPane a = definition.Panes[first];
                    LayoutPane b = definition.Panes[second];
                    if (a.Right == b.X && Overlaps(a.Y, a.Bottom, b.Y, b.Bottom) &&
                        scaled[second].Left - scaled[first].Right !=
                            OwnedWindowLayoutCoordinator.InternalGap)
                    {
                        return false;
                    }

                    if (b.Right == a.X && Overlaps(a.Y, a.Bottom, b.Y, b.Bottom) &&
                        scaled[first].Left - scaled[second].Right !=
                            OwnedWindowLayoutCoordinator.InternalGap)
                    {
                        return false;
                    }

                    if (a.Bottom == b.Y && Overlaps(a.X, a.Right, b.X, b.Right) &&
                        scaled[second].Top - scaled[first].Bottom !=
                            OwnedWindowLayoutCoordinator.InternalGap)
                    {
                        return false;
                    }

                    if (b.Bottom == a.Y && Overlaps(a.X, a.Right, b.X, b.Right) &&
                        scaled[first].Top - scaled[second].Bottom !=
                            OwnedWindowLayoutCoordinator.InternalGap)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool MinimizePreservesPane()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        _ = coordinator.TryActivate(Alpha);
        OwnedLayoutApplyResult applied = coordinator.TryApply(
            LayoutPreset.EqualColumns,
            WorkArea);
        OwnedWindowState before = State(coordinator, Beta);
        bool minimized = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Minimized);
        OwnedWindowState whileMinimized = State(coordinator, Beta);
        bool unavailable = !coordinator
            .GetAvailability(LayoutPreset.EqualColumns)
            .IsEnabled;
        bool restored = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Visible);
        OwnedWindowState after = State(coordinator, Beta);
        return
            applied.Status == OwnedLayoutApplyStatus.Applied &&
            minimized &&
            restored &&
            unavailable &&
            whileMinimized.AssignedPaneIndex == before.AssignedPaneIndex &&
            whileMinimized.Bounds == before.Bounds &&
            after.AssignedPaneIndex == before.AssignedPaneIndex &&
            after.Bounds == before.Bounds &&
            coordinator.GetAvailability(LayoutPreset.EqualColumns).IsEnabled &&
            coordinator.AppliedPreset == LayoutPreset.EqualColumns &&
            coordinator.ActiveWindowId == Beta;
    }

    private static bool CloseBreaksLayout()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        OwnedWindowState floating = State(coordinator, Beta);
        _ = coordinator.TryApply(LayoutPreset.EqualColumns, WorkArea);
        bool closed = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Closed);
        bool broken =
            coordinator.AppliedPreset is null &&
            coordinator.CaptureSnapshot().Windows.All(
                window => window.AssignedPaneIndex is null);
        bool reopened = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Visible);
        OwnedWindowState after = State(coordinator, Beta);
        return
            closed &&
            broken &&
            reopened &&
            after.Bounds == floating.FloatingBounds &&
            after.FloatingBounds == floating.FloatingBounds &&
            after.AssignedPaneIndex is null &&
            coordinator.ActiveWindowId == Beta;
    }

    private static bool ManualMoveBreaksLayout()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        _ = coordinator.TryApply(LayoutPreset.EqualRows, WorkArea);
        Rect manual = new(713, 81, 706, 531);
        bool moved = coordinator.TrySetManualBounds(Beta, manual);
        OwnedWindowState state = State(coordinator, Beta);
        return
            moved &&
            coordinator.AppliedPreset is null &&
            coordinator.ActiveWindowId == Beta &&
            state.Bounds == manual &&
            state.FloatingBounds == manual &&
            coordinator.CaptureSnapshot().Windows.All(
                window => window.AssignedPaneIndex is null);
    }

    private static bool MaximizeRestoresUsableTiledLayout()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        _ = coordinator.TryActivate(Alpha);
        _ = coordinator.TryApply(
            LayoutPreset.WideLeftNarrowRight,
            WorkArea);
        OwnedLayoutApplyResult maximized = coordinator.TryToggleMaximize(
            Alpha,
            WorkArea);
        OwnedLayoutApplyResult restored = coordinator.TryToggleMaximize(
            Alpha,
            WorkArea);
        return
            maximized.Status == OwnedLayoutApplyStatus.Applied &&
            maximized.AppliedPreset == LayoutPreset.Maximized &&
            restored.Status == OwnedLayoutApplyStatus.Applied &&
            restored.AppliedPreset == LayoutPreset.WideLeftNarrowRight &&
            coordinator.AppliedPreset == LayoutPreset.WideLeftNarrowRight &&
            State(coordinator, Alpha).AssignedPaneIndex == 0;
    }

    private static bool MaximizeRestoresFreeformFallback()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(3);
        OwnedWindowState original = State(coordinator, Alpha);
        _ = coordinator.TryActivate(Alpha);
        _ = coordinator.TryApply(LayoutPreset.ThreeColumns, WorkArea);
        _ = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Minimized);
        _ = coordinator.TrySetLifecycle(
            Gamma,
            OwnedWindowLifecycle.Minimized);
        OwnedLayoutApplyResult maximized = coordinator.TryToggleMaximize(
            Alpha,
            WorkArea);
        OwnedLayoutApplyResult restored = coordinator.TryToggleMaximize(
            Alpha,
            WorkArea);
        OwnedWindowState final = State(coordinator, Alpha);
        return
            maximized.Status == OwnedLayoutApplyStatus.Applied &&
            restored.Status == OwnedLayoutApplyStatus.RestoredFreeform &&
            restored.AppliedPreset is null &&
            coordinator.AppliedPreset is null &&
            final.Bounds == original.FloatingBounds &&
            final.AssignedPaneIndex is null;
    }

    private static bool UnknownIdIsNoOp()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        OwnedWindowId unknown = new("third-party-or-unregistered");
        string before = Fingerprint(coordinator.CaptureSnapshot());
        bool activated = coordinator.TryActivate(unknown);
        bool lifecycle = coordinator.TrySetLifecycle(
            unknown,
            OwnedWindowLifecycle.Visible);
        bool moved = coordinator.TrySetManualBounds(
            unknown,
            new Rect(1, 1, 200, 200));
        OwnedLayoutApplyResult maximized = coordinator.TryToggleMaximize(
            unknown,
            WorkArea);
        return
            !activated &&
            !lifecycle &&
            !moved &&
            maximized.Status == OwnedLayoutApplyStatus.UnknownOwnedWindow &&
            Fingerprint(coordinator.CaptureSnapshot()) == before;
    }

    private static bool ReapplyIsIdempotent()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
        _ = coordinator.TryActivate(Beta);
        OwnedLayoutApplyResult first = coordinator.TryApply(
            LayoutPreset.CenterMainColumns,
            WorkArea);
        string afterFirst = Fingerprint(coordinator.CaptureSnapshot());
        OwnedLayoutApplyResult second = coordinator.TryApply(
            LayoutPreset.CenterMainColumns,
            WorkArea);
        return
            first.Status == OwnedLayoutApplyStatus.Applied &&
            second.Status == OwnedLayoutApplyStatus.Applied &&
            afterFirst == Fingerprint(coordinator.CaptureSnapshot()) &&
            first.Placements.SequenceEqual(second.Placements);
    }

    private static bool AllCatalogsProduceCompletePlans()
    {
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
            OwnedLayoutApplyResult result = coordinator.TryApply(
                definition.Preset,
                WorkArea);
            if (!LayoutCatalog.IsExactCover(definition) ||
                result.Status != OwnedLayoutApplyStatus.Applied ||
                result.AppliedPreset != definition.Preset ||
                result.Placements.Count != definition.PaneCount ||
                result.Placements.Select(placement => placement.WindowId)
                    .Distinct()
                    .Count() != definition.PaneCount ||
                !result.Placements
                    .Select(placement => placement.PaneIndex)
                    .Order()
                    .SequenceEqual(
                        Enumerable.Range(0, definition.PaneCount)
                            .Select(index => (int?)index)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EqualAreaPrimaryUsesReadingOrder()
    {
        foreach (LayoutDefinition definition in LayoutCatalog.All.Where(
                     definition =>
                         definition.Panes
                             .Select(pane => pane.Width * pane.Height)
                             .Distinct()
                             .Count() == 1))
        {
            int expected = definition.Panes
                .Select((pane, index) => new { Pane = pane, Index = index })
                .OrderBy(candidate => candidate.Pane.Y)
                .ThenBy(candidate => candidate.Pane.X)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Index;
            if (OwnedWindowLayoutCoordinator.PrimaryPaneIndex(definition) !=
                expected)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InvalidWorkAreaIsNoOp()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        _ = coordinator.TryApply(LayoutPreset.EqualColumns, WorkArea);
        string before = Fingerprint(coordinator.CaptureSnapshot());
        OwnedLayoutApplyResult empty = coordinator.TryApply(
            LayoutPreset.EqualColumns,
            Rect.Empty);
        OwnedLayoutApplyResult tooSmall = coordinator.TryApply(
            LayoutPreset.EqualColumns,
            new Rect(0, 0, 1, 1));
        return
            empty.Status == OwnedLayoutApplyStatus.InvalidWorkArea &&
            empty.AppliedPreset == LayoutPreset.EqualColumns &&
            tooSmall.Status == OwnedLayoutApplyStatus.InvalidWorkArea &&
            tooSmall.AppliedPreset == LayoutPreset.EqualColumns &&
            Fingerprint(coordinator.CaptureSnapshot()) == before;
    }

    private static OwnedWindowLayoutCoordinator CreateCoordinator(
        int visibleCount)
    {
        OwnedWindowId[] ids = [Alpha, Beta, Gamma, Delta];
        Rect[] bounds =
        [
            new(596, 63, 930, 667),
            new(720, 104, 760, 600),
            new(322, 92, 640, 520),
            new(410, 140, 680, 500),
        ];
        OwnedWindowLayoutCoordinator coordinator = new();
        for (int index = 0; index < ids.Length; index++)
        {
            OwnedWindowLifecycle lifecycle = index < visibleCount
                ? OwnedWindowLifecycle.Visible
                : OwnedWindowLifecycle.Closed;
            if (!coordinator.TryRegister(ids[index], lifecycle, bounds[index]))
            {
                throw new InvalidOperationException(
                    $"Could not register test window {ids[index]}.");
            }
        }

        return coordinator;
    }

    private static bool HostedInitialState() => WithShell(shell =>
        shell.IsOwnedWindowLayoutFreeform && shell.CurrentLayoutGlyph.IsFreeform &&
        shell.VisibleOwnedWindowCount == 1 && shell.AvailableLayoutCount == 1 &&
        shell.LayoutRailList.SelectedItems.Count == 0 &&
        shell.ActiveOwnedWindowId == "explorer");

    private static bool HostedAvailability() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        shell.UpdateLayout();
        return shell.VisibleOwnedWindowCount == 2 && shell.AvailableLayoutCount == 7 &&
            shell.IsOwnedWindowLayoutFreeform && LayoutCatalog.All.All(definition =>
                shell.IsLayoutAvailableForTest(definition.Preset) == (definition.PaneCount <= 2));
    });

    private static bool HostedSplitBounds() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string active in new[] { "explorer", "task-manager" })
        {
            ActivateBody(shell, active);
            foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount == 2))
            {
                if (!shell.SelectLayoutForTest(definition.Preset))
                {
                    return false;
                }

                shell.UpdateLayout();
                IReadOnlyList<Rect> expected = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(definition.Preset, WorkArea);
                int primary = OwnedWindowLayoutCoordinator.PrimaryPaneIndex(definition);
                string other = active == "explorer" ? "task-manager" : "explorer";
                Rect first = shell.GetOwnedWindowBoundsForTest(active);
                Rect second = shell.GetOwnedWindowBoundsForTest(other);
                if (first != expected[primary] || second != expected[1 - primary] ||
                    first.IntersectsWith(second) || !WorkArea.Contains(first) || !WorkArea.Contains(second) ||
                    shell.CurrentLayout != definition.Preset || shell.CurrentLayoutGlyph.IsFreeform)
                {
                    return false;
                }
            }
        }

        return true;
    });

    private static bool HostedCompactContent() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string active in new[] { "explorer", "task-manager" })
        {
            ActivateBody(shell, active);
            foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount == 2))
            {
                _ = shell.SelectLayoutForTest(definition.Preset);
                shell.UpdateLayout();
                Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
                Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
                if (shell.ExplorerItemList.ActualHeight < 44 ||
                    shell.TaskManagerPanel.ProcessViewportHeightForTest < 43 ||
                    shell.IsExplorerCompactWidthForTest != (explorer.Width < 820) ||
                    shell.IsExplorerCompactHeightForTest != (explorer.Height < 400) ||
                    shell.TaskManagerPanel.IsCompactWidthForTest != (task.Width < 820) ||
                    shell.TaskManagerPanel.IsCompactHeightForTest != (task.Height < 400) ||
                    shell.ExplorerItemList.Items.Count == 0 || shell.TaskManagerPanel.ProcessRows.Count != 8 ||
                    shell.ExplorerCloseButton.ActualWidth < 28 || shell.TaskManagerCloseButton.ActualWidth < 28)
                {
                    throw new InvalidOperationException($"{active}/{definition.Preset}: explorer={shell.ExplorerItemList.ActualHeight:F1}, task={shell.TaskManagerPanel.ProcessViewportHeightForTest:F1}");
                }

                bool compactExplorer = explorer.Width < 820 || explorer.Height < 400;
                bool compactTask = task.Width < 820 || task.Height < 400;
                Rect titleBounds = shell.TaskManagerTitleBar.TransformToAncestor(shell)
                    .TransformBounds(new Rect(shell.TaskManagerTitleBar.RenderSize));
                if (!task.Contains(titleBounds) || !shell.TaskManagerPanel.ClipToBounds)
                {
                    throw new InvalidOperationException($"{definition.Preset}: title={titleBounds}, task={task}");
                }
                if ((shell.ExplorerFolderPanel.Visibility == Visibility.Collapsed) != compactExplorer ||
                    (shell.ExplorerStoragePanel.Visibility == Visibility.Collapsed) != compactExplorer ||
                    (shell.TaskManagerPanel.SummaryPanel.Visibility == Visibility.Collapsed) != compactTask ||
                    (shell.TaskManagerPanel.CompactSamplingStateText.Visibility == Visibility.Visible) != compactTask)
                {
                    return false;
                }
            }
        }

        return true;
    });

    private static bool HostedDisabledNoOp() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
        Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
        foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount > 2))
        {
            ListBoxItem item = (ListBoxItem)shell.LayoutRailList.ItemContainerGenerator.ContainerFromItem(definition);
            if (item.IsEnabled || !AutomationProperties.GetHelpText(item).Contains("REQUIRES", StringComparison.Ordinal) ||
                shell.SelectLayoutForTest(definition.Preset) || shell.CurrentLayout != LayoutPreset.EqualColumns ||
                shell.GetOwnedWindowBoundsForTest("explorer") != explorer ||
                shell.GetOwnedWindowBoundsForTest("task-manager") != task)
            {
                return false;
            }
        }

        return true;
    });

    private static bool HostedLifecycle() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        ActivateBody(shell, "explorer");
        _ = shell.SelectLayoutForTest(LayoutPreset.NarrowLeftWideRight);
        Rect tiled = shell.GetOwnedWindowBoundsForTest("explorer");
        Invoke(shell.ExplorerMinimizeButton);
        bool minimized = shell.VisibleOwnedWindowCount == 1 && shell.AvailableLayoutCount == 1 &&
            shell.CurrentLayout == LayoutPreset.NarrowLeftWideRight;
        Invoke(shell.TaskbarExplorerButton);
        bool restored = shell.GetOwnedWindowBoundsForTest("explorer") == tiled && shell.AvailableLayoutCount == 7;
        Invoke(shell.ExplorerMaximizeButton);
        bool maximized = shell.IsExplorerMaximized && shell.GetOwnedWindowBoundsForTest("explorer") == WorkArea;
        Invoke(shell.ExplorerMaximizeButton);
        return minimized && restored && maximized && !shell.IsExplorerMaximized &&
            shell.GetOwnedWindowBoundsForTest("explorer") == tiled &&
            AutomationProperties.GetName(shell.ExplorerMaximizeButton) == "Maximize Explorer";
    });

    private static bool HostedCloseReopen() => WithShell(shell =>
    {
        Rect floating = shell.GetOwnedWindowBoundsForTest("explorer");
        _ = shell.PrepareTaskManagerForSnapshot();
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualRows);
        Invoke(shell.ExplorerCloseButton);
        bool closed = shell.IsOwnedWindowLayoutFreeform && shell.VisibleOwnedWindowCount == 1;
        Invoke(shell.TaskbarExplorerButton);
        return closed && shell.IsOwnedWindowLayoutFreeform &&
            shell.GetOwnedWindowBoundsForTest("explorer") == floating && shell.VisibleOwnedWindowCount == 2;
    });

    private static bool HostedFocusAndMove() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        ActivateBody(shell, "explorer");
        bool explorerActive = shell.ActiveOwnedWindowId == "explorer" &&
            Panel.GetZIndex(shell.ExplorerWindow) > Panel.GetZIndex(shell.TaskManagerWindow);
        ActivateBody(shell, "task-manager");
        bool taskActive = shell.ActiveOwnedWindowId == "task-manager" &&
            Panel.GetZIndex(shell.TaskManagerWindow) > Panel.GetZIndex(shell.ExplorerWindow);
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Rect moved = new(420, 90, 700, 600);
        shell.MoveOwnedWindowForTest("task-manager", moved);
        return explorerActive && taskActive && shell.IsOwnedWindowLayoutFreeform &&
            shell.CurrentLayoutGlyph.IsFreeform && shell.LayoutRailList.SelectedItems.Count == 0 &&
            shell.GetOwnedWindowBoundsForTest("task-manager") == moved;
    });

    private static bool HostedTitleClickPreservesFloatingRestore() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string id in new[] { "explorer", "task-manager" })
        {
            Rect floating = shell.GetOwnedWindowBoundsForTest(id);
            Button maximize = id == "explorer" ? shell.ExplorerMaximizeButton : shell.TaskManagerMaximizeButton;
            Invoke(maximize);
            Point press = new(WorkArea.Left + 40, WorkArea.Top + 20);
            shell.RaiseOwnedTitleGestureForTest(id, press);
            shell.RaiseOwnedTitleGestureForTest(id, press, new Point(
                press.X + SystemParameters.MinimumHorizontalDragDistance / 2,
                press.Y + SystemParameters.MinimumVerticalDragDistance / 2));
            shell.RaiseOwnedTitleGestureForTest(id, press, loseCapture: true);
            bool maximized = id == "explorer" ? shell.IsExplorerMaximized : shell.IsTaskManagerMaximizedForTest;
            if (!maximized || shell.GetOwnedWindowBoundsForTest(id) != WorkArea)
            {
                return false;
            }

            Invoke(maximize);
            if (shell.GetOwnedWindowBoundsForTest(id) != floating)
            {
                return false;
            }
        }

        return true;
    });

    private static bool HostedTitleClickPreservesTiledLayout() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string id in new[] { "explorer", "task-manager" })
        {
            ActivateBody(shell, id);
            _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
            Rect tiled = shell.GetOwnedWindowBoundsForTest(id);
            Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
            Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
            Point press = new(tiled.Left + 40, tiled.Top + 20);
            shell.RaiseOwnedTitleGestureForTest(id, press);
            shell.RaiseOwnedTitleGestureForTest(id, press);
            shell.RaiseOwnedTitleGestureForTest(id, press, new Point(
                press.X + SystemParameters.MinimumHorizontalDragDistance / 2,
                press.Y + SystemParameters.MinimumVerticalDragDistance / 2));
            shell.RaiseOwnedTitleGestureForTest(id, press, loseCapture: true);
            if (shell.IsOwnedWindowLayoutFreeform || shell.CurrentLayout != LayoutPreset.EqualColumns ||
                shell.GetOwnedWindowBoundsForTest("explorer") != explorer ||
                shell.GetOwnedWindowBoundsForTest("task-manager") != task ||
                shell.ActiveOwnedWindowId != id)
            {
                return false;
            }

            Button maximize = id == "explorer" ? shell.ExplorerMaximizeButton : shell.TaskManagerMaximizeButton;
            Invoke(maximize);
            Invoke(maximize);
            if (shell.GetOwnedWindowBoundsForTest(id) != tiled ||
                shell.CurrentLayout != LayoutPreset.EqualColumns || shell.IsOwnedWindowLayoutFreeform)
            {
                return false;
            }
        }

        return true;
    });

    private static bool HostedTitleDragCommitsRealMove() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string id in new[] { "explorer", "task-manager" })
        {
            foreach (bool loseCapture in new[] { false, true })
            {
                ActivateBody(shell, id);
                _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
                Rect tiled = shell.GetOwnedWindowBoundsForTest(id);
                Point press = new(tiled.Left + 40, tiled.Top + 20);
                shell.RaiseOwnedTitleGestureForTest(id, press, new Point(press.X + 64, press.Y + 48), loseCapture);
                Rect moved = shell.GetOwnedWindowBoundsForTest(id);
                if (!shell.IsOwnedWindowLayoutFreeform || !shell.CurrentLayoutGlyph.IsFreeform ||
                    shell.LayoutRailList.SelectedItems.Count != 0 || moved == tiled)
                {
                    throw new InvalidOperationException($"{id}/lost={loseCapture}: tiled={tiled}, moved={moved}, freeform={shell.IsOwnedWindowLayoutFreeform}, selected={shell.LayoutRailList.SelectedItems.Count}");
                }

                // A completed or interrupted drag must not leak into the next click.
                shell.RaiseOwnedTitleGestureForTest(id, new Point(moved.Left + 40, moved.Top + 20));
                // An available tiled preset takes restore precedence. Make it unavailable
                // to verify that the committed floating bounds survive maximize/restore.
                Invoke(id == "explorer" ? shell.TaskManagerMinimizeButton : shell.ExplorerMinimizeButton);
                Button maximize = id == "explorer" ? shell.ExplorerMaximizeButton : shell.TaskManagerMaximizeButton;
                Invoke(maximize);
                if (shell.GetOwnedWindowBoundsForTest(id) != WorkArea)
                {
                    return false;
                }

                Invoke(maximize);
                if (shell.GetOwnedWindowBoundsForTest(id) != moved)
                {
                    throw new InvalidOperationException($"{id}/lost={loseCapture}: drag={moved}, restored={shell.GetOwnedWindowBoundsForTest(id)}");
                }

                Invoke(id == "explorer" ? shell.TaskbarSystemButton : shell.TaskbarExplorerButton);
                _ = shell.PrepareTaskManagerForSnapshot();
            }
        }

        return true;
    });

    private static void ActivateBody(DesktopShellSurface shell, string id)
    {
        UIElement body = id == "explorer" ? shell.ExplorerItemList : shell.TaskManagerPanel.ProcessList;
        body.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, body)
        {
            RoutedEvent = Keyboard.GotKeyboardFocusEvent,
        });
    }

    private static void Invoke(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool WithShell(Func<DesktopShellSurface, bool> execute)
    {
        DesktopShellSurface shell = new();
        Window host = new()
        {
            Width = 1600, Height = 900, Left = -32000, Top = -32000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Black, Content = shell,
        };
        try
        {
            host.Show();
            DispatcherFrame frame = new();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            shell.UpdateLayout();
            shell.PrepareLayoutRailForSnapshot();
            return execute(shell);
        }
        finally
        {
            shell.TaskManagerPanel.StopSampling();
            host.Content = null;
            host.Close();
        }
    }

    private static OwnedWindowState State(
        OwnedWindowLayoutCoordinator coordinator,
        OwnedWindowId id)
    {
        if (!coordinator.TryGetWindow(id, out OwnedWindowState? state) ||
            state is null)
        {
            throw new InvalidOperationException($"Missing test window {id}.");
        }

        return state;
    }

    private static string Fingerprint(OwnedWindowLayoutSnapshot snapshot)
    {
        StringBuilder builder = new();
        _ = builder.Append(snapshot.AppliedPreset?.ToString() ?? "FREEFORM")
            .Append('|')
            .Append(snapshot.LastTiledPreset?.ToString() ?? "NONE")
            .Append('|')
            .Append(snapshot.ActiveWindowId?.ToString() ?? "NONE");
        foreach (OwnedWindowState window in snapshot.Windows)
        {
            _ = builder.Append('|')
                .Append(window.Id)
                .Append(':')
                .Append(window.Lifecycle)
                .Append(':')
                .Append(Rectangle(window.Bounds))
                .Append(':')
                .Append(Rectangle(window.FloatingBounds))
                .Append(':')
                .Append(window.ActivationOrdinal)
                .Append(':')
                .Append(window.RegistrationOrder)
                .Append(':')
                .Append(window.AssignedPaneIndex?.ToString(
                    CultureInfo.InvariantCulture) ?? "NONE");
        }

        return builder.ToString();
    }

    private static string Rectangle(Rect bounds) =>
        string.Join(
            ',',
            bounds.X.ToString("R", CultureInfo.InvariantCulture),
            bounds.Y.ToString("R", CultureInfo.InvariantCulture),
            bounds.Width.ToString("R", CultureInfo.InvariantCulture),
            bounds.Height.ToString("R", CultureInfo.InvariantCulture));

    private static bool IsInteger(double value) => value == Math.Round(value);

    private static bool Overlaps(
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd) =>
        Math.Max(firstStart, secondStart) < Math.Min(firstEnd, secondEnd);

    private static void Add(
        ICollection<OwnedWindowLayoutScenario> scenarios,
        string name,
        Func<bool> execute)
    {
        bool passed;
        string detail;
        try
        {
            passed = execute();
            detail = passed ? "passed" : "predicate-returned-false";
        }
        catch (Exception exception)
        {
            passed = false;
            detail = $"{exception.GetType().Name}: {exception.Message}";
        }

        scenarios.Add(new(name, passed, detail));
    }
}
