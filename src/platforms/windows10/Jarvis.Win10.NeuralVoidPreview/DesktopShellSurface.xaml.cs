using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    public const double LayoutItemHeight = 54.0;
    public const double LayoutViewportHeight = 556.0;
    public const double LayoutColumnCenterX = 63.0;
    public const double LayoutGlyphLeftX =
        LayoutColumnCenterX - LayoutGlyphWidth / 2.0;

    internal const double LayoutAxisX = 126.0;
    internal const double TaskbarTop = 800.0;

    private static readonly TimeSpan RailCloseDelay =
        TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan RailMotionDuration =
        TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan RailScrollDwell =
        TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan RailScrollFrameInterval =
        TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan RailReducedMotionInterval =
        TimeSpan.FromMilliseconds(240);
    private const double RailScrollVelocity = 168.0;
    private const double RailScrollBoundaryEpsilon = 0.5;

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _railCloseTimer;
    private readonly DispatcherTimer _railScrollTimer;
    private SolidColorBrush _accentBrush =
        CreateBrush(Color.FromRgb(240, 229, 0));
    private LayoutPreset _currentLayout = LayoutPreset.LeftMainRightStack;
    private LayoutPreset _lastTiledLayout = LayoutPreset.LeftMainRightStack;
    private bool _railOpen;
    private bool _draggingExplorer;
    private Point _explorerDragOffset;
    private Rect _explorerRestoreBounds = new(596, 63, 930, 667);
    private bool _explorerMaximized;
    private ScrollViewer? _layoutRailScrollViewer;
    private LayoutRailScrollDirection _layoutRailScrollDirection;
    private long _railScrollDwellDeadline;
    private long _railScrollLastTimestamp;

    public DesktopShellSurface()
    {
        InitializeComponent();
        _clockTimer = new(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RefreshClock(),
            Dispatcher)
        {
            IsEnabled = false,
        };
        _railCloseTimer = new(
            RailCloseDelay,
            DispatcherPriority.Input,
            RailCloseTimer_OnTick,
            Dispatcher)
        {
            IsEnabled = false,
        };
        _railScrollTimer = new(
            RailScrollFrameInterval,
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

    internal bool IsLayoutRailOpen => _railOpen;

    internal int LayoutOptionCount => LayoutRailList.Items.Count;

    internal LayoutDefinition? SelectedLayoutDefinition =>
        LayoutRailList.SelectedItem as LayoutDefinition;

    internal bool IsExplorerMaximized => _explorerMaximized;

    internal double LayoutRailVerticalOffset =>
        GetLayoutRailScrollViewer()?.VerticalOffset ?? 0.0;

    internal double LayoutRailScrollableHeight =>
        GetLayoutRailScrollViewer()?.ScrollableHeight ?? 0.0;

    internal bool IsLayoutRailAutoScrolling =>
        _railScrollTimer.IsEnabled;

    internal LayoutRailScrollDirection LayoutRailScrollDirection =>
        _layoutRailScrollDirection;

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

    internal void SetLayoutRailOpenForSnapshot(bool open) =>
        SetLayoutRailOpen(open, false);

    internal void SetClockForSnapshot(DateTime timestamp) =>
        RefreshClock(timestamp);

    internal void SelectLayoutForTest(LayoutPreset preset) =>
        SelectLayout(preset, false);

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
        StartLayoutRailAutoScroll(direction, false);

    internal void AdvanceLayoutRailAutoScrollForTest(
        TimeSpan elapsed) =>
        AdvanceLayoutRailAutoScroll(elapsed, true);

    internal void StopLayoutRailAutoScrollForTest() =>
        StopLayoutRailAutoScroll(true);

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
    }

    private void Surface_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        RefreshClock();
        _clockTimer.Start();
        Focus();
    }

    private void Surface_OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _clockTimer.Stop();
        _railCloseTimer.Stop();
        StopLayoutRailAutoScroll(false);
        _layoutRailScrollViewer = null;
    }

    private void Surface_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _railOpen)
        {
            StopLayoutRailAutoScroll(false);
            _railCloseTimer.Stop();
            SetLayoutRailOpen(false, true);
            CurrentLayoutButton.Focus();
            eventArgs.Handled = true;
        }
    }

    private void LayoutRailRegion_OnMouseEnter(
        object sender,
        MouseEventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        SetLayoutRailOpen(true, true);
    }

    private void LayoutRailRegion_OnMouseLeave(
        object sender,
        MouseEventArgs eventArgs)
    {
        StopLayoutRailAutoScroll(true);
        ScheduleLayoutRailClose();
    }

    private void CurrentLayoutButton_OnMouseEnter(
        object sender,
        MouseEventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        SetLayoutRailOpen(true, true);
    }

    private void CurrentLayoutButton_OnMouseLeave(
        object sender,
        MouseEventArgs eventArgs) =>
        ScheduleLayoutRailClose();

    private void CurrentLayoutButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        SetLayoutRailOpen(true, true);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            FocusSelectedLayout);
    }

    private void LayoutScrollUpHotZone_OnMouseEnter(
        object sender,
        MouseEventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        StartLayoutRailAutoScroll(LayoutRailScrollDirection.Up, true);
    }

    private void LayoutScrollDownHotZone_OnMouseEnter(
        object sender,
        MouseEventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        StartLayoutRailAutoScroll(LayoutRailScrollDirection.Down, true);
    }

    private void LayoutScrollHotZone_OnMouseLeave(
        object sender,
        MouseEventArgs eventArgs) =>
        StopLayoutRailAutoScroll(true);

    private void LayoutRailList_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs eventArgs) =>
        StopLayoutRailAutoScroll(false);

    private void LayoutRailPanel_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (!_railOpen ||
            eventArgs.OriginalSource is not DependencyObject source ||
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
        StopLayoutRailAutoScroll(false);
        if (eventArgs.Key is Key.Enter or Key.Space &&
            LayoutRailList.SelectedItem is LayoutDefinition definition)
        {
            SelectLayout(definition.Preset, true);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            _railCloseTimer.Stop();
            SetLayoutRailOpen(false, true);
            CurrentLayoutButton.Focus();
            eventArgs.Handled = true;
        }
    }

    private void SelectLayout(LayoutPreset preset, bool announce)
    {
        StopLayoutRailAutoScroll(false);
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

        _railCloseTimer.Stop();
        SetLayoutRailOpen(false, true);
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

    private void FocusSelectedLayout()
    {
        if (!_railOpen ||
            LayoutRailList.SelectedItem is not object selected)
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

        LayoutRailList.ScrollIntoView(selected);
        LayoutRailList.UpdateLayout();
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        int selectedIndex = LayoutRailList.Items.IndexOf(selected);
        if (scrollViewer is null || selectedIndex < 0)
        {
            return;
        }

        int anchorIndex = Math.Max(0, selectedIndex - 4);
        object anchor = LayoutRailList.Items[anchorIndex];
        LayoutRailList.ScrollIntoView(anchor);
        LayoutRailList.UpdateLayout();
        if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(anchor) is
            not ListBoxItem anchorItem)
        {
            return;
        }

        double anchorOffset =
            GetItemContentOffset(anchorItem, scrollViewer);
        scrollViewer.ScrollToVerticalOffset(
            ClampLayoutRailOffset(scrollViewer, anchorOffset));
        LayoutRailList.UpdateLayout();
    }

    private void QueueRevealSelectedLayout() =>
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            RevealSelectedLayout);

    private void StartLayoutRailAutoScroll(
        LayoutRailScrollDirection direction,
        bool includeDwell)
    {
        if (!_railOpen || direction == LayoutRailScrollDirection.None)
        {
            return;
        }

        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null ||
            !CanScrollLayoutRail(scrollViewer, direction))
        {
            StopLayoutRailAutoScroll(false);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        _layoutRailScrollDirection = direction;
        _railScrollDwellDeadline =
            now +
            (includeDwell
                ? (long)(RailScrollDwell.TotalSeconds * Stopwatch.Frequency)
                : 0L);
        _railScrollLastTimestamp = now;
        _railScrollTimer.Interval =
            UsesSmoothLayoutRailMotion
                ? RailScrollFrameInterval
                : RailReducedMotionInterval;
        _railScrollTimer.Start();
        UpdateLayoutRailScrollSignals();
    }

    private void LayoutRailScrollTimer_OnTick(
        object? sender,
        EventArgs eventArgs)
    {
        long now = Stopwatch.GetTimestamp();
        if (now < _railScrollDwellDeadline)
        {
            _railScrollLastTimestamp = now;
            return;
        }

        long start = Math.Max(
            _railScrollLastTimestamp,
            _railScrollDwellDeadline);
        double elapsedSeconds = Math.Clamp(
            (now - start) / (double)Stopwatch.Frequency,
            0.0,
            0.05);
        _railScrollLastTimestamp = now;
        AdvanceLayoutRailAutoScroll(
            TimeSpan.FromSeconds(elapsedSeconds),
            false);
    }

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
            forceContinuous || UsesSmoothLayoutRailMotion
                ? RailScrollVelocity * Math.Clamp(
                    elapsed.TotalSeconds,
                    0.0,
                    0.25)
                : LayoutItemHeight;
        delta *= (int)_layoutRailScrollDirection;
        double target = ClampLayoutRailOffset(
            scrollViewer,
            scrollViewer.VerticalOffset + delta);
        scrollViewer.ScrollToVerticalOffset(target);
        LayoutRailList.UpdateLayout();
        if (!CanScrollLayoutRail(
                scrollViewer,
                _layoutRailScrollDirection))
        {
            StopLayoutRailAutoScroll(false);
        }
    }

    private void StopLayoutRailAutoScroll(bool snapToItem)
    {
        _railScrollTimer.Stop();
        _layoutRailScrollDirection = LayoutRailScrollDirection.None;
        _railScrollDwellDeadline = 0L;
        _railScrollLastTimestamp = 0L;
        LayoutScrollUpSignal.Opacity = 0.0;
        LayoutScrollDownSignal.Opacity = 0.0;
        if (snapToItem && _railOpen)
        {
            SnapLayoutRailToNearestItem();
        }
    }

    private void SnapLayoutRailToNearestItem()
    {
        ScrollViewer? scrollViewer = GetLayoutRailScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        double nearestOffset = scrollViewer.VerticalOffset;
        double nearestDistance = double.PositiveInfinity;
        foreach (object item in LayoutRailList.Items)
        {
            if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(item) is
                not ListBoxItem container)
            {
                continue;
            }

            double itemOffset = GetItemContentOffset(container, scrollViewer);
            double distance =
                Math.Abs(itemOffset - scrollViewer.VerticalOffset);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestOffset = itemOffset;
            }
        }

        scrollViewer.ScrollToVerticalOffset(
            ClampLayoutRailOffset(scrollViewer, nearestOffset));
        LayoutRailList.UpdateLayout();
    }

    private void UpdateLayoutRailScrollSignals()
    {
        LayoutScrollUpSignal.Opacity =
            _layoutRailScrollDirection == LayoutRailScrollDirection.Up
                ? 1.0
                : 0.0;
        LayoutScrollDownSignal.Opacity =
            _layoutRailScrollDirection == LayoutRailScrollDirection.Down
                ? 1.0
                : 0.0;
    }

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
        if (LayoutRailList.Items.Count == 0 ||
            LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                LayoutRailList.Items[0]) is not ListBoxItem firstItem)
        {
            return 0.0;
        }

        return Math.Clamp(
            GetItemContentOffset(firstItem, scrollViewer),
            0.0,
            scrollViewer.ScrollableHeight);
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

    private static bool UsesSmoothLayoutRailMotion =>
        SystemParameters.ClientAreaAnimation &&
        !SystemParameters.HighContrast;

    private void ScheduleLayoutRailClose()
    {
        _railCloseTimer.Stop();
        _railCloseTimer.Start();
    }

    private void RailCloseTimer_OnTick(
        object? sender,
        EventArgs eventArgs)
    {
        _railCloseTimer.Stop();
        SetLayoutRailOpen(false, true);
    }

    private void SetLayoutRailOpen(bool open, bool animate)
    {
        if (!open)
        {
            StopLayoutRailAutoScroll(false);
        }

        if (_railOpen == open &&
            ((open && LayoutRailPanel.Visibility == Visibility.Visible) ||
             (!open && LayoutRailPanel.Visibility == Visibility.Collapsed)))
        {
            return;
        }

        _railOpen = open;
        if (open)
        {
            SyncRailSelection(_currentLayout);
        }

        bool useMotion =
            animate &&
            SystemParameters.ClientAreaAnimation &&
            !SystemParameters.HighContrast;
        LayoutAxisScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            null);
        LayoutRailPanel.BeginAnimation(
            OpacityProperty,
            null);

        if (!useMotion)
        {
            LayoutAxisScale.ScaleY = open ? 1.0 : 0.0;
            LayoutRailPanel.Opacity = open ? 1.0 : 0.0;
            LayoutRailPanel.Visibility =
                open ? Visibility.Visible : Visibility.Collapsed;
            if (open)
            {
                RevealSelectedLayout();
                QueueRevealSelectedLayout();
            }

            return;
        }

        if (open)
        {
            LayoutRailPanel.Visibility = Visibility.Visible;
            QueueRevealSelectedLayout();
        }

        ExponentialEase easing = new()
        {
            EasingMode = EasingMode.EaseOut,
            Exponent = 5.0,
        };
        DoubleAnimation axisAnimation = new(
            open ? 0.0 : 1.0,
            open ? 1.0 : 0.0,
            RailMotionDuration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        DoubleAnimation opacityAnimation = new(
            open ? 0.0 : 1.0,
            open ? 1.0 : 0.0,
            TimeSpan.FromMilliseconds(140))
        {
            BeginTime = open ? TimeSpan.FromMilliseconds(45) : TimeSpan.Zero,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        if (!open)
        {
            opacityAnimation.Completed += (_, _) =>
            {
                if (!_railOpen)
                {
                    LayoutRailPanel.Visibility = Visibility.Collapsed;
                }
            };
        }

        LayoutAxisScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            axisAnimation);
        LayoutRailPanel.BeginAnimation(
            OpacityProperty,
            opacityAnimation);
    }

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
        if (ExplorerWindow.Visibility != Visibility.Visible)
        {
            RestoreExplorer();
            Announce("EXPLORER / RESTORED");
        }
        else
        {
            Announce("EXPLORER / ACTIVE");
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
