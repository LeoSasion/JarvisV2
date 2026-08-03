using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.DesktopPresence;

namespace Jarvis.ControlCenter;

public partial class MainWindow : Window
{
    public const string DesignContractSeed = "32fb29e4";

    private readonly DispatcherTimer clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };
    private readonly ConversationSurfaceViewModel conversation;
    private readonly DesktopStartupRegistration startupRegistration = new();
    private DesktopRecentSessionStore? recentSessionStore;
    private DesktopStartupRegistrationReceipt? startupRegistrationReceipt;
    private DesktopRecentSessionEntry? latestRecentSession;
    private IInputElement? focusBeforeImmersive;
    private WindowState windowStateBeforeImmersive;
    private GridLength headerHeightBeforeImmersive;
    private GridLength statusDockHeightBeforeImmersive;
    private GridLength workspaceRailWidthBeforeImmersive;
    private GridLength runtimeInspectorWidthBeforeImmersive;
    private Thickness conversationMarginBeforeImmersive;
    private bool immersiveMode;
    private bool shutdownInProgress;
    private bool exitRequested;
    private bool closeAuthorized;
    private bool desktopPresenceBusy;
    private bool desktopTrayAvailable = true;

    public event EventHandler? DesktopHideRequested;

    public event EventHandler? RuntimePhaseChanged;

    public string DesktopRuntimePhaseLabel => conversation.PhaseLabel;

    public MainWindow()
        : this(ConversationSurfaceViewModel.CreateIdle())
    {
    }

    public MainWindow(ConversationSurfaceViewModel conversation)
    {
        this.conversation = conversation ??
            throw new ArgumentNullException(nameof(conversation));
        InitializeComponent();
        DataContext = conversation;
        HandoffConstellationVfx.Attach(
            this,
            UserStage,
            PiStage,
            ToolStage,
            JarvisStage);
        conversation.PropertyChanged += OnConversationPropertyChanged;
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        UpdateClock();
        UpdateConversationChrome();
        if (conversation.Phase == ConversationRuntimePhase.Preview)
        {
            SummonHotKeyStatus.Text = UiText.Get(
                "Loc.Presence.HotKeyPreview");
        }
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    public async Task InitializeConversationAsync(
        CancellationToken cancellationToken = default)
    {
        await conversation.InitializeAsync(cancellationToken);
        UpdateConversationChrome();
        PromptInput.Focus();
    }

    public async Task<bool> ResumeLatestSessionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!conversation.CanLaunchSession || desktopPresenceBusy)
        {
            return false;
        }
        desktopPresenceBusy = true;
        UpdateDesktopPresenceControls();
        try
        {
            DesktopRecentSessionStore store = recentSessionStore ??=
                new DesktopRecentSessionStore();
            DesktopRecentSessionCatalog catalog =
                await store.LoadAsync(cancellationToken);
            DesktopRecentSessionEntry? entry = FindLatestAvailable(catalog);
            latestRecentSession = entry;
            if (entry is null)
            {
                conversation.ReportUiError(
                    UiText.Get("Loc.Presence.NoRecent"));
                return false;
            }

            DesktopSessionLaunchAdmissionReceipt admission =
                DesktopSessionLaunchAdmission.Admit(
                    entry.WorkspaceRoot,
                    entry.Provider);
            if (admission.Result != "passed" || admission.Options is null)
            {
                conversation.ReportUiError(
                    UiText.Get("Loc.Presence.ResumeFailed") + " " +
                    string.Join(" ", admission.Failures));
                return false;
            }
            await LaunchAdmittedSessionAsync(
                admission.Options,
                store,
                cancellationToken);
            return conversation.Phase == ConversationRuntimePhase.Ready;
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                InvalidOperationException or
                NotSupportedException or
                System.Security.Cryptography.CryptographicException or
                UnauthorizedAccessException)
        {
            conversation.ReportUiError(
                UiText.Get("Loc.Presence.ResumeFailed") + " " +
                exception.Message);
            return false;
        }
        finally
        {
            desktopPresenceBusy = false;
            UpdateDesktopPresenceControls();
        }
    }

    public void RequestApplicationExit()
    {
        if (exitRequested)
        {
            return;
        }
        exitRequested = true;
        Close();
    }

    public void SetDesktopSummonHotKeyReceipt(
        DesktopSummonHotKeyReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        SummonHotKeyStatus.Text = receipt.Registered
            ? UiText.Format("Loc.Presence.HotKeyReady", receipt.Chord)
            : UiText.Format("Loc.Presence.HotKeyUnavailable", receipt.Chord);
        SummonHotKeyStatus.Foreground = receipt.Registered
            ? (Brush)FindResource("CyanBrush")
            : (Brush)FindResource("AmberBrush");
        string helpText = receipt.Registered
            ? UiText.Get("Loc.Presence.HotKeyReadyTooltip")
            : UiText.Get(desktopTrayAvailable
                ? "Loc.Presence.HotKeyUnavailableTooltip"
                : "Loc.Presence.HotKeyUnavailableNoTrayTooltip");
        SummonHotKeyStatus.ToolTip = helpText;
        AutomationProperties.SetHelpText(
            SummonHotKeyStatus,
            helpText);
    }

    public void ReportDesktopPresenceUnavailable()
    {
        desktopTrayAvailable = false;
        AutomationProperties.SetName(
            CloseWindowButton,
            UiText.Get("Loc.Window.CloseTaskbarAutomation"));
        CloseWindowButton.ToolTip = UiText.Get(
            "Loc.Window.CloseTaskbarTooltip");
        SummonHotKeyDescription.Text = UiText.Get(
            "Loc.Presence.DegradedDescription");
        conversation.ReportUiError(
            UiText.Get("Loc.Presence.TrayUnavailable"));
    }

    public void FocusConversationInput()
    {
        if (PromptInput.IsVisible && PromptInput.IsEnabled)
        {
            _ = PromptInput.Focus();
            return;
        }
        _ = Focus();
    }

    private async void OnWindowLoaded(
        object sender,
        RoutedEventArgs eventArgs)
    {
        Loaded -= OnWindowLoaded;
        await RefreshDesktopPresenceAsync();
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

    private void MinimizeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void ImmersiveModeButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ToggleImmersiveMode();
    }

    private void ImmersiveExitButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ExitImmersiveMode();
    }

    private void MainWindow_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F11)
        {
            eventArgs.Handled = true;
            if (!eventArgs.IsRepeat)
            {
                ToggleImmersiveMode();
            }
            return;
        }
        if (eventArgs.Key == Key.Escape && eventArgs.IsRepeat)
        {
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key == Key.Escape && immersiveMode)
        {
            eventArgs.Handled = true;
            ExitImmersiveMode();
        }
    }

    private void ToggleImmersiveMode()
    {
        if (immersiveMode)
        {
            ExitImmersiveMode();
            return;
        }
        EnterImmersiveMode();
    }

    private void EnterImmersiveMode()
    {
        focusBeforeImmersive = Keyboard.FocusedElement;
        windowStateBeforeImmersive = WindowState;
        headerHeightBeforeImmersive = HeaderRow.Height;
        statusDockHeightBeforeImmersive = StatusDockRow.Height;
        workspaceRailWidthBeforeImmersive = WorkspaceRailColumn.Width;
        runtimeInspectorWidthBeforeImmersive = RuntimeInspectorColumn.Width;
        conversationMarginBeforeImmersive = ConversationWorkspace.Margin;

        immersiveMode = true;
        HeaderChrome.Visibility = Visibility.Collapsed;
        WorkspaceRail.Visibility = Visibility.Collapsed;
        RuntimeInspector.Visibility = Visibility.Collapsed;
        StatusDock.Visibility = Visibility.Collapsed;
        HeaderRow.Height = new GridLength(0);
        StatusDockRow.Height = new GridLength(0);
        WorkspaceRailColumn.Width = new GridLength(0);
        RuntimeInspectorColumn.Width = new GridLength(0);
        ConversationWorkspace.Margin = new Thickness(18, 14, 18, 16);
        ConversationShortcuts.Visibility = Visibility.Collapsed;
        ImmersiveExitButton.Visibility = Visibility.Visible;
        WindowState = WindowState.Maximized;

        if (!ConversationWorkspace.IsKeyboardFocusWithin)
        {
            ImmersiveExitButton.Focus();
        }
    }

    private void ExitImmersiveMode()
    {
        if (!immersiveMode)
        {
            return;
        }

        immersiveMode = false;
        HeaderChrome.Visibility = Visibility.Visible;
        WorkspaceRail.Visibility = Visibility.Visible;
        RuntimeInspector.Visibility = Visibility.Visible;
        StatusDock.Visibility = Visibility.Visible;
        HeaderRow.Height = headerHeightBeforeImmersive;
        StatusDockRow.Height = statusDockHeightBeforeImmersive;
        WorkspaceRailColumn.Width = workspaceRailWidthBeforeImmersive;
        RuntimeInspectorColumn.Width = runtimeInspectorWidthBeforeImmersive;
        ConversationWorkspace.Margin = conversationMarginBeforeImmersive;
        ConversationShortcuts.Visibility = Visibility.Visible;
        ImmersiveExitButton.Visibility = Visibility.Collapsed;
        WindowState = windowStateBeforeImmersive;

        IInputElement? elementToRestore = focusBeforeImmersive;
        focusBeforeImmersive = null;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (elementToRestore?.Focus() != true)
                {
                    ImmersiveModeButton.Focus();
                }
            }));
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void ExitJarvisButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        RequestApplicationExit();
    }

    private async void SubmitButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await SubmitPromptAsync();
    }

    private async void CancelButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await conversation.CancelAsync();
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Cancellation failed closed: {exception.Message}");
        }
    }

    private async void ApproveWorkspaceEditButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string proposalId })
        {
            conversation.ReportUiError(
                "Workspace edit approval failed closed: proposal identity was missing.");
            return;
        }
        try
        {
            await conversation.ApplyWorkspaceEditAsync(proposalId);
            if (conversation.CanSubmit)
            {
                PromptInput.Focus();
            }
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Workspace edit approval failed closed: {exception.Message}");
        }
    }

    private async void RejectWorkspaceEditButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string proposalId })
        {
            conversation.ReportUiError(
                "Workspace edit rejection failed closed: proposal identity was missing.");
            return;
        }
        try
        {
            await conversation.RejectWorkspaceEditAsync(proposalId);
            if (conversation.CanSubmit)
            {
                PromptInput.Focus();
            }
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Workspace edit rejection failed closed: {exception.Message}");
        }
    }

    private async void ModelSetupButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            bool configured = false;
            bool replacementRequired = false;
            try
            {
                configured =
                    await conversation.CredentialStore.GetApiKeyAsync() is not null;
            }
            catch (Exception exception)
                when (exception is
                    System.IO.InvalidDataException or
                    System.Security.Cryptography.CryptographicException)
            {
                replacementRequired = true;
            }
            ModelSetupWindow setup = new(
                conversation.CredentialStore,
                configured,
                replacementRequired)
            {
                Owner = this,
            };
            if (setup.ShowDialog() == true)
            {
                await conversation.RefreshCredentialAsync();
            }
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Model setup failed closed: {exception.Message}");
        }
    }

    private async void SessionLaunchButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!conversation.CanLaunchSession)
        {
            return;
        }
        try
        {
            DesktopRecentSessionCatalog recentSessions;
            DesktopRecentSessionStore? recentStore = null;
            try
            {
                recentStore = recentSessionStore ??=
                    new DesktopRecentSessionStore();
                recentSessions = await recentStore.LoadAsync();
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    NotSupportedException or
                    System.Security.Cryptography.CryptographicException or
                    UnauthorizedAccessException)
            {
                recentSessions = DesktopRecentSessionStore.EmptyCatalog();
                conversation.ReportUiError(
                    "Recent work could not be opened; a new session is still available: " +
                    exception.Message);
            }
            SessionLaunchWindow launcher = new(
                ResolveInitialWorkspace(recentSessions),
                recentSessions.Entries)
            {
                Owner = this,
            };
            if (
                launcher.ShowDialog() != true ||
                launcher.Options is null)
            {
                return;
            }
            ConversationLaunchOptions options = launcher.Options;
            await LaunchAdmittedSessionAsync(options, recentStore);
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Session launch failed closed: {exception.Message}");
        }
    }

    private async void ResumeLatestButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await ResumeLatestSessionAsync();
    }

    private void StartupRegistrationButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (desktopPresenceBusy)
        {
            return;
        }
        desktopPresenceBusy = true;
        UpdateDesktopPresenceControls();
        try
        {
            string executablePath = Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The current desktop executable path is unavailable.");
            DesktopStartupRegistrationReceipt current =
                startupRegistrationReceipt ??
                startupRegistration.Inspect(executablePath);
            startupRegistrationReceipt = startupRegistration.SetEnabled(
                executablePath,
                enabled: !current.Enabled);
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                InvalidOperationException or
                NotSupportedException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            conversation.ReportUiError(
                UiText.Get("Loc.Presence.RegistrationFailed") + " " +
                exception.Message);
        }
        finally
        {
            desktopPresenceBusy = false;
            UpdateDesktopPresenceControls();
        }
    }

    private async Task LaunchAdmittedSessionAsync(
        ConversationLaunchOptions options,
        DesktopRecentSessionStore? recentStore,
        CancellationToken cancellationToken = default)
    {
        await conversation.LaunchAsync(options, cancellationToken);
        UpdateConversationChrome();
        if (
            conversation.Phase == ConversationRuntimePhase.Ready &&
            recentStore is not null)
        {
            try
            {
                await recentStore.RememberAsync(
                    options.WorkspaceRoot,
                    options.Provider,
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is
                    ArgumentException or
                    IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    NotSupportedException or
                    System.Security.Cryptography.CryptographicException or
                    UnauthorizedAccessException)
            {
                conversation.ReportUiError(
                    "Session is ready, but recent-work persistence failed closed: " +
                    exception.Message);
            }
        }
        if (conversation.Phase == ConversationRuntimePhase.Ready)
        {
            PromptInput.Focus();
        }
        await RefreshDesktopPresenceAsync(loadRecentSessions: false);
    }

    private async Task RefreshDesktopPresenceAsync(
        bool loadRecentSessions = true)
    {
        try
        {
            string executablePath = Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The current desktop executable path is unavailable.");
            startupRegistrationReceipt =
                startupRegistration.Inspect(executablePath);
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                InvalidOperationException or
                NotSupportedException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            startupRegistrationReceipt = null;
            conversation.ReportUiError(
                UiText.Get("Loc.Presence.InspectFailed") + " " +
                exception.Message);
        }

        if (loadRecentSessions)
        {
            try
            {
                DesktopRecentSessionStore store = recentSessionStore ??=
                    new DesktopRecentSessionStore();
                DesktopRecentSessionCatalog catalog = await store.LoadAsync();
                latestRecentSession = FindLatestAvailable(catalog);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    NotSupportedException or
                    System.Security.Cryptography.CryptographicException or
                    UnauthorizedAccessException)
            {
                latestRecentSession = null;
                conversation.ReportUiError(
                    UiText.Get("Loc.Presence.RecentInspectFailed") + " " +
                    exception.Message);
            }
        }
        UpdateDesktopPresenceControls();
    }

    private void UpdateDesktopPresenceControls()
    {
        bool canLaunchLatest =
            !desktopPresenceBusy &&
            latestRecentSession is not null &&
            conversation.CanLaunchSession;
        ResumeLatestButton.Visibility = latestRecentSession is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ResumeLatestButton.IsEnabled = canLaunchLatest;
        SessionLaunchButton.SetResourceReference(
            StyleProperty,
            latestRecentSession is null
                ? "PrimaryActionButton"
                : "ActionButton");

        DesktopStartupRegistrationReceipt? receipt =
            startupRegistrationReceipt;
        StartupPresenceStatus.Text = receipt?.State switch
        {
            DesktopStartupRegistrationState.EnabledForCurrentExecutable =>
                UiText.Get("Loc.Presence.Enabled"),
            DesktopStartupRegistrationState.EnabledForDifferentExecutable =>
                UiText.Get("Loc.Presence.EnabledElsewhere"),
            DesktopStartupRegistrationState.Disabled =>
                UiText.Get("Loc.Presence.Disabled"),
            _ => UiText.Get("Loc.Presence.Unknown"),
        };
        StartupPresenceStatus.Foreground = receipt?.Enabled == true
            ? (Brush)FindResource("CyanBrush")
            : (Brush)FindResource("TextMutedBrush");
        StartupRegistrationButton.Content = receipt?.Enabled == true
            ? UiText.Get("Loc.Presence.Disable")
            : UiText.Get("Loc.Presence.Enable");
        StartupRegistrationButton.IsEnabled =
            !desktopPresenceBusy &&
            receipt is not null &&
            conversation.Phase != ConversationRuntimePhase.Preview;
    }

    private static DesktopRecentSessionEntry? FindLatestAvailable(
        DesktopRecentSessionCatalog catalog) =>
        catalog.Entries.FirstOrDefault(entry =>
            DesktopSessionLaunchAdmission.AdmitWorkspace(entry.WorkspaceRoot)
                .Result == "passed");

    private async void StartReviewedIterationButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        string mission = PromptInput.Text.Trim();
        if (mission.Length == 0)
        {
            conversation.ReportUiError(
                "Type the reviewed iteration mission in the composer before starting the reviewed loop.");
            PromptInput.Focus();
            return;
        }
        try
        {
            await conversation.StartReviewedIterationAsync(mission);
            PromptInput.Clear();
            TranscriptScroll.ScrollToEnd();
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Reviewed iteration admission failed closed: {exception.Message}");
        }
    }

    private async void ResumeReviewedIterationButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await conversation.ResumeReviewedIterationAsync();
            TranscriptScroll.ScrollToEnd();
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Reviewed iteration re-arm failed closed: {exception.Message}");
        }
    }

    private async void RunTrustedValidationButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await conversation.RunTrustedValidationAsync();
            TranscriptScroll.ScrollToEnd();
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Trusted validation failed closed: {exception.Message}");
        }
    }

    private async void StopReviewedIterationButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await conversation.StopReviewedIterationAsync();
            if (conversation.CanSubmit)
            {
                PromptInput.Focus();
            }
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Reviewed iteration stop failed closed: {exception.Message}");
        }
    }

    private static string? ResolveInitialWorkspace(
        DesktopRecentSessionCatalog recentSessions)
    {
        string currentDirectory = Environment.CurrentDirectory;
        string gitMarker = Path.Combine(currentDirectory, ".git");
        if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
        {
            return currentDirectory;
        }
        return recentSessions.Entries
            .Select(entry => entry.WorkspaceRoot)
            .FirstOrDefault(workspace =>
                DesktopSessionLaunchAdmission.AdmitWorkspace(workspace)
                    .Result == "passed");
    }

    private async void PromptInput_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            eventArgs.Handled = true;
            await SubmitPromptAsync();
            return;
        }
        if (eventArgs.Key == Key.Escape && conversation.CanCancel)
        {
            eventArgs.Handled = true;
            try
            {
                await conversation.CancelAsync();
            }
            catch (Exception exception)
            {
                conversation.ReportUiError(
                    $"Cancellation failed closed: {exception.Message}");
            }
        }
    }

    private async Task SubmitPromptAsync()
    {
        string prompt = PromptInput.Text.Trim();
        if (prompt.Length == 0)
        {
            conversation.ReportUiError(
                "Enter a request before handing control to Pi.");
            PromptInput.Focus();
            return;
        }

        try
        {
            await conversation.SubmitAsync(prompt);
            PromptInput.Clear();
            TranscriptScroll.ScrollToEnd();
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Turn submission failed closed: {exception.Message}");
        }
    }

    private void OnConversationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        UpdateConversationChrome();
        if (
            eventArgs.PropertyName == nameof(conversation.CanLaunchSession) ||
            eventArgs.PropertyName == nameof(conversation.Phase))
        {
            UpdateDesktopPresenceControls();
        }
        if (eventArgs.PropertyName == nameof(conversation.Phase))
        {
            RuntimePhaseChanged?.Invoke(this, EventArgs.Empty);
        }
        if (eventArgs.PropertyName == nameof(conversation.Turns))
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                TranscriptScroll.ScrollToEnd);
        }
    }

    private void UpdateConversationChrome()
    {
        EmptyConversationMessage.Visibility = conversation.HasTurns
            ? Visibility.Collapsed
            : Visibility.Visible;
        OwnershipProgress.Value = conversation.HandoffProgress;

        double progress = conversation.HandoffProgress;
        if (conversation.HandoffComplete)
        {
            SetStageState(UserStage, active: true, complete: false);
            SetStageState(PiStage, active: false, complete: true);
            SetStageState(ToolStage, active: false, complete: true);
            SetStageState(JarvisStage, active: false, complete: true);
        }
        else
        {
            SetStageState(UserStage, progress <= 0.75, progress > 0.75);
            SetStageState(
                PiStage,
                progress is > 0.75 and < 1.75,
                progress >= 1.75);
            SetStageState(
                ToolStage,
                progress is >= 1.75 and < 2.75,
                progress >= 2.75);
            SetStageState(JarvisStage, progress >= 2.75, false);
        }

        RuntimeStatusDot.Fill = conversation.Phase switch
        {
            ConversationRuntimePhase.Ready =>
                (Brush)FindResource("CyanBrush"),
            ConversationRuntimePhase.Starting or
            ConversationRuntimePhase.Stopping =>
                (Brush)FindResource("AmberBrush"),
            ConversationRuntimePhase.Faulted =>
                (Brush)FindResource("RedBrush"),
            _ => (Brush)FindResource("TextFaintBrush"),
        };
        HandoffConstellationVfx.SetState(
            conversation.HandoffProgress,
            conversation.HandoffComplete,
            conversation.PendingWorkspaceEdit is not null,
            conversation.HasActiveTurn,
            conversation.Phase);
    }

    private void SetStageState(
        Border stage,
        bool active,
        bool complete)
    {
        stage.BorderBrush = active
            ? (Brush)FindResource("CyanBrush")
            : complete
                ? (Brush)FindResource("LineBrightBrush")
                : (Brush)FindResource("LineBrush");
        stage.Background = active
            ? (Brush)FindResource("CyanDimBrush")
            : (Brush)FindResource("PanelAltBrush");
        stage.Opacity = active || complete ? 1 : 0.72;
    }

    private async void OnWindowClosing(
        object? sender,
        CancelEventArgs eventArgs)
    {
        if (closeAuthorized)
        {
            return;
        }
        if (!exitRequested)
        {
            eventArgs.Cancel = true;
            DesktopHideRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (!conversation.HasOwnedRuntime)
        {
            return;
        }
        eventArgs.Cancel = true;
        if (shutdownInProgress)
        {
            return;
        }

        shutdownInProgress = true;
        PromptInput.IsEnabled = false;
        try
        {
            using CancellationTokenSource timeout = new(
                TimeSpan.FromSeconds(12));
            await conversation.ShutdownAsync(timeout.Token);
        }
        finally
        {
            closeAuthorized = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        Closing -= OnWindowClosing;
        Loaded -= OnWindowLoaded;
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        HandoffConstellationVfx.Detach();
        clockTimer.Stop();
        base.OnClosed(eventArgs);
    }
}
