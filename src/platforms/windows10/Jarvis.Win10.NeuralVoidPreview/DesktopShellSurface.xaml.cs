using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;
using Jarvis.Win10.OwnedTaskManager;

namespace Jarvis.Win10.NeuralVoidPreview;

internal enum LayoutRailScrollDirection
{
    None = 0,
    Up = -1,
    Down = 1,
}

public partial class DesktopShellSurface :
    UserControl,
    INotifyPropertyChanged
{
    public const double LayoutGlyphWidth = 70.0;
    public const double LayoutGlyphHeight = 42.0;
    public const double LayoutItemHeight = 64.0;
    public const double LayoutViewportHeight = 556.0;
    public const double LayoutColumnCenterX = 63.0;
    public const double LayoutGlyphLeftX =
        LayoutColumnCenterX - LayoutGlyphWidth / 2.0;

    internal const double LayoutAxisX = 126.0;
    internal const double TaskbarTop = 800.0;
    internal const double LayoutRailCenterY = 330.0;
    internal const double LayoutRailViewportCenterY = LayoutViewportHeight / 2.0;
    internal static readonly Rect OwnedWorkArea = new(148.0, 24.0, 1428.0, 752.0);

    private static readonly OwnedWindowId ExplorerOwnedWindowId =
        new("explorer");
    private static readonly OwnedWindowId TaskManagerOwnedWindowId =
        new("task-manager");
    private const double ExplorerCompactWidthThreshold = 820.0;
    private const double ExplorerCompactHeightThreshold = 400.0;

    private static readonly TimeSpan RailReducedMotionInterval =
        TimeSpan.FromMilliseconds(200);
    private const double RailResponseHalfExtent = LayoutViewportHeight / 2.0;
    private const double RailCreepDistance = 16.0;
    private const double RailCreepVelocity = 8.0;
    private const double RailLinearPressureWeight = 0.65;
    private const double RailMaxVelocity = 180.0;
    private const double RailReducedMotionMinimumStep = 1.0;
    private const double RailReducedMotionMaximumStep =
        LayoutItemHeight / 2.0;
    private const double RailScrollBoundaryEpsilon = 0.5;
    private const double RailFeatherDepth = 256.0;
    private const double RailFeatherInnerOffset =
        RailFeatherDepth / LayoutViewportHeight;
    private const double RailFeatherMiddleOffset = 120.0 / LayoutViewportHeight;
    private const double RailFeatherOuterOffset = 36.0 / LayoutViewportHeight;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _railScrollTimer;
    private readonly ExplorerWorkspace _explorerWorkspace = new();
    private readonly OwnedWindowLayoutCoordinator _ownedWindowLayouts = new();
    private readonly LinearGradientBrush _layoutRailMaskNone =
        CreateLayoutRailFeatherMask(false, false);
    private readonly LinearGradientBrush _layoutRailMaskTop =
        CreateLayoutRailFeatherMask(true, false);
    private readonly LinearGradientBrush _layoutRailMaskBottom =
        CreateLayoutRailFeatherMask(false, true);
    private readonly LinearGradientBrush _layoutRailMaskBoth =
        CreateLayoutRailFeatherMask(true, true);
    private SolidColorBrush _accentBrush =
        CreateBrush(Color.FromRgb(240, 229, 0));
    private bool _draggingExplorer;
    private Point _explorerDragOffset;
    private Point _explorerDragStart;
    private bool _explorerDragStarted;
    private bool _draggingTaskManager;
    private Point _taskManagerDragOffset;
    private Point _taskManagerDragStart;
    private bool _taskManagerDragStarted;
    private Point? _ownedTitlePointerOverrideForTest;
    private MouseButtonState? _ownedTitleButtonOverrideForTest;
    private readonly DataTemplate _explorerFullItemTemplate;
    private readonly DataTemplate _explorerCompactItemTemplate;
    private bool _explorerCompactWidth;
    private bool _explorerCompactHeight;
    private ScrollViewer? _layoutRailScrollViewer;
    private LayoutRailScrollDirection _layoutRailScrollDirection;
    private bool _layoutRailRenderingSubscribed;
    private bool _layoutRailSmoothMotion;
    private double _layoutRailVelocity;
    private double _layoutRailRequestedOffset;
    private double? _layoutRailMinimumOffset;
    private TimeSpan? _layoutRailLastRenderingTime;
    private Window? _hostWindow;
    private bool _systemParametersSubscribed;
    private bool _highContrast = SystemParameters.HighContrast;
    private DispatcherOperation? _pendingLayoutReveal;
    private double? _layoutRailPointerYOverrideForTest;
    private int _layoutRailMouseMoveInvocationCount;
    private string _adjacentRailProbeDetailForTest = "not-run";
    private bool _refreshingExplorerPresentation;

    public DesktopShellSurface()
    {
        InitializeComponent();
        _explorerFullItemTemplate = ExplorerItemList.ItemTemplate;
        _explorerCompactItemTemplate =
            (DataTemplate)FindResource("ExplorerCompactItemTemplate");
        if (!_ownedWindowLayouts.TryRegister(
                ExplorerOwnedWindowId,
                OwnedWindowLifecycle.Visible,
                GetOwnedWindowBounds(ExplorerWindow)) ||
            !_ownedWindowLayouts.TryRegister(
                TaskManagerOwnedWindowId,
                OwnedWindowLifecycle.Closed,
                GetOwnedWindowBounds(TaskManagerWindow)))
        {
            throw new InvalidOperationException(
                "The owned desktop window registration could not be initialized.");
        }

        _explorerWorkspace.Changed += ExplorerWorkspace_OnChanged;
        LayoutRailHitRegion.AddHandler(
            Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(LayoutRailRegion_OnPreviewMouseMove),
            true);
        LayoutRailViewport.OpacityMask = _layoutRailMaskBoth;
        _clockTimer = new(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RefreshClock(),
            Dispatcher)
        {
            IsEnabled = false,
        };
        _railScrollTimer = new(
            RailReducedMotionInterval,
            DispatcherPriority.Background,
            LayoutRailScrollTimer_OnTick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        RefreshExplorerPresentation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        private set
        {
            _accentBrush = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(AccentBrush)));
        }
    }

    internal LayoutPreset? CurrentLayout => _ownedWindowLayouts.AppliedPreset;

    internal bool IsOwnedWindowLayoutFreeform => CurrentLayout is null;

    internal int VisibleOwnedWindowCount => _ownedWindowLayouts.VisibleWindowCount;

    internal int AvailableLayoutCount =>
        LayoutCatalog.All.Count(
            definition =>
                _ownedWindowLayouts.GetAvailability(definition.Preset).IsEnabled);

    internal string? ActiveOwnedWindowId =>
        _ownedWindowLayouts.ActiveWindowId?.Value;

    internal bool IsLayoutRailOpen =>
        LayoutRailPanel.Visibility == Visibility.Visible &&
        LayoutRailPanel.Opacity == 1.0 &&
        LayoutAxisScale.ScaleY == 1.0;

    internal int LayoutOptionCount => LayoutRailList.Items.Count;

    internal LayoutDefinition? SelectedLayoutDefinition =>
        LayoutRailList.SelectedItem as LayoutDefinition;

    internal bool IsExplorerMaximized =>
        IsOwnedWindowMaximized(ExplorerOwnedWindowId);

    internal bool IsTaskManagerMaximizedForTest =>
        IsOwnedWindowMaximized(TaskManagerOwnedWindowId);

    internal bool IsExplorerCompactWidthForTest => _explorerCompactWidth;

    internal bool IsExplorerCompactHeightForTest => _explorerCompactHeight;

    internal bool IsTaskManagerVisibleForTest =>
        TaskManagerWindow.Visibility == Visibility.Visible;

    internal bool IsTaskManagerRunningForTest =>
        TaskManagerRunningIndicator.Visibility == Visibility.Visible;

    internal bool IsTaskManagerSamplingForTest =>
        TaskManagerPanel.IsSamplingActiveForTest;

    internal double TaskManagerIndicatorOpacityForTest =>
        TaskManagerRunningIndicator.Opacity;

    internal ExplorerWorkspace ExplorerWorkspaceForTest => _explorerWorkspace;

    internal string ExplorerBreadcrumbForTest => ExplorerBreadcrumbText.Text;

    internal int ExplorerVisibleItemCountForTest => ExplorerItemList.Items.Count;

    internal bool ExplorerEmptyStateVisibleForTest =>
        ExplorerEmptyState.Visibility == Visibility.Visible;

    internal bool ExplorerPreviewVisibleForTest =>
        ExplorerPreviewPanel.Visibility == Visibility.Visible;

    internal bool ExplorerItemListEnabledForTest => ExplorerItemList.IsEnabled;

    internal bool ExplorerPreviewCyclesKeyboardFocusForTest =>
        KeyboardNavigation.GetTabNavigation(ExplorerPreviewPanel) ==
            KeyboardNavigationMode.Cycle &&
        KeyboardNavigation.GetControlTabNavigation(ExplorerPreviewPanel) ==
            KeyboardNavigationMode.Cycle;

    internal string ExplorerPreviewNameForTest =>
        ExplorerPreviewNameText.Text;

    internal string? ExplorerSelectedItemIdForTest =>
        (ExplorerItemList.SelectedItem as ExplorerWorkspaceItem)?.Id;

    internal bool ExplorerOpenEnabledForTest => ExplorerOpenButton.IsEnabled;

    internal bool ExplorerRenameEnabledForTest =>
        ExplorerRenameButton.IsEnabled;

    internal bool ExplorerBackEnabledForTest => ExplorerBackButton.IsEnabled;

    internal bool ExplorerForwardEnabledForTest =>
        ExplorerForwardButton.IsEnabled;

    internal bool ExplorerUpEnabledForTest => ExplorerUpButton.IsEnabled;

    internal double LayoutRailVerticalOffset =>
        GetLayoutRailScrollViewer()?.VerticalOffset ?? 0.0;

    internal double LayoutRailScrollableHeight =>
        GetLayoutRailScrollViewer()?.ScrollableHeight ?? 0.0;

    internal bool IsLayoutRailAutoScrolling =>
        _layoutRailRenderingSubscribed || _railScrollTimer.IsEnabled;

    internal LayoutRailScrollDirection LayoutRailScrollDirection =>
        _layoutRailScrollDirection;

    internal double LayoutRailVelocity => _layoutRailVelocity;

    internal LinearGradientBrush LayoutRailFeatherMask =>
        LayoutRailViewport.OpacityMask as LinearGradientBrush ??
        throw new InvalidOperationException("layout-rail-mask-missing");

    internal string AdjacentRailProbeDetailForTest =>
        _adjacentRailProbeDetailForTest;

    internal bool IsSystemParametersSubscribedForTest =>
        _systemParametersSubscribed;

    internal bool HighContrastStateForTest => _highContrast;

    internal bool CanScrollLayoutRailUp =>
        GetLayoutRailScrollViewer() is ScrollViewer scrollViewer &&
        CanScrollLayoutRail(
            scrollViewer,
            LayoutRailScrollDirection.Up);

    internal bool CanScrollLayoutRailDown =>
        GetLayoutRailScrollViewer() is ScrollViewer scrollViewer &&
        CanScrollLayoutRail(
            scrollViewer,
            LayoutRailScrollDirection.Down);

    internal Rect CurrentLayoutGlyphBounds =>
        GetBoundsOnDesktop(CurrentLayoutGlyph);

    internal Rect? SelectedRailLayoutGlyphBounds
    {
        get
        {
            if (LayoutRailList.SelectedItem is not object selected ||
                LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                    selected) is not ListBoxItem item)
            {
                return null;
            }

            LayoutGlyph? glyph = FindVisualDescendant<LayoutGlyph>(item);
            return glyph is null ? null : GetBoundsOnDesktop(glyph);
        }
    }

    internal IReadOnlyList<Rect> LayoutRailGlyphBounds
    {
        get
        {
            List<Rect> bounds = [];
            foreach (object item in LayoutRailList.Items)
            {
                if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                        item) is ListBoxItem container &&
                    FindVisualDescendant<LayoutGlyph>(container) is
                        LayoutGlyph glyph)
                {
                    bounds.Add(GetBoundsOnDesktop(glyph));
                }
            }

            return bounds;
        }
    }

    internal bool IsSelectedLayoutFullyVisible
    {
        get
        {
            if (LayoutRailList.SelectedItem is not object selected ||
                LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                    selected) is not ListBoxItem item)
            {
                return false;
            }

            Point topLeft =
                item.TranslatePoint(new Point(0.0, 0.0), LayoutRailList);
            return
                topLeft.Y >= -RailScrollBoundaryEpsilon &&
                topLeft.Y + item.ActualHeight <=
                    LayoutRailList.ActualHeight + RailScrollBoundaryEpsilon;
        }
    }

    public void ApplyFrame(RgbFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AccentBrush = CreateBrush(
            Color.FromRgb(frame.Red, frame.Green, frame.Blue));
    }

    internal void PrepareLayoutRailForSnapshot() =>
        PrepareLayoutRailPresentation();

    internal void SetClockForSnapshot(DateTime timestamp) =>
        RefreshClock(timestamp);

    internal bool PrepareExplorerPreviewForSnapshot()
    {
        if (_explorerWorkspace.CurrentLocationId !=
                ExplorerWorkspace.DocumentsId &&
            !_explorerWorkspace.TryNavigateTo(ExplorerWorkspace.DocumentsId))
        {
            return false;
        }

        if (!_explorerWorkspace.SelectItem(ExplorerWorkspace.ProjectBriefId))
        {
            return false;
        }

        ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());
        return ExplorerPreviewVisibleForTest &&
            ExplorerPreviewNameForTest == "PROJECT_BRIEF_0826.DOCX";
    }

    internal bool PrepareTaskManagerForSnapshot()
    {
        TaskManagerPanel.StopSampling();
        TaskManagerPanel.ApplySnapshotForPreview(
            new OwnedTaskSnapshot(
                1,
                "captured-read-only-process-snapshot",
                new DateTimeOffset(2026, 8, 6, 6, 39, 0, TimeSpan.Zero),
                8,
                10,
                2,
                8,
                64,
                0,
                true,
                false,
                false,
                false,
                [
                    new("JARVIS2", 4200, 18.6, true, 440_401_920, 34),
                    new("CODEX", 5012, 11.8, true, 361_758_720, 29),
                    new("EXPLORER", 1244, 3.2, true, 188_743_680, 42),
                    new("DWM", 820, 2.1, true, 108_003_328, 19),
                    new("POWERSHELL", 6124, 1.6, true, 84_934_656, 17),
                    new("SEARCHAPP", 1940, 0.7, true, 73_400_320, 15),
                    new("SVCHOST", 1092, 0.3, true, 38_797_312, 12),
                    new("SYSTEM", 4, 0.1, true, 4_194_304, 198),
                ]));
        _ = _ownedWindowLayouts.TrySetLifecycle(
            TaskManagerOwnedWindowId,
            OwnedWindowLifecycle.Visible);
        ApplyCoordinatorBounds(
            TaskManagerOwnedWindowId,
            TaskManagerWindow);
        TaskManagerWindow.Visibility = Visibility.Visible;
        TaskManagerRunningIndicator.Visibility = Visibility.Visible;
        TaskManagerRunningIndicator.Opacity = 1.0;
        ActivateOwnedWindow(TaskManagerWindow);
        RefreshLayoutAvailability();
        return
            TaskManagerPanel.ProcessRows.Count == 8 &&
            TaskManagerPanel.SamplingStateForTest == "DETERMINISTIC PREVIEW";
    }

    internal bool SelectLayoutForTest(LayoutPreset preset)
    {
        bool applied = SelectLayout(preset, false);
        CancelPendingLayoutReveal();
        RevealSelectedLayout();
        return applied;
    }

    internal void PreviewLayoutForTest(LayoutPreset preset)
    {
        SyncRailSelection(preset);
        CancelPendingLayoutReveal();
        RevealSelectedLayout();
    }

    internal void RaiseRailKeyForTest(Key key) =>
        LayoutRailList.RaiseEvent(
            new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(LayoutRailList) ??
                    throw new InvalidOperationException(
                        "The rail test host was not connected."),
                0,
                key)
            {
                RoutedEvent = PreviewKeyDownEvent,
            });

    internal LayoutPreset? LayoutRailSelectedValueForTest =>
        LayoutRailList.SelectedValue is LayoutPreset preset ? preset : null;

    internal string InteractionStatusForTest =>
        InteractionStatusText.Text;

    internal bool TaskManagerProcessListHasKeyboardFocusForTest =>
        TaskManagerPanel.ProcessListHasKeyboardFocusForTest;

    internal string SelectedProcessNameForTest =>
        TaskManagerPanel.SelectedProcessName;

    internal void FocusLayoutRailForTest() => _ = LayoutRailList.Focus();

    internal void FocusTaskManagerProcessListForTest() =>
        _ = TaskManagerPanel.ProcessList.Focus();

    internal void RaiseTaskManagerKeyForTest(Key key) =>
        TaskManagerWindow.RaiseEvent(
            new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(TaskManagerWindow) ??
                    throw new InvalidOperationException(
                        "The Task Manager test host was not connected."),
                0,
                key)
            {
                RoutedEvent = PreviewKeyDownEvent,
            });

    internal bool LayoutRailHasKeyboardFocusForTest =>
        LayoutRailPanel.IsKeyboardFocusWithin;

    internal bool ExplorerItemsHaveKeyboardFocusForTest =>
        ExplorerItemList.IsKeyboardFocusWithin;

    internal bool TaskManagerPanelHasKeyboardFocusForTest =>
        TaskManagerPanel.IsKeyboardFocusWithin;

    internal void RestoreExplorerForTest()
    {
        RestoreExplorer();
    }

    internal bool IsLayoutAvailableForTest(LayoutPreset preset) =>
        _ownedWindowLayouts.GetAvailability(preset).IsEnabled;

    internal Rect GetOwnedWindowBoundsForTest(string id) =>
        id switch
        {
            "explorer" => GetOwnedWindowBounds(ExplorerWindow),
            "task-manager" => GetOwnedWindowBounds(TaskManagerWindow),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };

    internal void MoveOwnedWindowForTest(string id, Rect bounds)
    {
        FrameworkElement window = id switch
        {
            "explorer" => ExplorerWindow,
            "task-manager" => TaskManagerWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        SetOwnedWindowBounds(window, bounds);
        CompleteOwnedWindowManualMove(GetOwnedWindowId(window), window);
    }

    internal void RaiseOwnedTitleGestureForTest(
        string id,
        Point press,
        Point? move = null,
        bool loseCapture = false)
    {
        UIElement title = id switch
        {
            "explorer" => ExplorerTitleBar,
            "task-manager" => TaskManagerTitleBar,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        try
        {
            _ownedTitlePointerOverrideForTest = press;
            _ownedTitleButtonOverrideForTest = MouseButtonState.Pressed;
            title.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseDownEvent,
            });
            if (move is Point pointer)
            {
                _ownedTitlePointerOverrideForTest = pointer;
                title.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.MouseMoveEvent,
                });
            }

            if (loseCapture)
            {
                title.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.LostMouseCaptureEvent,
                });
            }
            else
            {
                _ownedTitleButtonOverrideForTest = MouseButtonState.Released;
                title.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent,
                });
            }
        }
        finally
        {
            _ownedTitlePointerOverrideForTest = null;
            _ownedTitleButtonOverrideForTest = null;
            title.ReleaseMouseCapture();
        }
    }

    internal void BeginLayoutRailAutoScrollForTest(
        LayoutRailScrollDirection direction) =>
        StartLayoutRailAutoScroll(direction, RailMaxVelocity / 2.0);

    internal void UpdateLayoutRailPointerForTest(double pointerY) =>
        UpdateLayoutRailScrollIntent(pointerY);

    internal bool RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(
        double pointerY)
    {
        LayoutGlyph? source = FindLayoutRailGlyphNearestViewportCenter();
        return source is not null &&
            RaiseHandledLayoutRailMouseMoveForTest(source, pointerY);
    }

    internal IReadOnlyList<(
        ListBoxItem Item,
        UIElement HitSource,
        double CenterY)> GetCenteredAdjacentRailItemsForTest()
    {
        _adjacentRailProbeDetailForTest = "starting";
        int middleIndex = -1;
        double bestDistance = double.MaxValue;
        for (int index = 1; index < LayoutRailList.Items.Count - 1; index++)
        {
            if (LayoutRailList.Items[index - 1] is not LayoutDefinition upper ||
                LayoutRailList.Items[index] is not LayoutDefinition middle ||
                LayoutRailList.Items[index + 1] is not LayoutDefinition lower ||
                upper.PaneCount != middle.PaneCount ||
                middle.PaneCount != lower.PaneCount)
            {
                continue;
            }

            if (!_ownedWindowLayouts.GetAvailability(middle.Preset).IsEnabled)
            {
                continue;
            }

            double distance = Math.Abs(
                index - (LayoutRailList.Items.Count - 1) / 2.0);
            if (distance < bestDistance)
            {
                middleIndex = index;
                bestDistance = distance;
            }
        }

        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (middleIndex < 1 ||
            scrollViewer is null ||
            GetRailGlyph(middleIndex) is not LayoutGlyph middleGlyph)
        {
            _adjacentRailProbeDetailForTest =
                $"setup-failed/index={middleIndex}/" +
                $"scroll={scrollViewer is not null}/" +
                $"glyph={GetRailGlyph(middleIndex) is not null}";
            return [];
        }

        Point middleOrigin = middleGlyph.TranslatePoint(
            new Point(0.0, 0.0),
            LayoutRailViewport);
        double middleCenter = middleOrigin.Y + middleGlyph.ActualHeight / 2.0;
        scrollViewer.ScrollToVerticalOffset(
            ClampLayoutRailOffset(
                scrollViewer,
                scrollViewer.VerticalOffset +
                    middleCenter - LayoutRailViewportCenterY));
        LayoutRailList.UpdateLayout();

        List<(
            ListBoxItem Item,
            UIElement HitSource,
            double CenterY)> result = [];
        for (int index = middleIndex - 1; index <= middleIndex + 1; index++)
        {
            if (LayoutRailList.ItemContainerGenerator.ContainerFromIndex(index) is not
                    ListBoxItem item ||
                GetRailGlyph(index) is not LayoutGlyph glyph)
            {
                _adjacentRailProbeDetailForTest =
                    $"realization-failed/index={index}";
                return [];
            }

            Point origin = glyph.TranslatePoint(
                new Point(0.0, 0.0),
                LayoutRailViewport);
            Point center = new(
                origin.X + glyph.ActualWidth / 2.0,
                origin.Y + glyph.ActualHeight / 2.0);
            IInputElement? rawHit = LayoutRailViewport.InputHitTest(center);
            if (rawHit is not DependencyObject hit ||
                hit is not UIElement hitElement)
            {
                _adjacentRailProbeDetailForTest =
                    $"hit-failed/index={index}/" +
                    $"point={center.X:F2},{center.Y:F2}/" +
                    $"hit={rawHit?.GetType().Name ?? "null"}";
                return [];
            }

            DependencyObject? owner =
                ItemsControl.ContainerFromElement(LayoutRailList, hit);
            if (!ReferenceEquals(owner, item))
            {
                _adjacentRailProbeDetailForTest =
                    $"owner-failed/index={index}/" +
                    $"hit={hit.GetType().Name}/" +
                    $"owner={owner?.GetType().Name ?? "null"}";
                return [];
            }

            result.Add((item, hitElement, center.Y));
        }

        _adjacentRailProbeDetailForTest = "passed";
        return result;
    }

    internal bool RaiseHandledLayoutRailMouseMoveForTest(
        UIElement source,
        double pointerY)
    {
        ArgumentNullException.ThrowIfNull(source);

        int invocationCount = _layoutRailMouseMoveInvocationCount;
        _layoutRailPointerYOverrideForTest = pointerY;
        try
        {
            MouseEventArgs eventArgs = new(
                Mouse.PrimaryDevice,
                Environment.TickCount)
            {
                RoutedEvent = Mouse.PreviewMouseMoveEvent,
                Source = source,
                Handled = true,
            };
            source.RaiseEvent(eventArgs);
        }
        finally
        {
            _layoutRailPointerYOverrideForTest = null;
        }

        return _layoutRailMouseMoveInvocationCount == invocationCount + 1;
    }

    internal static double EvaluateLayoutRailVelocityForTest(
        double signedPressure)
    {
        double pressure = Math.Clamp(Math.Abs(signedPressure), 0.0, 1.0);
        if (pressure <= 0.0)
        {
            return 0.0;
        }

        double pointerY =
            LayoutRailViewportCenterY +
            Math.CopySign(RailResponseHalfExtent * pressure, signedPressure);
        return EvaluateLayoutRailVelocity(pointerY);
    }

    internal static double EvaluateLayoutRailVelocityAtViewportYForTest(
        double pointerY) =>
        EvaluateLayoutRailVelocity(pointerY);

    internal static TimeSpan ReducedMotionIntervalForTest =>
        RailReducedMotionInterval;

    internal static double ReducedMotionStepForTest(double speed) =>
        GetReducedMotionStep(speed);

    internal void AdvanceLayoutRailAutoScrollForTest(
        TimeSpan elapsed) =>
        AdvanceLayoutRailAutoScroll(elapsed, true);

    internal void StopLayoutRailAutoScrollForTest() =>
        StopLayoutRailAutoScroll();

    internal void ApplyHighContrastStateForTest(bool highContrast) =>
        ApplyHighContrastState(highContrast);

    internal void ScrollLayoutRailToBoundaryForTest(bool bottom)
    {
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(
            bottom
                ? scrollViewer.ScrollableHeight
                : GetMinimumLayoutRailOffset(scrollViewer));
        LayoutRailList.UpdateLayout();
        UpdateLayoutRailEdgeFeather();
    }

    internal void ScrollLayoutRailToFractionForTest(double fraction)
    {
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        double minimum = GetMinimumLayoutRailOffset(scrollViewer);
        double clampedFraction = Math.Clamp(fraction, 0.0, 1.0);
        scrollViewer.ScrollToVerticalOffset(
            minimum +
            (scrollViewer.ScrollableHeight - minimum) * clampedFraction);
        LayoutRailList.UpdateLayout();
        UpdateLayoutRailEdgeFeather();
    }

    private void Surface_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        AttachHostWindow();
        AttachSystemParameters();
        RefreshClock();
        _clockTimer.Start();
        UpdateOwnedWindowLayoutDensity();
        PrepareLayoutRailPresentation();
        Focus();
    }

    private void Surface_OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _clockTimer.Stop();
        TaskManagerPanel.StopSampling();
        EndExplorerTitleDrag();
        EndTaskManagerTitleDrag();
        StopLayoutRailAutoScroll();
        CancelPendingLayoutReveal();
        DetachSystemParameters();
        DetachHostWindow();
        if (_layoutRailScrollViewer is not null)
        {
            _layoutRailScrollViewer.ScrollChanged -=
                LayoutRailScrollViewer_OnScrollChanged;
        }

        _layoutRailScrollViewer = null;
        _layoutRailMinimumOffset = null;
    }

    private void AttachSystemParameters()
    {
        if (_systemParametersSubscribed)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged +=
            SystemParameters_OnStaticPropertyChanged;
        _systemParametersSubscribed = true;
        ApplyHighContrastState(SystemParameters.HighContrast);
    }

    private void DetachSystemParameters()
    {
        if (!_systemParametersSubscribed)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -=
            SystemParameters_OnStaticPropertyChanged;
        _systemParametersSubscribed = false;
    }

    private void SystemParameters_OnStaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName) &&
            eventArgs.PropertyName != nameof(SystemParameters.HighContrast))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(
                    () =>
                    {
                        if (_systemParametersSubscribed)
                        {
                            ApplyHighContrastState(
                                SystemParameters.HighContrast);
                        }
                    }));
            return;
        }

        ApplyHighContrastState(SystemParameters.HighContrast);
    }

    private void ApplyHighContrastState(bool highContrast)
    {
        if (_highContrast == highContrast)
        {
            return;
        }

        _highContrast = highContrast;
        StopLayoutRailAutoScroll();
        UpdateLayoutRailEdgeFeather();
    }

    private void AttachHostWindow()
    {
        Window? host = Window.GetWindow(this);
        if (ReferenceEquals(host, _hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = host;
        if (_hostWindow is not null)
        {
            _hostWindow.Deactivated += HostWindow_OnDeactivated;
            _hostWindow.StateChanged += HostWindow_OnStateChanged;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.Deactivated -= HostWindow_OnDeactivated;
        _hostWindow.StateChanged -= HostWindow_OnStateChanged;
        _hostWindow = null;
    }

    private void HostWindow_OnDeactivated(object? sender, EventArgs eventArgs) =>
        StopLayoutRailAutoScroll();

    private void HostWindow_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized)
        {
            StopLayoutRailAutoScroll();
            TaskManagerPanel.StopSampling();
            return;
        }

        if (TaskManagerWindow.Visibility == Visibility.Visible &&
            TaskManagerRunningIndicator.Visibility == Visibility.Visible)
        {
            TaskManagerPanel.StartSampling();
        }
    }

    private void LayoutRailRegion_OnPreviewMouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        _layoutRailMouseMoveInvocationCount++;
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            StopLayoutRailAutoScroll();
            return;
        }

        UpdateLayoutRailScrollIntent(
            _layoutRailPointerYOverrideForTest ??
            eventArgs.GetPosition(LayoutRailViewport).Y);
    }

    private void LayoutRailRegion_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs) =>
        StopLayoutRailAutoScroll();

    private void LayoutRailRegion_OnMouseLeave(
        object sender,
        MouseEventArgs eventArgs)
        => StopLayoutRailAutoScroll();

    private void CurrentLayoutButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            FocusSelectedLayout);
    }

    private void LayoutRailList_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs eventArgs) =>
        StopLayoutRailAutoScroll();

    private void LayoutRailPanel_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(LayoutRailList, source) is not
                ListBoxItem { DataContext: LayoutDefinition definition } item)
        {
            return;
        }

        if (item.IsEnabled)
        {
            _ = SelectLayout(definition.Preset, true);
        }
        else
        {
            AnnounceUnavailableLayout(definition.Preset);
        }

        eventArgs.Handled = true;
    }

    private void LayoutRailPanel_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        StopLayoutRailAutoScroll();
        if (eventArgs.Key is Key.Enter or Key.Space &&
            LayoutRailList.SelectedItem is LayoutDefinition definition)
        {
            if (_ownedWindowLayouts.GetAvailability(definition.Preset).IsEnabled)
            {
                _ = SelectLayout(definition.Preset, true);
            }
            else
            {
                AnnounceUnavailableLayout(definition.Preset);
            }

            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            // Only consume Escape when the rail itself holds keyboard focus;
            // otherwise stop any active rail motion and let the key bubble to
            // the owning window's global Escape action.
            if (LayoutRailPanel.IsKeyboardFocusWithin)
            {
                ReturnRailFocusToActiveWindow();
                eventArgs.Handled = true;
            }

            return;
        }

        LayoutDefinition? target = eventArgs.Key switch
        {
            Key.Up => StepEnabledLayout(-1),
            Key.Down => StepEnabledLayout(1),
            Key.Home => EdgeEnabledLayout(first: true),
            Key.End => EdgeEnabledLayout(first: false),
            _ => null,
        };
        if (target is not null)
        {
            PreviewRailSelection(target);
            eventArgs.Handled = true;
        }
    }

    private LayoutDefinition? StepEnabledLayout(int delta)
    {
        List<LayoutDefinition> items =
            [.. LayoutRailList.Items.OfType<LayoutDefinition>()];
        for (int index = LayoutRailList.SelectedIndex + delta;
                index >= 0 && index < items.Count;
                index += delta)
        {
            if (_ownedWindowLayouts.GetAvailability(
                    items[index].Preset).IsEnabled)
            {
                return items[index];
            }
        }

        return null;
    }

    private LayoutDefinition? EdgeEnabledLayout(bool first)
    {
        List<LayoutDefinition> ordered =
            [.. LayoutRailList.Items.OfType<LayoutDefinition>()];
        if (!first)
        {
            ordered.Reverse();
        }

        return ordered.FirstOrDefault(definition =>
            _ownedWindowLayouts.GetAvailability(definition.Preset).IsEnabled);
    }

    private void PreviewRailSelection(LayoutDefinition definition)
    {
        SyncRailSelection(definition.Preset);
        CancelPendingLayoutReveal();
        RevealSelectedLayout();
        Announce($"LAYOUT PREVIEW / {definition.Id.ToUpperInvariant()}");
    }

    private void ReturnRailFocusToActiveWindow()
    {
        if (ActiveOwnedWindowId == "task-manager" &&
            TaskManagerWindow.Visibility == Visibility.Visible)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => _ = TaskManagerPanel.Focus()));
        }
        else
        {
            FocusExplorerItems();
        }

        Announce("RAIL CLOSED / FOCUS RETURNED TO ACTIVE WINDOW");
    }

    private bool SelectLayout(LayoutPreset preset, bool announce)
    {
        StopLayoutRailAutoScroll();
        LayoutDefinition definition = LayoutCatalog.Get(preset);
        OwnedLayoutAvailability availability =
            _ownedWindowLayouts.GetAvailability(preset);
        if (!availability.IsEnabled)
        {
            ApplyCurrentLayoutPresentation();
            RefreshLayoutAvailability();
            if (announce)
            {
                AnnounceUnavailableLayout(preset);
            }

            return false;
        }

        OwnedLayoutApplyResult result =
            _ownedWindowLayouts.TryApply(preset, OwnedWorkArea);
        if (!result.Succeeded)
        {
            ApplyCurrentLayoutPresentation();
            RefreshLayoutAvailability();
            if (announce)
            {
                Announce($"LAYOUT / {result.Status.ToString().ToUpperInvariant()}");
            }

            return false;
        }

        ApplyOwnedLayoutResult(result);
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        QueueRevealSelectedLayout();
        if (announce)
        {
            Announce($"LAYOUT / {definition.Id.ToUpperInvariant()}");
        }

        return true;
    }

    private void ApplyCurrentLayoutPresentation()
    {
        if (_ownedWindowLayouts.AppliedPreset is not LayoutPreset preset)
        {
            LayoutRailList.SelectedIndex = -1;
            CurrentLayoutGlyph.IsFreeform = true;
            CurrentLayoutGlyph.IsSelected = true;
            const string freeformName = "Current layout: Freeform";
            AutomationProperties.SetName(CurrentLayoutButton, freeformName);
            AutomationProperties.SetHelpText(
                CurrentLayoutButton,
                "Owned windows keep their independent floating bounds.");
            CurrentLayoutButton.ToolTip = freeformName;
            UpdateOwnedWindowControlNames();
            return;
        }

        LayoutDefinition definition = LayoutCatalog.Get(preset);
        SyncRailSelection(preset);
        CurrentLayoutGlyph.IsFreeform = false;
        CurrentLayoutGlyph.Preset = preset;
        CurrentLayoutGlyph.IsSelected = true;
        string accessibleName =
            $"Current layout: {definition.AutomationName}";
        AutomationProperties.SetName(CurrentLayoutButton, accessibleName);
        AutomationProperties.SetHelpText(
            CurrentLayoutButton,
            $"Applied to {definition.PaneCount} owned window " +
            $"{(definition.PaneCount == 1 ? "pane" : "panes")}.");
        CurrentLayoutButton.ToolTip = accessibleName;
        UpdateOwnedWindowControlNames();
    }

    private void SyncRailSelection(LayoutPreset preset)
    {
        if (LayoutRailList.SelectedValue is LayoutPreset selected &&
            selected == preset)
        {
            return;
        }

        LayoutRailList.SelectedValue = preset;
    }

    private void PrepareLayoutRailPresentation()
    {
        ApplyCurrentLayoutPresentation();
        _layoutRailMinimumOffset = null;
        LayoutRailList.UpdateLayout();
        RefreshLayoutAvailability();
        RevealSelectedLayout();
    }

    private void FocusSelectedLayout()
    {
        if (LayoutRailList.SelectedItem is null)
        {
            LayoutDefinition? firstAvailable = LayoutCatalog.All.FirstOrDefault(
                definition =>
                    _ownedWindowLayouts.GetAvailability(definition.Preset).IsEnabled);
            if (firstAvailable is not null)
            {
                LayoutRailList.SelectedValue = firstAvailable.Preset;
            }
        }

        if (LayoutRailList.SelectedItem is not object selected)
        {
            return;
        }

        RevealSelectedLayout();
        if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(selected) is
            ListBoxItem item)
        {
            item.Focus();
        }
    }

    private void RefreshLayoutAvailability()
    {
        LayoutRailList.ApplyTemplate();
        LayoutRailList.UpdateLayout();
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                    definition) is not ListBoxItem item)
            {
                continue;
            }

            OwnedLayoutAvailability availability =
                _ownedWindowLayouts.GetAvailability(definition.Preset);
            item.IsEnabled = availability.IsEnabled;
            string helpText = availability.IsEnabled
                ? $"Apply {definition.AutomationName} to visible JARVIS windows."
                : availability.StatusText + ".";
            item.ToolTip = availability.IsEnabled
                ? definition.AutomationName
                : $"{definition.AutomationName} / {availability.StatusText}";
            AutomationProperties.SetHelpText(item, helpText);
            AutomationProperties.SetItemStatus(
                item,
                availability.IsEnabled ? "Available" : "Unavailable");
        }
    }

    private void AnnounceUnavailableLayout(LayoutPreset preset)
    {
        OwnedLayoutAvailability availability =
            _ownedWindowLayouts.GetAvailability(preset);
        Announce($"LAYOUT / {availability.StatusText}");
    }

    private void RevealSelectedLayout()
    {
        if (LayoutRailList.SelectedItem is not object selected)
        {
            return;
        }

        if (IsSelectedLayoutFullyVisible)
        {
            UpdateLayoutRailEdgeFeather();
            return;
        }

        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        int selectedIndex = LayoutRailList.Items.IndexOf(selected);
        if (scrollViewer is null || selectedIndex < 0)
        {
            return;
        }

        int anchorIndex = Math.Max(0, selectedIndex - 4);
        object anchor = LayoutRailList.Items[anchorIndex];
        ListBoxItem? anchorItem =
            LayoutRailList.ItemContainerGenerator.ContainerFromItem(anchor)
                as ListBoxItem;
        if (anchorItem is null)
        {
            LayoutRailList.ScrollIntoView(anchor);
            LayoutRailList.UpdateLayout();
            anchorItem =
                LayoutRailList.ItemContainerGenerator.ContainerFromItem(anchor)
                    as ListBoxItem;
            if (anchorItem is null)
            {
                return;
            }
        }

        double anchorOffset =
            GetItemContentOffset(anchorItem, scrollViewer);
        scrollViewer.ScrollToVerticalOffset(
            ClampLayoutRailOffset(scrollViewer, anchorOffset));
        LayoutRailList.UpdateLayout();
        UpdateLayoutRailEdgeFeather();
    }

    private void QueueRevealSelectedLayout()
    {
        CancelPendingLayoutReveal();
        _pendingLayoutReveal = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(
                () =>
                {
                    _pendingLayoutReveal = null;
                    RevealSelectedLayout();
                }));
    }

    private void CancelPendingLayoutReveal()
    {
        if (_pendingLayoutReveal is
            { Status: DispatcherOperationStatus.Pending } pending)
        {
            pending.Abort();
        }

        _pendingLayoutReveal = null;
    }

    private void UpdateLayoutRailScrollIntent(double pointerY)
    {
        double velocity = EvaluateLayoutRailVelocity(pointerY);
        if (Math.Abs(velocity) < 0.01)
        {
            StopLayoutRailAutoScroll();
            return;
        }

        LayoutRailScrollDirection direction =
            velocity < 0.0
                ? LayoutRailScrollDirection.Up
                : LayoutRailScrollDirection.Down;
        StartLayoutRailAutoScroll(direction, Math.Abs(velocity));
    }

    private static double EvaluateLayoutRailVelocity(double pointerY)
    {
        double displacement =
            Math.Clamp(
                pointerY,
                0.0,
                LayoutViewportHeight) -
            LayoutRailViewportCenterY;
        double distance = Math.Abs(displacement);
        if (distance <= 0.0)
        {
            return 0.0;
        }

        double activation = SmoothStep(
            Math.Min(distance / RailCreepDistance, 1.0));
        double pressure = Math.Clamp(
            distance / RailResponseHalfExtent,
            0.0,
            1.0);
        double response =
            RailLinearPressureWeight * pressure +
            (1.0 - RailLinearPressureWeight) * SmoothStep(pressure);
        double speed =
            RailCreepVelocity * activation +
            (RailMaxVelocity - RailCreepVelocity) * response;

        return Math.CopySign(speed, displacement);
    }

    private static double SmoothStep(double value)
    {
        double unit = Math.Clamp(value, 0.0, 1.0);
        return unit * unit * (3.0 - 2.0 * unit);
    }

    private void StartLayoutRailAutoScroll(
        LayoutRailScrollDirection direction,
        double speed)
    {
        if (direction == LayoutRailScrollDirection.None ||
            !double.IsFinite(speed) ||
            speed <= 0.0)
        {
            StopLayoutRailAutoScroll();
            return;
        }

        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null ||
            !CanScrollLayoutRail(scrollViewer, direction))
        {
            StopLayoutRailAutoScroll();
            return;
        }

        double signedVelocity =
            Math.Clamp(speed, 0.0, RailMaxVelocity) * (int)direction;
        if (_layoutRailSmoothMotion &&
            _layoutRailRenderingSubscribed)
        {
            _layoutRailScrollDirection = direction;
            _layoutRailVelocity = signedVelocity;
            return;
        }

        if (_layoutRailScrollDirection == direction &&
            _railScrollTimer.IsEnabled)
        {
            _layoutRailVelocity = signedVelocity;
            return;
        }

        bool smoothMotion = UsesSmoothLayoutRailMotion;
        _railScrollTimer.Stop();
        UnsubscribeLayoutRailRendering();
        _layoutRailScrollDirection = direction;
        _layoutRailVelocity = signedVelocity;
        _layoutRailRequestedOffset = scrollViewer.VerticalOffset;
        _layoutRailLastRenderingTime = null;
        _layoutRailSmoothMotion = smoothMotion;
        if (_layoutRailSmoothMotion)
        {
            SubscribeLayoutRailRendering();
        }
        else
        {
            _railScrollTimer.Interval = RailReducedMotionInterval;
            _railScrollTimer.Start();
        }
    }

    private static double GetReducedMotionStep(double speed) =>
        Math.Clamp(
            Math.Abs(speed) * RailReducedMotionInterval.TotalSeconds,
            RailReducedMotionMinimumStep,
            RailReducedMotionMaximumStep);

    private void LayoutRailRendering_OnRendering(
        object? sender,
        EventArgs eventArgs)
    {
        if (eventArgs is not RenderingEventArgs rendering)
        {
            return;
        }

        TimeSpan renderingTime = rendering.RenderingTime;
        if (_layoutRailLastRenderingTime is not TimeSpan previous)
        {
            _layoutRailLastRenderingTime = renderingTime;
            return;
        }

        if (renderingTime < previous)
        {
            _layoutRailLastRenderingTime = renderingTime;
            return;
        }

        if (renderingTime == previous)
        {
            return;
        }

        _layoutRailLastRenderingTime = renderingTime;
        AdvanceLayoutRailAutoScroll(
            TimeSpan.FromSeconds(
                Math.Clamp(
                    (renderingTime - previous).TotalSeconds,
                    0.0,
                    1.0 / 30.0)),
            false);
    }

    private void LayoutRailScrollTimer_OnTick(
        object? sender,
        EventArgs eventArgs) =>
        AdvanceLayoutRailAutoScroll(TimeSpan.Zero, false);

    private void AdvanceLayoutRailAutoScroll(
        TimeSpan elapsed,
        bool forceContinuous)
    {
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (_layoutRailScrollDirection == LayoutRailScrollDirection.None ||
            scrollViewer is null)
        {
            return;
        }

        double delta =
            forceContinuous || _layoutRailSmoothMotion
                ? _layoutRailVelocity * Math.Clamp(
                    elapsed.TotalSeconds,
                    0.0,
                    1.0 / 30.0)
                : GetReducedMotionStep(_layoutRailVelocity) *
                    (int)_layoutRailScrollDirection;
        double target = ClampLayoutRailOffset(
            scrollViewer,
            _layoutRailRequestedOffset + delta);
        double minimum = GetMinimumLayoutRailOffset(scrollViewer);
        double maximum = scrollViewer.ScrollableHeight;
        bool reachedBoundary =
            _layoutRailScrollDirection == LayoutRailScrollDirection.Up
                ? target <= minimum + RailScrollBoundaryEpsilon
                : target >= maximum - RailScrollBoundaryEpsilon;
        if (reachedBoundary)
        {
            target =
                _layoutRailScrollDirection == LayoutRailScrollDirection.Up
                    ? minimum
                    : maximum;
        }

        _layoutRailRequestedOffset = target;
        if (Math.Abs(scrollViewer.VerticalOffset - target) > 0.001)
        {
            scrollViewer.ScrollToVerticalOffset(target);
        }

        if (forceContinuous)
        {
            LayoutRailList.UpdateLayout();
            UpdateLayoutRailEdgeFeather();
        }

        if (reachedBoundary)
        {
            StopLayoutRailAutoScroll();
        }
    }

    private void StopLayoutRailAutoScroll()
    {
        _railScrollTimer.Stop();
        UnsubscribeLayoutRailRendering();
        _layoutRailScrollDirection = LayoutRailScrollDirection.None;
        _layoutRailVelocity = 0.0;
        _layoutRailSmoothMotion = false;
        _layoutRailLastRenderingTime = null;
    }

    private void SubscribeLayoutRailRendering()
    {
        if (_layoutRailRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += LayoutRailRendering_OnRendering;
        _layoutRailRenderingSubscribed = true;
    }

    private void UnsubscribeLayoutRailRendering()
    {
        if (!_layoutRailRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= LayoutRailRendering_OnRendering;
        _layoutRailRenderingSubscribed = false;
    }

    private void LayoutRailScrollViewer_OnScrollChanged(
        object sender,
        ScrollChangedEventArgs eventArgs)
    {
        if (Math.Abs(eventArgs.ExtentHeightChange) > 0.001 ||
            Math.Abs(eventArgs.ViewportHeightChange) > 0.001)
        {
            _layoutRailMinimumOffset = null;
        }

        UpdateLayoutRailEdgeFeather();
    }

    private void UpdateLayoutRailEdgeFeather()
    {
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        bool featherTop =
            !_highContrast &&
            CanScrollLayoutRail(
                scrollViewer,
                LayoutRailScrollDirection.Up);
        bool featherBottom =
            !_highContrast &&
            CanScrollLayoutRail(
                scrollViewer,
                LayoutRailScrollDirection.Down);
        LinearGradientBrush mask =
            (featherTop, featherBottom) switch
            {
                (true, true) => _layoutRailMaskBoth,
                (true, false) => _layoutRailMaskTop,
                (false, true) => _layoutRailMaskBottom,
                _ => _layoutRailMaskNone,
            };
        if (!ReferenceEquals(LayoutRailViewport.OpacityMask, mask))
        {
            LayoutRailViewport.OpacityMask = mask;
        }
    }

    private static LinearGradientBrush CreateLayoutRailFeatherMask(
        bool featherTop,
        bool featherBottom)
    {
        Color opaque = MaskColor(1.0);
        GradientStopCollection stops =
        [
            new(featherTop ? MaskColor(0.0) : opaque, 0.0),
            new(featherTop ? MaskColor(0.2) : opaque, RailFeatherOuterOffset),
            new(featherTop ? MaskColor(0.5) : opaque, RailFeatherMiddleOffset),
            new(opaque, RailFeatherInnerOffset),
            new(opaque, 1.0 - RailFeatherInnerOffset),
            new(featherBottom ? MaskColor(0.5) : opaque, 1.0 - RailFeatherMiddleOffset),
            new(featherBottom ? MaskColor(0.2) : opaque, 1.0 - RailFeatherOuterOffset),
            new(featherBottom ? MaskColor(0.0) : opaque, 1.0),
        ];
        LinearGradientBrush brush = new(stops)
        {
            StartPoint = new Point(0.5, 0.0),
            EndPoint = new Point(0.5, 1.0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            SpreadMethod = GradientSpreadMethod.Pad,
        };
        brush.Freeze();
        return brush;
    }

    private static Color MaskColor(double opacity) =>
        Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * byte.MaxValue),
            byte.MaxValue,
            byte.MaxValue,
            byte.MaxValue);

    private ScrollViewer? GetLayoutRailScrollViewer()
    {
        if (_layoutRailScrollViewer is not null)
        {
            return _layoutRailScrollViewer;
        }

        LayoutRailList.ApplyTemplate();
        LayoutRailList.UpdateLayout();
        _layoutRailScrollViewer =
            FindVisualDescendant<ScrollViewer>(LayoutRailList);
        if (_layoutRailScrollViewer is not null)
        {
            _layoutRailMinimumOffset = null;
            _layoutRailScrollViewer.ScrollChanged +=
                LayoutRailScrollViewer_OnScrollChanged;
        }

        return _layoutRailScrollViewer;
    }

    private double ClampLayoutRailOffset(
        ScrollViewer scrollViewer,
        double offset) =>
        Math.Clamp(
            offset,
            GetMinimumLayoutRailOffset(scrollViewer),
            scrollViewer.ScrollableHeight);

    private double GetMinimumLayoutRailOffset(ScrollViewer scrollViewer)
    {
        if (_layoutRailMinimumOffset is double cached)
        {
            return Math.Clamp(cached, 0.0, scrollViewer.ScrollableHeight);
        }

        if (LayoutRailList.Items.Count == 0 ||
            LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                LayoutRailList.Items[0]) is not ListBoxItem firstItem)
        {
            return 0.0;
        }

        _layoutRailMinimumOffset = Math.Clamp(
            GetItemContentOffset(firstItem, scrollViewer),
            0.0,
            scrollViewer.ScrollableHeight);
        return _layoutRailMinimumOffset.Value;
    }

    private static double GetItemContentOffset(
        ListBoxItem item,
        ScrollViewer scrollViewer)
    {
        Point relative =
            item.TransformToAncestor(scrollViewer)
                .Transform(new Point(0.0, 0.0));
        return scrollViewer.VerticalOffset + relative.Y;
    }

    private bool CanScrollLayoutRail(
        ScrollViewer scrollViewer,
        LayoutRailScrollDirection direction) =>
        direction switch
        {
            LayoutRailScrollDirection.Up =>
                scrollViewer.VerticalOffset >
                GetMinimumLayoutRailOffset(scrollViewer) +
                    RailScrollBoundaryEpsilon,
            LayoutRailScrollDirection.Down =>
                scrollViewer.VerticalOffset <
                scrollViewer.ScrollableHeight - RailScrollBoundaryEpsilon,
            _ => false,
        };

    private LayoutGlyph? FindLayoutRailGlyphNearestViewportCenter()
    {
        LayoutGlyph? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (object item in LayoutRailList.Items)
        {
            if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(item) is not
                    ListBoxItem container ||
                FindVisualDescendant<LayoutGlyph>(container) is not
                    LayoutGlyph glyph)
            {
                continue;
            }

            Point origin =
                glyph.TranslatePoint(new Point(0.0, 0.0), LayoutRailViewport);
            if (origin.Y + glyph.ActualHeight <= 0.0 ||
                origin.Y >= LayoutViewportHeight)
            {
                continue;
            }

            double distance = Math.Abs(
                origin.Y + glyph.ActualHeight / 2.0 -
                LayoutRailViewportCenterY);
            if (distance < nearestDistance)
            {
                nearest = glyph;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private LayoutGlyph? GetRailGlyph(int index) =>
        LayoutRailList.ItemContainerGenerator.ContainerFromIndex(index) is
                ListBoxItem container
            ? FindVisualDescendant<LayoutGlyph>(container)
            : null;

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private Rect GetBoundsOnDesktop(FrameworkElement element)
    {
        Point origin =
            element.TranslatePoint(new Point(0.0, 0.0), DesktopCanvas);
        return new(
            origin.X,
            origin.Y,
            element.ActualWidth,
            element.ActualHeight);
    }

    private bool UsesSmoothLayoutRailMotion =>
        SystemParameters.ClientAreaAnimation &&
        !_highContrast;

    private void DesktopShortcut_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: string destination })
        {
            return;
        }

        if (destination is "THIS PC" or "PROJECTS" or "ARCHIVE")
        {
            string locationId = destination switch
            {
                "THIS PC" => ExplorerWorkspace.ThisPcId,
                "PROJECTS" => ExplorerWorkspace.ProjectsId,
                _ => ExplorerWorkspace.ArchiveId,
            };
            ClearExplorerPreview();
            _ = _explorerWorkspace.TryNavigateTo(locationId);
            RestoreExplorer();
            AnnounceExplorerLocation();
            FocusExplorerItems();
            return;
        }

        Announce($"DESKTOP / {destination}");
    }

    private void ExplorerTitleBar_OnMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (FindParent<Button>(eventArgs.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        ActivateOwnedWindow(ExplorerWindow);
        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        _explorerDragStart = pointer;
        _explorerDragStarted = false;
        _explorerDragOffset = new(
            pointer.X - Canvas.GetLeft(ExplorerWindow),
            pointer.Y - Canvas.GetTop(ExplorerWindow));
        _draggingExplorer = true;
        ExplorerTitleBar.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void ExplorerTitleBar_OnMouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (!_draggingExplorer ||
            (_ownedTitleButtonOverrideForTest ?? eventArgs.LeftButton) != MouseButtonState.Pressed)
        {
            return;
        }

        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        MoveOwnedWindowFromTitle(
            ExplorerOwnedWindowId,
            ExplorerWindow,
            pointer,
            _explorerDragStart,
            _explorerDragOffset,
            ref _explorerDragStarted);
    }

    private void ExplorerTitleBar_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
        => EndExplorerTitleDrag();

    private void EndExplorerTitleDrag()
    {
        bool moved = _explorerDragStarted;
        _draggingExplorer = false;
        _explorerDragStarted = false;
        ExplorerTitleBar.ReleaseMouseCapture();
        if (moved)
        {
            CompleteOwnedWindowManualMove(
                ExplorerOwnedWindowId,
                ExplorerWindow);
        }
    }

    private void ExplorerMinimizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
        => MinimizeExplorer();

    private void MinimizeExplorer()
    {
        _ = _ownedWindowLayouts.TrySetLifecycle(
            ExplorerOwnedWindowId,
            OwnedWindowLifecycle.Minimized);
        ExplorerWindow.Visibility = Visibility.Collapsed;
        ExplorerRunningIndicator.Opacity = 0.45;
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        UpdateOwnedWindowZOrder();
        QueueTaskbarFocus(TaskbarExplorerButton);
        Announce("EXPLORER / MINIMIZED");
    }

    private void ExplorerMaximizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        RestoreExplorer();
        bool wasMaximized = IsExplorerMaximized;
        OwnedLayoutApplyResult result =
            _ownedWindowLayouts.TryToggleMaximize(
                ExplorerOwnedWindowId,
                OwnedWorkArea);
        ApplyOwnedLayoutResult(result);
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        Announce(wasMaximized
            ? "EXPLORER / RESTORED"
            : "EXPLORER / EXPANDED");
    }

    private void ExplorerCloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _ = _ownedWindowLayouts.TrySetLifecycle(
            ExplorerOwnedWindowId,
            OwnedWindowLifecycle.Closed);
        ExplorerWindow.Visibility = Visibility.Collapsed;
        ExplorerRunningIndicator.Visibility = Visibility.Collapsed;
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        UpdateOwnedWindowZOrder();
        QueueTaskbarFocus(TaskbarExplorerButton);
        Announce("EXPLORER / CLOSED");
    }

    private void TaskbarExplorerButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ExplorerWindow.Visibility == Visibility.Visible &&
            IsOwnedWindowActive(ExplorerOwnedWindowId))
        {
            MinimizeExplorer();
        }
        else
        {
            bool wasVisible = ExplorerWindow.Visibility == Visibility.Visible;
            RestoreExplorer();
            if (_ownedWindowLayouts.AppliedPreset == LayoutPreset.Maximized)
            {
                OwnedLayoutApplyResult result =
                    _ownedWindowLayouts.TryApply(
                        LayoutPreset.Maximized,
                        OwnedWorkArea);
                ApplyOwnedLayoutResult(result);
                ApplyCurrentLayoutPresentation();
            }

            Announce(wasVisible
                ? "EXPLORER / ACTIVE"
                : "EXPLORER / RESTORED");
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => _ = ExplorerItemList.Focus()));
        }
    }

    private void RestoreExplorer()
    {
        bool wasVisible =
            ExplorerWindow.Visibility == Visibility.Visible;
        _ = _ownedWindowLayouts.TrySetLifecycle(
            ExplorerOwnedWindowId,
            OwnedWindowLifecycle.Visible);
        ApplyCoordinatorBounds(ExplorerOwnedWindowId, ExplorerWindow);
        ExplorerWindow.Visibility = Visibility.Visible;
        ExplorerRunningIndicator.Visibility = Visibility.Visible;
        ExplorerRunningIndicator.Opacity = 1.0;
        ActivateOwnedWindow(ExplorerWindow);
        RefreshLayoutAvailability();
        if (!wasVisible)
        {
            ApplyCurrentLayoutPresentation();
        }
    }

    private void ExplorerBackButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoBack);

    private void ExplorerForwardButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoForward);

    private void ExplorerUpButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        NavigateExplorerHistory(_explorerWorkspace.TryGoUp);

    private void ExplorerOpenButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());

    private void ExplorerRenameButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        BeginExplorerRename();

    private void ExplorerAccessButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / ACCESS PANEL NOT AVAILABLE IN THIS PREVIEW");

    private void ExplorerNewFolderButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        CreateExplorerFolder();

    private void ExplorerWorkspace_OnChanged(
        object? sender,
        EventArgs eventArgs) =>
        RefreshExplorerPresentation();

    private void RefreshExplorerPresentation()
    {
        _refreshingExplorerPresentation = true;
        try
        {
            ExplorerWorkspaceSnapshot snapshot =
                _explorerWorkspace.CaptureSnapshot();
            string breadcrumb = string.Join(
                "  /  ",
                snapshot.Breadcrumb.Select(segment =>
                    segment.Name.ToUpperInvariant()));

            ExplorerBreadcrumbText.Text = breadcrumb;
            ExplorerBreadcrumbText.ToolTip = breadcrumb;
            AutomationProperties.SetName(
                ExplorerBreadcrumbText,
                $"Current Explorer location: {breadcrumb}");

            string folderName =
                snapshot.CurrentLocation.Name.ToUpperInvariant();
            ExplorerFolderNameText.Text = folderName;
            ExplorerFolderNameText.ToolTip = folderName;
            AutomationProperties.SetName(
                ExplorerFolderNameText,
                $"Current folder: {folderName}");

            int itemCount = snapshot.Items.Count;
            string itemCountText = itemCount == 1
                ? "1 ITEM"
                : $"{itemCount} ITEMS";
            ExplorerItemCountText.Text = itemCountText;
            AutomationProperties.SetName(
                ExplorerItemCountText,
                itemCountText);

            ExplorerItemList.ItemsSource = snapshot.Items;
            ExplorerWorkspaceItem? selected = snapshot.Items.FirstOrDefault(
                item => item.IsSelected);
            ExplorerItemList.SelectedItem = selected;

            ExplorerBackButton.IsEnabled = snapshot.CanGoBack;
            ExplorerForwardButton.IsEnabled = snapshot.CanGoForward;
            ExplorerUpButton.IsEnabled = snapshot.CanGoUp;
            ExplorerOpenButton.IsEnabled = selected is not null;
            ExplorerRenameButton.IsEnabled = selected is not null;
            ExplorerAccessButton.IsEnabled = false;
            ExplorerNewFolderButton.IsEnabled = true;
            ExplorerEmptyState.Visibility = snapshot.IsEmpty
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (snapshot.RenameItemId is null)
            {
                ClearExplorerRenameError();
            }
        }
        finally
        {
            _refreshingExplorerPresentation = false;
        }
    }

    private void ClearExplorerRenameError()
    {
        ExplorerRenameErrorText.Text = string.Empty;
        ExplorerRenameErrorText.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(
            ExplorerRenameErrorText,
            "No Explorer rename error");
    }

    private void ShowExplorerRenameError(string message)
    {
        ExplorerRenameErrorText.Text = message.ToUpperInvariant();
        ExplorerRenameErrorText.Visibility = Visibility.Visible;
        AutomationProperties.SetName(
            ExplorerRenameErrorText,
            $"Explorer rename error: {message}");
    }

    private void NavigateExplorerHistory(Func<bool> navigation)
    {
        ClearExplorerPreview();
        ClearExplorerRenameError();
        if (navigation())
        {
            AnnounceExplorerLocation();
        }
        else
        {
            Announce("EXPLORER / NAVIGATION BOUNDARY");
        }

        FocusExplorerItems();
    }

    private void AnnounceExplorerLocation()
    {
        string location = string.Join(
            " / ",
            _explorerWorkspace.Breadcrumb.Select(segment => segment.Name));
        Announce($"EXPLORER / {location}");
    }

    private void FocusExplorerItems()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (ExplorerWindow.Visibility == Visibility.Visible)
                {
                    _ = ExplorerItemList.Focus();
                }
            }));
    }

    private void ApplyExplorerOpenResult(ExplorerOpenResult result)
    {
        ClearExplorerRenameError();
        switch (result.Disposition)
        {
            case ExplorerOpenDisposition.Navigated:
                ClearExplorerPreview();
                AnnounceExplorerLocation();
                FocusExplorerItems();
                break;
            case ExplorerOpenDisposition.PreviewRequested:
                ExplorerPreviewRequest request = result.PreviewRequest ??
                    throw new InvalidOperationException(
                        "explorer-preview-request-missing");
                ShowExplorerPreview(request);
                Announce(
                    $"EXPLORER / OWNED PREVIEW / {request.DisplayName}");
                break;
            default:
                ClearExplorerPreview();
                Announce("EXPLORER / SELECT AN ITEM");
                FocusExplorerItems();
                break;
        }
    }

    private void BeginExplorerRename()
    {
        ClearExplorerPreview();
        ExplorerRenameResult result =
            _explorerWorkspace.BeginRenameSelected();
        if (!result.Succeeded)
        {
            ShowExplorerRenameError(result.Message);
            Announce("EXPLORER / RENAME UNAVAILABLE");
            return;
        }

        ClearExplorerRenameError();
        Announce($"EXPLORER / RENAME / {result.Name}");
        QueueExplorerRenameFocus();
    }

    private void CreateExplorerFolder()
    {
        ClearExplorerPreview();
        ExplorerWorkspaceItem folder =
            _explorerWorkspace.CreateUniqueFolder();
        ClearExplorerRenameError();
        Announce($"EXPLORER / NEW FOLDER / {folder.Name}");
        QueueExplorerRenameFocus();
    }

    private void QueueExplorerRenameFocus()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                object? selected = ExplorerItemList.SelectedItem;
                if (selected is null)
                {
                    return;
                }

                ExplorerItemList.ScrollIntoView(selected);
                ExplorerItemList.UpdateLayout();
                ListBoxItem? container =
                    ExplorerItemList.ItemContainerGenerator.ContainerFromItem(
                        selected) as ListBoxItem;
                TextBox? editor = container is null
                    ? null
                    : FindVisualDescendant<TextBox>(container);
                if (editor is null || editor.Visibility != Visibility.Visible)
                {
                    return;
                }

                _ = editor.Focus();
                editor.SelectAll();
            }));
    }

    private void CommitExplorerRename(TextBox editor)
    {
        if (editor.DataContext is not ExplorerWorkspaceItem item ||
            !StringComparer.Ordinal.Equals(
                item.Id,
                _explorerWorkspace.RenameItemId))
        {
            return;
        }

        ExplorerRenameResult result =
            _explorerWorkspace.CommitRename(editor.Text);
        if (result.Succeeded)
        {
            ClearExplorerRenameError();
            Announce($"EXPLORER / RENAMED / {result.Name}");
            FocusExplorerItems();
            return;
        }

        ShowExplorerRenameError(result.Message);
        Announce("EXPLORER / RENAME NEEDS CORRECTION");
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                _ = editor.Focus();
                editor.SelectAll();
            }));
    }

    private void CancelExplorerRename()
    {
        if (!_explorerWorkspace.CancelRename())
        {
            return;
        }

        ClearExplorerRenameError();
        Announce("EXPLORER / RENAME CANCELLED");
        FocusExplorerItems();
    }

    private void ShowExplorerPreview(ExplorerPreviewRequest request)
    {
        ExplorerPreviewNameText.Text = request.DisplayName;
        ExplorerPreviewNameText.ToolTip = request.DisplayName;
        AutomationProperties.SetName(
            ExplorerPreviewNameText,
            $"Preview item: {request.DisplayName}");
        ExplorerPreviewTypeText.Text =
            $"{request.TypeLabel} / SESSION OBJECT";
        ExplorerPreviewKindText.Text = request.PreviewKind
            .Replace('-', ' ')
            .ToUpperInvariant();
        ExplorerItemList.IsEnabled = false;
        ExplorerPreviewPanel.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _ = ExplorerPreviewCloseButton.Focus()));
    }

    private void ClearExplorerPreview()
    {
        ExplorerPreviewPanel.Visibility = Visibility.Collapsed;
        ExplorerItemList.IsEnabled = true;
        ExplorerPreviewNameText.Text = string.Empty;
        ExplorerPreviewNameText.ToolTip = null;
        ExplorerPreviewTypeText.Text = string.Empty;
        ExplorerPreviewKindText.Text = string.Empty;
    }

    private void ExplorerPreviewCloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ClearExplorerPreview();
        Announce("EXPLORER / PREVIEW CLOSED");
        FocusExplorerItems();
    }

    private void ExplorerItemList_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_refreshingExplorerPresentation)
        {
            return;
        }

        ClearExplorerRenameError();
        ClearExplorerPreview();
        if (ExplorerItemList.SelectedItem is ExplorerWorkspaceItem item)
        {
            _ = _explorerWorkspace.SelectItem(item.Id);
            Announce($"EXPLORER / SELECTED / {item.Name}");
        }
        else
        {
            _explorerWorkspace.ClearSelection();
            Announce("EXPLORER / SELECTION CLEARED");
        }
    }

    private void ExplorerItemList_OnMouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left ||
            FindParent<ListBoxItem>(
                eventArgs.OriginalSource as DependencyObject) is not
                    ListBoxItem container ||
            container.DataContext is not ExplorerWorkspaceItem item)
        {
            return;
        }

        ApplyExplorerOpenResult(_explorerWorkspace.OpenItem(item.Id));
        eventArgs.Handled = true;
    }

    private void ExplorerRenameEditor_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs)
    {
        if (!_refreshingExplorerPresentation && sender is TextBox editor)
        {
            CommitExplorerRename(editor);
        }
    }

    private void ExplorerWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        Key key = eventArgs.Key == Key.System
            ? eventArgs.SystemKey
            : eventArgs.Key;
        if (ExplorerPreviewPanel.Visibility == Visibility.Visible &&
            key == Key.Escape)
        {
            ClearExplorerPreview();
            Announce("EXPLORER / PREVIEW CLOSED");
            FocusExplorerItems();
            eventArgs.Handled = true;
            return;
        }

        if (_explorerWorkspace.RenameItemId is not null &&
            Keyboard.FocusedElement is TextBox editor)
        {
            if (key == Key.Enter)
            {
                CommitExplorerRename(editor);
                eventArgs.Handled = true;
            }
            else if (key == Key.Escape)
            {
                CancelExplorerRename();
                eventArgs.Handled = true;
            }

            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            if (key == Key.Left)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoBack);
                eventArgs.Handled = true;
            }
            else if (key == Key.Right)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoForward);
                eventArgs.Handled = true;
            }
            else if (key == Key.Up)
            {
                NavigateExplorerHistory(_explorerWorkspace.TryGoUp);
                eventArgs.Handled = true;
            }

            return;
        }

        if (key == Key.N &&
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CreateExplorerFolder();
            eventArgs.Handled = true;
            return;
        }

        if (!ExplorerItemList.IsKeyboardFocusWithin)
        {
            return;
        }

        if (key == Key.Enter)
        {
            ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());
            eventArgs.Handled = true;
        }
        else if (key == Key.F2)
        {
            BeginExplorerRename();
            eventArgs.Handled = true;
        }
        else if (key == Key.Back)
        {
            NavigateExplorerHistory(_explorerWorkspace.TryGoUp);
            eventArgs.Handled = true;
        }
    }

    private void TaskManagerTitleBar_OnMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (FindParent<Button>(eventArgs.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        ActivateOwnedWindow(TaskManagerWindow);
        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        _taskManagerDragStart = pointer;
        _taskManagerDragStarted = false;
        _taskManagerDragOffset = new(
            pointer.X - Canvas.GetLeft(TaskManagerWindow),
            pointer.Y - Canvas.GetTop(TaskManagerWindow));
        _draggingTaskManager = true;
        TaskManagerTitleBar.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void TaskManagerTitleBar_OnMouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (!_draggingTaskManager ||
            (_ownedTitleButtonOverrideForTest ?? eventArgs.LeftButton) != MouseButtonState.Pressed)
        {
            return;
        }

        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        MoveOwnedWindowFromTitle(
            TaskManagerOwnedWindowId,
            TaskManagerWindow,
            pointer,
            _taskManagerDragStart,
            _taskManagerDragOffset,
            ref _taskManagerDragStarted);
    }

    private void TaskManagerTitleBar_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
        => EndTaskManagerTitleDrag();

    private void EndTaskManagerTitleDrag()
    {
        bool moved = _taskManagerDragStarted;
        _draggingTaskManager = false;
        _taskManagerDragStarted = false;
        TaskManagerTitleBar.ReleaseMouseCapture();
        if (moved)
        {
            CompleteOwnedWindowManualMove(
                TaskManagerOwnedWindowId,
                TaskManagerWindow);
        }
    }

    private void OwnedTitleBar_OnLostMouseCapture(object sender, MouseEventArgs eventArgs)
    {
        if (!ReferenceEquals(eventArgs.OriginalSource, sender))
        {
            return;
        }

        if (ReferenceEquals(sender, ExplorerTitleBar))
        {
            EndExplorerTitleDrag();
        }
        else if (ReferenceEquals(sender, TaskManagerTitleBar))
        {
            EndTaskManagerTitleDrag();
        }
    }

    private void TaskManagerMinimizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        MinimizeTaskManager();

    private void TaskManagerMaximizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        RestoreTaskManager();
        bool wasMaximized = IsTaskManagerMaximizedForTest;
        OwnedLayoutApplyResult result =
            _ownedWindowLayouts.TryToggleMaximize(
                TaskManagerOwnedWindowId,
                OwnedWorkArea);
        ApplyOwnedLayoutResult(result);
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        Announce(wasMaximized
            ? "TASK MANAGER / RESTORED"
            : "TASK MANAGER / EXPANDED");
    }

    private void TaskManagerCloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        CloseTaskManager();

    private void TaskManagerWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            CloseTaskManager();
            eventArgs.Handled = true;
            return;
        }

        // The read-only process ledger keeps its rows non-focusable for the
        // compact visual language, so arrow/Home/End navigation is driven here
        // and announced through the shared live region.
        if (!TaskManagerPanel.ProcessListHasKeyboardFocusForTest)
        {
            return;
        }

        bool moved = eventArgs.Key switch
        {
            Key.Up => TaskManagerPanel.TryMoveProcessSelection(-1),
            Key.Down => TaskManagerPanel.TryMoveProcessSelection(1),
            Key.Home => TaskManagerPanel.TrySelectEdgeProcess(first: true),
            Key.End => TaskManagerPanel.TrySelectEdgeProcess(first: false),
            _ => false,
        };
        if (moved)
        {
            AnnounceSelectedProcess();
            eventArgs.Handled = true;
        }
    }

    private void AnnounceSelectedProcess()
    {
        string name = TaskManagerPanel.SelectedProcessName;
        if (name.Length != 0)
        {
            Announce($"PROCESS / {name.ToUpperInvariant()}");
        }
    }

    private void TaskbarSystemButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (TaskManagerWindow.Visibility == Visibility.Visible &&
            IsOwnedWindowActive(TaskManagerOwnedWindowId))
        {
            MinimizeTaskManager();
        }
        else
        {
            bool wasClosed = GetOwnedWindowState(TaskManagerOwnedWindowId)
                .Lifecycle == OwnedWindowLifecycle.Closed;
            RestoreTaskManager();
            if (_ownedWindowLayouts.AppliedPreset == LayoutPreset.Maximized)
            {
                OwnedLayoutApplyResult result =
                    _ownedWindowLayouts.TryApply(
                        LayoutPreset.Maximized,
                        OwnedWorkArea);
                ApplyOwnedLayoutResult(result);
                ApplyCurrentLayoutPresentation();
            }

            Announce(
                wasClosed
                    ? "TASK MANAGER / OPENED / READ ONLY"
                    : "TASK MANAGER / ACTIVE");
        }
    }

    private void RestoreTaskManager()
    {
        _ = _ownedWindowLayouts.TrySetLifecycle(
            TaskManagerOwnedWindowId,
            OwnedWindowLifecycle.Visible);
        ApplyCoordinatorBounds(
            TaskManagerOwnedWindowId,
            TaskManagerWindow);
        TaskManagerWindow.Visibility = Visibility.Visible;
        TaskManagerRunningIndicator.Visibility = Visibility.Visible;
        TaskManagerRunningIndicator.Opacity = 1.0;
        ActivateOwnedWindow(TaskManagerWindow);
        RefreshLayoutAvailability();
        TaskManagerPanel.StartSampling();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _ = TaskManagerMinimizeButton.Focus()));
    }

    private void MinimizeTaskManager()
    {
        TaskManagerPanel.StopSampling();
        _ = _ownedWindowLayouts.TrySetLifecycle(
            TaskManagerOwnedWindowId,
            OwnedWindowLifecycle.Minimized);
        TaskManagerWindow.Visibility = Visibility.Collapsed;
        TaskManagerRunningIndicator.Visibility = Visibility.Visible;
        TaskManagerRunningIndicator.Opacity = 0.45;
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        UpdateOwnedWindowZOrder();
        QueueTaskbarFocus(TaskbarSystemButton);
        Announce("TASK MANAGER / MINIMIZED / SAMPLING PAUSED");
    }

    private void CloseTaskManager()
    {
        TaskManagerPanel.StopSampling();
        _ = _ownedWindowLayouts.TrySetLifecycle(
            TaskManagerOwnedWindowId,
            OwnedWindowLifecycle.Closed);
        TaskManagerWindow.Visibility = Visibility.Collapsed;
        TaskManagerRunningIndicator.Visibility = Visibility.Collapsed;
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        UpdateOwnedWindowZOrder();
        QueueTaskbarFocus(TaskbarSystemButton);
        Announce("TASK MANAGER / CLOSED / SAMPLING STOPPED");
    }

    private void ActivateOwnedWindow(FrameworkElement window)
    {
        OwnedWindowId id = GetOwnedWindowId(window);
        _ = _ownedWindowLayouts.TryActivate(id);
        UpdateOwnedWindowZOrder();
    }

    private void OwnedWindow_OnPreviewMouseDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (sender is FrameworkElement window)
        {
            ActivateOwnedWindow(window);
        }
    }

    private void OwnedWindow_OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is FrameworkElement window)
        {
            ActivateOwnedWindow(window);
        }
    }

    private void MoveOwnedWindowFromTitle(
        OwnedWindowId id,
        FrameworkElement window,
        Point pointer,
        Point start,
        Point offset,
        ref bool started)
    {
        if (!started &&
            Math.Abs(pointer.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pointer.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        double left = Math.Round(Math.Clamp(
            pointer.X - offset.X, LayoutAxisX + 18.0, 1600.0 - window.Width - 18.0));
        double top = Math.Round(Math.Clamp(
            pointer.Y - offset.Y, 18.0, TaskbarTop - window.Height - 18.0));
        if (left == Canvas.GetLeft(window) && top == Canvas.GetTop(window))
        {
            return;
        }

        if (!started)
        {
            BeginOwnedWindowManualMove(id, window);
            started = true;
        }

        Canvas.SetLeft(window, left);
        Canvas.SetTop(window, top);
    }

    private void BeginOwnedWindowManualMove(
        OwnedWindowId id,
        FrameworkElement window)
    {
        if (_ownedWindowLayouts.AppliedPreset is null)
        {
            return;
        }

        _ = _ownedWindowLayouts.TrySetManualBounds(
            id,
            GetOwnedWindowBounds(window));
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
    }

    private void CompleteOwnedWindowManualMove(
        OwnedWindowId id,
        FrameworkElement window)
    {
        _ = _ownedWindowLayouts.TrySetManualBounds(
            id,
            GetOwnedWindowBounds(window));
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
    }

    private void ApplyOwnedLayoutResult(OwnedLayoutApplyResult result)
    {
        if (!result.Succeeded)
        {
            return;
        }

        foreach (OwnedWindowPlacement placement in result.Placements)
        {
            FrameworkElement window = placement.WindowId == ExplorerOwnedWindowId
                ? ExplorerWindow
                : placement.WindowId == TaskManagerOwnedWindowId
                    ? TaskManagerWindow
                    : throw new InvalidOperationException(
                        $"Unregistered owned window placement {placement.WindowId}.");
            SetOwnedWindowBounds(window, placement.Bounds);
        }

        UpdateOwnedWindowZOrder();
        UpdateOwnedWindowLayoutDensity();
    }

    private void ApplyCoordinatorBounds(
        OwnedWindowId id,
        FrameworkElement window)
    {
        SetOwnedWindowBounds(window, GetOwnedWindowState(id).Bounds);
        UpdateOwnedWindowLayoutDensity();
    }

    private void UpdateOwnedWindowZOrder()
    {
        Panel.SetZIndex(
            ExplorerWindow,
            IsOwnedWindowActive(ExplorerOwnedWindowId) ? 8 : 5);
        Panel.SetZIndex(
            TaskManagerWindow,
            IsOwnedWindowActive(TaskManagerOwnedWindowId) ? 8 : 5);
    }

    private void UpdateOwnedWindowLayoutDensity()
    {
        _explorerCompactWidth =
            ExplorerWindow.Width < ExplorerCompactWidthThreshold;
        _explorerCompactHeight =
            ExplorerWindow.Height < ExplorerCompactHeightThreshold;
        bool compact = _explorerCompactWidth || _explorerCompactHeight;
        ExplorerFolderPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExplorerActionPanel.Visibility = _explorerCompactHeight ? Visibility.Collapsed : Visibility.Visible;
        ExplorerStoragePanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExplorerFolderColumn.Width = compact
            ? new GridLength(0.0)
            : new GridLength(286.0);
        ExplorerFolderDividerColumn.Width = compact
            ? new GridLength(0.0)
            : new GridLength(1.0);
        ExplorerFullHeader.Visibility = _explorerCompactWidth
            ? Visibility.Collapsed
            : Visibility.Visible;
        ExplorerCompactHeader.Visibility = _explorerCompactWidth
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExplorerItemList.ItemTemplate = _explorerCompactWidth
            ? _explorerCompactItemTemplate
            : _explorerFullItemTemplate;
        ExplorerTitleText.Text = _explorerCompactWidth
            ? "JARVIS2 / EXPLORER"
            : "JARVIS2 / FILE EXPLORER";
        ExplorerActionRow.Height = _explorerCompactHeight
            ? new GridLength(0.0)
            : new GridLength(90.0);
        ExplorerStorageRow.Height = compact
            ? new GridLength(0.0)
            : new GridLength(101.0);
        TaskManagerPanel.SetLayoutDensity(
            TaskManagerWindow.Width < ExplorerCompactWidthThreshold,
            TaskManagerWindow.Height < ExplorerCompactHeightThreshold);
        TaskManagerTitleText.Text = TaskManagerWindow.Width < ExplorerCompactWidthThreshold
            ? "TASK MANAGER"
            : "JARVIS2 / TASK MANAGER";
    }

    private void UpdateOwnedWindowControlNames()
    {
        string explorerAction = IsExplorerMaximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(
            ExplorerMaximizeButton,
            $"{explorerAction} Explorer");
        ExplorerMaximizeButton.ToolTip = $"{explorerAction.ToUpperInvariant()} EXPLORER";
        string taskManagerAction = IsTaskManagerMaximizedForTest
            ? "Restore"
            : "Maximize";
        AutomationProperties.SetName(
            TaskManagerMaximizeButton,
            $"{taskManagerAction} Task Manager");
        TaskManagerMaximizeButton.ToolTip =
            $"{taskManagerAction.ToUpperInvariant()} TASK MANAGER";
    }

    private bool IsOwnedWindowActive(OwnedWindowId id) =>
        _ownedWindowLayouts.ActiveWindowId == id;

    private bool IsOwnedWindowMaximized(OwnedWindowId id) =>
        _ownedWindowLayouts.AppliedPreset == LayoutPreset.Maximized &&
        GetOwnedWindowState(id).AssignedPaneIndex == 0;

    private OwnedWindowState GetOwnedWindowState(OwnedWindowId id) =>
        _ownedWindowLayouts.TryGetWindow(id, out OwnedWindowState? state) &&
        state is not null
            ? state
            : throw new InvalidOperationException(
                $"Owned window {id} is not registered.");

    private static OwnedWindowId GetOwnedWindowId(FrameworkElement window) =>
        window.Name switch
        {
            "ExplorerWindow" => ExplorerOwnedWindowId,
            "TaskManagerWindow" => TaskManagerOwnedWindowId,
            _ => throw new ArgumentOutOfRangeException(
                nameof(window),
                window.Name,
                "The element is not a registered owned window."),
        };

    private static Rect GetOwnedWindowBounds(FrameworkElement window) =>
        new(
            Canvas.GetLeft(window),
            Canvas.GetTop(window),
            window.Width,
            window.Height);

    private static void SetOwnedWindowBounds(
        FrameworkElement window,
        Rect bounds)
    {
        Canvas.SetLeft(window, bounds.X);
        Canvas.SetTop(window, bounds.Y);
        window.Width = bounds.Width;
        window.Height = bounds.Height;
    }

    private void QueueTaskbarFocus(Button button)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _ = button.Focus()));
    }

    private void TaskbarUtilityButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: string command })
        {
            Announce($"TASKBAR / {command}");
        }
    }

    private void RefreshClock()
    {
        RefreshClock(DateTime.Now);
    }

    private void RefreshClock(DateTime now)
    {
        ClockTimeText.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        ClockDateText.Text = now.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
    }

    private void Announce(string text)
    {
        InteractionStatusText.Text = text;
        AutomationProperties.SetName(InteractionStatusText, text);
    }

    private static T? FindParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
