using System.Windows;
using System.Windows.Media;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.NeuralVoidPreview;

/// <summary>
/// Retained, vector-only desktop decoration for the owned-process preview.
/// Static graphite datums are recorded once; only the two small RGB focus
/// junctions are redrawn for animated frames.
/// </summary>
public sealed class NeuralVectorLayer : FrameworkElement
{
    private const double DesignWidth = 1600.0;
    private const double DesignHeight = 900.0;

    private readonly DrawingVisual _staticVisual = new();
    private readonly DrawingVisual _signalVisual = new();
    private readonly VisualCollection _visuals;
    private readonly RetainedVectorScene _staticScene =
        Win10NeuralVectorSceneFactory.CreateStaticScene();
    private readonly WpfRetainedVectorSceneRenderer _staticRenderer =
        new(Win10NeuralVectorSceneFactory.CreatePalette());
    private readonly SolidColorBrush _accentBrush =
        new(Color.FromRgb(0x00, 0xFF, 0x9A));
    private readonly SolidColorBrush _structureBrush =
        new(Color.FromArgb(0x78, 0x2D, 0x3A, 0x38));
    private readonly SolidColorBrush _ghostBrush =
        new(Color.FromArgb(0x34, 0x2D, 0x3A, 0x38));
    private readonly Pen _accentPen;
    private readonly Pen _structurePen;
    private readonly Pen _ghostPen;
    private double _phase;
    private string _effectId = "static";

    public NeuralVectorLayer()
    {
        _accentPen = CreatePen(_accentBrush, 1.0);
        _structurePen = CreatePen(_structureBrush, 1.0);
        _ghostPen = CreatePen(_ghostBrush, 1.0);
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
                [
                    40.5,
                    63.5,
                    82.5,
                    186.5,
                    952.5,
                    1547.5,
                    1567.5,
                ],
                [
                    40.5,
                    60.5,
                    78.5,
                    813.5,
                    877.5,
                ]));

        WpfVectorSceneRenderReceipt receipt =
            _staticRenderer.Render(context, _staticScene);
        if (receipt.Result !=
            "rendered-retained-vector-scene")
        {
            context.Pop();
            context.Pop();
            return;
        }
        DrawRegistrationMarks(context);

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
        double wave =
            _effectId == "static"
                ? 0.25
                : (Math.Sin(cycle * Math.PI * 2.0) + 1.0) * 0.5;
        DrawFocusJunction(
            context,
            new Point(186.5, 78.5),
            wave);
        DrawFocusJunction(
            context,
            new Point(663.5, 357.5),
            wave * 0.72);

        context.Pop();
    }

    private void DrawRegistrationMarks(DrawingContext context)
    {
        DrawRegistrationSquare(
            context,
            _structurePen,
            new Point(43.5, 40.5));
        DrawRegistrationSquare(
            context,
            _ghostPen,
            new Point(62.5, 40.5));
        DrawRegistrationSquare(
            context,
            _ghostPen,
            new Point(81.5, 40.5));
        DrawRegistrationSquare(
            context,
            _structurePen,
            new Point(952.5, 60.5));
        DrawRegistrationSquare(
            context,
            _ghostPen,
            new Point(1567.5, 548.5));
        DrawRegistrationSquare(
            context,
            _structurePen,
            new Point(43.5, 877.5));
        DrawRegistrationSquare(
            context,
            _ghostPen,
            new Point(1567.5, 877.5));
    }

    private void DrawFocusJunction(
        DrawingContext context,
        Point focus,
        double pulse)
    {
        context.DrawLine(
            _accentPen,
            new Point(focus.X - 6.0, focus.Y),
            new Point(focus.X + 6.0, focus.Y));
        context.DrawLine(
            _accentPen,
            new Point(focus.X, focus.Y - 6.0),
            new Point(focus.X, focus.Y + 6.0));

        double ringRadius = 3.5 + (pulse * 2.5);
        context.PushOpacity(0.24 + (pulse * 0.28));
        context.DrawEllipse(
            null,
            _accentPen,
            focus,
            ringRadius,
            ringRadius);
        context.Pop();
        context.DrawEllipse(
            _accentBrush,
            null,
            focus,
            2.2,
            2.2);
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
                center.X - 2.0,
                center.Y - 2.0,
                4.0,
                4.0));
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
}
