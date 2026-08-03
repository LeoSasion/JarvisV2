using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using Jarvis.DesktopPresence;
using Forms = System.Windows.Forms;

namespace Jarvis.ControlCenter;

internal sealed class DesktopPresenceCoordinator : IDisposable
{
    private readonly MainWindow window;
    private readonly DesktopSummonHotKey summonHotKey = new();
    private readonly Forms.NotifyIcon? trayIcon;
    private readonly Forms.ContextMenuStrip? trayMenu;
    private readonly IReadOnlyList<Icon> ownedIcons = [];
    private HwndSource? windowSource;
    private DesktopAttentionSnapshot attentionSnapshot;
    private WindowState restoreState = WindowState.Normal;
    private int disposed;

    public DesktopPresenceCoordinator(MainWindow window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        attentionSnapshot = window.DesktopAttention;
        List<Icon> icons = [];
        Forms.ContextMenuStrip? menu = null;
        Forms.NotifyIcon? notifyIcon = null;
        try
        {
            foreach (JarvisPresenceSignal signal in
                Enum.GetValues<JarvisPresenceSignal>())
            {
                icons.Add(JarvisPresenceIcon.Create(signal));
            }
            menu = new Forms.ContextMenuStrip();
            menu.ShowImageMargin = false;
            Forms.ToolStripMenuItem openItem = new(
                UiText.Get("Loc.Tray.Open"));
            openItem.Click += (_, _) => ShowWindow();
            Forms.ToolStripMenuItem exitItem = new(
                UiText.Get("Loc.Tray.Exit"));
            exitItem.Click += (_, _) => window.RequestApplicationExit();
            _ = menu.Items.Add(openItem);
            _ = menu.Items.Add(new Forms.ToolStripSeparator());
            _ = menu.Items.Add(exitItem);

            notifyIcon = new Forms.NotifyIcon();
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Icon = GetPresenceIcon(icons, attentionSnapshot.Kind);
            notifyIcon.Text = CreateTrayText();
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += OnTrayIconDoubleClick;
            notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
            ownedIcons = icons;
            trayMenu = menu;
            trayIcon = notifyIcon;
        }
        catch (Exception exception) when (IsRecoverableIntegrationFailure(exception))
        {
            if (notifyIcon is not null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            menu?.Dispose();
            foreach (Icon createdIcon in icons)
            {
                createdIcon.Dispose();
            }
            window.ReportDesktopPresenceUnavailable();
        }
        window.SourceInitialized += OnWindowSourceInitialized;
        window.DesktopHideRequested += OnDesktopHideRequested;
        window.RuntimePhaseChanged += OnRuntimePhaseChanged;
        window.DesktopAttentionChanged += OnDesktopAttentionChanged;
        window.StateChanged += OnWindowStateChanged;
        window.Closed += OnWindowClosed;
    }

    public bool CanHideToNotificationArea => trayIcon is not null;

    public void HideWindow()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }
        if (!CanHideToNotificationArea)
        {
            window.ShowActivated = true;
            window.ShowInTaskbar = true;
            if (!window.IsVisible)
            {
                window.Show();
            }
            window.WindowState = WindowState.Minimized;
            return;
        }
        if (window.WindowState != WindowState.Minimized)
        {
            restoreState = window.WindowState;
        }
        window.ShowInTaskbar = false;
        window.Hide();
    }

    public void ShowWindow()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }
        window.ShowActivated = true;
        window.ShowInTaskbar = true;
        if (!window.IsVisible)
        {
            window.Show();
        }
        window.WindowState = restoreState == WindowState.Minimized
            ? WindowState.Normal
            : restoreState;
        _ = window.Activate();
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle != nint.Zero)
        {
            _ = DesktopSummonHotKey.TrySetForegroundWindow(handle);
        }
        window.FocusConversationInput();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        window.SourceInitialized -= OnWindowSourceInitialized;
        window.DesktopHideRequested -= OnDesktopHideRequested;
        window.RuntimePhaseChanged -= OnRuntimePhaseChanged;
        window.DesktopAttentionChanged -= OnDesktopAttentionChanged;
        window.StateChanged -= OnWindowStateChanged;
        window.Closed -= OnWindowClosed;
        if (windowSource is not null)
        {
            windowSource.RemoveHook(OnWindowMessage);
            windowSource = null;
        }
        summonHotKey.Dispose();
        if (trayIcon is not null)
        {
            trayIcon.DoubleClick -= OnTrayIconDoubleClick;
            trayIcon.BalloonTipClicked -= OnBalloonTipClicked;
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }
        trayMenu?.Dispose();
        foreach (Icon icon in ownedIcons)
        {
            icon.Dispose();
        }
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs eventArgs)
    {
        window.SourceInitialized -= OnWindowSourceInitialized;
        nint handle = new WindowInteropHelper(window).Handle;
        windowSource = HwndSource.FromHwnd(handle) ??
            throw new InvalidOperationException(
                "The Control Center window source is unavailable.");
        windowSource.AddHook(OnWindowMessage);
        DesktopSummonHotKeyReceipt receipt = summonHotKey.Register(handle);
        window.SetDesktopSummonHotKeyReceipt(receipt);
    }

    private nint OnWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (DesktopSummonHotKey.IsSummonMessage(message, wParam))
        {
            handled = true;
            ShowWindow();
        }
        return nint.Zero;
    }

    private void OnDesktopHideRequested(object? sender, EventArgs eventArgs) =>
        HideWindow();

    private void OnRuntimePhaseChanged(object? sender, EventArgs eventArgs) =>
        UpdateTrayText();

    private void OnDesktopAttentionChanged(
        object? sender,
        EventArgs eventArgs)
    {
        DesktopAttentionSnapshot previous = attentionSnapshot;
        attentionSnapshot = window.DesktopAttention;
        UpdateTrayState();
        if (
            !window.IsVisible &&
            DesktopAttentionModel.ShouldSignal(previous, attentionSnapshot))
        {
            ShowAttentionSignal(attentionSnapshot.Kind);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        if (window.WindowState != WindowState.Minimized)
        {
            restoreState = window.WindowState;
        }
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs eventArgs) =>
        ShowWindow();

    private void OnBalloonTipClicked(object? sender, EventArgs eventArgs) =>
        ShowWindow();

    private void OnWindowClosed(object? sender, EventArgs eventArgs) =>
        Dispose();

    private string CreateTrayText()
    {
        const int maximumNotifyIconTextLength = 63;
        string text = UiText.Format(
            "Loc.Tray.Tooltip",
            GetTrayAttentionLabel(attentionSnapshot.Kind),
            DesktopSummonHotKey.Chord);
        return text.Length <= maximumNotifyIconTextLength
            ? text
            : text[..maximumNotifyIconTextLength];
    }

    private void UpdateTrayText()
    {
        if (trayIcon is null)
        {
            return;
        }
        try
        {
            trayIcon.Text = CreateTrayText();
        }
        catch (Exception exception) when (IsRecoverableIntegrationFailure(exception))
        {
            window.SetDesktopAttentionDeliveryUnavailable();
        }
    }

    private void UpdateTrayState()
    {
        if (trayIcon is null)
        {
            return;
        }
        try
        {
            trayIcon.Icon = GetPresenceIcon(
                ownedIcons,
                attentionSnapshot.Kind);
            trayIcon.Text = CreateTrayText();
        }
        catch (Exception exception) when (IsRecoverableIntegrationFailure(exception))
        {
            window.SetDesktopAttentionDeliveryUnavailable();
        }
    }

    private void ShowAttentionSignal(DesktopAttentionKind kind)
    {
        if (trayIcon is null)
        {
            return;
        }
        (string titleKey, string bodyKey, Forms.ToolTipIcon icon) =
            kind switch
            {
                DesktopAttentionKind.Completed =>
                    (
                        "Loc.Attention.Notification.CompletedTitle",
                        "Loc.Attention.Notification.CompletedBody",
                        Forms.ToolTipIcon.Info),
                DesktopAttentionKind.OwnerActionRequired =>
                    (
                        "Loc.Attention.Notification.OwnerTitle",
                        "Loc.Attention.Notification.OwnerBody",
                        Forms.ToolTipIcon.Warning),
                DesktopAttentionKind.Faulted =>
                    (
                        "Loc.Attention.Notification.FaultedTitle",
                        "Loc.Attention.Notification.FaultedBody",
                        Forms.ToolTipIcon.Error),
                _ => throw new InvalidOperationException(
                    "The attention state does not own a desktop signal."),
            };
        try
        {
            trayIcon.ShowBalloonTip(
                5000,
                UiText.Get(titleKey),
                UiText.Get(bodyKey),
                icon);
            window.SetDesktopAttentionDeliveryReceipt(kind);
        }
        catch (Exception exception) when (IsRecoverableIntegrationFailure(exception))
        {
            window.SetDesktopAttentionDeliveryUnavailable();
        }
    }

    private static Icon GetPresenceIcon(
        IReadOnlyList<Icon> icons,
        DesktopAttentionKind kind)
    {
        JarvisPresenceSignal signal = kind switch
        {
            DesktopAttentionKind.Working => JarvisPresenceSignal.Working,
            DesktopAttentionKind.OwnerActionRequired =>
                JarvisPresenceSignal.OwnerActionRequired,
            DesktopAttentionKind.Faulted => JarvisPresenceSignal.Faulted,
            _ => JarvisPresenceSignal.Ready,
        };
        return icons[(int)signal];
    }

    private static string GetTrayAttentionLabel(
        DesktopAttentionKind kind) =>
        UiText.Get(kind switch
        {
            DesktopAttentionKind.Working =>
                "Loc.Attention.Tray.Working",
            DesktopAttentionKind.Completed =>
                "Loc.Attention.Tray.Completed",
            DesktopAttentionKind.OwnerActionRequired =>
                "Loc.Attention.Tray.Owner",
            DesktopAttentionKind.Faulted =>
                "Loc.Attention.Tray.Faulted",
            _ => "Loc.Attention.Tray.Ready",
        });

    private static bool IsRecoverableIntegrationFailure(Exception exception) =>
        exception is not OutOfMemoryException and
        not AccessViolationException;

}
