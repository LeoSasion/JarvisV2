using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleTransparent = 0x00000020L;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long ExtendedStyleNoActivate = 0x08000000L;
    private const int NonClientHitTestMessage = 0x0084;
    private const int TransparentHitTest = -1;
    private const int RenderSampleHz = 30;
    private const double SignalHue = 175.18;
    private const double SignalSaturation = 0.61712;
    private const double SignalValue = 0.87059;

    private readonly nint targetWindowHandle;
    private readonly TaskbarTargetIdentity target;
    private readonly DispatcherTimer monitor;
    private readonly DateTimeOffset expiresAtUtc;
    private readonly Stopwatch signalClock = Stopwatch.StartNew();
    private readonly IReadOnlyList<RgbFrame> signalFrames;
    private NativeRectangle lastRectangle;
    private uint lastDpi;

    public OverlayWindow(
        nint targetWindowHandle,
        TaskbarTargetIdentity target,
        int ttlSeconds)
    {
        InitializeComponent();
        this.targetWindowHandle = targetWindowHandle;
        this.target = target;
        StartedAtUtc = DateTimeOffset.UtcNow;
        expiresAtUtc = StartedAtUtc.AddSeconds(ttlSeconds);
        signalFrames = BuildSignalFrames();

        monitor = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000.0 / RenderSampleHz),
            DispatcherPriority.Render,
            OnMonitorTick,
            Dispatcher);
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => monitor.Stop();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; private set; }

    public nint OverlayWindowHandle { get; private set; }

    public int VisibleSamples { get; private set; }

    public int HiddenSamples { get; private set; }

    public int FullscreenRetreatSamples { get; private set; }

    public int AccessibilityRetreatSamples { get; private set; }

    public int RepositionCount { get; private set; }

    public int RenderedFrameCount { get; private set; }

    public bool TargetRetiredOrIncompatible { get; private set; }

    public bool GlowRendered { get; private set; }

    protected override void OnContentRendered(EventArgs eventArgs)
    {
        base.OnContentRendered(eventArgs);
        OnMonitorTick(this, EventArgs.Empty);
        monitor.Start();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        CompletedAtUtc = DateTimeOffset.UtcNow;
        signalClock.Stop();
        base.OnClosed(eventArgs);
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        HwndSource source =
            PresentationSource.FromVisual(this) as HwndSource ??
            throw new InvalidOperationException(
                "The owned taskbar overlay HWND source is unavailable.");
        OverlayWindowHandle = source.Handle;
        Marshal.SetLastPInvokeError(0);
        nint extendedStyleValue = GetWindowLongPtr(
            source.Handle,
            ExtendedStyleIndex);
        int getStyleError = Marshal.GetLastPInvokeError();
        if (extendedStyleValue == nint.Zero && getStyleError != 0)
        {
            throw new InvalidOperationException(
                "The owned taskbar overlay extended styles could not be read.");
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
                "The owned taskbar overlay extended styles could not be set.");
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

        if (!NativeTaskbarTarget.TryReadExact(
                targetWindowHandle,
                target,
                out NativeTaskbarSnapshot snapshot,
                out _))
        {
            TargetRetiredOrIncompatible = true;
            Close();
            return;
        }

        PositionOverTaskbarEdge(snapshot);
        bool accessibilityRetreat = SystemParameters.HighContrast;
        bool retreat =
            accessibilityRetreat ||
            !snapshot.Visible ||
            snapshot.EdgeOccludedByFullscreen;
        if (retreat)
        {
            HiddenSamples++;
            if (snapshot.EdgeOccludedByFullscreen)
            {
                FullscreenRetreatSamples++;
            }
            if (accessibilityRetreat)
            {
                AccessibilityRetreatSamples++;
            }
            if (Visibility != Visibility.Hidden)
            {
                Visibility = Visibility.Hidden;
            }
            return;
        }

        VisibleSamples++;
        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }
        Opacity = 1.0;
        RenderSignalFrame();
    }

    private void PositionOverTaskbarEdge(NativeTaskbarSnapshot snapshot)
    {
        if (snapshot.Rectangle.Equals(lastRectangle) &&
            snapshot.Dpi == lastDpi)
        {
            return;
        }

        double scale = snapshot.Dpi / 96.0;
        Left = snapshot.Rectangle.Left / scale;
        Top = snapshot.Rectangle.Top / scale;
        Width = snapshot.Rectangle.Width / scale;
        Height = TaskbarOverlayPolicy.EdgeHeightDips;
        lastRectangle = snapshot.Rectangle;
        lastDpi = snapshot.Dpi;
        RepositionCount++;
    }

    private void RenderSignalFrame()
    {
        bool animate =
            !SystemParameters.HighContrast &&
            SystemParameters.ClientAreaAnimation;
        int frameIndex = animate
            ? (int)(signalClock.Elapsed.TotalSeconds * RenderSampleHz) %
                signalFrames.Count
            : RenderSampleHz / 4;
        ApplySignalFrame(signalFrames[frameIndex], false);
        RenderedFrameCount++;
    }

    internal void ApplyPreviewFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= signalFrames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        ApplySignalFrame(signalFrames[frameIndex], true);
    }

    private void ApplySignalFrame(RgbFrame rgb, bool forceGlow)
    {
        GlowRendered |= Rail.ApplyFrame(rgb, forceGlow);
    }

    internal static IReadOnlyList<RgbFrame> BuildSignalFrames()
    {
        RgbFrame[] frames = new RgbFrame[RenderSampleHz];
        for (int index = 0; index < frames.Length; index++)
        {
            double phase = index / (double)frames.Length;
            RgbFrame rgb = RgbEffectEngine.Sample(
                SignalHue,
                SignalSaturation,
                SignalValue,
                "signal-pulse",
                phase);
            VisualSignalCompilationReceipt compilation =
                VisualSignalFrameCompiler.Compile(
                    VisualSignalFrameFactory.Create(
                        index,
                        index / (double)RenderSampleHz,
                        30.0,
                        1.0,
                        rgb));
            if (!compilation.ReadyForOwnedProcessPrototype)
            {
                throw new InvalidOperationException(
                    "Shared visual signal frame was rejected.");
            }

            frames[index] = compilation.SafeFrame.Accent;
        }

        return frames;
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
