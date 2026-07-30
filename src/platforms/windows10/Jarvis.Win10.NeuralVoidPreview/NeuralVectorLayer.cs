using System.Windows;
using System.Windows.Media;

namespace Jarvis.Win10.NeuralVoidPreview;

/// <summary>
/// Retained, vector-only decoration for the owned-process preview.
/// Static point/line/plane geometry is recorded once into a DrawingVisual.
/// Only the small signal visual is redrawn for each RGB frame.
/// </summary>
public sealed class NeuralVectorLayer : FrameworkElement
{
    private const double DesignWidth = 1600.0;
    private const double DesignHeight = 900.0;

    private readonly DrawingVisual _staticVisual = new();
    private readonly DrawingVisual _signalVisual = new();
    private readonly VisualCollection _visuals;
    private readonly SolidColorBrush _accentBrush =
        new(Color.FromRgb(0x00, 0xFF, 0x9A));
    private readonly SolidColorBrush _dimBrush =
        new(Color.FromArgb(0x78, 0x00, 0xFF, 0x9A));
    private readonly SolidColorBrush _faintBrush =
        new(Color.FromArgb(0x2C, 0x00, 0xFF, 0x9A));
    private readonly SolidColorBrush _planeBrush =
        new(Color.FromArgb(0x0B, 0x00, 0xFF, 0x9A));
    private readonly Pen _accentPen;
    private readonly Pen _dimPen;
    private readonly Pen _faintPen;
    private double _phase;
    private string _effectId = "static";

    public NeuralVectorLayer()
    {
        _accentPen = CreatePen(_accentBrush, 1.0);
        _dimPen = CreatePen(_dimBrush, 1.0);
        _faintPen = CreatePen(_faintBrush, 1.0);
        _visuals = new VisualCollection(this)
        {
            _staticVisual,
            _signalVisual,
        };
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) =>
        _visuals[index];

    public void ApplyFrame(
        Color accent,
        double phase,
        string effectId)
    {
        bool colorChanged =
            _accentBrush.Color.R != accent.R ||
            _accentBrush.Color.G != accent.G ||
            _accentBrush.Color.B != accent.B;
        if (colorChanged)
        {
            _accentBrush.Color = accent;
            _dimBrush.Color =
                Color.FromArgb(0x78, accent.R, accent.G, accent.B);
            _faintBrush.Color =
                Color.FromArgb(0x2C, accent.R, accent.G, accent.B);
            _planeBrush.Color =
                Color.FromArgb(0x0B, accent.R, accent.G, accent.B);
        }

        bool signalChanged =
            colorChanged ||
            !string.Equals(
                _effectId,
                effectId,
                StringComparison.Ordinal) ||
            Math.Abs(_phase - phase) > 0.000001;
        _phase = phase;
        _effectId = effectId;
        if (signalChanged)
        {
            RedrawSignal();
        }
    }

    protected override void OnRenderSizeChanged(
        SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RedrawStatic();
        RedrawSignal();
    }

    private void RedrawStatic()
    {
        using DrawingContext context = _staticVisual.RenderOpen();
        if (RenderSize.Width <= 0.0 || RenderSize.Height <= 0.0)
        {
            return;
        }

        context.PushTransform(
            new ScaleTransform(
                RenderSize.Width / DesignWidth,
                RenderSize.Height / DesignHeight));
        context.PushGuidelineSet(
            new GuidelineSet(
                [72.5, 108.5, 186.5, 1477.5, 1527.5],
                [64.5, 78.5, 813.5, 841.5]));

        DrawPlanes(context);
        DrawStructuralRails(context);
        DrawOpenFrame(
            context,
            new Rect(186.5, 78.5, 1291.0, 735.0),
            28.0,
            _dimPen);
        DrawOpenFrame(
            context,
            new Rect(432.5, 313.5, 897.0, 132.0),
            16.0,
            _faintPen);
        DrawRegistrationTicks(context);
        DrawNodes(context);

        context.Pop();
        context.Pop();
    }

    private void RedrawSignal()
    {
        using DrawingContext context = _signalVisual.RenderOpen();
        if (RenderSize.Width <= 0.0 || RenderSize.Height <= 0.0)
        {
            return;
        }

        context.PushTransform(
            new ScaleTransform(
                RenderSize.Width / DesignWidth,
                RenderSize.Height / DesignHeight));

        double cycle = _phase - Math.Floor(_phase);
        double amplitude = _effectId switch
        {
            "signal-pulse" => 4.0,
            "breathe" => 2.8,
            "spectrum" => 2.4,
            _ => 1.4,
        };
        StreamGeometry waveform =
            CreateWaveform(
                new Point(1384.5, 34.5),
                180.0,
                amplitude,
                cycle);
        context.DrawGeometry(null, _dimPen, waveform);

        double routeX = 188.5 + (cycle * 1285.0);
        context.DrawLine(
            _accentPen,
            new Point(routeX - 18.0, 813.5),
            new Point(routeX + 18.0, 813.5));
        context.DrawEllipse(
            _accentBrush,
            null,
            new Point(routeX, 813.5),
            2.8,
            2.8);

        double nodePulse =
            _effectId == "static"
                ? 0.0
                : (Math.Sin(cycle * Math.PI * 2.0) + 1.0) * 0.5;
        context.PushOpacity(0.18 + (nodePulse * 0.22));
        context.DrawEllipse(
            null,
            _accentPen,
            new Point(1477.5, 78.5),
            7.0 + (nodePulse * 4.0),
            7.0 + (nodePulse * 4.0));
        context.Pop();
        context.Pop();
    }

