using System.Windows;
using System.Windows.Media;

namespace Jarvis.Win10.NeuralVoidPreview;

public sealed class LayoutGlyph : FrameworkElement
{
    public static readonly DependencyProperty PresetProperty =
        DependencyProperty.Register(
            nameof(Preset),
            typeof(LayoutPreset),
            typeof(LayoutGlyph),
            new FrameworkPropertyMetadata(
                LayoutPreset.LeftMainRightStack,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(LayoutGlyph),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(LayoutGlyph),
            new FrameworkPropertyMetadata(
                Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(
            nameof(Accent),
            typeof(Brush),
            typeof(LayoutGlyph),
            new FrameworkPropertyMetadata(
                Brushes.Yellow,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public LayoutPreset Preset
    {
        get => (LayoutPreset)GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 20 || ActualHeight < 16)
        {
            return;
        }

        Pen contentPen = CreatePen(Stroke, 2.0);
        Pen accentPen = CreatePen(Accent, 2.0);
        Rect frame = new(
            7.0,
            6.0,
            Math.Max(2.0, Math.Floor(ActualWidth - 14.0)),
            Math.Max(2.0, Math.Floor(ActualHeight - 12.0)));

        drawingContext.DrawRectangle(null, contentPen, frame);
        DrawTopology(drawingContext, contentPen, frame);

        if (IsSelected)
        {
            DrawSelectionCorners(drawingContext, accentPen, frame);
            double markerSize = 5.0;
            drawingContext.DrawRectangle(
                Accent,
                null,
                new Rect(
                    frame.Right - markerSize - 4.0,
                    frame.Bottom - markerSize - 4.0,
                    markerSize,
                    markerSize));
        }
    }

    private void DrawTopology(
        DrawingContext drawingContext,
        Pen pen,
        Rect frame)
    {
        LayoutDefinition definition = LayoutCatalog.Get(Preset);
        int[,] owners = new int[LayoutCatalog.GridSize, LayoutCatalog.GridSize];
        for (int x = 0; x < LayoutCatalog.GridSize; x++)
        {
            for (int y = 0; y < LayoutCatalog.GridSize; y++)
            {
                owners[x, y] = -1;
            }
        }

        for (int owner = 0; owner < definition.Panes.Count; owner++)
        {
            LayoutPane pane = definition.Panes[owner];
            for (int x = pane.X; x < pane.Right; x++)
            {
                for (int y = pane.Y; y < pane.Bottom; y++)
                {
                    owners[x, y] = owner;
                }
            }
        }

        for (int boundary = 1; boundary < LayoutCatalog.GridSize; boundary++)
        {
            int start = -1;
            for (int y = 0; y <= LayoutCatalog.GridSize; y++)
            {
                bool divided =
                    y < LayoutCatalog.GridSize &&
                    owners[boundary - 1, y] != owners[boundary, y];
                if (divided && start < 0)
                {
                    start = y;
                }
                else if (!divided && start >= 0)
                {
                    double x = GridX(frame, boundary);
                    Line(
                        drawingContext,
                        pen,
                        x,
                        GridY(frame, start),
                        x,
                        GridY(frame, y));
                    start = -1;
                }
            }
        }

        for (int boundary = 1; boundary < LayoutCatalog.GridSize; boundary++)
        {
            int start = -1;
            for (int x = 0; x <= LayoutCatalog.GridSize; x++)
            {
                bool divided =
                    x < LayoutCatalog.GridSize &&
                    owners[x, boundary - 1] != owners[x, boundary];
                if (divided && start < 0)
                {
                    start = x;
                }
                else if (!divided && start >= 0)
                {
                    double y = GridY(frame, boundary);
                    Line(
                        drawingContext,
                        pen,
                        GridX(frame, start),
                        y,
                        GridX(frame, x),
                        y);
                    start = -1;
                }
            }
        }
    }

    private static double GridX(Rect frame, int coordinate) =>
        frame.Left + frame.Width * coordinate / LayoutCatalog.GridSize;

    private static double GridY(Rect frame, int coordinate) =>
        frame.Top + frame.Height * coordinate / LayoutCatalog.GridSize;

    private static void DrawSelectionCorners(
        DrawingContext drawingContext,
        Pen pen,
        Rect frame)
    {
        const double offset = 5.0;
        const double length = 9.0;
        double left = frame.Left - offset;
        double top = frame.Top - offset;
        double right = frame.Right + offset;
        double bottom = frame.Bottom + offset;

        Line(drawingContext, pen, left, top + length, left, top);
        Line(drawingContext, pen, left, top, left + length, top);
        Line(drawingContext, pen, right - length, top, right, top);
        Line(drawingContext, pen, right, top, right, top + length);
        Line(drawingContext, pen, left, bottom - length, left, bottom);
        Line(drawingContext, pen, left, bottom, left + length, bottom);
        Line(drawingContext, pen, right - length, bottom, right, bottom);
        Line(drawingContext, pen, right, bottom - length, right, bottom);
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        Pen pen = new(brush, thickness)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
            LineJoin = PenLineJoin.Miter,
        };
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        pen.Freeze();
        return pen;
    }

    private static void Line(
        DrawingContext drawingContext,
        Pen pen,
        double x1,
        double y1,
        double x2,
        double y2) =>
        drawingContext.DrawLine(
            pen,
            new Point(Pixel(x1), Pixel(y1)),
            new Point(Pixel(x2), Pixel(y2)));

    private static double Pixel(double value) => Math.Round(value);
}
