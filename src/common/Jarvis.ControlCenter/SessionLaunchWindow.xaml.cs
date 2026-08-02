using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Jarvis.ControlCenter;

public sealed record RecentSessionLaunchItem(
    string WorkspaceRoot,
    string WorkspaceName,
    ConversationProviderKind Provider,
    string ActionLabel,
    string MetadataLabel,
    string AutomationName,
    bool CanResume);

public partial class SessionLaunchWindow : Window
{
    private bool admitting;

    public SessionLaunchWindow(
        string? initialWorkspace = null,
        IReadOnlyList<DesktopRecentSessionEntry>? recentSessions = null)
    {
        InitializeComponent();
        IReadOnlyList<RecentSessionLaunchItem> recentItems =
            CreateRecentItems(recentSessions ?? []);
        RecentSessionsList.ItemsSource = recentItems;
        RecentSessionEmpty.Visibility = recentItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(initialWorkspace))
        {
            WorkspaceInput.Text = initialWorkspace;
            RecentSessionLaunchItem? matching = recentItems.FirstOrDefault(
                item => string.Equals(
                    item.WorkspaceRoot,
                    initialWorkspace,
                    StringComparison.OrdinalIgnoreCase));
            if (matching is not null)
            {
                SelectProvider(matching.Provider);
            }
        }
        Loaded += (_, _) =>
        {
            WorkspaceInput.Focus();
            WorkspaceInput.SelectAll();
        };
    }

    public ConversationLaunchOptions? Options { get; private set; }

    private void BrowseButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose the single workspace Jarvis may read",
            Multiselect = false,
        };
        string candidate = WorkspaceInput.Text.Trim();
        if (Directory.Exists(candidate))
        {
            dialog.InitialDirectory = Path.GetFullPath(candidate);
        }
        if (dialog.ShowDialog(this) == true)
        {
            WorkspaceInput.Text = dialog.FolderName;
            WorkspaceInput.CaretIndex = WorkspaceInput.Text.Length;
        }
    }

    private void WorkspaceInput_OnTextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs eventArgs)
    {
        if (!IsInitialized)
        {
            return;
        }
        Options = null;
        string value = WorkspaceInput.Text;
        if (string.IsNullOrWhiteSpace(value))
        {
            StartButton.IsEnabled = false;
            SetAdmissionState(
                "AWAITING WORKSPACE",
                "Browse to an existing project directory outside protected Windows locations.",
                "TextFaintBrush");
            return;
        }
        DesktopWorkspaceAdmissionReceipt workspace =
            DesktopSessionLaunchAdmission.AdmitWorkspace(value);
        StartButton.IsEnabled =
            !admitting && workspace.Result == "passed";
        SetAdmissionState(
            workspace.Result == "passed"
                ? "READY TO VERIFY RUNTIME"
                : "WORKSPACE NOT ADMITTED",
            workspace.Result == "passed"
                ? "The local path boundary passed. Start will verify the packaged Pi runtime."
                : workspace.Failure ?? "Choose another workspace.",
            workspace.Result == "passed" ? "CyanBrush" : "RedBrush");
    }

    private void StartButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        ConversationProviderKind provider =
            OpenAiProviderOption.IsChecked == true
                ? ConversationProviderKind.OpenAiResponses
                : ConversationProviderKind.LocalDiagnostic;
        AdmitAndClose(provider, resume: false);
    }

    private void RecentSessionButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (
            admitting ||
            sender is not System.Windows.Controls.Button
            {
                Tag: RecentSessionLaunchItem item,
            } ||
            !item.CanResume)
        {
            return;
        }
        WorkspaceInput.Text = item.WorkspaceRoot;
        WorkspaceInput.CaretIndex = WorkspaceInput.Text.Length;
        SelectProvider(item.Provider);
        AdmitAndClose(item.Provider, resume: true);
    }

    private void AdmitAndClose(
        ConversationProviderKind provider,
        bool resume)
    {
        if (admitting)
        {
            return;
        }
        admitting = true;
        StartButton.IsEnabled = false;
        RecentSessionsList.IsEnabled = false;
        SetAdmissionState(
            resume ? "VERIFYING RECENT WORK" : "VERIFYING RUNTIME",
            resume
                ? "Rechecking the workspace and packaged runtime before restoring its encrypted checkpoint."
                : "Checking workspace, packaged hashes and the desktop-owned Pi sidecar.",
            "CyanBrush");
        try
        {
            DesktopSessionLaunchAdmissionReceipt admission =
                DesktopSessionLaunchAdmission.Admit(
                    WorkspaceInput.Text,
                    provider);
            if (admission.Result != "passed" || admission.Options is null)
            {
                SetAdmissionState(
                    "SESSION NOT ADMITTED",
                    admission.Failures.FirstOrDefault() ??
                        "Choose another workspace or repair the portable runtime.",
                    "RedBrush");
                WorkspaceInput.Focus();
                return;
            }
            Options = admission.Options;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            Options = null;
            SetAdmissionState(
                "SESSION NOT ADMITTED",
                $"Runtime verification failed closed: {exception.Message}",
                "RedBrush");
            WorkspaceInput.Focus();
        }
        finally
        {
            admitting = false;
            RecentSessionsList.IsEnabled = true;
            StartButton.IsEnabled =
                DesktopSessionLaunchAdmission.AdmitWorkspace(
                    WorkspaceInput.Text).Result == "passed";
        }
    }

    private static IReadOnlyList<RecentSessionLaunchItem> CreateRecentItems(
        IReadOnlyList<DesktopRecentSessionEntry> recentSessions)
    {
        List<RecentSessionLaunchItem> items = [];
        foreach (DesktopRecentSessionEntry entry in recentSessions.Take(3))
        {
            DesktopWorkspaceAdmissionReceipt admission =
                DesktopSessionLaunchAdmission.AdmitWorkspace(
                    entry.WorkspaceRoot);
            bool canResume = admission.Result == "passed";
            string workspaceName = Path.GetFileName(entry.WorkspaceRoot);
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                workspaceName = entry.WorkspaceRoot;
            }
            string provider = entry.Provider ==
                    ConversationProviderKind.OpenAiResponses
                ? "OPENAI RESPONSES"
                : "LOCAL DIAGNOSTIC";
            string opened = entry.LastOpenedAtUtc
                .ToLocalTime()
                .ToString("yyyy.MM.dd HH:mm");
            items.Add(new RecentSessionLaunchItem(
                entry.WorkspaceRoot,
                workspaceName,
                entry.Provider,
                canResume ? "VERIFY & RESUME" : "UNAVAILABLE",
                $"{provider} // {opened}",
                canResume
                    ? $"Verify and resume recent workspace {workspaceName} at {entry.WorkspaceRoot} with {provider}"
                    : $"Recent workspace {workspaceName} at {entry.WorkspaceRoot} is unavailable",
                canResume));
        }
        return items;
    }

    private void SelectProvider(ConversationProviderKind provider)
    {
        OpenAiProviderOption.IsChecked =
            provider == ConversationProviderKind.OpenAiResponses;
        LocalProviderOption.IsChecked =
            provider != ConversationProviderKind.OpenAiResponses;
    }

    private void SetAdmissionState(
        string state,
        string detail,
        string brushResource)
    {
        AdmissionState.Text = state;
        AdmissionDetail.Text = detail;
        Brush brush = (Brush)FindResource(brushResource);
        AdmissionState.Foreground = brush;
        AdmissionDot.Fill = brush;
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!admitting)
        {
            Close();
        }
    }

    private void Window_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && !admitting)
        {
            eventArgs.Handled = true;
            Close();
        }
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
}
