using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Jarvis.ControlCenter;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };

    public MainWindow()
    {
        InitializeComponent();
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        UpdateClock();
    }

    private void UpdateClock()
    {
        LocalClock.Text = DateTimeOffset.Now.ToString("HH:mm:ss");
        LocalDate.Text = DateTimeOffset.Now.ToString("yyyy.MM.dd");
    }

    private void TitleBar_OnMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        clockTimer.Stop();
        base.OnClosed(eventArgs);
    }
}
