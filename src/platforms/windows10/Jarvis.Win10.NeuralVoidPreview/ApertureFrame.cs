using System.Windows;
using System.Windows.Media;

namespace Jarvis.Win10.NeuralVoidPreview;

public enum ApertureFocusCorner
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Reusable, bitmap-free window contour based on the selected aperture
/// grammar. Static graphite geometry and the small RGB focus junction are
/// retained in separate DrawingVisuals so color animation does not rebuild
/// the full frame.
/// </summary>
public sealed class ApertureFrame : FrameworkElement
{
    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush),
            typeof(Brush),
            typeof(ApertureFrame),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                OnStaticAppearanceChanged));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(ApertureFrame),
            new FrameworkPropertyMetadata(
                Brushes.Transparent,
                OnFocusAppearanceChanged));

    public static readonly DependencyProperty FocusCornerProperty =
        DependencyProperty.Register(
            nameof(FocusCorner),
            typeof(ApertureFocusCorner),
            typeof(ApertureFrame),
            new FrameworkPropertyMetadata(
                ApertureFocusCorner.None,
                OnFocusAppearanceChanged));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(double),
            typeof(ApertureFrame),
            new FrameworkPropertyMetadata(
                16.0,
                OnStaticAppearanceChanged),
            IsFiniteNonNegative);

    public static readonly DependencyProperty CornerLengthProperty =
        DependencyProperty.Register(
            nameof(CornerLength),
            typeof(double),
            typeof(ApertureFrame),
            new FrameworkPropertyMetadata(
                26.0,
                OnStaticAppearanceChanged),
            IsFiniteNonNegative);

    private readonly DrawingVisual _staticVisual = new();
    private readonly DrawingVisual _focusVisual = new();
    private readonly VisualCollection _visuals;

    public ApertureFrame()
    {
        _visuals = new VisualCollection(this)
        {
            _staticVisual,
            _focusVisual,
        };
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public ApertureFocusCorner FocusCorner
    {
        get => (ApertureFocusCorner)GetValue(FocusCornerProperty);
        set => SetValue(FocusCornerProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double CornerLength
    {
        get => (double)GetValue(CornerLengthProperty);
        set => SetValue(CornerLengthProperty, value);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) =>
        _visuals[index];

    protected override void OnRenderSizeChanged(
        SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RedrawStatic();
        RedrawFocus();
    }

    private void RedrawStatic()
    {
        using DrawingContext context = _staticVisual.RenderOpen();
        if (RenderSize.Width < 4.0 || RenderSize.Height < 4.0)
        {
            return;
        }

        double radius = Math.Min(
            CornerRadius,
            Math.Min(
                (RenderSize.Width - 1.0) / 4.0,
                (RenderSize.Height - 1.0) / 4.0));
        double length = Math.Min(
            CornerLength,
            Math.Min(
                (RenderSize.Width - 1.0) / 5.0,
                (RenderSize.Height - 1.0) / 5.0));
        Rect frame =
            new(
                0.5,
                0.5,
                RenderSize.Width - 1.0,
                RenderSize.Height - 1.0);
        Pen linePen = CreatePen(LineBrush, 1.0);
        StreamGeometry contour =
            CreateApertureGeometry(frame, radius, length);
        context.DrawGeometry(null, linePen, contour);

        DrawRegistrationSquare(
            context,
            linePen,
            new Point(frame.Left + 4.0, frame.Top + 4.0));
        DrawRegistrationSquare(
            context,
            linePen,
            new Point(frame.Right - 4.0, frame.Top + 4.0));
        DrawRegistrationSquare(
            context,
            linePen,
            new Point(frame.Right - 4.0, frame.Bottom - 4.0));
    }

    private void RedrawFocus()
    {
        using DrawingContext context = _focusVisual.RenderOpen();
        if (
            FocusCorner == ApertureFocusCorner.None ||
            RenderSize.Width < 4.0 ||
            RenderSize.Height < 4.0)
        {
            return;
        }

        Point focus =
            FocusCorner switch
            {
                ApertureFocusCorner.TopLeft =>
                    new Point(0.5, 0.5),
                ApertureFocusCorner.TopRight =>
                    new Point(RenderSize.Width - 0.5, 0.5),
                ApertureFocusCorner.BottomLeft =>
                    new Point(0.5, RenderSize.Height - 0.5),
                ApertureFocusCorner.BottomRight =>
                    new Point(
                        RenderSize.Width - 0.5,
                        RenderSize.Height - 0.5),
                _ => default,
            };
        double horizontalDirection =
            FocusCorner is
                ApertureFocusCorner.TopLeft or
                ApertureFocusCorner.BottomLeft
                ? 1.0
                : -1.0;
        double verticalDirection =
            FocusCorner is
                ApertureFocusCorner.TopLeft or
                ApertureFocusCorner.TopRight
                ? 1.0
                : -1.0;
        double horizontalLength =
            Math.Min(122.0, RenderSize.Width * 0.34);
        double verticalLength =
            Math.Min(82.0, RenderSize.Height * 0.28);
        Pen accentPen = CreatePen(AccentBrush, 1.0);

        context.DrawLine(
            accentPen,
            focus,
            new Point(
                focus.X + (horizontalDirection * horizontalLength),
                focus.Y));
        context.DrawLine(
            accentPen,
            focus,
            new Point(
                focus.X,
                focus.Y + (verticalDirection * verticalLength)));
        context.DrawLine(
            accentPen,
            new Point(focus.X - 6.0, focus.Y),
            new Point(focus.X + 6.0, focus.Y));
        context.DrawLine(
            accentPen,
            new Point(focus.X, focus.Y - 6.0),
            new Point(focus.X, focus.Y + 6.0));

        context.PushOpacity(0.42);
        context.DrawEllipse(
            null,
            accentPen,
            focus,
            5.0,
            5.0);
        context.Pop();
        context.DrawEllipse(
            AccentBrush,
            null,
            focus,
            2.0,
            2.0);
    }

    private static StreamGeometry CreateApertureGeometry(
        Rect frame,
        double radius,
        double length)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            DrawTangentCorner(
                context,
                new Point(frame.Left, frame.Top + radius + length),
                new Point(frame.Left, frame.Top + radius),
                new Point(frame.Left + radius, frame.Top),
                new Point(frame.Left + radius + length, frame.Top),
                radius,
                SweepDirection.Clockwise);
            DrawTangentCorner(
                context,
                new Point(frame.Right - radius - length, frame.Top),
                new Point(frame.Right - radius, frame.Top),
                new Point(frame.Right, frame.Top + radius),
                new Point(frame.Right, frame.Top + radius + length),
                radius,
                SweepDirection.Clockwise);
            DrawTangentCorner(
                context,
                new Point(frame.Right, frame.Bottom - radius - length),
                new Point(frame.Right, frame.Bottom - radius),
                new Point(frame.Right - radius, frame.Bottom),
                new Point(frame.Right - radius - length, frame.Bottom),
                radius,
                SweepDirection.Clockwise);
            DrawTangentCorner(
                context,
                new Point(frame.Left + radius + length, frame.Bottom),
                new Point(frame.Left + radius, frame.Bottom),
                new Point(frame.Left, frame.Bottom - radius),
                new Point(frame.Left, frame.Bottom - radius - length),
                radius,
                SweepDirection.Clockwise);

            DrawSplitEdge(
                context,
                new Point(frame.Left + radius + length, frame.Top),
                new Point(frame.Right - radius - length, frame.Top));
            DrawSplitEdge(
                context,
                new Point(frame.Right, frame.Top + radius + length),
                new Point(frame.Right, frame.Bottom - radius - length));
            DrawSplitEdge(
                context,
                new Point(frame.Right - radius - length, frame.Bottom),
                new Point(frame.Left + radius + length, frame.Bottom));
            DrawSplitEdge(
                context,
                new Point(frame.Left, frame.Bottom - radius - length),
                new Point(frame.Left, frame.Top + radius + length));
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawTangentCorner(
        StreamGeometryContext context,
        Point start,
        Point arcStart,
        Point arcEnd,
        Point end,
        double radius,
        SweepDirection sweepDirection)
    {
        context.BeginFigure(start, false, false);
        context.LineTo(arcStart, true, false);
        context.ArcTo(
            arcEnd,
            new Size(radius, radius),
            0.0,
            false,
            sweepDirection,
            true,
            false);
        context.LineTo(end, true, false);
    }

    private static void DrawSplitEdge(
        StreamGeometryContext context,
        Point start,
        Point end)
    {
        Vector delta = end - start;
        if (delta.Length < 24.0)
        {
            return;
        }

        Point firstEnd = start + (delta * 0.47);
        Point secondStart = start + (delta * 0.53);
        context.BeginFigure(start, false, false);
        context.LineTo(firstEnd, true, false);
        context.BeginFigure(secondStart, false, false);
        context.LineTo(end, true, false);
    }

    private static void DrawRegistrationSquare(
        DrawingContext context,
        Pen pen,
        Point center)
    {
        context.DrawRectangle(
            null,
            pen,
            new Rect(
                center.X - 1.5,
                center.Y - 1.5,
                3.0,
                3.0));
    }

    private static Pen CreatePen(
        Brush brush,
        double thickness) =>
        new(brush, thickness)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
            LineJoin = PenLineJoin.Round,
        };

    private static bool IsFiniteNonNegative(object value) =>
        value is double number &&
        double.IsFinite(number) &&
        number >= 0.0;

    private static void OnStaticAppearanceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs _)
    {
        ((ApertureFrame)dependencyObject).RedrawStatic();
    }

    private static void OnFocusAppearanceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs _)
    {
        ((ApertureFrame)dependencyObject).RedrawFocus();
    }
}
