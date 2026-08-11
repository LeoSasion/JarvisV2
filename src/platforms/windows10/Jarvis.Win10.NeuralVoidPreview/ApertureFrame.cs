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
/// Reusable, bitmap-free orthogonal window contour. Static graphite geometry
/// and the small RGB focus registration are retained in separate
/// DrawingVisuals so color animation does not rebuild the full frame.
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

        if (!Win10ApertureVectorSceneFactory.TryCreate(
                RenderSize.Width,
                RenderSize.Height,
                CornerLength,
                LineBrush,
                out Win10ApertureVectorSceneInputs? inputs) ||
            inputs is null)
        {
            return;
        }

        WpfRetainedVectorSceneRenderer renderer =
            new(inputs.Palette);
        WpfVectorSceneRenderReceipt receipt =
            renderer.Render(context, inputs.Scene);
        if (receipt.Result !=
            "rendered-retained-vector-scene")
        {
            return;
        }
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

        if (!Win10ApertureVectorSceneFactory.TryCreateFocus(
                RenderSize.Width,
                RenderSize.Height,
                FocusCorner,
                AccentBrush,
                out Win10ApertureVectorSceneInputs? inputs) ||
            inputs is null)
        {
            return;
        }

        WpfRetainedVectorSceneRenderer renderer =
            new(inputs.Palette);
        renderer.Render(context, inputs.Scene);
    }

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
