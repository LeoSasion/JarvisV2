using System.Windows;
using System.Windows.Controls;

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
        List<EdgeBarScenario> scenarios = [];
        DesktopShellSurface surface = new();
        surface.Measure(new Size(1600, 900));
        surface.Arrange(new Rect(0, 0, 1600, 900));
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
            "hover-rail-open-state",
            () =>
            {
                surface.SetLayoutRailOpenForSnapshot(true);
                return
                    surface.IsLayoutRailOpen &&
                    surface.LayoutRailPanel.Visibility == Visibility.Visible &&
                    surface.LayoutRailPanel.Opacity == 1.0 &&
                    surface.LayoutAxisScale.ScaleY == 1.0;
            });

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
            "edge-hover-stops-at-scroll-boundaries",
            () =>
            {
                surface.ScrollLayoutRailToBoundaryForTest(true);
                double bottomOffset = surface.LayoutRailVerticalOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                bool bottomStopped =
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    surface.LayoutRailVerticalOffset == bottomOffset;
                surface.ScrollLayoutRailToBoundaryForTest(false);
                double topOffset = surface.LayoutRailVerticalOffset;
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Up);
                return
                    bottomStopped &&
                    !surface.IsLayoutRailAutoScrolling &&
                    surface.LayoutRailScrollDirection ==
                        LayoutRailScrollDirection.None &&
                    surface.LayoutRailVerticalOffset == topOffset;
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
                    TimeSpan.FromMilliseconds(200));
                bool selectionPreserved =
                    surface.CurrentLayout == selected &&
                    surface.SelectedLayoutDefinition?.Preset == selected &&
                    surface.CurrentLayoutGlyph.Preset == selected;
                surface.StopLayoutRailAutoScrollForTest();
                return
                    selectionPreserved &&
                    surface.IsLayoutRailOpen &&
                    !surface.IsLayoutRailAutoScrolling;
            });

        Add(
            scenarios,
            "closing-rail-stops-scroll-and-reveals-selection",
            () =>
            {
                surface.ScrollLayoutRailToBoundaryForTest(false);
                surface.BeginLayoutRailAutoScrollForTest(
                    LayoutRailScrollDirection.Down);
                bool armed = surface.IsLayoutRailAutoScrolling;
                surface.SetLayoutRailOpenForSnapshot(false);
                bool stoppedOnClose =
                    !surface.IsLayoutRailOpen &&
                    !surface.IsLayoutRailAutoScrolling;
                surface.SetLayoutRailOpenForSnapshot(true);
                return
                    armed &&
                    stoppedOnClose &&
                    surface.IsSelectedLayoutFullyVisible;
            });

        Add(
            scenarios,
            "first-and-last-layouts-are-revealed-on-open",
            () =>
            {
                surface.SelectLayoutForTest(LayoutPreset.Maximized);
                surface.SetLayoutRailOpenForSnapshot(true);
                bool firstVisible = surface.IsSelectedLayoutFullyVisible;
                surface.SelectLayoutForTest(LayoutPreset.FourQuadrants);
                surface.SetLayoutRailOpenForSnapshot(true);
                return
                    firstVisible &&
                    surface.IsSelectedLayoutFullyVisible;
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
                        surface.IsLayoutRailOpen)
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
                    expanded &&
                    restoredMaximized &&
                    !surface.IsExplorerMaximized &&
                    surface.CurrentLayout == LayoutPreset.TopSplitBottomMain &&
                    Canvas.GetLeft(surface.ExplorerWindow) == 596.0 &&
                    surface.ExplorerWindow.Width == 930.0;
            });

        int passedCount = scenarios.Count(scenario => scenario.Passed);
        return new(
            5,
            "jarvisv2-layout-rail-edge-bar-test",
            passedCount == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passedCount,
            true,
            false,
            "not-run",
            scenarios);
    }

    private static void Invoke(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
}
