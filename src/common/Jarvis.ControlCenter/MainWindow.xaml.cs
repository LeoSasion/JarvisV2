using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.ControlCenter;

public partial class MainWindow : Window
{
    public const string DesignContractSeed = "32fb29e4";

    private readonly DispatcherTimer clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };
    private readonly ConversationSurfaceViewModel conversation;
    private bool shutdownInProgress;
    private bool closeAuthorized;

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
        conversation.PropertyChanged += OnConversationPropertyChanged;
        clockTimer.Tick += (_, _) => UpdateClock();
        clockTimer.Start();
        UpdateClock();
        UpdateConversationChrome();
        Closing += OnWindowClosing;
    }

    public async Task InitializeConversationAsync(
        CancellationToken cancellationToken = default)
    {
        await conversation.InitializeAsync(cancellationToken);
        UpdateConversationChrome();
        PromptInput.Focus();
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

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        Close();
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
            SessionLaunchWindow launcher = new(ResolveInitialWorkspace())
            {
                Owner = this,
            };
            if (
                launcher.ShowDialog() != true ||
                launcher.Options is null)
            {
                return;
            }
            await conversation.LaunchAsync(launcher.Options);
            UpdateConversationChrome();
            if (conversation.Phase == ConversationRuntimePhase.Ready)
            {
                PromptInput.Focus();
            }
        }
        catch (Exception exception)
        {
            conversation.ReportUiError(
                $"Session launch failed closed: {exception.Message}");
        }
    }

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

    private static string? ResolveInitialWorkspace()
    {
        string currentDirectory = Environment.CurrentDirectory;
        string gitMarker = Path.Combine(currentDirectory, ".git");
        return Directory.Exists(gitMarker) || File.Exists(gitMarker)
            ? currentDirectory
            : null;
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
        if (closeAuthorized || !conversation.HasOwnedRuntime)
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
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        clockTimer.Stop();
        base.OnClosed(eventArgs);
    }
}
