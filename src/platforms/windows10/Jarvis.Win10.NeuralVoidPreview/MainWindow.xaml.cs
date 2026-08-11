using System.Windows;
using System.Windows.Input;
using Jarvis.VisualEffects;
using Jarvis.Win10.RgbThemeModel;

namespace Jarvis.Win10.NeuralVoidPreview;

public partial class MainWindow : Window
{
    private const double HorizonYellowHue = 56.470588;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RgbFrame frame = RgbEffectEngine.Sample(
                HorizonYellowHue,
                1.0,
                1.0,
                "static",
                0.0);
            PreviewSurface.ApplyFrame(frame);
            PreviewSurface.Focus();
        };
    }

    private void Window_OnKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
        }
    }
}
