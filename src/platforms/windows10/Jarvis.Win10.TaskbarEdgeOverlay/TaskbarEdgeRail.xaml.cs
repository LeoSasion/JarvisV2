using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

public partial class TaskbarEdgeRail : UserControl
{
    private readonly SolidColorBrush edgeBrush = new();
    private readonly SolidColorBrush lineBrush = new();
    private readonly SolidColorBrush glowBrush = new();
    private readonly SolidColorBrush coreBrush = new();

    public TaskbarEdgeRail()
    {
        InitializeComponent();
        EdgeLine.Fill = edgeBrush;
        SignalLine.Stroke = lineBrush;
        PulseGlow.Stroke = glowBrush;
        PulseCore.Stroke = coreBrush;
        PulsePoint.Fill = coreBrush;
    }

    internal bool ApplyFrame(RgbFrame rgb, bool forceGlow)
    {
        Color color = Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue);
        edgeBrush.Color = Color.FromArgb(74, color.R, color.G, color.B);
        lineBrush.Color = Color.FromArgb(142, color.R, color.G, color.B);
        glowBrush.Color = Color.FromArgb(196, color.R, color.G, color.B);
        coreBrush.Color = Color.FromArgb(242, color.R, color.G, color.B);

        bool renderGlow =
            forceGlow ||
            !SystemParameters.HighContrast &&
            SystemParameters.ClientAreaAnimation &&
            (RenderCapability.Tier >> 16) > 0;
        PulseGlow.Visibility =
            renderGlow ? Visibility.Visible : Visibility.Collapsed;
        return renderGlow;
    }
}