    private void DrawPlanes(DrawingContext context)
    {
        StreamGeometry upperPlane =
            CreatePolygon(
                [
                    new Point(186.5, 78.5),
                    new Point(1477.5, 78.5),
                    new Point(1451.5, 104.5),
                    new Point(212.5, 104.5),
                ]);
        context.DrawGeometry(_planeBrush, null, upperPlane);

        StreamGeometry lowerPlane =
            CreatePolygon(
                [
                    new Point(186.5, 785.5),
                    new Point(1477.5, 785.5),
                    new Point(1477.5, 813.5),
                    new Point(214.5, 813.5),
                ]);
        context.DrawGeometry(_planeBrush, null, lowerPlane);
    }

    private void DrawStructuralRails(DrawingContext context)
    {
        context.DrawLine(
            _faintPen,
            new Point(72.5, 64.5),
            new Point(1576.5, 64.5));
        context.DrawLine(
            _faintPen,
            new Point(72.5, 64.5),
            new Point(72.5, 841.5));

        StreamGeometry upperRoute =
            CreatePolyline(
                [
                    new Point(108.5, 92.5),
                    new Point(150.5, 92.5),
                    new Point(172.5, 78.5),
                    new Point(186.5, 78.5),
                ]);
        context.DrawGeometry(null, _dimPen, upperRoute);

        StreamGeometry lowerRoute =
            CreatePolyline(
                [
                    new Point(72.5, 782.5),
                    new Point(96.5, 782.5),
                    new Point(120.5, 813.5),
                    new Point(186.5, 813.5),
                ]);
        context.DrawGeometry(null, _accentPen, lowerRoute);

        StreamGeometry statusRoute =
            CreatePolyline(
                [
                    new Point(1477.5, 78.5),
                    new Point(1511.5, 78.5),
                    new Point(1527.5, 94.5),
                    new Point(1576.5, 94.5),
                ]);
        context.DrawGeometry(null, _dimPen, statusRoute);

        StreamGeometry selectedRoute =
            CreatePolyline(
                [
                    new Point(1329.5, 379.5),
                    new Point(1361.5, 379.5),
                    new Point(1381.5, 399.5),
                    new Point(1437.5, 399.5),
                ]);
        context.DrawGeometry(null, _faintPen, selectedRoute);
    }

    private void DrawRegistrationTicks(DrawingContext context)
    {
        Point[] anchors =
        [
            new(186.5, 78.5),
            new(1477.5, 78.5),
            new(186.5, 813.5),
            new(1477.5, 813.5),
        ];
        foreach (Point anchor in anchors)
        {
            context.DrawLine(
                _faintPen,
                new Point(anchor.X - 4.0, anchor.Y),
                new Point(anchor.X + 4.0, anchor.Y));
            context.DrawLine(
                _faintPen,
                new Point(anchor.X, anchor.Y - 4.0),
                new Point(anchor.X, anchor.Y + 4.0));
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        DrawNode(context, new Point(72.5, 108.5), false);
        DrawNode(context, new Point(72.5, 278.5), false);
        DrawNode(context, new Point(72.5, 460.5), false);
        DrawNode(context, new Point(72.5, 782.5), true);
        DrawNode(context, new Point(186.5, 78.5), false);
        DrawNode(context, new Point(186.5, 813.5), false);
        DrawNode(context, new Point(1477.5, 78.5), true);
        DrawNode(context, new Point(1477.5, 813.5), false);
        DrawNode(context, new Point(1381.5, 399.5), false);
        DrawNode(context, new Point(1527.5, 94.5), false);
    }

    private void DrawNode(
        DrawingContext context,
        Point center,
        bool active)
    {
        context.DrawEllipse(
            active ? _accentBrush : null,
            active ? _accentPen : _dimPen,
            center,
            active ? 3.2 : 2.2,
            active ? 3.2 : 2.2);
    }

    private static void DrawOpenFrame(
        DrawingContext context,
        Rect rect,
        double length,
        Pen pen)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext stream = geometry.Open())
        {
            DrawCorner(
                stream,
                new Point(rect.Left, rect.Top + length),
                new Point(rect.Left, rect.Top),
                new Point(rect.Left + length, rect.Top));
            DrawCorner(
                stream,
                new Point(rect.Right - length, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Top + length));
            DrawCorner(
                stream,
                new Point(rect.Left, rect.Bottom - length),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left + length, rect.Bottom));
            DrawCorner(
                stream,
                new Point(rect.Right - length, rect.Bottom),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Right, rect.Bottom - length));
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawCorner(
        StreamGeometryContext context,
        Point start,
        Point corner,
        Point end)
    {
        context.BeginFigure(start, false, false);
        context.LineTo(corner, true, false);
        context.LineTo(end, true, false);
    }

    private static StreamGeometry CreatePolyline(
        IReadOnlyList<Point> points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreatePolygon(
        IReadOnlyList<Point> points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            for (int index = 1; index < points.Count; index++)
            {
                context.LineTo(points[index], true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateWaveform(
        Point origin,
        double width,
        double amplitude,
        double cycle)
    {
        const int SampleCount = 48;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int index = 0; index <= SampleCount; index++)
            {
                double progress = index / (double)SampleCount;
                double envelope =
                    Math.Sin(progress * Math.PI);
                double angle =
                    (progress * Math.PI * 12.0) +
                    (cycle * Math.PI * 2.0);
                Point point =
                    new(
                        origin.X + (progress * width),
                        origin.Y +
                        (Math.Sin(angle) * amplitude * envelope));
                if (index == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Pen CreatePen(
        Brush brush,
        double thickness) =>
        new(brush, thickness)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
            LineJoin = PenLineJoin.Miter,
        };
}
