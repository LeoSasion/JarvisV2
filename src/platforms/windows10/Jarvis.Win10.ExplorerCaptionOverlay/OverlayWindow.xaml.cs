using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionOverlay;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleTransparent = 0x00000020L;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long ExtendedStyleNoActivate = 0x08000000L;
    private const int NonClientHitTestMessage = 0x0084;
    private const int TransparentHitTest = -1;
    private const double CaptionHeightPixels = 32;

    private readonly nint targetWindowHandle;
    private readonly ExplorerCaptionTargetIdentity target;
    private readonly DispatcherTimer monitor;
    private readonly DateTimeOffset expiresAtUtc;
    private NativeRectangle lastRectangle;
    private uint lastDpi;

    public OverlayWindow(
        nint targetWindowHandle,
        ExplorerCaptionTargetIdentity target,
        int ttlSeconds)
    {
        InitializeComponent();
        this.targetWindowHandle = targetWindowHandle;
        this.target = target;
        StartedAtUtc = DateTimeOffset.UtcNow;
        expiresAtUtc = StartedAtUtc.AddSeconds(ttlSeconds);

        monitor = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Normal,
            OnMonitorTick,
            Dispatcher);
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => monitor.Stop();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; private set; }

    public nint OverlayWindowHandle { get; private set; }

    public int ForegroundSamples { get; private set; }

    public int HiddenSamples { get; private set; }

    public int RepositionCount { get; private set; }

    public bool TargetRetired { get; private set; }

    protected override void OnContentRendered(EventArgs eventArgs)
    {
        base.OnContentRendered(eventArgs);
        OnMonitorTick(this, EventArgs.Empty);
        monitor.Start();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        CompletedAtUtc = DateTimeOffset.UtcNow;
        base.OnClosed(eventArgs);
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        HwndSource source =
            PresentationSource.FromVisual(this) as HwndSource ??
            throw new InvalidOperationException(
                "The owned overlay HWND source is unavailable.");
        OverlayWindowHandle = source.Handle;
        Marshal.SetLastPInvokeError(0);
        nint extendedStyleValue = GetWindowLongPtr(
            source.Handle,
            ExtendedStyleIndex);
        int getStyleError = Marshal.GetLastPInvokeError();
        if (extendedStyleValue == nint.Zero && getStyleError != 0)
        {
            throw new InvalidOperationException(
                "The owned overlay extended styles could not be read.");
        }

        long extendedStyle = extendedStyleValue.ToInt64();
        Marshal.SetLastPInvokeError(0);
        nint result = SetWindowLongPtr(
            source.Handle,
            ExtendedStyleIndex,
            new nint(
                extendedStyle |
                ExtendedStyleTransparent |
                ExtendedStyleToolWindow |
                ExtendedStyleNoActivate));
        if (result == nint.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            throw new InvalidOperationException(
                "The owned overlay extended styles could not be set.");
        }

        source.AddHook(OwnWindowProcedure);
    }

    private nint OwnWindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == NonClientHitTestMessage)
        {
            handled = true;
            return new nint(TransparentHitTest);
        }

        return nint.Zero;
    }

    private void OnMonitorTick(object? sender, EventArgs eventArgs)
    {
        if (DateTimeOffset.UtcNow >= expiresAtUtc)
        {
            Close();
            return;
        }

        if (!NativeOverlayTarget.TryReadExact(
                targetWindowHandle,
                target,
                out NativeTargetSnapshot snapshot,
                out _))
        {
            TargetRetired = true;
            Close();
            return;
        }

        PositionOverCaption(snapshot);
        if (snapshot.IsForeground)
        {
            ForegroundSamples++;
            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }
        }
        else
        {
            HiddenSamples++;
            if (Visibility != Visibility.Hidden)
            {
                Visibility = Visibility.Hidden;
            }
        }
    }

    private void PositionOverCaption(NativeTargetSnapshot snapshot)
    {
        NativeRectangle rectangle = snapshot.Rectangle;
        if (rectangle.Equals(lastRectangle) && snapshot.Dpi == lastDpi)
        {
            return;
        }

        double scale = snapshot.Dpi / 96.0;
        Left = rectangle.Left / scale;
        Top = rectangle.Top / scale;
        Width = (rectangle.Right - rectangle.Left) / scale;
        Height = CaptionHeightPixels / scale;
        lastRectangle = rectangle;
        lastDpi = snapshot.Dpi;
        RepositionCount++;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern nint GetWindowLongPtr(
        nint windowHandle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern nint SetWindowLongPtr(
        nint windowHandle,
        int index,
        nint newValue);
}
