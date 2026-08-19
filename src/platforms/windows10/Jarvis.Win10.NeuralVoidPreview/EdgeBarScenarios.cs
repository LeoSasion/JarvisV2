using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record EdgeBarScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record EdgeBarTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool OwnProcessOnly,
    bool ShellMutationSupported,
    string LiveExplorer,
    IReadOnlyList<EdgeBarScenario> Scenarios);

internal static class EdgeBarScenarios
{
    public static EdgeBarTestReceipt Run()
    {
        DesktopShellSurface surface = new();
        Window testHost = CreateTestHost(surface);
        try
        {
            testHost.Show();
            return RunHosted(surface);
        }
        finally
        {
            testHost.Content = null;
            testHost.Close();
        }
    }

    private static EdgeBarTestReceipt RunHosted(
        DesktopShellSurface surface)
    {
        List<EdgeBarScenario> scenarios = [];
        surface.Measure(new Size(1600, 900));
        surface.Arrange(new Rect(0, 0, 1600, 900));
        surface.UpdateLayout();
        surface.PrepareLayoutRailForSnapshot();
        surface.UpdateLayout();

        Add(
            scenarios,
            "layout-catalog-sixteen-unique-closed-topologies",
            () =>
                LayoutCatalog.All.Count == 16 &&
                Enum.GetValues<LayoutPreset>().Length == 16 &&
                LayoutCatalog.All
                    .Select(definition => definition.Signature)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == 16 &&
                LayoutCatalog.All.All(LayoutCatalog.IsExactCover) &&
                LayoutCatalog.HasOrthogonalClosure());

        Add(
            scenarios,
            "layout-catalog-pane-count-distribution",
            () =>
                LayoutCatalog.All.Count(definition => definition.PaneCount == 1) == 1 &&
                LayoutCatalog.All.Count(definition => definition.PaneCount == 2) == 6 &&
                LayoutCatalog.All.Count(definition => definition.PaneCount == 3) == 8 &&
                LayoutCatalog.All.Count(definition => definition.PaneCount == 4) == 1);

        Add(
            scenarios,
            "layout-catalog-stable-rail-order",
            () =>
                LayoutCatalog.All[0].Preset == LayoutPreset.Maximized &&
                LayoutCatalog.All[^1].Preset == LayoutPreset.FourQuadrants &&
                LayoutCatalog.All.SequenceEqual(
                    LayoutCatalog.All.OrderBy(definition => definition.RailOrder)) &&
                LayoutCatalog.All.Select(definition => definition.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 16);

        Add(
            scenarios,
            "initial-current-layout-is-data-driven",
            () =>
                surface.LayoutOptionCount == 16 &&
                surface.CurrentLayout == LayoutPreset.LeftMainRightStack &&
                surface.CurrentLayoutGlyph.Preset == surface.CurrentLayout &&
                surface.SelectedLayoutDefinition?.Preset == surface.CurrentLayout &&
                surface.LayoutRailList.SelectedItems.Count == 1);

        Add(
            scenarios,
            "layout-rail-is-permanently-visible",
            () =>
                surface.IsLayoutRailOpen &&
                surface.LayoutRailPanel.Visibility == Visibility.Visible &&
                surface.LayoutRailPanel.Opacity == 1.0 &&
                surface.LayoutAxisScale.ScaleY == 1.0);

        Add(
            scenarios,
            "layout-rail-escape-bubbles-to-host-window",
            EscapeFromLayoutRailClosesHostedMainWindow);

        Add(
            scenarios,
            "rail-viewport-is-compact-and-scrollable",
            () =>
            {
                LayoutDefinition selected =
                    surface.SelectedLayoutDefinition ??
                    throw new InvalidOperationException(
                        "missing-selected-layout");
                ListBoxItem? selectedItem =
                    surface.LayoutRailList.ItemContainerGenerator
                        .ContainerFromItem(selected) as ListBoxItem;
                return
                    surface.LayoutRailScrollableHeight > 0.0 &&
                    surface.LayoutRailList.ActualHeight ==
                        DesktopShellSurface.LayoutViewportHeight &&
                    surface.LayoutRailList.ActualHeight <
                        surface.LayoutOptionCount *
                        DesktopShellSurface.LayoutItemHeight &&
                    selectedItem?.ActualHeight ==
                        DesktopShellSurface.LayoutItemHeight &&
                    surface.IsSelectedLayoutFullyVisible;
            });

        Add(
            scenarios,
            "layout-glyph-drawn-bounds-share-permanent-x-axis",
            () =>
            {
                surface.UpdateLayout();
                Rect current = surface.CurrentLayoutGlyphBounds;
                Rect? rail = surface.SelectedRailLayoutGlyphBounds;
                IReadOnlyList<Rect> allRailGlyphs =
                    surface.LayoutRailGlyphBounds;
                if (rail is null ||
                    allRailGlyphs.Count != surface.LayoutOptionCount)
                {
                    return false;
                }

                double railCenter = rail.Value.Left + rail.Value.Width / 2.0;
                double currentCenter = current.Left + current.Width / 2.0;
                return
                    NearlyEqual(rail.Value.Left, current.Left) &&
                    NearlyEqual(
                        rail.Value.Left,
                        DesktopShellSurface.LayoutGlyphLeftX) &&
                    NearlyEqual(rail.Value.Width, current.Width) &&
                    NearlyEqual(rail.Value.Height, current.Height) &&
                    NearlyEqual(
                        rail.Value.Width,
                        DesktopShellSurface.LayoutGlyphWidth) &&
                    NearlyEqual(
                        rail.Value.Height,
                        DesktopShellSurface.LayoutGlyphHeight) &&
                    NearlyEqual(railCenter, currentCenter) &&
                    NearlyEqual(
                        railCenter,
                        DesktopShellSurface.LayoutColumnCenterX) &&
                    allRailGlyphs.All(bounds =>
                        NearlyEqual(bounds.Left, current.Left) &&
                        NearlyEqual(bounds.Width, current.Width) &&
                        NearlyEqual(bounds.Height, current.Height) &&
                        NearlyEqual(
                            bounds.Left + bounds.Width / 2.0,
                            DesktopShellSurface.LayoutColumnCenterX));
            });

        Add(
            scenarios,
            "edge-hover-scrolls-both-directions-exclusively",
            () =>
            {
                surface.ScrollLayoutRailToBoundaryForTest(false);
                double topOffset = surface.LayoutRailVerticalOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                bool downArmed =
                    surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Down;
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromMilliseconds(200));
                double downOffset = surface.LayoutRailVerticalOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Up);
                bool upExclusive =
                    surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Up;
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromMilliseconds(100));
                double upOffset = surface.LayoutRailVerticalOffset;
                surface.StopLayoutRailAutoScrollForTest();
                return
                    downArmed &&
                    upExclusive &&
                    downOffset > topOffset &&
                    upOffset < downOffset &&
                    !surface.IsLayoutRailAutoScrolling;
            });

        Add(
            scenarios,
            "edge-boundaries-remain-stable-after-pointer-leave",
            () =>
            {
                surface.SelectLayoutForTest(LayoutPreset.FourQuadrants);
                surface.ScrollLayoutRailToBoundaryForTest(true);
                double bottomOffset = surface.LayoutRailScrollableHeight;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Up);
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0 / 60.0));
                bool movedUp =
                    surface.LayoutRailVerticalOffset < bottomOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0));
                bool bottomStable =
                    NearlyEqual(
                        surface.LayoutRailVerticalOffset,
                        bottomOffset) &&
                    !surface.CanScrollLayoutRailDown &&
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    surface.IsSelectedLayoutFullyVisible &&
                    surface.CurrentLayout == LayoutPreset.FourQuadrants &&
                    LayoutGlyphsSharePermanentAxis(surface) &&
                    MaskEndIsOpaque(
                        surface.LayoutRailFeatherMask,
                        top: false);

                surface.SelectLayoutForTest(LayoutPreset.Maximized);
                surface.ScrollLayoutRailToBoundaryForTest(false);
                double topOffset = surface.LayoutRailVerticalOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0 / 60.0));
                bool movedDown =
                    surface.LayoutRailVerticalOffset > topOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Up);
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0));
                return
                    movedUp &&
                    bottomStable &&
                    movedDown &&
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    NearlyEqual(
                        surface.LayoutRailVerticalOffset,
                        topOffset) &&
                    !surface.CanScrollLayoutRailUp &&
                    surface.IsSelectedLayoutFullyVisible &&
                    surface.CurrentLayout == LayoutPreset.Maximized &&
                    LayoutGlyphsSharePermanentAxis(surface) &&
                    MaskEndIsOpaque(
                        surface.LayoutRailFeatherMask,
                        top: true);
            });

        Add(
            scenarios,
            "auto-scroll-preserves-selection-and-stops-on-leave",
            () =>
            {
                LayoutPreset selected = surface.CurrentLayout;
                surface.ScrollLayoutRailToBoundaryForTest(false);
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0 / 60.0));
                double offsetBeforeStop =
                    surface.LayoutRailVerticalOffset;
                bool selectionPreserved =
                    surface.CurrentLayout == selected &&
                    surface.SelectedLayoutDefinition?.Preset == selected &&
                    surface.CurrentLayoutGlyph.Preset == selected;
                surface.StopLayoutRailAutoScrollForTest();
                return
                    selectionPreserved &&
                    NearlyEqual(
                        surface.LayoutRailVerticalOffset,
                        offsetBeforeStop) &&
                    surface.IsLayoutRailOpen &&
                    !surface.IsLayoutRailAutoScrolling;
            });

        Add(
            scenarios,
            "edge-feather-signals-top-middle-and-bottom-capability",
            () =>
            {
                surface.ScrollLayoutRailToBoundaryForTest(false);
                bool topState =
                    !surface.CanScrollLayoutRailUp &&
                    surface.CanScrollLayoutRailDown &&
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [255, 255, 255, 255, 255, 128, 51, 0]);

                surface.ScrollLayoutRailToFractionForTest(0.5);
                bool middleState =
                    surface.CanScrollLayoutRailUp &&
                    surface.CanScrollLayoutRailDown &&
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [0, 51, 128, 255, 255, 128, 51, 0]);

                surface.ScrollLayoutRailToBoundaryForTest(true);
                bool bottomState =
                    surface.CanScrollLayoutRailUp &&
                    !surface.CanScrollLayoutRailDown &&
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [0, 51, 128, 255, 255, 255, 255, 255]);

                return
                    topState &&
                    middleState &&
                    bottomState &&
                    surface.IsLayoutRailOpen;
            });

        Add(
            scenarios,
            "rail-feather-mask-is-continuous-and-symmetric",
            () =>
            {
                surface.ScrollLayoutRailToFractionForTest(0.5);
                LinearGradientBrush mask = surface.LayoutRailFeatherMask;
                GradientStopCollection stops = mask.GradientStops;
                if (stops.Count != 8)
                {
                    return false;
                }

                bool offsetsStrictlyIncrease =
                    Enumerable.Range(1, stops.Count - 1).All(index =>
                        stops[index].Offset > stops[index - 1].Offset);
                bool mirrorSymmetric =
                    Enumerable.Range(0, stops.Count).All(index =>
                        NearlyEqual(
                            stops[index].Offset +
                            stops[^(index + 1)].Offset,
                            1.0) &&
                        stops[index].Color.A ==
                            stops[^(index + 1)].Color.A);
                bool boundedSlope =
                    Enumerable.Range(1, stops.Count - 1).All(index =>
                    {
                        double pixels =
                            (stops[index].Offset -
                                stops[index - 1].Offset) *
                            DesktopShellSurface.LayoutViewportHeight;
                        double alphaDelta = Math.Abs(
                            stops[index].Color.A -
                            stops[index - 1].Color.A);
                        return pixels > 0.0 && alphaDelta / pixels <= 16.0;
                    });
                double topFeatherPixels =
                    (stops[3].Offset - stops[0].Offset) *
                    DesktopShellSurface.LayoutViewportHeight;
                double bottomFeatherPixels =
                    (stops[^1].Offset - stops[^4].Offset) *
                    DesktopShellSurface.LayoutViewportHeight;
                double opaqueCorePixels =
                    (stops[4].Offset - stops[3].Offset) *
                    DesktopShellSurface.LayoutViewportHeight;
                return
                    mask.IsFrozen &&
                    mask.MappingMode == BrushMappingMode.RelativeToBoundingBox &&
                    mask.StartPoint == new Point(0.5, 0.0) &&
                    mask.EndPoint == new Point(0.5, 1.0) &&
                    offsetsStrictlyIncrease &&
                    mirrorSymmetric &&
                    boundedSlope &&
                    NearlyEqual(topFeatherPixels, 256.0) &&
                    NearlyEqual(bottomFeatherPixels, 256.0) &&
                    NearlyEqual(opaqueCorePixels, 44.0) &&
                    surface.LayoutRailViewport.ActualWidth == 86.0 &&
                    surface.LayoutRailViewport.ActualHeight ==
                        DesktopShellSurface.LayoutViewportHeight;
            });

        Add(
            scenarios,
            "high-contrast-change-stops-scroll-and-has-loaded-lifetime",
            () =>
            {
                surface.ApplyHighContrastStateForTest(false);
                surface.ScrollLayoutRailToFractionForTest(0.5);
                bool ordinaryMask =
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [0, 51, 128, 255, 255, 128, 51, 0]);
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                bool scrollingBeforeChange =
                    surface.IsLayoutRailAutoScrolling;

                surface.ApplyHighContrastStateForTest(true);
                bool highContrastApplied =
                    surface.HighContrastStateForTest &&
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [255, 255, 255, 255, 255, 255, 255, 255]);

                surface.ApplyHighContrastStateForTest(false);
                bool ordinaryMaskRestored =
                    !surface.HighContrastStateForTest &&
                    MaskMatches(
                        surface.LayoutRailFeatherMask,
                        [0, 51, 128, 255, 255, 128, 51, 0]);

                DesktopShellSurface lifecycleSurface = new();
                Window lifecycleHost = CreateTestHost(lifecycleSurface);
                bool subscribedWhileLoaded;
                bool unsubscribedAfterUnload;
                try
                {
                    lifecycleHost.Show();
                    PumpDispatcher();
                    subscribedWhileLoaded =
                        lifecycleSurface.IsSystemParametersSubscribedForTest;
                    lifecycleHost.Content = null;
                    lifecycleHost.Close();
                    PumpDispatcher();
                    unsubscribedAfterUnload =
                        !lifecycleSurface.IsSystemParametersSubscribedForTest;
                }
                finally
                {
                    lifecycleHost.Content = null;
                    if (lifecycleHost.IsVisible)
                    {
                        lifecycleHost.Close();
                    }
                }

                return
                    surface.IsSystemParametersSubscribedForTest &&
                    ordinaryMask &&
                    scrollingBeforeChange &&
                    highContrastApplied &&
                    ordinaryMaskRestored &&
                    subscribedWhileLoaded &&
                    unsubscribedAfterUnload;
            });

        Add(
            scenarios,
            "rail-pressure-velocity-is-nonlinear-monotone-and-symmetric",
            () =>
            {
                double zero =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(0.0);
                double quarter =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(0.25);
                double half =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(0.5);
                double threeQuarter =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(0.75);
                double edge =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(1.0);
                double negativeHalf =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(-0.5);
                double negativeEdge =
                    DesktopShellSurface.EvaluateLayoutRailVelocityForTest(-1.0);
                double adjacentWhite = Math.Abs(
                    DesktopShellSurface.EvaluateLayoutRailVelocityAtViewportYForTest(
                        DesktopShellSurface.LayoutRailViewportCenterY + 22.0));
                double adjacentUpper = Math.Abs(
                    DesktopShellSurface.EvaluateLayoutRailVelocityAtViewportYForTest(
                        DesktopShellSurface.LayoutRailViewportCenterY - 42.0));
                double adjacentLower = Math.Abs(
                    DesktopShellSurface.EvaluateLayoutRailVelocityAtViewportYForTest(
                        DesktopShellSurface.LayoutRailViewportCenterY + 86.0));
                TimeSpan reducedInterval =
                    DesktopShellSurface.ReducedMotionIntervalForTest;
                double adjacentReducedStep =
                    DesktopShellSurface.ReducedMotionStepForTest(adjacentWhite);
                double edgeReducedStep =
                    DesktopShellSurface.ReducedMotionStepForTest(edge);

                surface.ScrollLayoutRailToFractionForTest(0.5);
                surface.UpdateLayoutRailPointerForTest(
                    DesktopShellSurface.LayoutViewportHeight);
                bool downWired =
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Down &&
                    NearlyEqual(surface.LayoutRailVelocity, edge);
                surface.UpdateLayoutRailPointerForTest(
                    DesktopShellSurface.LayoutRailViewportCenterY);
                bool centerStops =
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    NearlyEqual(surface.LayoutRailVelocity, 0.0);
                surface.UpdateLayoutRailPointerForTest(
                    DesktopShellSurface.LayoutRailViewportCenterY + 4.0);
                bool nearCenterCreeps =
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Down &&
                    surface.LayoutRailVelocity > 0.0 &&
                    surface.LayoutRailVelocity < adjacentWhite;
                surface.UpdateLayoutRailPointerForTest(
                    DesktopShellSurface.LayoutRailViewportCenterY + 16.0);
                bool activationReached =
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Down &&
                    surface.LayoutRailVelocity >= 14.0 &&
                    surface.LayoutRailVelocity < 20.0;
                surface.UpdateLayoutRailPointerForTest(0.0);
                bool upWired =
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Up &&
                    NearlyEqual(surface.LayoutRailVelocity, -edge);
                surface.StopLayoutRailAutoScrollForTest();

                return
                    double.IsFinite(zero) &&
                    double.IsFinite(quarter) &&
                    double.IsFinite(half) &&
                    double.IsFinite(threeQuarter) &&
                    double.IsFinite(edge) &&
                    NearlyEqual(zero, 0.0) &&
                    quarter >= adjacentWhite &&
                    quarter < half &&
                    half < threeQuarter &&
                    threeQuarter < edge &&
                    quarter < edge * 0.30 &&
                    threeQuarter > edge * 0.75 &&
                    NearlyEqual(negativeHalf, -half) &&
                    NearlyEqual(negativeEdge, -edge) &&
                    adjacentWhite >= 17.5 &&
                    adjacentUpper >= 27.5 &&
                    adjacentLower >= 52.5 &&
                    NearlyEqual(reducedInterval.TotalMilliseconds, 200.0) &&
                    adjacentReducedStep >= 3.5 &&
                    NearlyEqual(edgeReducedStep, 32.0) &&
                    downWired &&
                    centerStops &&
                    nearCenterCreeps &&
                    activationReached &&
                    upWired;
            });

        Add(
            scenarios,
            "white-glyph-handled-preview-mousemove-drives-perceptible-pressure",
            () =>
            {
                surface.ScrollLayoutRailToFractionForTest(0.5);
                LayoutPreset layoutBefore = surface.CurrentLayout;
                object? selectedBefore = surface.LayoutRailList.SelectedItem;

                bool slowRouted =
                    surface.RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(
                        DesktopShellSurface.LayoutRailViewportCenterY + 16.0);
                double slow = Math.Abs(surface.LayoutRailVelocity);
                bool middleRouted =
                    surface.RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(
                        DesktopShellSurface.LayoutRailViewportCenterY +
                        DesktopShellSurface.LayoutViewportHeight / 4.0);
                double middle = Math.Abs(surface.LayoutRailVelocity);
                bool edgeRouted =
                    surface.RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(
                        DesktopShellSurface.LayoutViewportHeight);
                double edge = Math.Abs(surface.LayoutRailVelocity);
                bool upperEdgeRouted =
                    surface.RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(
                        0.0);
                double upperEdge = surface.LayoutRailVelocity;
                surface.StopLayoutRailAutoScrollForTest();

                return
                    slowRouted &&
                    middleRouted &&
                    edgeRouted &&
                    upperEdgeRouted &&
                    slow >= 14.0 &&
                    slow < 20.0 &&
                    slow < middle &&
                    middle < edge &&
                    NearlyEqual(edge, 180.0) &&
                    NearlyEqual(upperEdge, -180.0) &&
                    surface.CurrentLayout == layoutBefore &&
                    ReferenceEquals(
                        surface.LayoutRailList.SelectedItem,
                        selectedBefore) &&
                    surface.LayoutRailList.SelectedItems.Count == 1 &&
                    LayoutGlyphsSharePermanentAxis(surface);
            });

        AddDetailed(
            scenarios,
            "actual-adjacent-layout-centers-drive-visible-bidirectional-scroll",
            () =>
            {
                IReadOnlyList<(
                    ListBoxItem Item,
                    UIElement HitSource,
                    double CenterY)> triplet =
                    surface.GetCenteredAdjacentRailItemsForTest();
                if (triplet.Count != 3)
                {
                    return (
                        false,
                        $"triplet-count={triplet.Count}/" +
                        surface.AdjacentRailProbeDetailForTest);
                }

                LayoutPreset layoutBefore = surface.CurrentLayout;
                object? selectedBefore = surface.LayoutRailList.SelectedItem;
                double offsetBefore = surface.LayoutRailVerticalOffset;
                double upperDistance = Math.Abs(
                    triplet[0].CenterY -
                    DesktopShellSurface.LayoutRailViewportCenterY);
                double lowerDistance = Math.Abs(
                    triplet[2].CenterY -
                    DesktopShellSurface.LayoutRailViewportCenterY);

                bool upperRouted =
                    surface.RaiseHandledLayoutRailMouseMoveForTest(
                        triplet[0].HitSource,
                        triplet[0].CenterY);
                double upperSpeed = Math.Abs(surface.LayoutRailVelocity);
                bool upperActive =
                    surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Up;
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0 / 30.0));
                double upperOffset = surface.LayoutRailVerticalOffset;
                surface.StopLayoutRailAutoScrollForTest();

                triplet = surface.GetCenteredAdjacentRailItemsForTest();
                if (triplet.Count != 3)
                {
                    return (
                        false,
                        $"recentered-triplet-count={triplet.Count}/" +
                        surface.AdjacentRailProbeDetailForTest);
                }

                double lowerOffsetBefore = surface.LayoutRailVerticalOffset;
                bool lowerRouted =
                    surface.RaiseHandledLayoutRailMouseMoveForTest(
                        triplet[2].HitSource,
                        triplet[2].CenterY);
                double lowerSpeed = Math.Abs(surface.LayoutRailVelocity);
                bool lowerActive =
                    surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.Down;
                surface.AdvanceLayoutRailAutoScrollForTest(
                    TimeSpan.FromSeconds(1.0 / 30.0));
                double lowerOffset = surface.LayoutRailVerticalOffset;
                surface.StopLayoutRailAutoScrollForTest();

                double upperDelta = offsetBefore - upperOffset;
                double lowerDelta = lowerOffset - lowerOffsetBefore;

                bool passed =
                    Math.Abs(
                        triplet[1].CenterY -
                        DesktopShellSurface.LayoutRailViewportCenterY) <= 0.5 &&
                    Math.Abs(upperDistance - lowerDistance) <= 0.5 &&
                    upperRouted &&
                    lowerRouted &&
                    upperActive &&
                    lowerActive &&
                    upperSpeed >= 40.0 &&
                    lowerSpeed >= 40.0 &&
                    Math.Abs(upperSpeed - lowerSpeed) <= 1.0 &&
                    upperDelta >= 1.0 &&
                    lowerDelta >= 1.0 &&
                    upperDelta <= 3.0 &&
                    lowerDelta <= 3.0 &&
                    surface.CurrentLayout == layoutBefore &&
                    ReferenceEquals(
                        surface.LayoutRailList.SelectedItem,
                        selectedBefore) &&
                    LayoutGlyphsSharePermanentAxis(surface);
                return (
                    passed,
                    $"centers={triplet[0].CenterY:F2}," +
                    $"{triplet[1].CenterY:F2},{triplet[2].CenterY:F2};" +
                    $"speed={upperSpeed:F2},{lowerSpeed:F2};" +
                    $"routed={upperRouted},{lowerRouted};" +
                    $"active={upperActive},{lowerActive};" +
                    $"delta={upperDelta:F2},{lowerDelta:F2}");
            });

        Add(
            scenarios,
            "first-and-last-layouts-are-revealed-while-rail-stays-visible",
            () =>
            {
                surface.SelectLayoutForTest(LayoutPreset.Maximized);
                bool firstVisible = surface.IsSelectedLayoutFullyVisible;
                double topOffset = surface.LayoutRailVerticalOffset;
                surface.SelectLayoutForTest(LayoutPreset.EqualColumns);
                bool visibleSelectionPreservedOffset =
                    surface.IsSelectedLayoutFullyVisible &&
                    NearlyEqual(
                        surface.LayoutRailVerticalOffset,
                        topOffset);
                surface.SelectLayoutForTest(LayoutPreset.FourQuadrants);
                return
                    firstVisible &&
                    visibleSelectionPreservedOffset &&
                    surface.IsSelectedLayoutFullyVisible &&
                    surface.IsLayoutRailOpen;
            });

        Add(
            scenarios,
            "every-catalog-layout-updates-current-slot",
            () =>
            {
                foreach (LayoutDefinition definition in LayoutCatalog.All)
                {
                    surface.SelectLayoutForTest(definition.Preset);
                    if (surface.CurrentLayout != definition.Preset ||
                        surface.CurrentLayoutGlyph.Preset != definition.Preset ||
                        surface.SelectedLayoutDefinition?.Preset != definition.Preset ||
                        surface.LayoutRailList.SelectedItems.Count != 1 ||
                        !surface.IsLayoutRailOpen)
                    {
                        return false;
                    }
                }

                return true;
            });

        Add(
            scenarios,
            "bottom-slot-matches-rail-glyph-box-and-axis",
            () =>
                surface.CurrentLayoutGlyph.Width ==
                    DesktopShellSurface.LayoutGlyphWidth &&
                surface.CurrentLayoutGlyph.Height ==
                    DesktopShellSurface.LayoutGlyphHeight &&
                surface.LayoutRailPanel.Margin.Left +
                    surface.LayoutRailPanel.Width / 2.0 ==
                    surface.CurrentLayoutButton.Width / 2.0 &&
                surface.CurrentLayoutButton.Width ==
                    DesktopShellSurface.LayoutAxisX);

        Add(
            scenarios,
            "orthogonal-axis-crosses-taskbar",
            () =>
                Canvas.GetLeft(surface.LayoutAxisLower) +
                    surface.LayoutAxisLower.Width ==
                    DesktopShellSurface.LayoutAxisX &&
                Canvas.GetTop(surface.LayoutAxisLower) ==
                    DesktopShellSurface.TaskbarTop &&
                Canvas.GetTop(surface.TaskbarChrome) ==
                    DesktopShellSurface.TaskbarTop &&
                surface.LayoutAxisLower.Width == 2.0 &&
                !surface.LayoutAxisUpper.IsHitTestVisible &&
                !surface.LayoutAxisLower.IsHitTestVisible &&
                surface.TaskbarChrome.BorderThickness.Top == 2.0);

        Add(
            scenarios,
            "taskbar-icons-begin-right-of-axis",
            () =>
                new[]
                {
                    surface.TaskbarSearchButton,
                    surface.TaskbarTaskViewButton,
                    surface.TaskbarExplorerButton,
                    surface.TaskbarTerminalButton,
                    surface.TaskbarSystemButton,
                }.All(
                    button =>
                        Canvas.GetLeft(button) >
                        DesktopShellSurface.LayoutAxisX));

        Add(
            scenarios,
            "explorer-is-right-side-floating-window",
            () =>
            {
                surface.RestoreExplorerForTest();
                return
                    Canvas.GetLeft(surface.ExplorerWindow) == 596.0 &&
                    Canvas.GetTop(surface.ExplorerWindow) == 63.0 &&
                    surface.ExplorerWindow.Width == 930.0 &&
                    surface.ExplorerWindow.Height == 667.0 &&
                    Canvas.GetLeft(surface.ExplorerWindow) +
                        surface.ExplorerWindow.Width < 1600.0 &&
                    Canvas.GetTop(surface.ExplorerWindow) +
                        surface.ExplorerWindow.Height <
                        DesktopShellSurface.TaskbarTop;
            });

        Add(
            scenarios,
            "maximized-layout-drives-explorer-state",
            () =>
            {
                surface.SelectLayoutForTest(LayoutPreset.NarrowLeftWideRight);
                surface.SelectLayoutForTest(LayoutPreset.Maximized);
                bool expanded =
                    surface.IsExplorerMaximized &&
                    Canvas.GetLeft(surface.ExplorerWindow) == 148.0 &&
                    Canvas.GetTop(surface.ExplorerWindow) == 24.0 &&
                    surface.ExplorerWindow.Width == 1428.0 &&
                    surface.ExplorerWindow.Height == 752.0;
                surface.SelectLayoutForTest(LayoutPreset.NarrowLeftWideRight);
                return
                    expanded &&
                    !surface.IsExplorerMaximized &&
                    Canvas.GetLeft(surface.ExplorerWindow) == 596.0 &&
                    surface.ExplorerWindow.Width == 930.0;
            });

        Add(
            scenarios,
            "inner-explorer-controls-preserve-layout-state",
            () =>
            {
                surface.SelectLayoutForTest(LayoutPreset.TopSplitBottomMain);
                Invoke(surface.ExplorerMinimizeButton);
                bool minimized =
                    surface.ExplorerWindow.Visibility == Visibility.Collapsed;
                Invoke(surface.TaskbarExplorerButton);
                bool restoredTiled =
                    surface.ExplorerWindow.Visibility == Visibility.Visible &&
                    surface.CurrentLayout == LayoutPreset.TopSplitBottomMain;
                Invoke(surface.TaskbarExplorerButton);
                bool minimizedFromActiveTaskbar =
                    surface.ExplorerWindow.Visibility == Visibility.Collapsed;
                Invoke(surface.TaskbarExplorerButton);
                bool restoredFromTaskbar =
                    surface.ExplorerWindow.Visibility == Visibility.Visible &&
                    surface.CurrentLayout == LayoutPreset.TopSplitBottomMain;
                Invoke(surface.ExplorerMaximizeButton);
                bool expanded =
                    surface.IsExplorerMaximized &&
                    surface.CurrentLayout == LayoutPreset.Maximized;
                Invoke(surface.ExplorerMinimizeButton);
                Invoke(surface.TaskbarExplorerButton);
                bool restoredMaximized =
                    surface.ExplorerWindow.Visibility == Visibility.Visible &&
                    surface.IsExplorerMaximized &&
                    surface.CurrentLayout == LayoutPreset.Maximized;
                Invoke(surface.ExplorerMaximizeButton);
                return
                    minimized &&
                    restoredTiled &&
                    minimizedFromActiveTaskbar &&
                    restoredFromTaskbar &&
                    expanded &&
                    restoredMaximized &&
                    !surface.IsExplorerMaximized &&
                    surface.CurrentLayout == LayoutPreset.TopSplitBottomMain &&
                    Canvas.GetLeft(surface.ExplorerWindow) == 596.0 &&
                    surface.ExplorerWindow.Width == 930.0;
            });

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        EdgeBarTestReceipt receipt = new(
            8,
            "jarvisv2-layout-rail-edge-bar-test",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            true,
            false,
            "not-run",
            scenarios);
        return receipt;
    }

    private static Window CreateTestHost(DesktopShellSurface surface) =>
        new()
        {
            Width = 1600,
            Height = 900,
            Left = -32000,
            Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Content = surface,
        };

    private static bool EscapeFromLayoutRailClosesHostedMainWindow()
    {
        MainWindow testHost = new()
        {
            Left = -32000,
            Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        bool hostClosed = false;
        testHost.Closed += (_, _) => hostClosed = true;
        try
        {
            testHost.Show();
            PumpDispatcher();
            DesktopShellSurface surface = testHost.PreviewSurface;
            PresentationSource? source =
                PresentationSource.FromVisual(surface.LayoutRailList);
            if (source is null)
            {
                return false;
            }

            surface.ScrollLayoutRailToFractionForTest(0.5);
            surface.BeginLayoutRailAutoScrollForTest(
                LayoutRailScrollDirection.Down);
            bool scrollingBeforeEscape =
                surface.IsLayoutRailAutoScrolling;

            KeyEventArgs preview =
                new(
                    Keyboard.PrimaryDevice,
                    source,
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
            surface.LayoutRailList.RaiseEvent(preview);
            bool previewAllowsGlobalRoute =
                !preview.Handled &&
                !surface.IsLayoutRailAutoScrolling;
            if (!previewAllowsGlobalRoute)
            {
                return false;
            }

            KeyEventArgs bubble =
                new(
                    Keyboard.PrimaryDevice,
                    source,
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                };
            surface.LayoutRailList.RaiseEvent(bubble);
            return scrollingBeforeEscape && hostClosed;
        }
        finally
        {
            if (testHost.IsVisible)
            {
                testHost.Close();
            }
        }
    }

    private static void PumpDispatcher()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Invoke(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static bool MaskMatches(
        LinearGradientBrush mask,
        IReadOnlyList<byte> expectedAlphas) =>
        mask.GradientStops.Count == expectedAlphas.Count &&
        mask.GradientStops
            .Select(stop => stop.Color.A)
            .SequenceEqual(expectedAlphas);

    private static bool MaskEndIsOpaque(
        LinearGradientBrush mask,
        bool top)
    {
        IEnumerable<GradientStop> endStops =
            top
                ? mask.GradientStops.Take(4)
                : mask.GradientStops.Skip(mask.GradientStops.Count - 4);
        return endStops.All(stop => stop.Color.A == byte.MaxValue);
    }

    private static bool LayoutGlyphsSharePermanentAxis(
        DesktopShellSurface surface)
    {
        Rect current = surface.CurrentLayoutGlyphBounds;
        IReadOnlyList<Rect> rail = surface.LayoutRailGlyphBounds;
        return
            rail.Count == surface.LayoutOptionCount &&
            rail.All(bounds =>
                NearlyEqual(bounds.Left, current.Left) &&
                NearlyEqual(bounds.Width, current.Width) &&
                NearlyEqual(bounds.Height, current.Height) &&
                NearlyEqual(
                    bounds.Left + bounds.Width / 2.0,
                    DesktopShellSurface.LayoutColumnCenterX));
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 0.01;

    private static void Add(
        ICollection<EdgeBarScenario> scenarios,
        string name,
        Func<bool> action)
    {
        try
        {
            bool passed = action();
            scenarios.Add(
                new(
                    name,
                    passed,
                    passed ? "passed" : "assertion-failed"));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }

    private static void AddDetailed(
        ICollection<EdgeBarScenario> scenarios,
        string name,
        Func<(bool Passed, string Detail)> action)
    {
        try
        {
            (bool passed, string detail) = action();
            scenarios.Add(new(name, passed, detail));
        }
        catch (Exception exception)
        {
            scenarios.Add(
                new(
                    name,
                    false,
                    $"{exception.GetType().Name}: {exception.Message}"));
        }
    }
}
