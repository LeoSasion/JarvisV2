using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Jarvis.Win10.NeuralVoidPreview;

/// <summary>A transparent shared-edge overlay, not a pane or reserved gutter.</summary>
internal sealed class PartitionDivider : FrameworkElement
{
    private static readonly DependencyPropertyKey ValuePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Value),typeof(double),typeof(PartitionDivider),new FrameworkPropertyMetadata(0d,OnValueChanged));
    public static readonly DependencyProperty ValueProperty = ValuePropertyKey.DependencyProperty;
    private readonly DesktopShellSurface _shell;
    private bool _gestureActive;
    private PartitionGeometry? _gestureStart;
    private double _anchorPointer;
    private double _anchorPosition;
    private Point? _pointerOverrideForTest = null;
    private MouseButtonState? _buttonOverrideForTest = null;
    internal bool HasGesture => _gestureActive;
    internal PartitionCutKey Key { get; }
    internal long Generation { get; }
    internal bool IsCurrent => IsLoaded && IsEnabled && IsVisible &&
        _shell.PartitionState is PartitionGeometry geometry && geometry.Ratios.ContainsKey(Key) &&
        Generation == _shell.PartitionEpoch;
    private PartitionGeometry Geometry => _shell.PartitionState ?? throw new InvalidOperationException("Partition is no longer active.");

    internal PartitionDivider(DesktopShellSurface shell, PartitionDividerSpan span, long generation)
    {
        _shell = shell; Key = span.Key; Generation = generation;
        Focusable = true;
        Cursor = Key.Axis == PartitionAxis.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
        ToolTip = "DRAG SHARED EDGE / ARROW KEYS 8 DIP / SHIFT 32 DIP / HOME OR END";
        AutomationProperties.SetName(this,$"{Key.Axis} shared divider {Key.GridCoordinate}");
        AutomationProperties.SetHelpText(this,"Ratio range of the shared edge. Arrow keys move 8 DIP; Shift moves 32 DIP.");
        Panel.SetZIndex(this,100);
        UpdateSpan(span);
        Unloaded += (_,_) => FinishGesture();
        IsEnabledChanged += (_,_) => { if (!IsEnabled) FinishGesture(); };
    }

    internal void UpdateSpan(PartitionDividerSpan span)
    {
        const double hitWidth = 8;
        bool vertical = Key.Axis == PartitionAxis.Vertical;
        Canvas.SetLeft(this,vertical ? span.Position-hitWidth/2 : span.Start);
        Canvas.SetTop(this,vertical ? span.Start : span.Position-hitWidth/2);
        Width = vertical ? hitWidth : span.End-span.Start;
        Height = vertical ? span.End-span.Start : hitWidth;
        SetValue(ValuePropertyKey,Geometry.Ratios[Key]);
        InvalidateVisual();
    }

    private static void OnValueChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (UIElementAutomationPeer.FromElement((PartitionDivider)source) is AutomationPeer peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty,args.OldValue,args.NewValue);
    }

    public double Value => (double)GetValue(ValueProperty);
    internal (double Minimum,double Maximum) Limits
    {
        get
        {
            if (!IsCurrent || !Geometry.TryGetLimits(Key,out double minimum,out double maximum))
                throw new ElementNotEnabledException();
            double start = Key.Axis == PartitionAxis.Vertical ? Geometry.WorkArea.Left : Geometry.WorkArea.Top;
            double length = Key.Axis == PartitionAxis.Vertical ? Geometry.WorkArea.Width : Geometry.WorkArea.Height;
            return ((minimum-start)/length,(maximum-start)/length);
        }
    }

    internal void SetRatio(double ratio)
    {
        if (!IsCurrent) throw new ElementNotEnabledException();
        var limits = Limits;
        if (!double.IsFinite(ratio) || ratio < limits.Minimum || ratio > limits.Maximum)
            throw new ArgumentOutOfRangeException(nameof(ratio));
        PartitionGeometry geometry = Geometry;
        double start = Key.Axis == PartitionAxis.Vertical ? geometry.WorkArea.Left : geometry.WorkArea.Top;
        double length = Key.Axis == PartitionAxis.Vertical ? geometry.WorkArea.Width : geometry.WorkArea.Height;
        if (!_shell.ResizePartition(Key,start+length*ratio,Generation)) throw new InvalidOperationException("Partition resize rejected.");
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        if (!IsCurrent || _gestureActive) return;
        Point point = _pointerOverrideForTest ?? args.GetPosition(_shell.DesktopCanvas);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        _shell.BeginPartitionGesture(this);
        double position = Geometry.Dividers.Single(span => span.Key == Key).Position;
        if (!CaptureMouse() || !ReferenceEquals(Mouse.Captured,this)) return;
        _gestureStart = Geometry;
        _anchorPointer = Key.Axis == PartitionAxis.Vertical ? point.X : point.Y;
        _anchorPosition = position;
        _gestureActive = true;
        // Deliberately do not Focus(): pointer resizing retains the content owner.
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (!_gestureActive) return;
        if (!IsCurrent || !ReferenceEquals(Mouse.Captured,this) ||
            (_buttonOverrideForTest ?? args.LeftButton) != MouseButtonState.Pressed)
        { FinishGesture(); return; }
        Point point = _pointerOverrideForTest ?? args.GetPosition(_shell.DesktopCanvas);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        double displacement = (Key.Axis == PartitionAxis.Vertical ? point.X : point.Y)-_anchorPointer;
        double position = _anchorPosition+displacement;
        if (!double.IsFinite(displacement) || !double.IsFinite(position)) return;
        _ = _shell.ResizePartition(Key,position,Generation,pointerGesture:true);
        args.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    { if (_gestureActive) { FinishGesture(); args.Handled = true; } }
    protected override void OnLostMouseCapture(MouseEventArgs args)
    { FinishGesture(); base.OnLostMouseCapture(args); }

    internal void FinishGesture(bool rollback = false)
    {
        PartitionGeometry? start = _gestureActive && IsCurrent ? _gestureStart : null;
        // Invalidate first: ReleaseMouseCapture can synchronously re-enter loss.
        _gestureActive = false;
        _gestureStart = null;
        if (ReferenceEquals(Mouse.Captured,this)) ReleaseMouseCapture();
        if (rollback && start is not null) _shell.RestorePartitionCuts(start,Generation);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        if (!IsCurrent) return;
        int direction = Key.Axis == PartitionAxis.Vertical
            ? args.Key == System.Windows.Input.Key.Left ? -1 : args.Key == System.Windows.Input.Key.Right ? 1 : 0
            : args.Key == System.Windows.Input.Key.Up ? -1 : args.Key == System.Windows.Input.Key.Down ? 1 : 0;
        if (direction != 0)
        {
            double start = Key.Axis == PartitionAxis.Vertical ? Geometry.WorkArea.Left : Geometry.WorkArea.Top;
            double length = Key.Axis == PartitionAxis.Vertical ? Geometry.WorkArea.Width : Geometry.WorkArea.Height;
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 32 : 8;
            _ = _shell.ResizePartition(Key,start+length*Value+step*direction,Generation);
            args.Handled = true;
        }
        else if (args.Key is System.Windows.Input.Key.Home or System.Windows.Input.Key.End)
        {
            var limits = Limits; SetRatio(args.Key == System.Windows.Input.Key.Home ? limits.Minimum : limits.Maximum);
            args.Handled = true;
        }
        base.OnPreviewKeyDown(args);
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        if (IsKeyboardFocused)
        {
            bool vertical = Key.Axis == PartitionAxis.Vertical;
            context.DrawRectangle(Brushes.White,null,vertical
                ? new Rect(ActualWidth/2-0.5,0,1,ActualHeight)
                : new Rect(0,ActualHeight/2-0.5,ActualWidth,1));
        }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args) { InvalidateVisual(); base.OnGotKeyboardFocus(args); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs args) { InvalidateVisual(); base.OnLostKeyboardFocus(args); }
    protected override AutomationPeer OnCreateAutomationPeer() => new DividerPeer(this);

    private sealed class DividerPeer(PartitionDivider owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(PartitionDivider);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override AutomationOrientation GetOrientationCore() => owner.Key.Axis == PartitionAxis.Vertical ? AutomationOrientation.Vertical : AutomationOrientation.Horizontal;
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public bool IsReadOnly => !owner.IsCurrent;
        public double Value => owner.IsCurrent ? owner.Value : throw new ElementNotEnabledException();
        public double Minimum => owner.Limits.Minimum;
        public double Maximum => owner.Limits.Maximum;
        public double SmallChange => 8/(owner.Key.Axis == PartitionAxis.Vertical ? owner.Geometry.WorkArea.Width : owner.Geometry.WorkArea.Height);
        public double LargeChange => SmallChange*4;
        public void SetValue(double value) => owner.SetRatio(value);
    }
}
