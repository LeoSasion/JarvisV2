using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using Jarvis.Win10.RgbThemeModel;

namespace Jarvis.Win10.NeuralVoidPreview;

public partial class NeuralVoidPreviewSurface :
    UserControl,
    INotifyPropertyChanged
{
    private SolidColorBrush _accentBrush =
        CreateBrush(Color.FromRgb(0x00, 0xFF, 0x9A));
    private SolidColorBrush _accentDimBrush =
        CreateBrush(Color.FromArgb(0x98, 0x00, 0xFF, 0x9A));
    private SolidColorBrush _accentFaintBrush =
        CreateBrush(Color.FromArgb(0x28, 0x00, 0xFF, 0x9A));
    private string _accentHex = "#00FF9A";

    public NeuralVoidPreviewSurface()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    public SolidColorBrush AccentDimBrush
    {
        get => _accentDimBrush;
        private set => SetField(ref _accentDimBrush, value);
    }

    public SolidColorBrush AccentFaintBrush
    {
        get => _accentFaintBrush;
        private set => SetField(ref _accentFaintBrush, value);
    }

    public string AccentHex
    {
        get => _accentHex;
        private set => SetField(ref _accentHex, value);
    }

    public void ApplyFrame(RgbFrame frame)
    {
        Color accent =
            Color.FromRgb(frame.Red, frame.Green, frame.Blue);
        AccentBrush = CreateBrush(accent);
        AccentDimBrush =
            CreateBrush(
                Color.FromArgb(
                    0x98,
                    frame.Red,
                    frame.Green,
                    frame.Blue));
        AccentFaintBrush =
            CreateBrush(
                Color.FromArgb(
                    0x28,
                    frame.Red,
                    frame.Green,
                    frame.Blue));
        AccentHex = frame.Hex;
        VectorLayer.ApplyFrame(
            accent,
            frame.Phase,
            frame.EffectId);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
