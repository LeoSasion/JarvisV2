using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;
using Jarvis.Win10.RgbThemeModel;

namespace Jarvis.Win10.NeuralVoidPreview;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _startedAt =
        DateTimeOffset.UtcNow;
    private string _effectId = "signal-pulse";

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            (_, _) => RenderFrame(),
            Dispatcher);
        Loaded += (_, _) =>
        {
            RenderFrame();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void PresetButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string hueText } &&
            double.TryParse(
                hueText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double hue))
        {
            HueSlider.Value = hue;
        }
    }

    private void HueSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (!IsInitialized)
        {
            return;
        }

        HueValue.Text =
            $"{eventArgs.NewValue.ToString("F1", CultureInfo.InvariantCulture)}°";
        RenderFrame();
    }

    private void EffectSelector_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (EffectSelector.SelectedItem is
            ComboBoxItem { Tag: string effectId })
        {
            _effectId = effectId;
            if (IsInitialized)
            {
                RenderFrame();
            }
        }
    }

    private void RenderFrame()
    {
        double seconds =
            (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
        double cyclesPerSecond = _effectId switch
        {
            "breathe" => 12.0 / 60.0,
            "spectrum" => 4.0 / 60.0,
            "signal-pulse" => 30.0 / 60.0,
            _ => 0.0,
        };
        double phase =
            cyclesPerSecond == 0.0
                ? 0.0
                : seconds * cyclesPerSecond;
        RgbFrame frame =
            RgbEffectEngine.Sample(
                HueSlider.Value,
                1.0,
                1.0,
                _effectId,
                phase);
        PreviewSurface.ApplyFrame(frame);
        FrameStatus.Text =
            $"{frame.Hex} / {_effectId.ToUpperInvariant()}";
        FrameStatus.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    frame.Red,
                    frame.Green,
                    frame.Blue));
    }
}
