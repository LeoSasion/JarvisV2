using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;

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
    private LayoutPreset _currentLayout = LayoutPreset.LeftMainRightStack;
    private LayoutPreset _lastTiledLayout = LayoutPreset.LeftMainRightStack;
    private bool _draggingExplorer;
    private Point _explorerDragOffset;
    private Rect _explorerRestoreBounds = new(596, 63, 930, 667);
    private bool _explorerMaximized;
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

    public DesktopShellSurface()
    {
        InitializeComponent();
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
        ApplyLayoutSelection(_currentLayout);
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

    internal LayoutPreset CurrentLayout => _currentLayout;

    internal bool IsLayoutRailOpen =>
        LayoutRailPanel.Visibility == Visibility.Visible &&
        LayoutRailPanel.Opacity == 1.0 &&
        LayoutAxisScale.ScaleY == 1.0;

    internal int LayoutOptionCount => LayoutRailList.Items.Count;

    internal LayoutDefinition? SelectedLayoutDefinition =>
        LayoutRailList.SelectedItem as LayoutDefinition;

    internal bool IsExplorerMaximized => _explorerMaximized;

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

    internal void SelectLayoutForTest(LayoutPreset preset)
    {
        SelectLayout(preset, false);
        CancelPendingLayoutReveal();
        RevealSelectedLayout();
    }

    internal void RestoreExplorerForTest()
    {
        if (_explorerMaximized)
        {
            SelectLayout(_lastTiledLayout, false);
        }

        RestoreExplorer();
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
        PrepareLayoutRailPresentation();
        Focus();
    }

    private void Surface_OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _clockTimer.Stop();
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
                ListBoxItem { DataContext: LayoutDefinition definition })
        {
            return;
        }

        SelectLayout(definition.Preset, true);
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
            SelectLayout(definition.Preset, true);
            eventArgs.Handled = true;
        }
    }

    private void SelectLayout(LayoutPreset preset, bool announce)
    {
        StopLayoutRailAutoScroll();
        LayoutDefinition definition = LayoutCatalog.Get(preset);
        if (preset != LayoutPreset.Maximized)
        {
            _lastTiledLayout = preset;
        }

        _currentLayout = preset;
        ApplyLayoutSelection(preset);
        if (preset == LayoutPreset.Maximized)
        {
            RestoreExplorer();
            ExpandExplorerBounds();
        }
        else if (_explorerMaximized)
        {
            RestoreExplorerBounds();
        }

        QueueRevealSelectedLayout();
        if (announce)
        {
            Announce($"LAYOUT / {definition.Id.ToUpperInvariant()}");
        }
    }

    private void ApplyLayoutSelection(LayoutPreset preset)
    {
        LayoutDefinition definition = LayoutCatalog.Get(preset);
        SyncRailSelection(preset);
        CurrentLayoutGlyph.Preset = preset;
        CurrentLayoutGlyph.IsSelected = true;
        string accessibleName =
            $"Current layout: {definition.AutomationName}";
        AutomationProperties.SetName(CurrentLayoutButton, accessibleName);
        CurrentLayoutButton.ToolTip = accessibleName;
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
        SyncRailSelection(_currentLayout);
        _layoutRailMinimumOffset = null;
        LayoutRailList.UpdateLayout();
        RevealSelectedLayout();
    }

    private void FocusSelectedLayout()
    {
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
            RestoreExplorer();
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

        Point pointer = eventArgs.GetPosition(DesktopCanvas);
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
            eventArgs.LeftButton != MouseButtonState.Pressed ||
            _explorerMaximized)
        {
            return;
        }

        Point pointer = eventArgs.GetPosition(DesktopCanvas);
        double left = Math.Clamp(
            pointer.X - _explorerDragOffset.X,
            LayoutAxisX + 18.0,
            1600.0 - ExplorerWindow.Width - 18.0);
        double top = Math.Clamp(
            pointer.Y - _explorerDragOffset.Y,
            18.0,
            TaskbarTop - ExplorerWindow.Height - 18.0);
        Canvas.SetLeft(ExplorerWindow, Math.Round(left));
        Canvas.SetTop(ExplorerWindow, Math.Round(top));
    }

    private void ExplorerTitleBar_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        _draggingExplorer = false;
        ExplorerTitleBar.ReleaseMouseCapture();
    }

    private void ExplorerMinimizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
        => MinimizeExplorer();

    private void MinimizeExplorer()
    {
        ExplorerWindow.Visibility = Visibility.Collapsed;
        ExplorerRunningIndicator.Opacity = 0.45;
        Announce("EXPLORER / MINIMIZED");
    }

    private void ExplorerMaximizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_explorerMaximized)
        {
            SelectLayout(LayoutPreset.Maximized, false);
            Announce("EXPLORER / EXPANDED");
        }
        else
        {
            SelectLayout(_lastTiledLayout, false);
            Announce("EXPLORER / RESTORED");
        }
    }

    private void ExplorerCloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ExplorerWindow.Visibility = Visibility.Collapsed;
        ExplorerRunningIndicator.Visibility = Visibility.Collapsed;
        Announce("EXPLORER / CLOSED");
    }

    private void TaskbarExplorerButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ExplorerWindow.Visibility == Visibility.Visible)
        {
            MinimizeExplorer();
        }
        else
        {
            RestoreExplorer();
            Announce("EXPLORER / RESTORED");
        }
    }

    private void RestoreExplorer()
    {
        ExplorerWindow.Visibility = Visibility.Visible;
        ExplorerRunningIndicator.Visibility = Visibility.Visible;
        ExplorerRunningIndicator.Opacity = 1.0;
    }

    private void ExpandExplorerBounds()
    {
        if (_explorerMaximized)
        {
            return;
        }

        _explorerRestoreBounds = new(
            Canvas.GetLeft(ExplorerWindow),
            Canvas.GetTop(ExplorerWindow),
            ExplorerWindow.Width,
            ExplorerWindow.Height);
        Canvas.SetLeft(ExplorerWindow, 148.0);
        Canvas.SetTop(ExplorerWindow, 24.0);
        ExplorerWindow.Width = 1428.0;
        ExplorerWindow.Height = 752.0;
        _explorerMaximized = true;
    }

    private void RestoreExplorerBounds()
    {
        Canvas.SetLeft(ExplorerWindow, _explorerRestoreBounds.X);
        Canvas.SetTop(ExplorerWindow, _explorerRestoreBounds.Y);
        ExplorerWindow.Width = _explorerRestoreBounds.Width;
        ExplorerWindow.Height = _explorerRestoreBounds.Height;
        _explorerMaximized = false;
    }

    private void ExplorerBackButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / BACK");

    private void ExplorerForwardButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / FORWARD");

    private void ExplorerUpButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / THIS PC");

    private void ExplorerOpenButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / OPEN PROJECT_BRIEF_0826.DOCX");

    private void ExplorerRenameButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / RENAME READY");

    private void ExplorerAccessButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / ACCESS READY");

    private void ExplorerNewFolderButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        Announce("EXPLORER / NEW FOLDER READY");

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
