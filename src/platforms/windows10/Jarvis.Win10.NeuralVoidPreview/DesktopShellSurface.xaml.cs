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
using static Jarvis.Win10.NeuralVoidPreview.VisualTreeSearch;

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
    private const int MaximumExplorerSessions = 4;
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
    private ExplorerWorkspace _explorerWorkspace => PrimaryExplorerSurface.Workspace;
    private readonly Dictionary<OwnedWindowId, ExplorerEntry> _explorers = [];
    private readonly Func<int, ExplorerSurface> _createExplorerSurface;
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
    private bool _draggingTaskManager;
    private Point _taskManagerDragOffset;
    private Point _taskManagerDragStart;
    private bool _taskManagerDragStarted;
    private Point? _ownedTitlePointerOverrideForTest;
    private int? _ownedTitleClickCountOverrideForTest;
    private MouseButtonState? _ownedTitleButtonOverrideForTest;
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
    private DispatcherOperation? _pendingOwnedFocus;
    private int _ownedFocusIntentGeneration;
    private double? _layoutRailPointerYOverrideForTest;
    private int _layoutRailMouseMoveInvocationCount;
    private string _adjacentRailProbeDetailForTest = "not-run";

    private sealed class ExplorerEntry(
        OwnedWindowId id, Border frame, ExplorerSurface surface, Border titleBar,
        TextBlock titleText, Button minimizeButton, Button maximizeButton, Button closeButton)
    {
        internal OwnedWindowId Id { get; } = id;
        internal Border Frame { get; } = frame;
        internal ExplorerSurface Surface { get; } = surface;
        internal Border TitleBar { get; } = titleBar;
        internal TextBlock TitleText { get; } = titleText;
        internal Button MinimizeButton { get; } = minimizeButton;
        internal Button MaximizeButton { get; } = maximizeButton;
        internal Button CloseButton { get; } = closeButton;
        internal bool Dragging;
        internal Point DragOffset;
        internal Point DragStart;
        internal bool DragStarted;
    }

    internal IReadOnlyList<string> ExplorerSessionIdsForTest =>
        _explorers.Keys.Select(id => id.Value).ToArray();

    internal ExplorerSurface GetExplorerSurfaceForTest(string id) =>
        _explorers[new OwnedWindowId(id)].Surface;

    private void ConfigureExplorerTaskbarMenu()
    {
        ContextMenu menu = new()
        {
            Style = (Style)FindResource("ExplorerSessionMenu"),
        };
        MenuItem create = new()
        {
            Header = "_New Explorer window",
            InputGestureText = "Shift+F10 / N",
            CommandParameter = "new",
            Style = (Style)FindResource("ExplorerSessionMenuItem"),
        };
        AutomationProperties.SetName(create, "New Explorer window");
        create.Click += (_, _) =>
        {
            if (_explorers.Count >= MaximumExplorerSessions) return;
            menu.IsOpen = false;
            _ = TaskbarExplorerButton.Focus();
            CreateExplorerSession();
        };
        menu.Items.Add(create);
        menu.Opened += (_, _) =>
        {
            CancelExplorerPendingFocus();
            RefreshExplorerTaskbarMenu();
        };
        TaskbarExplorerButton.ContextMenu = menu;
        TaskbarExplorerButton.PreviewKeyDown += TaskbarExplorerButton_OnPreviewKeyDown;
        TaskbarExplorerButton.ToolTip = "EXPLORER / RIGHT CLICK OR SHIFT+F10 / NEW WINDOW";
        AutomationProperties.SetHelpText(TaskbarExplorerButton,
            "Activate the recent Explorer. Right click or Shift+F10 opens session commands and New Explorer window.");
    }

    private void TaskbarExplorerButton_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Apps ||
            (eventArgs.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
        {
            ContextMenu menu = TaskbarExplorerButton.ContextMenu;
            menu.PlacementTarget = TaskbarExplorerButton;
            menu.IsOpen = true;
            eventArgs.Handled = true;
        }
    }

    private void CreateExplorerSession()
    {
        // Closed sessions retain their exact surface/workspace identity and slot.
        if (_explorers.Count >= MaximumExplorerSessions) return;
        ExplorerEntry entry;
        try { entry = PrepareExplorerSession(_explorers.Count + 1); }
        catch (Exception) { Announce("EXPLORER / NEW PREPARATION FAILED / NO CHANGE"); return; }
        if (_ownedWindowLayouts.IsPartition)
        {
            OwnedLayoutApplyResult result = _ownedWindowLayouts.TryShowPartition(entry.Id,new(entry.Id,GetOwnedWindowBounds(entry.Frame)));
            if (!result.Succeeded) { entry.Surface.SuspendInteraction(); Announce("PARTITION / NEW REJECTED / NO CHANGE"); return; }
            PublishExplorerSession(entry);
            ApplyOwnedLayoutResult(result);
            RefreshLayoutAvailability();
            entry.Surface.FocusExplorerItems();
            Announce($"EXPLORER / NEW WINDOW / {entry.Id.Value}");
            return;
        }
        if (!_ownedWindowLayouts.TryRegister(entry.Id, OwnedWindowLifecycle.Visible, GetOwnedWindowBounds(entry.Frame)))
            throw new InvalidOperationException($"Could not register Explorer session {entry.Id}.");
        PublishExplorerSession(entry);
        ActivateOwnedWindow(entry.Frame);
        UpdateOwnedWindowLayoutDensity();
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        RefreshExplorerTaskbarState();
        entry.Surface.FocusExplorerItems();
        Announce($"EXPLORER / NEW WINDOW / {entry.Id.Value}");
    }

    private ExplorerEntry PrepareExplorerSession(int number)
    {
        OwnedWindowId id = new($"explorer-{number}");
        ExplorerSurface surface = _createExplorerSurface(number) ?? throw new InvalidOperationException("Explorer factory returned no surface.");
        if (surface.IsLoaded || surface.Parent is not null) throw new InvalidOperationException("Explorer factory must return a detached surface.");
        surface.SetBinding(ExplorerSurface.AccentBrushProperty,
            new System.Windows.Data.Binding(nameof(AccentBrush)) { Source = this });
        surface.Announced += Announce;
        TextBlock titleText = new()
        {
            Text = $"JARVIS2 / FILE EXPLORER / {id.Value}",
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Button minimize = CreateExplorerTitleButton("Minimize", id,
            new System.Windows.Shapes.Path { Width = 16, StrokeThickness = 1.4,
                Stroke = (Brush)FindResource("TextBrush"), Data = Geometry.Parse("M 0,1 L 16,1") });
        minimize.Click += ExplorerMinimizeButton_OnClick;
        Button maximize = CreateExplorerTitleButton("Maximize", id,
            new System.Windows.Shapes.Rectangle { Width = 15, Height = 15, StrokeThickness = 1.5,
                Stroke = (Brush)FindResource("TextBrush") });
        maximize.Click += ExplorerMaximizeButton_OnClick;
        Button close = CreateExplorerTitleButton("Close", id,
            new System.Windows.Shapes.Path { Width = 16, Height = 16, StrokeThickness = 1.5,
                Stroke = (Brush)FindResource("TextBrush"), Data = Geometry.Parse("M 1,1 L 15,15 M 15,1 L 1,15") });
        close.Click += ExplorerCloseButton_OnClick;
        Grid titleGrid = new();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        titleText.Margin = new Thickness(20, 0, 12, 0);
        titleGrid.Children.Add(titleText);
        StackPanel controls = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(minimize); controls.Children.Add(maximize); controls.Children.Add(close);
        Grid.SetColumn(controls, 1); titleGrid.Children.Add(controls);
        Border titleBar = new()
        {
            Child = titleGrid, BorderBrush = (Brush)FindResource("SecondaryRuleBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1), ClipToBounds = true,
        };
        Grid body = new();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(titleBar); Grid.SetRow(surface, 1); body.Children.Add(surface);
        Border frame = new()
        {
            Child = body, Width = 930, Height = 667, BorderThickness = new Thickness(2),
            BorderBrush = (Brush)FindResource("PrimaryRuleBrush"), Background = Brushes.Black,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(frame, 596 - 28 * (number - 1));
        Canvas.SetTop(frame, 63 + 18 * (number - 1));
        ExplorerEntry entry = new(id, frame, surface, titleBar, titleText, minimize, maximize, close);
        ConfigureExplorerEntry(entry);
        titleBar.MouseLeftButtonDown += ExplorerTitleBar_OnMouseLeftButtonDown;
        titleBar.MouseMove += ExplorerTitleBar_OnMouseMove;
        titleBar.MouseLeftButtonUp += ExplorerTitleBar_OnMouseLeftButtonUp;
        titleBar.LostMouseCapture += OwnedTitleBar_OnLostMouseCapture;
        frame.PreviewMouseDown += OwnedWindow_OnPreviewMouseDown;
        frame.GotKeyboardFocus += OwnedWindow_OnGotKeyboardFocus;
        return entry;
    }

    private void PublishExplorerSession(ExplorerEntry entry)
    {
        _explorers.Add(entry.Id, entry);
        DesktopCanvas.Children.Add(entry.Frame);
    }

    private Button CreateExplorerTitleButton(string action, OwnedWindowId id, UIElement glyph)
    {
        Button button = new()
        {
            Style = (Style)FindResource("WindowControlButton"),
            Content = glyph,
            ToolTip = $"{action.ToUpperInvariant()} EXPLORER / {id.Value}",
        };
        AutomationProperties.SetName(button, $"{action} Explorer {id.Value}");
        return button;
    }

    public DesktopShellSurface() : this(_ => new ExplorerSurface()) { }

    internal DesktopShellSurface(Func<int, ExplorerSurface> createExplorerSurface)
    {
        ArgumentNullException.ThrowIfNull(createExplorerSurface);
        _createExplorerSurface = createExplorerSurface;
        InitializeComponent();
        _explorers.Add(ExplorerOwnedWindowId, new(
            ExplorerOwnedWindowId, ExplorerWindow, PrimaryExplorerSurface,
            ExplorerTitleBar, ExplorerTitleText, ExplorerMinimizeButton,
            ExplorerMaximizeButton, ExplorerCloseButton));
        PrimaryExplorerSurface.Announced += Announce;
        ConfigureExplorerEntry(_explorers[ExplorerOwnedWindowId]);
        ConfigureExplorerTaskbarMenu();
        ConfigureLayoutCommands();
        PreviewKeyDown += PartitionSurface_OnPreviewKeyDown;
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

        LayoutRailHitRegion.AddHandler(
            Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(LayoutRailRegion_OnPreviewMouseMove),
            true);
        LayoutRailPanel.GotKeyboardFocus += (_, _) => CancelExplorerPendingFocus();
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
        RefreshExplorerTaskbarState();
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
                _ownedWindowLayouts.GetAvailability(definition.Preset)
                    .StatusText == "AVAILABLE");

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

    internal bool IsExplorerCompactWidthForTest => PrimaryExplorerSurface.IsCompactWidth;

    internal bool IsExplorerCompactHeightForTest => PrimaryExplorerSurface.IsCompactHeight;

    internal bool IsTaskManagerVisibleForTest =>
        TaskManagerWindow.Visibility == Visibility.Visible;

    internal bool IsTaskManagerRunningForTest =>
        TaskManagerRunningIndicator.Visibility == Visibility.Visible;

    internal bool IsTaskManagerSamplingForTest =>
        TaskManagerPanel.IsSamplingActiveForTest;

    internal double TaskManagerIndicatorOpacityForTest =>
        TaskManagerRunningIndicator.Opacity;

    // PRIMARY compatibility aliases; new feature commands resolve a session ID.
    internal Button ExplorerBackButton => PrimaryExplorerSurface.ExplorerBackButton;
    internal Button ExplorerForwardButton => PrimaryExplorerSurface.ExplorerForwardButton;
    internal Button ExplorerUpButton => PrimaryExplorerSurface.ExplorerUpButton;
    internal TextBlock ExplorerBreadcrumbText => PrimaryExplorerSurface.ExplorerBreadcrumbText;
    internal Border ExplorerActionPanel => PrimaryExplorerSurface.ExplorerActionPanel;
    internal Button ExplorerOpenButton => PrimaryExplorerSurface.ExplorerOpenButton;
    internal Button ExplorerRenameButton => PrimaryExplorerSurface.ExplorerRenameButton;
    internal Button ExplorerAccessButton => PrimaryExplorerSurface.ExplorerAccessButton;
    internal Button ExplorerNewFolderButton => PrimaryExplorerSurface.ExplorerNewFolderButton;
    internal ColumnDefinition ExplorerFolderColumn => PrimaryExplorerSurface.ExplorerFolderColumn;
    internal ColumnDefinition ExplorerFolderDividerColumn => PrimaryExplorerSurface.ExplorerFolderDividerColumn;
    internal Grid ExplorerFolderPanel => PrimaryExplorerSurface.ExplorerFolderPanel;
    internal TextBlock ExplorerFolderNameText => PrimaryExplorerSurface.ExplorerFolderNameText;
    internal TextBlock ExplorerItemCountText => PrimaryExplorerSurface.ExplorerItemCountText;
    internal TextBlock ExplorerRenameErrorText => PrimaryExplorerSurface.ExplorerRenameErrorText;
    internal Grid ExplorerFullHeader => PrimaryExplorerSurface.ExplorerFullHeader;
    internal Grid ExplorerCompactHeader => PrimaryExplorerSurface.ExplorerCompactHeader;
    internal ListBox ExplorerItemList => PrimaryExplorerSurface.ExplorerItemList;
    internal Grid ExplorerEmptyState => PrimaryExplorerSurface.ExplorerEmptyState;
    internal Grid ExplorerPreviewPanel => PrimaryExplorerSurface.ExplorerPreviewPanel;
    internal Button ExplorerPreviewCloseButton => PrimaryExplorerSurface.ExplorerPreviewCloseButton;
    internal TextBlock ExplorerPreviewNameText => PrimaryExplorerSurface.ExplorerPreviewNameText;
    internal TextBlock ExplorerPreviewTypeText => PrimaryExplorerSurface.ExplorerPreviewTypeText;
    internal TextBlock ExplorerPreviewKindText => PrimaryExplorerSurface.ExplorerPreviewKindText;
    internal Border ExplorerStoragePanel => PrimaryExplorerSurface.ExplorerStoragePanel;

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

        PrimaryExplorerSurface.ApplyExplorerOpenResult(_explorerWorkspace.OpenSelected());
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
        _ownedWindowLayouts.GetAvailability(preset).StatusText == "AVAILABLE";

    internal bool IsLayoutSelectableForTest(LayoutPreset preset) =>
        _ownedWindowLayouts.GetAvailability(preset).IsEnabled;

    internal Rect GetOwnedWindowBoundsForTest(string id) =>
        GetOwnedWindowBounds(ResolveOwnedFrame(new OwnedWindowId(id)));

    internal void MoveOwnedWindowForTest(string id, Rect bounds)
    {
        FrameworkElement window = ResolveOwnedFrame(new OwnedWindowId(id));
        if (!_ownedWindowLayouts.TrySetManualBounds(GetOwnedWindowId(window), bounds)) return;
        SetOwnedWindowBounds(window, bounds);
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
    }

    internal void RaiseOwnedTitleGestureForTest(
        string id,
        Point press,
        Point? move = null,
        bool loseCapture = false,
        int clickCount = 1)
    {
        UIElement title = _explorers.TryGetValue(new OwnedWindowId(id), out ExplorerEntry? entry)
            ? entry.TitleBar
            : id == "task-manager" ? TaskManagerTitleBar
            : throw new ArgumentOutOfRangeException(nameof(id));
        try
        {
            _ownedTitlePointerOverrideForTest = press;
            _ownedTitleButtonOverrideForTest = MouseButtonState.Pressed;
            _ownedTitleClickCountOverrideForTest = clickCount;
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
            _ownedTitleClickCountOverrideForTest = null;
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
        FinishPartitionGestures();
        _clockTimer.Stop();
        TaskManagerPanel.StopSampling();
        foreach (ExplorerEntry entry in _explorers.Values)
        {
            EndExplorerTitleDrag(entry);
            entry.Surface.SuspendInteraction();
        }
        CancelOwnedFocus();
        TaskbarExplorerButton.ContextMenu.IsOpen = false;
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

    private void HostWindow_OnDeactivated(object? sender, EventArgs eventArgs)
    {
        FinishPartitionGestures();
        StopLayoutRailAutoScroll();
    }

    private void HostWindow_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized)
        {
            FinishPartitionGestures();
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

    private void ConfigureLayoutCommands()
    {
        ContextMenu menu = new() { Style = (Style)FindResource("ExplorerSessionMenu") };
        MenuItem freeform = new()
        {
            Header = "_Freeform", CommandParameter = "freeform",
            Style = (Style)FindResource("ExplorerSessionMenuItem"),
        };
        AutomationProperties.SetName(freeform,"Freeform");
        AutomationProperties.SetHelpText(freeform,"Restore independent floating bounds; retained sessions are kept.");
        freeform.Click += (_,_) =>
        {
            menu.IsOpen = false;
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryExitPartition());
            RefreshLayoutAvailability();
            Announce("LAYOUT / FREEFORM");
        };
        menu.Items.Add(freeform);
        menu.Opened += (_,_) => CancelExplorerPendingFocus();
        CurrentLayoutButton.ContextMenu = menu;
        CurrentLayoutButton.PreviewKeyDown += (_,args) =>
        {
            if (args.Key == Key.Apps || (args.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                menu.PlacementTarget = CurrentLayoutButton; menu.IsOpen = true; args.Handled = true;
            }
        };
    }

    private void CurrentLayoutButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        QueueOwnedFocus(FocusSelectedLayout);
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
            QueueOwnedFocus(() => _ = TaskManagerPanel.Focus(), TaskManagerOwnedWindowId);
        }
        else if (_ownedWindowLayouts.ActiveWindowId is OwnedWindowId id &&
            _explorers.TryGetValue(id, out ExplorerEntry? entry) &&
            entry.Frame.Visibility == Visibility.Visible)
        {
            entry.Surface.FocusExplorerItems();
        }
        else
        {
            _ = Focus();
        }

        Announce("RAIL CLOSED / FOCUS RETURNED TO ACTIVE WINDOW");
    }

    private bool SelectLayout(LayoutPreset preset, bool announce)
    {
        if (!_ownedWindowLayouts.CanProjectPartition(preset,OwnedWorkArea))
        { Announce("PARTITION / GEOMETRY REJECTED / NO CHANGE"); return false; }
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

        List<ExplorerEntry> prepared = [];
        OwnedLayoutApplyResult result;
        try
        {
            int deficit = Math.Max(0, definition.PaneCount - _ownedWindowLayouts.CaptureSnapshot().Windows.Count);
            if (_explorers.Count + deficit > MaximumExplorerSessions) return false;
            for (int index = 0; index < deficit; index++)
                prepared.Add(PrepareExplorerSession(_explorers.Count + index + 1));
            result = _ownedWindowLayouts.TryCommitPartition(preset, OwnedWorkArea,
                prepared.Select(entry => new OwnedWindowAdmission(entry.Id, GetOwnedWindowBounds(entry.Frame))).ToArray());
        }
        catch (Exception)
        {
            foreach (ExplorerEntry entry in prepared) entry.Surface.SuspendInteraction();
            Announce("PARTITION / PREPARATION FAILED / NO CHANGE");
            return false;
        }
        if (result.Succeeded)
            foreach (ExplorerEntry entry in prepared) PublishExplorerSession(entry);
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
        RefreshLayoutAvailability();
        QueueRevealSelectedLayout();
        if (announce)
        {
            string fillSuffix = result.VisibleWindowCount < definition.PaneCount
                ? $" / FILLS {result.VisibleWindowCount} OF {definition.PaneCount}"
                : string.Empty;
            Announce(
                $"LAYOUT / {definition.Id.ToUpperInvariant()}{fillSuffix}");
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
                "Owned windows keep their independent floating bounds. Right click or Shift+F10 for Freeform.");
            CurrentLayoutButton.ToolTip = freeformName + " / RIGHT CLICK OR SHIFT+F10 / FREEFORM";
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
        int filledPaneCount = _ownedWindowLayouts.CaptureSnapshot().Windows.Count(
            window => _ownedWindowLayouts.IsWindowShown(window.Id) && window.AssignedPaneIndex.HasValue);
        AutomationProperties.SetHelpText(
            CurrentLayoutButton,
            $"{filledPaneCount} of {definition.PaneCount} panes filled by " +
            "visible owned windows. Right click or Shift+F10 for Freeform; drag shared edges or use arrow keys to resize.");
        CurrentLayoutButton.ToolTip = accessibleName + " / DRAG SHARED EDGES / SHIFT+F10 / FREEFORM";
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
        int registeredCount = _ownedWindowLayouts.CaptureSnapshot().Windows.Count;
        foreach (LayoutDefinition definition in LayoutCatalog.All)
        {
            if (LayoutRailList.ItemContainerGenerator.ContainerFromItem(
                    definition) is not ListBoxItem item)
            {
                continue;
            }

            OwnedLayoutAvailability availability =
                _ownedWindowLayouts.GetAvailability(definition.Preset);
            int required = availability.RequiredPaneCount;
            int deficit = Math.Max(0,required-registeredCount);
            int restoreCount = Math.Max(0,required-deficit-availability.VisibleWindowCount);
            item.IsEnabled = true;
            string helpText = $"{definition.AutomationName} fills all {required} panes; restores {restoreCount} retained owned " +
                $"windows and creates {deficit} independent Explorers if needed. Only assigned frames are shown. " +
                "Older hidden or closed sessions may be reused to keep coverage. Drag shared edges to resize; Shift+F10 offers Freeform.";
            item.ToolTip = $"{definition.AutomationName} / FULL PARTITION / RESTORES {restoreCount} / CREATES {deficit}";
            AutomationProperties.SetHelpText(item,helpText);
            AutomationProperties.SetItemStatus(item,$"{required} of {required} panes / Full Partition");
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
            ExplorerEntry entry = RecentExplorer();
            RestoreExplorer(entry);
            entry.Surface.NavigateTo(locationId);
            return;
        }

        Announce($"DESKTOP / {destination}");
    }

    private ExplorerEntry ExplorerTitleEntry(object sender) =>
        sender is FrameworkElement { Tag: OwnedWindowId id } &&
            _explorers.TryGetValue(id, out ExplorerEntry? entry) ? entry :
        throw new InvalidOperationException("Explorer title requires an explicit registered session ID.");

    private void ExplorerTitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (FindParent<Button>(eventArgs.OriginalSource as DependencyObject) is not null) return;
        ExplorerEntry entry = ExplorerTitleEntry(sender);
        if (GetOwnedWindowState(entry.Id).Lifecycle != OwnedWindowLifecycle.Visible) return;
        if ((_ownedTitleClickCountOverrideForTest ?? eventArgs.ClickCount) == 2)
        {
            EndExplorerTitleDrag(entry);
            ToggleExplorerMaximize(entry);
            entry.Surface.FocusExplorerItems();
            eventArgs.Handled = true;
            return;
        }

        ActivateOwnedWindow(entry.Frame);
        entry.Surface.FocusExplorerItems();
        if (_ownedWindowLayouts.IsPartition) { eventArgs.Handled = true; return; }
        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        entry.DragStart = pointer;
        entry.DragStarted = false;
        entry.DragOffset = new Point(pointer.X - Canvas.GetLeft(entry.Frame), pointer.Y - Canvas.GetTop(entry.Frame));
        entry.Dragging = true;
        entry.TitleBar.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void ExplorerTitleBar_OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        ExplorerEntry entry = ExplorerTitleEntry(sender);
        if (!entry.Dragging || (_ownedTitleButtonOverrideForTest ?? eventArgs.LeftButton) != MouseButtonState.Pressed) return;
        Point pointer = _ownedTitlePointerOverrideForTest ?? eventArgs.GetPosition(DesktopCanvas);
        MoveOwnedWindowFromTitle(entry.Id, entry.Frame, pointer, entry.DragStart, entry.DragOffset, ref entry.DragStarted);
    }

    private void ExplorerTitleBar_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs) =>
        EndExplorerTitleDrag(ExplorerTitleEntry(sender));

    private void EndExplorerTitleDrag(ExplorerEntry entry)
    {
        bool moved = entry.DragStarted;
        entry.Dragging = false;
        entry.DragStarted = false;
        entry.TitleBar.ReleaseMouseCapture();
        if (moved) CompleteOwnedWindowManualMove(entry.Id, entry.Frame);
    }

    private void ConfigureExplorerEntry(ExplorerEntry entry)
    {
        entry.Surface.IsFocusIntentCurrent = () => IsLoaded &&
            GetOwnedWindowState(entry.Id).Lifecycle == OwnedWindowLifecycle.Visible && IsOwnedWindowActive(entry.Id);
        entry.Surface.ActivationRequested += () => ActivateOwnedWindow(entry.Frame);
        entry.TitleBar.Tag = entry.Id;
        entry.MinimizeButton.CommandParameter = entry.Id;
        entry.MaximizeButton.CommandParameter = entry.Id;
        entry.CloseButton.CommandParameter = entry.Id;
        AutomationProperties.SetName(entry.Frame, $"JARVIS2 Explorer session {entry.Id.Value}");
    }

    private ExplorerEntry ExplorerCommandEntry(object sender) =>
        sender is Button { CommandParameter: OwnedWindowId id } &&
            _explorers.TryGetValue(id, out ExplorerEntry? entry) ? entry :
        throw new InvalidOperationException("Explorer command requires an explicit registered session ID.");

    private void ExplorerMinimizeButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        MinimizeExplorer(ExplorerCommandEntry(sender));

    private void MinimizeExplorer(ExplorerEntry entry)
    {
        if (HideExplorer(entry, OwnedWindowLifecycle.Minimized)) Announce($"EXPLORER / MINIMIZED / {entry.Id.Value}");
    }

    private void ExplorerMaximizeButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        ToggleExplorerMaximize(ExplorerCommandEntry(sender));

    private void ToggleExplorerMaximize(ExplorerEntry entry)
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryTogglePartitionZoom(entry.Id));
            RefreshLayoutAvailability();
            Announce($"EXPLORER / ZOOM OR RESTORE / {entry.Id.Value}");
            return;
        }
        RestoreExplorer(entry);
        bool wasMaximized = IsOwnedWindowMaximized(entry.Id);
        OwnedLayoutApplyResult result = _ownedWindowLayouts.TryToggleMaximize(entry.Id, OwnedWorkArea);
        ApplyOwnedLayoutResult(result);
        RefreshLayoutAvailability();
        Announce($"EXPLORER / {(wasMaximized ? "RESTORED" : "EXPANDED")} / {entry.Id.Value}");
    }

    private void ExplorerCloseButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        CloseExplorer(ExplorerCommandEntry(sender));

    private void CloseExplorer(ExplorerEntry entry)
    {
        if (HideExplorer(entry, OwnedWindowLifecycle.Closed)) Announce($"EXPLORER / CLOSED / {entry.Id.Value}");
    }

    private bool HideExplorer(ExplorerEntry entry, OwnedWindowLifecycle lifecycle)
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            if (!HidePartitionWindow(entry.Id,lifecycle)) return false;
            QueueTaskbarFocus(TaskbarExplorerButton);
            return true;
        }
        EndExplorerTitleDrag(entry);
        entry.Surface.SuspendInteraction();
        _ = _ownedWindowLayouts.TrySetLifecycle(entry.Id, lifecycle);
        entry.Frame.Visibility = Visibility.Collapsed;
        ApplyCurrentLayoutPresentation();
        RefreshLayoutAvailability();
        UpdateOwnedWindowZOrder();
        RefreshExplorerTaskbarState();
        QueueTaskbarFocus(TaskbarExplorerButton);
        return true;
    }

    private ExplorerEntry RecentExplorer()
    {
        if (_ownedWindowLayouts.ActiveWindowId is OwnedWindowId active &&
            _explorers.TryGetValue(active, out ExplorerEntry? activeEntry)) return activeEntry;
        OwnedWindowState[] states = _ownedWindowLayouts.CaptureSnapshot().Windows
            .Where(state => _explorers.ContainsKey(state.Id))
            .OrderByDescending(state => state.ActivationOrdinal)
            .ThenByDescending(state => state.RegistrationOrder).ToArray();
        OwnedWindowState target = states.FirstOrDefault(state => state.Lifecycle != OwnedWindowLifecycle.Closed) ?? states[0];
        return _explorers[target.Id];
    }

    private void TaskbarExplorerButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        ExplorerEntry entry = RecentExplorer();
        if (entry.Frame.Visibility == Visibility.Visible && IsOwnedWindowActive(entry.Id))
        {
            MinimizeExplorer(entry);
            return;
        }

        bool wasVisible = entry.Frame.Visibility == Visibility.Visible;
        ActivateExplorerFromTaskbar(entry);
        Announce($"EXPLORER / {(wasVisible ? "ACTIVE" : "RESTORED")} / {entry.Id.Value}");
    }

    private void ActivateExplorerFromTaskbar(ExplorerEntry entry)
    {
        RestoreExplorer(entry);
        if (!_ownedWindowLayouts.IsPartition && _ownedWindowLayouts.AppliedPreset == LayoutPreset.Maximized)
        {
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryApply(LayoutPreset.Maximized, OwnedWorkArea));
        }
        entry.Surface.FocusExplorerItems();
    }

    private void RestoreExplorer() => RestoreExplorer(_explorers[ExplorerOwnedWindowId]);

    private void RestoreExplorer(ExplorerEntry entry)
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryShowPartition(entry.Id));
            RefreshLayoutAvailability();
            return;
        }
        bool wasVisible = entry.Frame.Visibility == Visibility.Visible;
        _ = _ownedWindowLayouts.TrySetLifecycle(entry.Id, OwnedWindowLifecycle.Visible);
        ApplyCoordinatorBounds(entry.Id, entry.Frame);
        entry.Frame.Visibility = Visibility.Visible;
        entry.Surface.ResumeInteraction();
        ActivateOwnedWindow(entry.Frame);
        RefreshLayoutAvailability();
        RefreshExplorerTaskbarState();
        if (!wasVisible) ApplyCurrentLayoutPresentation();
    }

    private void RefreshExplorerTaskbarState()
    {
        OwnedWindowState[] states = _ownedWindowLayouts.CaptureSnapshot().Windows
            .Where(state => _explorers.ContainsKey(state.Id)).ToArray();
        ExplorerRunningIndicator.Visibility = states.Any(state => state.Lifecycle != OwnedWindowLifecycle.Closed)
            ? Visibility.Visible : Visibility.Collapsed;
        ExplorerRunningIndicator.Opacity = states.Any(state => state.Lifecycle == OwnedWindowLifecycle.Visible) ? 1.0 : 0.45;
        RefreshExplorerTaskbarMenu();
    }

    private void RefreshExplorerTaskbarMenu()
    {
        ContextMenu menu = TaskbarExplorerButton.ContextMenu;
        MenuItem create = (MenuItem)menu.Items[0];
        create.IsEnabled = _explorers.Count < MaximumExplorerSessions;
        string help = create.IsEnabled
            ? $"Create an independent memory-only Explorer session ({_explorers.Count} of {MaximumExplorerSessions} retained slots used)."
            : $"Limit: {MaximumExplorerSessions} retained Explorer sessions. Closing retains the slot; reopen an existing session by ID below.";
        create.ToolTip = help;
        AutomationProperties.SetHelpText(create, help);
        while (menu.Items.Count > 1) menu.Items.RemoveAt(1);
        foreach (ExplorerEntry entry in _explorers.Values)
        {
            OwnedWindowLifecycle lifecycle = GetOwnedWindowState(entry.Id).Lifecycle;
            string action = lifecycle == OwnedWindowLifecycle.Visible ? "Activate" :
                lifecycle == OwnedWindowLifecycle.Suppressed ? "Show" : lifecycle == OwnedWindowLifecycle.Minimized ? "Restore" : "Reopen";
            MenuItem command = new()
            {
                Header = $"{action} Explorer / {entry.Id.Value}",
                CommandParameter = entry.Id,
                Style = (Style)FindResource("ExplorerSessionMenuItem"),
            };
            AutomationProperties.SetName(command, $"{action} Explorer session {entry.Id.Value}");
            command.Click += ExplorerSessionMenuItem_OnClick;
            menu.Items.Add(command);
        }
    }

    private void ExplorerSessionMenuItem_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { CommandParameter: OwnedWindowId id } &&
            _explorers.TryGetValue(id, out ExplorerEntry? entry))
        {
            TaskbarExplorerButton.ContextMenu.IsOpen = false;
            _ = TaskbarExplorerButton.Focus();
            ActivateExplorerFromTaskbar(entry);
            Announce($"EXPLORER / ACTIVE / {id.Value}");
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

        if ((_ownedTitleClickCountOverrideForTest ?? eventArgs.ClickCount) == 2)
        {
            TaskManagerTitleBar.ReleaseMouseCapture();
            _draggingTaskManager = false;
            ToggleTaskManagerMaximize();
            eventArgs.Handled = true;
            return;
        }

        ActivateOwnedWindow(TaskManagerWindow);
        if (_ownedWindowLayouts.IsPartition) { eventArgs.Handled = true; return; }
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

        if (sender is FrameworkElement { Tag: OwnedWindowId id } && _explorers.TryGetValue(id, out ExplorerEntry? entry))
        {
            EndExplorerTitleDrag(entry);
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
        RoutedEventArgs eventArgs) => ToggleTaskManagerMaximize();

    private void ToggleTaskManagerMaximize()
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryTogglePartitionZoom(TaskManagerOwnedWindowId));
            RefreshLayoutAvailability();
            Announce("TASK MANAGER / ZOOM OR RESTORE");
            return;
        }
        RestoreTaskManager();
        bool wasMaximized = IsTaskManagerMaximizedForTest;
        OwnedLayoutApplyResult result =
            _ownedWindowLayouts.TryToggleMaximize(
                TaskManagerOwnedWindowId,
                OwnedWorkArea);
        ApplyOwnedLayoutResult(result);
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
            if (!_ownedWindowLayouts.IsPartition && _ownedWindowLayouts.AppliedPreset == LayoutPreset.Maximized)
            {
                OwnedLayoutApplyResult result =
                    _ownedWindowLayouts.TryApply(
                        LayoutPreset.Maximized,
                        OwnedWorkArea);
                ApplyOwnedLayoutResult(result);
            }

            Announce(
                wasClosed
                    ? "TASK MANAGER / OPENED / READ ONLY"
                    : "TASK MANAGER / ACTIVE");
        }
    }

    private void RestoreTaskManager()
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            ApplyOwnedLayoutResult(_ownedWindowLayouts.TryShowPartition(TaskManagerOwnedWindowId));
            RefreshLayoutAvailability();
            QueueOwnedFocus(() => _ = TaskManagerMinimizeButton.Focus(),TaskManagerOwnedWindowId);
            return;
        }
        bool wasVisible =
            TaskManagerWindow.Visibility == Visibility.Visible;
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
        if (!wasVisible)
        {
            ApplyCurrentLayoutPresentation();
        }

        TaskManagerPanel.StartSampling();
        QueueOwnedFocus(() => _ = TaskManagerMinimizeButton.Focus(), TaskManagerOwnedWindowId);
    }

    private void MinimizeTaskManager()
    {
        if (_ownedWindowLayouts.IsPartition)
        {
            if (HidePartitionWindow(TaskManagerOwnedWindowId,OwnedWindowLifecycle.Minimized))
            { QueueTaskbarFocus(TaskbarSystemButton); Announce("TASK MANAGER / MINIMIZED / SAMPLING PAUSED"); }
            return;
        }
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
        if (_ownedWindowLayouts.IsPartition)
        {
            if (HidePartitionWindow(TaskManagerOwnedWindowId,OwnedWindowLifecycle.Closed))
            { QueueTaskbarFocus(TaskbarSystemButton); Announce("TASK MANAGER / CLOSED / SAMPLING STOPPED"); }
            return;
        }
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

    private bool HidePartitionWindow(OwnedWindowId id, OwnedWindowLifecycle lifecycle)
    {
        ExplorerEntry? prepared = null;
        OwnedLayoutApplyResult result;
        try
        {
            if (_ownedWindowLayouts.PartitionRefillDeficit(id) != 0)
            {
                if (_explorers.Count >= MaximumExplorerSessions)
                { Announce("PARTITION / REPLACEMENT FAILED / RETAINED LIMIT / NO CHANGE"); return false; }
                prepared = PrepareExplorerSession(_explorers.Count+1);
            }
            result = _ownedWindowLayouts.TryHidePartition(id,lifecycle,
                prepared is null ? null : new(prepared.Id,GetOwnedWindowBounds(prepared.Frame)));
        }
        catch (Exception)
        { prepared?.Surface.SuspendInteraction(); Announce("PARTITION / REPLACEMENT FAILED / NO CHANGE"); return false; }
        if (!result.Succeeded)
        { prepared?.Surface.SuspendInteraction(); Announce("PARTITION / REPLACEMENT FAILED / NO CHANGE"); return false; }
        if (prepared is not null) PublishExplorerSession(prepared);
        ApplyOwnedLayoutResult(result);
        RefreshLayoutAvailability();
        return true;
    }

    private void ActivateOwnedWindow(FrameworkElement window)
    {
        OwnedWindowId id = GetOwnedWindowId(window);
        if (_applyingPartitionGeometry && IsOwnedWindowActive(id)) return;
        if (!_ownedWindowLayouts.TryActivate(id)) return;
        CancelOwnedFocus();
        foreach (ExplorerEntry entry in _explorers.Values)
            if (entry.Id != id) entry.Surface.CancelPendingFocus();
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
        if (_ownedWindowLayouts.IsPartition) return;
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
            ApplyCurrentLayoutPresentation();
            return;
        }

        {
            foreach (OwnedWindowState state in _ownedWindowLayouts.CaptureSnapshot().Windows.Where(state => !_ownedWindowLayouts.IsWindowShown(state.Id)))
            {
                FrameworkElement hidden = ResolveOwnedFrame(state.Id);
                hidden.Visibility = Visibility.Collapsed;
                if (_explorers.TryGetValue(state.Id, out ExplorerEntry? entry)) entry.Surface.SuspendInteraction();
                else TaskManagerPanel.StopSampling();
            }
        }
        foreach (OwnedWindowPlacement placement in result.Placements)
        {
            FrameworkElement window = ResolveOwnedFrame(placement.WindowId);
            SetOwnedWindowBounds(window, placement.Bounds);
            if (window.Visibility != Visibility.Visible)
            {
                window.Visibility = Visibility.Visible;
                if (_explorers.TryGetValue(placement.WindowId, out ExplorerEntry? restored)) restored.Surface.ResumeInteraction();
                if (placement.WindowId == TaskManagerOwnedWindowId)
                {
                    TaskManagerRunningIndicator.Visibility = Visibility.Visible;
                    TaskManagerRunningIndicator.Opacity = 1.0;
                    TaskManagerPanel.StartSampling();
                }
            }
        }

        OwnedWindowLifecycle taskLifecycle = GetOwnedWindowState(TaskManagerOwnedWindowId).Lifecycle;
        TaskManagerRunningIndicator.Visibility = taskLifecycle == OwnedWindowLifecycle.Closed ? Visibility.Collapsed : Visibility.Visible;
        TaskManagerRunningIndicator.Opacity = TaskManagerWindow.IsVisible ? 1.0 : 0.45;
        RefreshExplorerTaskbarState();
        UpdateOwnedWindowZOrder();
        UpdateOwnedWindowLayoutDensity(result.Placements.Select(placement => placement.WindowId).ToHashSet());
        ApplyCurrentLayoutPresentation();
        RefreshPartitionDividers();
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
        int zIndex = 5;
        // Lifecycle promotion preserves history; the active identity must still be on top.
        foreach (OwnedWindowState state in _ownedWindowLayouts.CaptureSnapshot().Windows
            .OrderBy(state => state.Id == _ownedWindowLayouts.ActiveWindowId)
            .ThenBy(state => state.ActivationOrdinal).ThenBy(state => state.RegistrationOrder))
        {
            Panel.SetZIndex(ResolveOwnedFrame(state.Id), zIndex++);
        }
    }

    private void UpdateOwnedWindowLayoutDensity(IReadOnlySet<OwnedWindowId>? affected = null)
    {
        foreach (ExplorerEntry entry in _explorers.Values.Where(entry => affected is null || affected.Contains(entry.Id)))
        {
            entry.Surface.SetLayoutDensity(
                entry.Frame.Width < ExplorerCompactWidthThreshold,
                entry.Frame.Height < ExplorerCompactHeightThreshold,
                entry.Frame.Width - entry.Frame.BorderThickness.Left - entry.Frame.BorderThickness.Right);
            ((Grid)entry.Frame.Child).RowDefinitions[0].Height = new GridLength(entry.Surface.IsCompactHeight ? 32 : 42);
            entry.TitleBar.ClipToBounds = entry.Surface.IsCompactWidth || entry.Surface.IsCompactHeight;
            foreach (Button control in new[] { entry.MinimizeButton, entry.MaximizeButton, entry.CloseButton })
                control.Height = entry.Surface.IsCompactHeight ? 28 : 40;
            entry.TitleText.Text = entry.Id != ExplorerOwnedWindowId
                ? $"EXPLORER / {entry.Id.Value}"
                : entry.Surface.IsCompactWidth ? "JARVIS2 / EXPLORER" : "JARVIS2 / FILE EXPLORER";
        }
        if (affected is not null && !affected.Contains(TaskManagerOwnedWindowId)) return;
        TaskManagerPanel.SetLayoutDensity(
            TaskManagerWindow.Width < ExplorerCompactWidthThreshold,
            TaskManagerWindow.Height < ExplorerCompactHeightThreshold);
        TaskManagerTitleText.Text = TaskManagerWindow.Width < ExplorerCompactWidthThreshold
            ? "TASK MANAGER"
            : "JARVIS2 / TASK MANAGER";
    }

    private void UpdateOwnedWindowControlNames()
    {
        foreach (ExplorerEntry entry in _explorers.Values)
        {
            string action = IsOwnedWindowMaximized(entry.Id) ? "Restore" : "Maximize";
            AutomationProperties.SetName(entry.MaximizeButton,
                entry.Id == ExplorerOwnedWindowId ? $"{action} Explorer" : $"{action} Explorer {entry.Id.Value}");
            entry.MaximizeButton.ToolTip = $"{action.ToUpperInvariant()} EXPLORER / {entry.Id.Value}";
        }
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
        (_ownedWindowLayouts.IsPartition ? _ownedWindowLayouts.IsWindowShown(id) : GetOwnedWindowState(id).AssignedPaneIndex == 0);

    private OwnedWindowState GetOwnedWindowState(OwnedWindowId id) =>
        _ownedWindowLayouts.TryGetWindow(id, out OwnedWindowState? state) &&
        state is not null
            ? state
            : throw new InvalidOperationException(
                $"Owned window {id} is not registered.");

    private FrameworkElement ResolveOwnedFrame(OwnedWindowId id) =>
        _explorers.TryGetValue(id, out ExplorerEntry? entry) ? entry.Frame :
        id == TaskManagerOwnedWindowId ? TaskManagerWindow :
        throw new ArgumentOutOfRangeException(nameof(id), "The owned frame is not registered.");

    private OwnedWindowId GetOwnedWindowId(FrameworkElement window) =>
        _explorers.Values.FirstOrDefault(entry => ReferenceEquals(entry.Frame, window))?.Id ??
        (ReferenceEquals(window, TaskManagerWindow) ? TaskManagerOwnedWindowId :
            throw new ArgumentOutOfRangeException(nameof(window), "The element is not a registered owned frame."));

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

    private void CancelExplorerPendingFocus()
    {
        CancelOwnedFocus();
        foreach (ExplorerEntry entry in _explorers.Values) entry.Surface.CancelPendingFocus();
    }

    private void CancelOwnedFocus()
    {
        _ownedFocusIntentGeneration++;
        if (_pendingOwnedFocus is { Status: DispatcherOperationStatus.Pending } pending) pending.Abort();
        _pendingOwnedFocus = null;
    }

    private void QueueOwnedFocus(Action focus, OwnedWindowId? owner = null)
    {
        CancelOwnedFocus();
        int generation = _ownedFocusIntentGeneration;
        IInputElement? focusOrigin = Keyboard.FocusedElement;
        _pendingOwnedFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _pendingOwnedFocus = null;
            if (generation == _ownedFocusIntentGeneration && IsLoaded &&
                ReferenceEquals(Keyboard.FocusedElement, focusOrigin) &&
                (owner is not OwnedWindowId id || (IsOwnedWindowActive(id) && GetOwnedWindowState(id).Lifecycle == OwnedWindowLifecycle.Visible)))
                focus();
        }));
    }

    private void QueueTaskbarFocus(Button button) => QueueOwnedFocus(() => _ = button.Focus());

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

    private static SolidColorBrush CreateBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
