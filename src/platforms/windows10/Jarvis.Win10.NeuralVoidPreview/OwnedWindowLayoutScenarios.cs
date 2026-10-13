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
        Add(
            scenarios,
            "all-presets-selectable-with-fill-notes-1-7-15-16-full",
            AvailabilityCounts);
        Add(
            scenarios,
            "partial-fill-places-visible-windows-in-leading-panes",
            PartialFillPlacesLeadingPanes);

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
            "maximize-restores-last-tiled-layout-even-partially-filled",
            MaximizeRestoresLastTiledEvenPartial);
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
        Add(scenarios, "auto-restore-validates-before-mutating", AutoRestoreValidatesBeforeMutating);
        Add(scenarios, "auto-restore-prefers-minimized-mru-window", AutoRestorePrefersMinimizedMru);
        Add(scenarios, "hosted-starts-freeform-with-all-layouts-selectable", HostedInitialState);
        Add(scenarios, "hosted-layout-opens-missing-owned-window", HostedLayoutOpensMissingWindow);
        Add(scenarios, "hosted-rail-describes-planned-window-restore", HostedRailDescribesPlannedRestore);
        Add(scenarios, "hosted-current-layout-reports-occupied-panes", HostedCurrentLayoutReportsOccupiedPanes);
        Add(scenarios, "hosted-layout-restores-minimized-explorer", () => HostedLayoutRestoresMinimizedWindow("explorer"));
        Add(scenarios, "hosted-layout-restores-minimized-task-manager", () => HostedLayoutRestoresMinimizedWindow("task-manager"));
        Add(scenarios, "hosted-layout-recovers-after-all-windows-closed", HostedLayoutRecoversAllClosed);
        Add(scenarios, "hosted-single-pane-keeps-spare-window-closed", HostedSinglePaneKeepsSpareClosed);
        Add(scenarios, "hosted-passive-actions-leave-spare-window-minimized", HostedPassiveActionsKeepSpareMinimized);
        Add(scenarios, "hosted-snapshot-render-host-excludes-pointer-input", HostedSnapshotHostExcludesPointerInput);
        Add(scenarios, "hosted-title-double-click-toggles-maximize-and-restore", HostedTitleDoubleClickToggles);
        Add(scenarios, "hosted-open-keeps-all-sixteen-layouts-selectable", HostedAvailability);
        Add(scenarios, "hosted-six-splits-place-both-active-window-choices", HostedSplitBounds);
        Add(scenarios, "hosted-compact-layouts-retain-content-and-controls", HostedCompactContent);
        Add(scenarios, "hosted-partial-fill-moves-windows-and-keeps-rail-live", HostedPartialFill);
        Add(scenarios, "hosted-minimize-restore-and-maximize-retain-pane", HostedLifecycle);
        Add(scenarios, "hosted-close-and-reopen-return-to-floating-bounds", HostedCloseReopen);
        Add(scenarios, "hosted-body-focus-and-manual-move-update-active-freeform-state", HostedFocusAndMove);
        Add(scenarios, "hosted-title-click-and-jitter-preserve-maximized-floating-restore", HostedTitleClickPreservesFloatingRestore);
        Add(scenarios, "hosted-title-clicks-preserve-tiled-layout-and-maximize-restore", HostedTitleClickPreservesTiledLayout);
        Add(scenarios, "hosted-title-drag-and-capture-loss-commit-only-real-moves", HostedTitleDragCommitsRealMove);
        Add(scenarios, "hosted-arrow-keys-step-every-layout-and-preview-preserves-applied-state", HostedArrowPreviewNavigation);
        Add(scenarios, "hosted-real-populated-three-and-four-pane-explorer-layouts", HostedRealExplorerLayouts);
        Add(scenarios, "hosted-secondary-title-focus-rail-and-mru-routing", HostedSecondaryTitleFocusRouting);
        Add(scenarios, "hosted-secondary-identity-lifecycle-retains-real-content", HostedSecondaryIdentityLifecycle);
        Add(scenarios, "hosted-promoted-active-frame-above-mru-after-rail-restore-and-maximize", HostedPromotedActiveFrameAboveMru);
        Add(scenarios, "hosted-secondary-title-hands-actual-keyboard-mutation-to-owning-content", HostedSecondaryTitleKeyboardHandoff);
        Add(scenarios, "hosted-secondary-minimize-preserves-intervening-search-focus", () => HostedShellHidePreservesNewerFocus("Minimize"));
        Add(scenarios, "hosted-secondary-close-preserves-intervening-search-focus", () => HostedShellHidePreservesNewerFocus("Close"));
        Add(scenarios, "hosted-task-manager-rail-return-preserves-intervening-search-focus", HostedTaskManagerReturnPreservesNewerFocus);
        Add(scenarios, "hosted-current-layout-button-preserves-intervening-search-focus", () => HostedCurrentLayoutPreservesNewerFocus(foreignExplorer: false));
        Add(scenarios, "hosted-current-layout-button-preserves-intervening-foreign-explorer-focus", () => HostedCurrentLayoutPreservesNewerFocus(foreignExplorer: true));
        Add(scenarios, "hosted-current-layout-unload-event-aborts-retained-operation-before-dispatch", HostedCurrentLayoutUnloadAbortsFocus);
        Add(scenarios, "hosted-current-layout-second-click-aborts-superseded-operation-before-dispatch", HostedCurrentLayoutSupersedesFocus);
        Add(scenarios, "hosted-current-layout-button-focuses-selected-item-with-preview-and-escape-intact", HostedCurrentLayoutFocusAndNavigation);
        Add(scenarios, "hosted-escape-returns-focus-to-active-window", HostedEscapeReturnsFocus);

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
            int full = LayoutCatalog.All.Count(definition =>
                coordinator.GetAvailability(definition.Preset).StatusText ==
                    "AVAILABLE");
            bool allSelectable = LayoutCatalog.All.All(definition =>
                coordinator.GetAvailability(definition.Preset).IsEnabled);
            if (full != expected[visibleCount - 1] || !allSelectable)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PartialFillPlacesLeadingPanes()
    {
        foreach (int visibleCount in new[] { 1, 2 })
        {
            OwnedWindowLayoutCoordinator coordinator =
                CreateCoordinator(visibleCount);
            foreach (LayoutDefinition definition in LayoutCatalog.All.Where(
                         definition => definition.PaneCount > visibleCount))
            {
                OwnedLayoutApplyResult result = coordinator.TryApply(
                    definition.Preset,
                    WorkArea);
                IReadOnlyList<Rect> panes =
                    OwnedWindowLayoutCoordinator.ScaleWithInternalGap(
                        definition.Preset,
                        WorkArea);
                int primary = OwnedWindowLayoutCoordinator.PrimaryPaneIndex(
                    definition);
                if (result.Status != OwnedLayoutApplyStatus.Applied ||
                    result.Placements.Count != visibleCount ||
                    !panes.Contains(result.Placements[0].Bounds) ||
                    result.Placements[0].PaneIndex != primary ||
                    !result.Placements[0].IsActive ||
                    coordinator.GetAvailability(definition.Preset).StatusText !=
                        $"FILLS {visibleCount} OF {definition.PaneCount} PANES")
                {
                    return false;
                }

                foreach (OwnedWindowPlacement placement in result.Placements)
                {
                    if (!panes.Contains(placement.Bounds))
                    {
                        return false;
                    }
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
        string whileStatus = coordinator
            .GetAvailability(LayoutPreset.EqualColumns)
            .StatusText;
        OwnedLayoutApplyResult maximized = coordinator.TryToggleMaximize(Alpha, WorkArea);
        OwnedLayoutApplyResult tiledAgain = coordinator.TryToggleMaximize(Alpha, WorkArea);
        bool restored = coordinator.TrySetLifecycle(
            Beta,
            OwnedWindowLifecycle.Visible);
        OwnedWindowState after = State(coordinator, Beta);
        return
            applied.Status == OwnedLayoutApplyStatus.Applied &&
            minimized &&
            restored &&
            whileStatus == "FILLS 1 OF 2 PANES" &&
            maximized.Status == OwnedLayoutApplyStatus.Applied &&
            tiledAgain.AppliedPreset == LayoutPreset.EqualColumns &&
            whileMinimized.AssignedPaneIndex == before.AssignedPaneIndex &&
            whileMinimized.Bounds == before.Bounds &&
            after.AssignedPaneIndex == before.AssignedPaneIndex &&
            after.Bounds == before.Bounds &&
            coordinator.GetAvailability(LayoutPreset.EqualColumns).StatusText ==
                "AVAILABLE" &&
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

    private static bool MaximizeRestoresLastTiledEvenPartial()
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
        IReadOnlyList<Rect> panes =
            OwnedWindowLayoutCoordinator.ScaleWithInternalGap(
                LayoutPreset.ThreeColumns,
                WorkArea);
        int primary = OwnedWindowLayoutCoordinator.PrimaryPaneIndex(
            LayoutCatalog.Get(LayoutPreset.ThreeColumns));
        OwnedWindowState final = State(coordinator, Alpha);
        return
            maximized.Status == OwnedLayoutApplyStatus.Applied &&
            restored.Status == OwnedLayoutApplyStatus.Applied &&
            restored.AppliedPreset == LayoutPreset.ThreeColumns &&
            coordinator.AppliedPreset == LayoutPreset.ThreeColumns &&
            final.Bounds == panes[primary] &&
            final.AssignedPaneIndex == primary &&
            original.FloatingBounds != panes[primary];
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

    private static bool AutoRestoreValidatesBeforeMutating()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(2);
        _ = coordinator.TrySetLifecycle(Beta, OwnedWindowLifecycle.Minimized);
        string before = Fingerprint(coordinator.CaptureSnapshot());
        OwnedLayoutApplyResult unknown = coordinator.TryApply(
            (LayoutPreset)(-1), WorkArea, restoreHiddenWindows: true);
        if (unknown.Status != OwnedLayoutApplyStatus.UnknownLayout ||
            Fingerprint(coordinator.CaptureSnapshot()) != before)
        {
            return false;
        }

        foreach ((LayoutPreset preset, Rect invalid) in new[]
        {
            (LayoutPreset.FourQuadrants, Rect.Empty),
            (LayoutPreset.FourQuadrants, new Rect(0, 0, 1, 1)),
            (LayoutPreset.EqualRows, new Rect(1e308, 24, 1e308, 752)),
            (LayoutPreset.EqualRows, new Rect(0, 24, 5e307, 752)),
        })
        {
            OwnedLayoutApplyResult result = coordinator.TryApply(
                preset, invalid, restoreHiddenWindows: true);
            if (result.Status != OwnedLayoutApplyStatus.InvalidWorkArea ||
                result.Placements.Count != 0 ||
                Fingerprint(coordinator.CaptureSnapshot()) != before)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AutoRestorePrefersMinimizedMru()
    {
        OwnedWindowLayoutCoordinator coordinator = CreateCoordinator(4);
        _ = coordinator.TryActivate(Beta);
        _ = coordinator.TryActivate(Alpha);
        _ = coordinator.TrySetLifecycle(Beta, OwnedWindowLifecycle.Closed);
        _ = coordinator.TrySetLifecycle(Gamma, OwnedWindowLifecycle.Minimized);
        _ = coordinator.TrySetLifecycle(Delta, OwnedWindowLifecycle.Minimized);
        OwnedLayoutApplyResult result = coordinator.TryApply(
            LayoutPreset.EqualColumns, WorkArea, restoreHiddenWindows: true);
        if (!result.Succeeded ||
            !result.Placements.Select(placement => placement.WindowId).SequenceEqual([Alpha, Delta]) ||
            State(coordinator, Gamma).Lifecycle != OwnedWindowLifecycle.Minimized ||
            State(coordinator, Beta).Lifecycle != OwnedWindowLifecycle.Closed)
        {
            return false;
        }

        string applied = Fingerprint(coordinator.CaptureSnapshot());
        return coordinator.TryApply(
                LayoutPreset.EqualColumns, WorkArea, restoreHiddenWindows: true).Succeeded &&
            Fingerprint(coordinator.CaptureSnapshot()) == applied;
    }

    private static bool HostedLayoutRestoresMinimizedWindow(string id) => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        Button minimize = id == "explorer"
            ? shell.ExplorerMinimizeButton
            : shell.TaskManagerMinimizeButton;
        Invoke(minimize);
        PumpIdle();
        string? active = shell.ActiveOwnedWindowId;
        shell.PreviewLayoutForTest(LayoutPreset.EqualColumns);
        shell.FocusLayoutRailForTest();
        PumpIdle();
        IInputElement? focusBeforeCommit = Keyboard.FocusedElement;
        if (shell.VisibleOwnedWindowCount != 1)
        {
            return false;
        }

        shell.RaiseRailKeyForTest(Key.Enter);
        shell.UpdateLayout();
        PumpIdle();
        IReadOnlyList<Rect> panes = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(
            LayoutPreset.EqualColumns, WorkArea);
        string other = id == "explorer" ? "task-manager" : "explorer";
        Border indicator = id == "explorer"
            ? shell.ExplorerRunningIndicator
            : shell.TaskManagerRunningIndicator;
        return shell.VisibleOwnedWindowCount == 2 &&
            focusBeforeCommit is not null &&
            ReferenceEquals(Keyboard.FocusedElement, focusBeforeCommit) &&
            shell.ActiveOwnedWindowId == active && active == other &&
            shell.GetOwnedWindowBoundsForTest(other) == panes[0] &&
            shell.GetOwnedWindowBoundsForTest(id) == panes[1] &&
            indicator.Visibility == Visibility.Visible && indicator.Opacity == 1.0 &&
            (id != "task-manager" || shell.TaskManagerPanel.IsSamplingActiveForTest);
    });

    private static bool HostedLayoutRecoversAllClosed() => WithShell(shell =>
    {
        Invoke(shell.ExplorerCloseButton);
        PumpIdle();
        if (shell.VisibleOwnedWindowCount != 0 ||
            !shell.SelectLayoutForTest(LayoutPreset.EqualColumns))
        {
            return false;
        }

        shell.UpdateLayout();
        return shell.VisibleOwnedWindowCount == 2 &&
            shell.ExplorerWindow.Visibility == Visibility.Visible &&
            shell.TaskManagerWindow.Visibility == Visibility.Visible &&
            shell.ExplorerRunningIndicator.Visibility == Visibility.Visible &&
            shell.TaskManagerRunningIndicator.Visibility == Visibility.Visible &&
            shell.TaskManagerPanel.IsSamplingActiveForTest;
    });

    private static bool HostedSinglePaneKeepsSpareClosed() => WithShell(shell =>
    {
        if (!shell.SelectLayoutForTest(LayoutPreset.Maximized))
        {
            return false;
        }

        Invoke(shell.ExplorerMaximizeButton);
        return shell.VisibleOwnedWindowCount == 1 &&
            shell.TaskManagerWindow.Visibility == Visibility.Collapsed &&
            shell.TaskManagerRunningIndicator.Visibility == Visibility.Collapsed &&
            !shell.TaskManagerPanel.IsSamplingActiveForTest;
    });

    private static bool HostedSnapshotHostExcludesPointerInput()
    {
        DesktopShellSurface surface = new();
        Window host = PreviewRenderer.CreateRenderHost(surface);
        try
        {
            return host.Content is UIElement stage &&
                !stage.IsHitTestVisible && !surface.IsHitTestVisible;
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static bool HostedPassiveActionsKeepSpareMinimized() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
        Invoke(shell.TaskManagerMinimizeButton);
        PumpIdle();
        Rect beforePreview = shell.GetOwnedWindowBoundsForTest("explorer");
        foreach (Key key in new[] { Key.Down, Key.End, Key.Home })
        {
            shell.RaiseRailKeyForTest(key);
            if (shell.VisibleOwnedWindowCount != 1 ||
                shell.GetOwnedWindowBoundsForTest("explorer") != beforePreview ||
                shell.CurrentLayout != LayoutPreset.EqualColumns)
            {
                return false;
            }
        }

        Invoke(shell.ExplorerMaximizeButton);
        Invoke(shell.ExplorerMaximizeButton);
        if (shell.CurrentLayout != LayoutPreset.EqualColumns ||
            shell.VisibleOwnedWindowCount != 1)
        {
            return false;
        }

        Invoke(shell.TaskbarExplorerButton);
        Invoke(shell.TaskbarExplorerButton);
        PumpIdle();
        return shell.VisibleOwnedWindowCount == 1 &&
            shell.ExplorerWindow.Visibility == Visibility.Visible &&
            shell.TaskManagerWindow.Visibility == Visibility.Collapsed &&
            !shell.TaskManagerPanel.IsSamplingActiveForTest;
    });

    private static bool HostedCurrentLayoutReportsOccupiedPanes() => WithShell(shell =>
    {
        foreach (string hidden in new[] { "task-manager", "explorer" })
        {
            string other = hidden == "task-manager" ? "explorer" : "task-manager";
            ActivateBody(shell, other);
            if (!shell.SelectLayoutForTest(LayoutPreset.FourQuadrants) ||
                !AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains(
                    "2 of 4 panes", StringComparison.Ordinal))
            {
                return false;
            }

            Rect hiddenBounds = shell.GetOwnedWindowBoundsForTest(hidden);
            Invoke(hidden == "task-manager"
                ? shell.TaskManagerMinimizeButton : shell.ExplorerMinimizeButton);
            if (!AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains(
                    "1 of 4 panes", StringComparison.Ordinal))
            {
                return false;
            }

            Button maximizeOther = other == "explorer"
                ? shell.ExplorerMaximizeButton : shell.TaskManagerMaximizeButton;
            Invoke(maximizeOther);
            Invoke(maximizeOther);
            Invoke(hidden == "task-manager"
                ? shell.TaskbarSystemButton : shell.TaskbarExplorerButton);
            PumpIdle();
            if (shell.GetOwnedWindowBoundsForTest(hidden) != hiddenBounds ||
                !AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains(
                    "2 of 4 panes", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    });

    private static bool HostedRailDescribesPlannedRestore() => WithShell(shell =>
    {
        LayoutDefinition definition = LayoutCatalog.Get(LayoutPreset.EqualColumns);
        ListBoxItem item = (ListBoxItem)shell.LayoutRailList.ItemContainerGenerator
            .ContainerFromItem(definition);
        return AutomationProperties.GetHelpText(item).Contains(
            "restores 1", StringComparison.Ordinal) &&
            AutomationProperties.GetItemStatus(item).Contains(
                "2 of 2", StringComparison.Ordinal);
    });

    private static bool HostedLayoutOpensMissingWindow() => WithShell(shell =>
    {
        if (shell.VisibleOwnedWindowCount != 1 ||
            !shell.SelectLayoutForTest(LayoutPreset.EqualColumns))
        {
            return false;
        }

        shell.UpdateLayout();
        PumpIdle();
        IReadOnlyList<Rect> panes = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(
            LayoutPreset.EqualColumns, WorkArea);
        return shell.VisibleOwnedWindowCount == 2 &&
            shell.TaskManagerWindow.Visibility == Visibility.Visible &&
            shell.TaskManagerRunningIndicator.Visibility == Visibility.Visible &&
            shell.TaskManagerRunningIndicator.Opacity == 1.0 &&
            shell.TaskManagerPanel.IsSamplingActiveForTest &&
            shell.ActiveOwnedWindowId == "explorer" &&
            shell.GetOwnedWindowBoundsForTest("explorer") == panes[0] &&
            shell.GetOwnedWindowBoundsForTest("task-manager") == panes[1];
    });

    private static bool HostedRealExplorerLayouts() => WithShell(shell =>
    {
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants);
        if (shell.ExplorerSessionIdsForTest.Count != 1 || shell.VisibleOwnedWindowCount != 2 ||
            !AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains("2 of 4 panes", StringComparison.Ordinal))
            throw new InvalidOperationException("explicit partial fill must not manufacture Explorer sessions");
        _ = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount == 3))
        {
            _ = shell.SelectLayoutForTest(definition.Preset);
            shell.UpdateLayout();
            PumpIdle();
            VerifyRealExplorerPanes(shell, definition, 3);
        }
        _ = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        _ = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        Invoke(shell.TaskManagerCloseButton);
        foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount == 4))
        {
            _ = shell.SelectLayoutForTest(definition.Preset);
            shell.UpdateLayout();
            PumpIdle();
            VerifyRealExplorerPanes(shell, definition, 4);
            foreach (string id in shell.ExplorerSessionIdsForTest)
            {
                ExplorerSurface surface = shell.GetExplorerSurfaceForTest(id);
                _ = surface.ExplorerItemList.Focus();
                surface.ExplorerItemList.SelectedIndex = 0;
                Invoke(surface.ExplorerOpenButton);
                PumpIdle();
                Border frame = ExplorerFrame(shell, id);
                Rect bounds = shell.GetOwnedWindowBoundsForTest(id);
                if (!surface.ExplorerPreviewCloseButton.IsVisible ||
                    !bounds.Contains(BoundsOnShell(surface.ExplorerPreviewCloseButton, shell)) ||
                    !ExplorerWorkspaceScenarios.Descendants(surface.ExplorerPreviewPanel).OfType<TextBlock>()
                        .Any(text => text.IsVisible && text.Text == surface.ExplorerPreviewNameText.Text &&
                            text.ActualHeight > 0 && bounds.Contains(BoundsOnShell(text, shell))))
                    throw new InvalidOperationException($"{id}: compact owned preview controls/name clipped");
                Invoke(surface.ExplorerPreviewCloseButton);
                Invoke(surface.ExplorerNewFolderButton);
                PumpIdle();
                TextBox editor = ExplorerWorkspaceScenarios.Descendants(surface).OfType<TextBox>().First(item => item.IsVisible);
                if (!bounds.Contains(BoundsOnShell(editor, shell)))
                    throw new InvalidOperationException($"{id}: compact rename editor outside pane");
                ExplorerWorkspaceScenarios.RaiseKey(editor, Key.Escape);
                PumpIdle();
            }
        }
        string retainedId = shell.ExplorerSessionIdsForTest[1];
        ExplorerSurface retained = shell.GetExplorerSurfaceForTest(retainedId);
        Invoke(ExplorerTitleButton(shell, retainedId, "Minimize"));
        _ = shell.SelectLayoutForTest(LayoutPreset.FourQuadrants);
        PumpIdle();
        return shell.VisibleOwnedWindowCount == 4 &&
            ReferenceEquals(retained, shell.GetExplorerSurfaceForTest(retainedId)) &&
            shell.ExplorerSessionIdsForTest.Count == 4 &&
            ExplorerFrame(shell, retainedId).Visibility == Visibility.Visible;
    });

    private static void VerifyRealExplorerPanes(DesktopShellSurface shell, LayoutDefinition definition, int filled)
    {
        IReadOnlyList<Rect> panes = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(definition.Preset, WorkArea);
        Rect[] visible = shell.ExplorerSessionIdsForTest.Select(id => ExplorerFrame(shell, id))
            .Where(frame => frame.IsVisible).Select(frame => BoundsOnShell(frame, shell))
            .Concat(shell.TaskManagerWindow.IsVisible ? new[] { BoundsOnShell(shell.TaskManagerWindow, shell) } : Array.Empty<Rect>()).ToArray();
        if (visible.Length != filled || visible.Distinct().Count() != filled || !visible.All(panes.Contains) ||
            !AutomationProperties.GetHelpText(shell.CurrentLayoutButton).Contains($"{filled} of {definition.PaneCount} panes", StringComparison.Ordinal))
            throw new InvalidOperationException($"{definition.Preset}: actual WPF populated pane count/bounds/metadata disagree");
        foreach (string id in shell.ExplorerSessionIdsForTest)
        {
            ExplorerSurface surface = shell.GetExplorerSurfaceForTest(id);
            if (!ExplorerFrame(shell, id).IsVisible) continue;
            Rect bounds = shell.GetOwnedWindowBoundsForTest(id);
            if (surface.ExplorerItemList.Items.Count < 6 || surface.ExplorerItemList.ActualHeight < 44 ||
                !surface.ExplorerActionPanel.IsVisible)
                throw new InvalidOperationException($"{definition.Preset}/{id}: populated compact content/actions missing (items={surface.ExplorerItemList.Items.Count}, viewport={surface.ExplorerItemList.ActualHeight}, actions={surface.ExplorerActionPanel.IsVisible}, pane={bounds})");
            foreach (Button button in new[] { surface.ExplorerOpenButton, surface.ExplorerRenameButton,
                surface.ExplorerNewFolderButton, surface.ExplorerBackButton, surface.ExplorerUpButton,
                ExplorerTitleButton(shell, id, "Close") })
            {
                Rect controlBounds = BoundsOnShell(button, shell);
                if (!button.IsVisible || controlBounds.Width < 28 || controlBounds.Height < 28 || !bounds.Contains(controlBounds))
                    throw new InvalidOperationException($"{definition.Preset}/{id}/{AutomationProperties.GetName(button)}: usable control not within pane ({controlBounds})");
            }
        }
    }

    private static Rect BoundsOnShell(FrameworkElement element, DesktopShellSurface shell) =>
        element.TransformToAncestor(shell).TransformBounds(new Rect(element.RenderSize));

    private static bool HostedSecondaryTitleFocusRouting() => WithShell(shell =>
    {
        string secondId = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        string thirdId = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string id in new[] { secondId, thirdId })
        {
            ExplorerSurface surface = shell.GetExplorerSurfaceForTest(id);
            InvokeSessionMenu(shell, id, "Activate");
            _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
            Rect tiled = shell.GetOwnedWindowBoundsForTest(id);
            Point press = new(tiled.Left + 40, tiled.Top + 20);
            shell.RaiseOwnedTitleGestureForTest(id, press);
            shell.RaiseOwnedTitleGestureForTest(id, press, new Point(press.X + 1, press.Y + 1));
            shell.RaiseOwnedTitleGestureForTest(id, press, loseCapture: true);
            if (shell.CurrentLayout != LayoutPreset.EqualColumns || shell.GetOwnedWindowBoundsForTest(id) != tiled)
                throw new InvalidOperationException($"{id}: title click/jitter broke tile");
            shell.RaiseOwnedTitleGestureForTest(id, press, clickCount: 2);
            if (shell.GetOwnedWindowBoundsForTest(id) != WorkArea)
                throw new InvalidOperationException($"{id}: real secondary title double-click did not maximize");
            shell.RaiseOwnedTitleGestureForTest(id, press, clickCount: 2);
            if (shell.GetOwnedWindowBoundsForTest(id) != tiled)
                throw new InvalidOperationException($"{id}: double-click restore lost exact pane");
            foreach (bool loseCapture in new[] { false, true })
            {
                _ = shell.SelectLayoutForTest(LayoutPreset.EqualColumns);
                tiled = shell.GetOwnedWindowBoundsForTest(id);
                press = new(tiled.Left + 40, tiled.Top + 20);
                shell.RaiseOwnedTitleGestureForTest(id, press, new Point(press.X + 64, press.Y + 48), loseCapture);
                if (!shell.IsOwnedWindowLayoutFreeform || shell.GetOwnedWindowBoundsForTest(id) == tiled ||
                    Mouse.Captured is not null)
                    throw new InvalidOperationException($"{id}: drag/capture-loss did not commit and release");
            }
            _ = surface.ExplorerItemList.Focus();
            PumpIdle();
            if (shell.ActiveOwnedWindowId != id ||
                Panel.GetZIndex(ExplorerFrame(shell, id)) <= Panel.GetZIndex(shell.TaskManagerWindow)) return false;
            surface.ExplorerItemList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            shell.FocusLayoutRailForTest();
            PumpIdle();
            shell.RaiseRailKeyForTest(Key.Escape);
            PumpIdle();
            if (!surface.ExplorerItemList.IsKeyboardFocusWithin || shell.ActiveOwnedWindowId != id)
                throw new InvalidOperationException($"{id}: rail Escape returned to another session");
            Invoke(shell.ThisPcShortcutButton);
            PumpIdle();
            if (surface.Workspace.CurrentLocationId != ExplorerWorkspace.ThisPcId ||
                shell.GetExplorerSurfaceForTest("explorer").Workspace.CurrentLocationId != ExplorerWorkspace.DocumentsId)
                throw new InvalidOperationException($"{id}: desktop shortcut did not target active Explorer");
            // When Task Manager is active, aggregate activation chooses MRU Explorer.
            ActivateBody(shell, "task-manager");
            Invoke(shell.TaskbarExplorerButton);
            PumpIdle();
            if (shell.ActiveOwnedWindowId != id || shell.ExplorerSessionIdsForTest.Count != 3) return false;
        }
        Invoke(ExplorerTitleButton(shell, thirdId, "Close"));
        int retained = shell.ExplorerSessionIdsForTest.Count;
        shell.PreviewLayoutForTest(LayoutPreset.FourQuadrants);
        shell.RaiseRailKeyForTest(Key.Down);
        Invoke(ExplorerTitleButton(shell, secondId, "Maximize"));
        Invoke(ExplorerTitleButton(shell, secondId, "Restore"));
        return ExplorerFrame(shell, thirdId).Visibility == Visibility.Collapsed &&
            shell.ExplorerSessionIdsForTest.Count == retained;
    });

    private static bool HostedShellHidePreservesNewerFocus(string action) => WithShell(shell =>
    {
        string id = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(id);
        _ = secondary.ExplorerItemList.Focus();
        PumpIdle();
        if (!secondary.ExplorerItemList.IsKeyboardFocused || shell.ActiveOwnedWindowId != id)
            throw new InvalidOperationException($"{action}: exact secondary must start focused and active");
        Invoke(ExplorerTitleButton(shell, id, action));
        string? active = shell.ActiveOwnedWindowId;
        _ = shell.TaskbarSearchButton.Focus();
        if (!shell.TaskbarSearchButton.IsKeyboardFocused)
            throw new InvalidOperationException($"{action}: newer Search intent was not established before Input drain");
        PumpIdle();
        if (!shell.TaskbarSearchButton.IsKeyboardFocused || shell.ActiveOwnedWindowId != active ||
            ExplorerFrame(shell, id).Visibility != Visibility.Collapsed || secondary.IsKeyboardFocusWithin)
            throw new InvalidOperationException(
                $"shell/{action}: focus={(Keyboard.FocusedElement as FrameworkElement)?.Name}, " +
                $"active={shell.ActiveOwnedWindowId}, expectedActive={active}, hidden={ExplorerFrame(shell, id).Visibility}");
        return true;
    });

    private static bool HostedTaskManagerReturnPreservesNewerFocus() => WithShell(shell =>
    {
        if (!shell.PrepareTaskManagerForSnapshot()) return false;
        _ = shell.TaskManagerPanel.Focus();
        shell.FocusLayoutRailForTest();
        PumpIdle();
        if (!shell.LayoutRailHasKeyboardFocusForTest || shell.ActiveOwnedWindowId != "task-manager")
            throw new InvalidOperationException("Task Manager return must begin with real rail focus and unchanged active owner");
        shell.RaiseRailKeyForTest(Key.Escape);
        _ = shell.TaskbarSearchButton.Focus();
        if (!shell.TaskbarSearchButton.IsKeyboardFocused || shell.ActiveOwnedWindowId != "task-manager")
            throw new InvalidOperationException("newer Search intent must be established without changing Task Manager owner");
        PumpIdle();
        if (!shell.TaskbarSearchButton.IsKeyboardFocused || shell.ActiveOwnedWindowId != "task-manager" ||
            !shell.IsTaskManagerVisibleForTest || shell.TaskManagerPanelHasKeyboardFocusForTest)
            throw new InvalidOperationException(
                $"shell/task-manager-return: focus={(Keyboard.FocusedElement as FrameworkElement)?.Name}, " +
                $"active={shell.ActiveOwnedWindowId}, visible={shell.IsTaskManagerVisibleForTest}");
        return true;
    });

    private static bool HostedCurrentLayoutPreservesNewerFocus(bool foreignExplorer) => WithShell(shell =>
    {
        string destinationId = foreignExplorer
            ? ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell)
            : "explorer";
        ExplorerSurface destinationExplorer = shell.GetExplorerSurfaceForTest(destinationId);
        PumpIdle();
        _ = shell.CurrentLayoutButton.Focus();
        PumpIdle();
        if (!shell.CurrentLayoutButton.IsKeyboardFocused || shell.LayoutRailHasKeyboardFocusForTest)
            throw new InvalidOperationException("current-layout click must start at its actual button, not a pre-focused rail item");
        DispatcherOperation operation = InvokeCurrentLayoutAndRetainOperation(shell);
        UIElement destination = foreignExplorer ? destinationExplorer.ExplorerItemList : shell.TaskbarSearchButton;
        _ = destination.Focus();
        string? active = shell.ActiveOwnedWindowId;
        if (!ReferenceEquals(Keyboard.FocusedElement, destination) ||
            operation.Status is not (DispatcherOperationStatus.Pending or DispatcherOperationStatus.Aborted))
            throw new InvalidOperationException("newer real keyboard focus must be established before the click's Input callback drains; owner activation may already abort it");
        PumpIdle();
        if (!ReferenceEquals(Keyboard.FocusedElement, destination) || shell.ActiveOwnedWindowId != active ||
            shell.LayoutRailHasKeyboardFocusForTest || !shell.IsOwnedWindowLayoutFreeform)
            throw new InvalidOperationException(
                $"current-layout/{(foreignExplorer ? "foreign-explorer" : "search")}: " +
                $"focus={(Keyboard.FocusedElement as FrameworkElement)?.Name}, active={shell.ActiveOwnedWindowId}, " +
                $"expectedActive={active}, operation={operation.Status}");
        if (foreignExplorer && operation.Status != DispatcherOperationStatus.Aborted)
            throw new InvalidOperationException("foreign Explorer activation must cancel the retained shell operation, not merely leave focus unchanged");
        return true;
    });

    private static bool HostedCurrentLayoutUnloadAbortsFocus() => WithShell(shell =>
    {
        _ = shell.CurrentLayoutButton.Focus();
        PumpIdle();
        if (!shell.CurrentLayoutButton.IsKeyboardFocused)
            throw new InvalidOperationException("unload trace must start at the real current-layout button");
        DispatcherOperation operation = InvokeCurrentLayoutAndRetainOperation(shell);
        // Deliver the owning surface's actual routed teardown event without
        // pumping Input. Keep the operation itself: an empty queue after drain
        // would not prove the Unloaded handler cancelled this callback.
        shell.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        if (operation.Status != DispatcherOperationStatus.Aborted)
            throw new InvalidOperationException($"current-layout/unload-before-pump: retained operation={operation.Status}, expected=Aborted");
        PumpIdle();
        return operation.Status == DispatcherOperationStatus.Aborted && !shell.LayoutRailHasKeyboardFocusForTest;
    });

    private static bool HostedCurrentLayoutSupersedesFocus() => WithShell(shell =>
    {
        _ = shell.CurrentLayoutButton.Focus();
        PumpIdle();
        if (!shell.CurrentLayoutButton.IsKeyboardFocused)
            throw new InvalidOperationException("supersession trace must start at the real current-layout button");
        DispatcherOperation first = InvokeCurrentLayoutAndRetainOperation(shell);
        DispatcherOperation second = InvokeCurrentLayoutAndRetainOperation(shell);
        if (ReferenceEquals(first, second) || first.Status != DispatcherOperationStatus.Aborted ||
            second.Status != DispatcherOperationStatus.Pending)
            throw new InvalidOperationException(
                $"current-layout/superseded-before-pump: first={first.Status}, second={second.Status}, expected=Aborted/Pending");
        PumpIdle();
        return first.Status == DispatcherOperationStatus.Aborted &&
            Keyboard.FocusedElement is ListBoxItem item && item.IsSelected &&
            shell.LayoutRailHasKeyboardFocusForTest && shell.CurrentLayout is null;
    });

    private static bool HostedCurrentLayoutFocusAndNavigation() => WithShell(shell =>
    {
        foreach (bool applied in new[] { false, true })
        {
            if (applied && !shell.SelectLayoutForTest(LayoutPreset.EqualColumns)) return false;
            PumpIdle();
            _ = shell.CurrentLayoutButton.Focus();
            PumpIdle();
            if (!shell.CurrentLayoutButton.IsKeyboardFocused || shell.LayoutRailHasKeyboardFocusForTest)
                throw new InvalidOperationException("normal rail handoff must not pre-focus its intended item");
            string? active = shell.ActiveOwnedWindowId;
            Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
            Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
            LayoutPreset? committed = shell.CurrentLayout;
            DispatcherOperation operation = InvokeCurrentLayoutAndRetainOperation(shell);
            PumpIdle();
            if (Keyboard.FocusedElement is not ListBoxItem item || !item.IsSelected ||
                !shell.LayoutRailHasKeyboardFocusForTest || shell.ActiveOwnedWindowId != active ||
                shell.CurrentLayout != committed ||
                shell.LayoutRailSelectedValueForTest != (applied ? LayoutPreset.EqualColumns : LayoutPreset.Maximized))
                throw new InvalidOperationException($"current-layout/normal: applied={applied}, focus={Keyboard.FocusedElement?.GetType().Name}, operation={operation.Status}");
            LayoutPreset? selected = shell.LayoutRailSelectedValueForTest;
            KeyEventArgs preview = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(shell), Environment.TickCount, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            item.RaiseEvent(preview);
            PumpIdle();
            if (!preview.Handled || shell.LayoutRailSelectedValueForTest == selected ||
                shell.CurrentLayout != committed || shell.GetOwnedWindowBoundsForTest("explorer") != explorer ||
                shell.GetOwnedWindowBoundsForTest("task-manager") != task)
                throw new InvalidOperationException("button handoff changed committed geometry or broke keyboard-only preview");
            KeyEventArgs escape = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(shell), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            ((UIElement)Keyboard.FocusedElement!).RaiseEvent(escape);
            PumpIdle();
            bool returned = active == "task-manager" ? shell.TaskManagerPanelHasKeyboardFocusForTest :
                active is not null && shell.GetExplorerSurfaceForTest(active).ExplorerItemList.IsKeyboardFocusWithin;
            if (!escape.Handled || !returned || shell.ActiveOwnedWindowId != active || shell.CurrentLayout != committed)
                throw new InvalidOperationException("focused rail Escape must be consumed and return to its unchanged active owned window");
            _ = shell.TaskbarSearchButton.Focus();
            KeyEventArgs externalEscape = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(shell), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            shell.LayoutRailPanel.RaiseEvent(externalEscape);
            if (externalEscape.Handled || !shell.TaskbarSearchButton.IsKeyboardFocused)
                throw new InvalidOperationException("rail handler must leave Escape unhandled when the rail does not own keyboard focus");
        }
        return true;
    });

    private static DispatcherOperation InvokeCurrentLayoutAndRetainOperation(DesktopShellSurface shell)
    {
        List<DispatcherOperation> posted = [];
        void OnPosted(object? sender, DispatcherHookEventArgs eventArgs)
        {
            if (eventArgs.Operation.Priority == DispatcherPriority.Input) posted.Add(eventArgs.Operation);
        }
        shell.Dispatcher.Hooks.OperationPosted += OnPosted;
        try { Invoke(shell.CurrentLayoutButton); }
        finally { shell.Dispatcher.Hooks.OperationPosted -= OnPosted; }
        if (posted.Count != 1 || posted[0].Status != DispatcherOperationStatus.Pending)
            throw new InvalidOperationException($"actual current-layout Click must queue one pending Input focus operation; posted={posted.Count}");
        return posted[0];
    }

    private static bool HostedSecondaryTitleKeyboardHandoff() => WithShell(shell =>
    {
        string id = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
        ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(id);
        primary.ExplorerItemList.SelectedIndex = 1;
        secondary.ExplorerItemList.SelectedIndex = 0;
        _ = primary.ExplorerItemList.Focus();
        PumpIdle();
        if (!primary.ExplorerItemList.IsKeyboardFocusWithin || shell.ActiveOwnedWindowId != "explorer")
            throw new InvalidOperationException("title handoff must start at the actually focused primary ledger");

        Rect bounds = shell.GetOwnedWindowBoundsForTest(id);
        Point press = new(bounds.Left + 40, bounds.Top + 20);
        shell.RaiseOwnedTitleGestureForTest(id, press);
        PumpIdle();
        if (shell.ActiveOwnedWindowId != id || Mouse.Captured is not null)
            throw new InvalidOperationException("released secondary title must activate its identity and release capture");
        // Route from WPF's real focus, not from a manually chosen destination.
        UIElement keySource = Keyboard.FocusedElement as UIElement ??
            throw new InvalidOperationException("secondary title left no actual keyboard target");
        ExplorerWorkspaceScenarios.RaiseKey(keySource, Key.F2);
        PumpIdle();
        if (primary.Workspace.RenameItemId is not null ||
            secondary.Workspace.RenameItemId != ExplorerWorkspace.ProjectBriefId ||
            shell.ActiveOwnedWindowId != id || !secondary.IsKeyboardFocusWithin)
            throw new InvalidOperationException(
                $"title/F2: source={keySource.GetType().Name}, primaryRename={primary.Workspace.RenameItemId}, " +
                $"secondaryRename={secondary.Workspace.RenameItemId}, active={shell.ActiveOwnedWindowId}");
        ExplorerWorkspaceScenarios.RaiseKey((UIElement)Keyboard.FocusedElement!, Key.Escape);
        PumpIdle();

        Invoke(secondary.ExplorerOpenButton);
        PumpIdle();
        _ = primary.ExplorerItemList.Focus();
        PumpIdle();
        shell.RaiseOwnedTitleGestureForTest(id, press);
        PumpIdle();
        if (!secondary.ExplorerPreviewCloseButton.IsKeyboardFocused || shell.ActiveOwnedWindowId != id ||
            primary.ExplorerPreviewPanel.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("secondary title did not hand focus to its own visible preview");
        Invoke(secondary.ExplorerPreviewCloseButton);
        PumpIdle();

        _ = primary.ExplorerItemList.Focus();
        PumpIdle();
        _ = secondary.Workspace.BeginRenameSelected();
        shell.RaiseOwnedTitleGestureForTest(id, press);
        PumpIdle();
        return Keyboard.FocusedElement is TextBox editor && secondary.IsAncestorOf(editor) &&
            editor.DataContext is ExplorerWorkspaceItem { IsRenameTarget: true } &&
            secondary.Workspace.RenameItemId == ExplorerWorkspace.ProjectBriefId &&
            primary.Workspace.RenameItemId is null && shell.ActiveOwnedWindowId == id;
    });

    private static bool HostedSecondaryIdentityLifecycle() => WithShell(shell =>
    {
        string id = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
        ExplorerSurface secondary = shell.GetExplorerSurfaceForTest(id);
        ExplorerSurface primary = shell.GetExplorerSurfaceForTest("explorer");
        Rect floating = shell.GetOwnedWindowBoundsForTest(id);
        InvokeSessionMenu(shell, id, "Activate");
        if (shell.ActiveOwnedWindowId != id) return false;
        _ = secondary.Workspace.TryNavigateTo(ExplorerWorkspace.ArchiveId);
        Invoke(secondary.ExplorerNewFolderButton);
        PumpIdle();
        TextBox editor = ExplorerWorkspaceScenarios.Descendants(secondary).OfType<TextBox>().First(item => item.IsVisible);
        editor.Text = "RETAINED_SESSION_FOLDER";
        ExplorerWorkspaceScenarios.RaiseKey(editor, Key.Enter);
        PumpIdle();
        string? selected = secondary.Workspace.SelectedItemId;
        Invoke(ExplorerTitleButton(shell, id, "Minimize"));
        PumpIdle();
        if (ExplorerFrame(shell, id).Visibility != Visibility.Collapsed ||
            ExplorerFrame(shell, "explorer").Visibility != Visibility.Visible ||
            shell.ExplorerRunningIndicator.Visibility != Visibility.Visible ||
            primary.Workspace.CurrentLocationId != ExplorerWorkspace.DocumentsId) return false;
        InvokeSessionMenu(shell, id, "Restore");
        PumpIdle();
        if (shell.ActiveOwnedWindowId != id || shell.GetOwnedWindowBoundsForTest(id) != floating ||
            !ReferenceEquals(secondary, shell.GetExplorerSurfaceForTest(id))) return false;
        Invoke(ExplorerTitleButton(shell, id, "Maximize"));
        Invoke(ExplorerTitleButton(shell, id, "Restore"));
        if (shell.GetOwnedWindowBoundsForTest(id) != floating) return false;
        Invoke(ExplorerTitleButton(shell, id, "Close"));
        PumpIdle();
        int retainedCount = shell.ExplorerSessionIdsForTest.Count;
        Invoke(shell.TaskbarExplorerButton);
        PumpIdle();
        // Aggregate activation chooses a running session, never the closed secondary.
        if (ExplorerFrame(shell, id).Visibility != Visibility.Collapsed ||
            shell.ExplorerSessionIdsForTest.Count != retainedCount) return false;
        InvokeSessionMenu(shell, id, "Reopen");
        PumpIdle();
        return ReferenceEquals(secondary, shell.GetExplorerSurfaceForTest(id)) &&
            secondary.Workspace.CurrentLocationId == ExplorerWorkspace.ArchiveId &&
            secondary.Workspace.SelectedItemId == selected &&
            secondary.Workspace.Items.Single().Name == "RETAINED_SESSION_FOLDER" &&
            primary.Workspace.Items.Count == 6 && shell.ActiveOwnedWindowId == id &&
            shell.GetOwnedWindowBoundsForTest(id) == floating &&
            shell.ExplorerRunningIndicator.Visibility == Visibility.Visible &&
            shell.ExplorerRunningIndicator.Opacity == 1.0;
    });

    private static bool HostedPromotedActiveFrameAboveMru() =>
        new[] { "explorer", "task-manager" }.All(activeId => WithShell(shell =>
        {
            void Require(bool condition, string detail)
            {
                if (!condition) throw new InvalidOperationException($"{activeId}: {detail}");
            }

            // Observe the production coordinator without mutating its activation history.
            OwnedWindowLayoutCoordinator coordinator =
                (OwnedWindowLayoutCoordinator)(typeof(DesktopShellSurface).GetField(
                    "_ownedWindowLayouts",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(shell) ?? throw new InvalidOperationException("Hosted coordinator absent"));
            FrameworkElement Frame(OwnedWindowId id) => id.Value == "task-manager"
                ? shell.TaskManagerWindow : ExplorerFrame(shell, id.Value);

            if (activeId == "task-manager")
            {
                Invoke(shell.TaskbarSystemButton);
                PumpIdle();
                // Leave Task Manager as the only visible candidate for promotion.
                Invoke(shell.ExplorerCloseButton);
                PumpIdle();
            }

            string secondaryId = ExplorerWorkspaceScenarios.CreateViaTaskbarMenu(shell);
            Require(secondaryId == "explorer-2", "real taskbar New did not activate explorer-2");
            string[] sessionIds = shell.ExplorerSessionIdsForTest.ToArray();
            Dictionary<string, ExplorerSurface> surfaces = sessionIds.ToDictionary(
                id => id, shell.GetExplorerSurfaceForTest);
            Require(sessionIds.Length == 2 && !ReferenceEquals(surfaces["explorer"], surfaces[secondaryId]) &&
                !ReferenceEquals(surfaces["explorer"].Workspace, surfaces[secondaryId].Workspace),
                "New must create a real independent surface/workspace");
            OwnedWindowLayoutSnapshot beforeMinimize = coordinator.CaptureSnapshot();

            Require(shell.TaskbarExplorerButton.Focus(), "external taskbar focus failed");
            Invoke(shell.TaskbarExplorerButton);
            PumpIdle();
            OwnedWindowLayoutSnapshot promoted = coordinator.CaptureSnapshot();
            Require(shell.ActiveOwnedWindowId == activeId &&
                Frame(new(activeId)).Visibility == Visibility.Visible &&
                ExplorerFrame(shell, secondaryId).Visibility == Visibility.Collapsed &&
                State(coordinator, new(secondaryId)).Lifecycle == OwnedWindowLifecycle.Minimized,
                "real taskbar minimize did not promote the remaining visible identity");
            Require(promoted.Windows.All(window => beforeMinimize.Windows.Single(before => before.Id == window.Id)
                .ActivationOrdinal == window.ActivationOrdinal), "minimize changed activation history");
            Require(State(coordinator, new(secondaryId)).ActivationOrdinal > State(coordinator, new(activeId)).ActivationOrdinal,
                "fixture must retain a newer ordinal on the minimized secondary");
            IInputElement? externalFocus = Keyboard.FocusedElement;
            Require(ReferenceEquals(externalFocus, shell.TaskbarExplorerButton), "taskbar did not retain actual external keyboard focus");

            void RequirePreservedIdentityAndHistory()
            {
                OwnedWindowLayoutSnapshot current = coordinator.CaptureSnapshot();
                Require(shell.ActiveOwnedWindowId == activeId && current.ActiveWindowId == promoted.ActiveWindowId,
                    "rail changed the promoted active identity");
                Require(ReferenceEquals(Keyboard.FocusedElement, externalFocus), "rail stole external keyboard focus");
                Require(shell.ExplorerSessionIdsForTest.SequenceEqual(sessionIds) &&
                    sessionIds.All(id => ReferenceEquals(surfaces[id], shell.GetExplorerSurfaceForTest(id))) &&
                    current.Windows.Count == promoted.Windows.Count && current.Windows.All(window =>
                    {
                        OwnedWindowState before = promoted.Windows.Single(original => original.Id == window.Id);
                        return window.ActivationOrdinal == before.ActivationOrdinal &&
                            window.RegistrationOrder == before.RegistrationOrder;
                    }), "rail allocated a session, replaced content or changed activation/registration history");
            }

            // These are explicit production rail commits, not title/body activation or
            // taskbar restoration (which would bump the ordinal and hide the defect).
            Require(shell.SelectLayoutForTest(LayoutPreset.EqualColumns), "EqualColumns rejected");
            PumpIdle();
            RequirePreservedIdentityAndHistory();
            IReadOnlyList<Rect> columns = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(LayoutPreset.EqualColumns, WorkArea);
            Require(shell.GetOwnedWindowBoundsForTest(activeId) == columns[0] &&
                shell.GetOwnedWindowBoundsForTest(secondaryId) == columns[1] &&
                ExplorerFrame(shell, secondaryId).Visibility == Visibility.Visible &&
                State(coordinator, new(secondaryId)).Lifecycle == OwnedWindowLifecycle.Visible,
                "explicit rail did not restore secondary into the right pane");
            OwnedWindowState[] extras = coordinator.CaptureSnapshot().Windows.Where(window => window.Id.Value != activeId).ToArray();
            Dictionary<OwnedWindowId, Visibility> extraVisibility = extras.ToDictionary(window => window.Id, window => Frame(window.Id).Visibility);

            Require(shell.SelectLayoutForTest(LayoutPreset.Maximized), "Maximized rejected");
            PumpIdle();
            RequirePreservedIdentityAndHistory();
            Require(shell.GetOwnedWindowBoundsForTest(activeId) == WorkArea &&
                WorkArea.IntersectsWith(shell.GetOwnedWindowBoundsForTest(secondaryId)), "fixture lost overlapping maximize geometry");
            Require(extras.All(before =>
            {
                OwnedWindowState after = State(coordinator, before.Id);
                return after.Bounds == before.Bounds && after.FloatingBounds == before.FloatingBounds &&
                    after.Lifecycle == before.Lifecycle && shell.GetOwnedWindowBoundsForTest(before.Id.Value) == before.Bounds &&
                    Frame(before.Id).Visibility == extraVisibility[before.Id];
            }), "maximize moved, hid or changed the lifecycle of a retained extra frame");

            OwnedWindowId[] expectedNonActive = extras.OrderBy(window => window.ActivationOrdinal)
                .ThenBy(window => window.RegistrationOrder).Select(window => window.Id).ToArray();
            Require(extras.OrderBy(window => Panel.GetZIndex(Frame(window.Id))).Select(window => window.Id)
                .SequenceEqual(expectedNonActive), "non-active frames lost historical MRU/registration stacking");
            int activeZ = Panel.GetZIndex(Frame(new(activeId)));
            int secondaryZ = Panel.GetZIndex(ExplorerFrame(shell, secondaryId));
            Require(extras.All(window => activeZ > Panel.GetZIndex(Frame(window.Id))),
                $"active maximized frame is obscured: activeZ={activeZ}, secondaryZ={secondaryZ}; " +
                "identity, focus, ordinals and extra bounds preserved");
            return true;
        }));

    internal static Border ExplorerFrame(DesktopShellSurface shell, string id) =>
        (Border)((Grid)shell.GetExplorerSurfaceForTest(id).Parent).Parent;

    internal static Button ExplorerTitleButton(DesktopShellSurface shell, string id, string action) =>
        ExplorerWorkspaceScenarios.Descendants(ExplorerFrame(shell, id)).OfType<Button>().Single(button =>
            button.CommandParameter is OwnedWindowId windowId && windowId.Value == id &&
            AutomationProperties.GetName(button).StartsWith(action + " ", StringComparison.Ordinal));

    internal static void InvokeSessionMenu(DesktopShellSurface shell, string id, string action)
    {
        ContextMenu menu = shell.TaskbarExplorerButton.ContextMenu;
        menu.PlacementTarget = shell.TaskbarExplorerButton;
        menu.IsOpen = true;
        PumpIdle();
        try
        {
            MenuItem? command = menu.Items.OfType<MenuItem>().SingleOrDefault(item =>
                item.CommandParameter is OwnedWindowId windowId && windowId.Value == id);
            if (command is null || command.Header is not string header ||
                !header.Contains(id, StringComparison.Ordinal) || !header.StartsWith(action, StringComparison.Ordinal))
                throw new InvalidOperationException($"ID-bearing {action} entry for {id} absent from taskbar context menu");
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally { menu.IsOpen = false; }
    }



    private static bool HostedInitialState() => WithShell(shell =>
        shell.IsOwnedWindowLayoutFreeform && shell.CurrentLayoutGlyph.IsFreeform &&
        shell.VisibleOwnedWindowCount == 1 && shell.AvailableLayoutCount == 1 &&
        LayoutCatalog.All.All(definition => shell.IsLayoutSelectableForTest(definition.Preset)) &&
        shell.LayoutRailList.SelectedItems.Count == 0 &&
        shell.ActiveOwnedWindowId == "explorer");

    private static bool HostedAvailability() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        shell.UpdateLayout();
        return shell.VisibleOwnedWindowCount == 2 && shell.AvailableLayoutCount == 7 &&
            shell.IsOwnedWindowLayoutFreeform && LayoutCatalog.All.All(definition =>
                shell.IsLayoutSelectableForTest(definition.Preset) &&
                shell.IsLayoutAvailableForTest(definition.Preset) == (definition.PaneCount <= 2));
    });

    private static bool HostedTitleDoubleClickToggles() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (string id in new[] { "explorer", "task-manager" })
        {
            Rect floating = shell.GetOwnedWindowBoundsForTest(id);
            Point press = new(WorkArea.Left + 40, WorkArea.Top + 20);
            shell.RaiseOwnedTitleGestureForTest(id, press, clickCount: 2);
            shell.UpdateLayout();
            bool maximized = id == "explorer"
                ? shell.IsExplorerMaximized
                : shell.IsTaskManagerMaximizedForTest;
            if (!maximized || shell.GetOwnedWindowBoundsForTest(id) != WorkArea)
            {
                return false;
            }

            shell.RaiseOwnedTitleGestureForTest(id, press, clickCount: 2);
            shell.UpdateLayout();
            bool restoredAgain = id == "explorer"
                ? !shell.IsExplorerMaximized
                : !shell.IsTaskManagerMaximizedForTest;
            if (!restoredAgain || shell.GetOwnedWindowBoundsForTest(id) != floating)
            {
                return false;
            }
        }

        return true;
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

    private static bool HostedPartialFill() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        foreach (LayoutDefinition definition in LayoutCatalog.All.Where(layout => layout.PaneCount > 2))
        {
            ListBoxItem item = (ListBoxItem)shell.LayoutRailList.ItemContainerGenerator.ContainerFromItem(definition);
            if (!item.IsEnabled ||
                !AutomationProperties.GetHelpText(item).Contains("fills the first", StringComparison.Ordinal) ||
                !shell.SelectLayoutForTest(definition.Preset))
            {
                return false;
            }

            shell.UpdateLayout();
            IReadOnlyList<Rect> panes = OwnedWindowLayoutCoordinator.ScaleWithInternalGap(definition.Preset, WorkArea);
            Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
            Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
            if (shell.CurrentLayout != definition.Preset ||
                !panes.Contains(explorer) || !panes.Contains(task) || explorer == task)
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

    private static bool HostedArrowPreviewNavigation() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        if (!shell.SelectLayoutForTest(LayoutPreset.EqualColumns))
        {
            return false;
        }

        shell.UpdateLayout();
        Rect explorer = shell.GetOwnedWindowBoundsForTest("explorer");
        Rect task = shell.GetOwnedWindowBoundsForTest("task-manager");
        shell.PreviewLayoutForTest(LayoutPreset.EqualColumns);
        LayoutPreset[] expected =
        [
            LayoutPreset.WideLeftNarrowRight,
            LayoutPreset.NarrowLeftWideRight,
            LayoutPreset.EqualRows,
            LayoutPreset.WideTopNarrowBottom,
            LayoutPreset.NarrowTopWideBottom,
        ];
        foreach (LayoutPreset preset in expected)
        {
            shell.RaiseRailKeyForTest(Key.Down);
            if (shell.LayoutRailSelectedValueForTest != preset ||
                shell.CurrentLayout != LayoutPreset.EqualColumns)
            {
                return false;
            }
        }

        // Further Down now steps into the three-pane family: every preset is
        // selectable and partial fill applies on commit.
        shell.RaiseRailKeyForTest(Key.Down);
        if (shell.LayoutRailSelectedValueForTest !=
                LayoutPreset.ThreeColumns ||
            shell.CurrentLayout != LayoutPreset.EqualColumns)
        {
            return false;
        }

        shell.RaiseRailKeyForTest(Key.End);
        if (shell.LayoutRailSelectedValueForTest !=
                LayoutPreset.FourQuadrants)
        {
            return false;
        }

        shell.RaiseRailKeyForTest(Key.Home);
        if (shell.LayoutRailSelectedValueForTest != LayoutPreset.Maximized ||
            shell.CurrentLayout != LayoutPreset.EqualColumns)
        {
            return false;
        }

        // Previewing must not have moved any window.
        if (shell.GetOwnedWindowBoundsForTest("explorer") != explorer ||
            shell.GetOwnedWindowBoundsForTest("task-manager") != task ||
            !shell.InteractionStatusForTest.StartsWith(
                "LAYOUT PREVIEW / ",
                StringComparison.Ordinal))
        {
            return false;
        }

        // Enter commits the previewed preset: the active window fills the work
        // area and the extra visible window keeps its previous pane bounds.
        shell.RaiseRailKeyForTest(Key.Enter);
        shell.UpdateLayout();
        return shell.CurrentLayout == LayoutPreset.Maximized &&
            shell.GetOwnedWindowBoundsForTest("task-manager") == WorkArea &&
            shell.GetOwnedWindowBoundsForTest("explorer") == explorer &&
            shell.InteractionStatusForTest == "LAYOUT / MAXIMIZED";
    });

    private static bool HostedEscapeReturnsFocus() => WithShell(shell =>
    {
        _ = shell.PrepareTaskManagerForSnapshot();
        ActivateBody(shell, "explorer");
        shell.FocusLayoutRailForTest();
        PumpIdle();
        if (!shell.LayoutRailHasKeyboardFocusForTest)
        {
            throw new InvalidOperationException(
                $"escape/rail-before: focus={Keyboard.FocusedElement?.GetType().Name}, " +
                $"active={shell.ActiveOwnedWindowId}");
        }

        shell.RaiseRailKeyForTest(Key.Escape);
        PumpIdle();
        if (!shell.ExplorerItemsHaveKeyboardFocusForTest ||
            shell.InteractionStatusForTest !=
                "RAIL CLOSED / FOCUS RETURNED TO ACTIVE WINDOW")
        {
            throw new InvalidOperationException(
                $"escape/explorer-return: focus={Keyboard.FocusedElement?.GetType().Name}, " +
                $"active={shell.ActiveOwnedWindowId}, status={shell.InteractionStatusForTest}");
        }

        ActivateBody(shell, "task-manager");
        shell.FocusLayoutRailForTest();
        PumpIdle();
        shell.RaiseRailKeyForTest(Key.Escape);
        PumpIdle();
        if (!shell.TaskManagerPanelHasKeyboardFocusForTest ||
            shell.ExplorerItemsHaveKeyboardFocusForTest ||
            shell.CurrentLayout is not null)
        {
            throw new InvalidOperationException(
                $"escape/task-manager-return: focus={Keyboard.FocusedElement?.GetType().Name}, " +
                $"active={shell.ActiveOwnedWindowId}, layout={shell.CurrentLayout}");
        }

        return true;
    });

    private static void PumpIdle()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
