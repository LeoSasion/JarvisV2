using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Jarvis.ControlCenter;

public partial class SessionLaunchWindow : Window
{
    private bool admitting;

    public SessionLaunchWindow(string? initialWorkspace = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialWorkspace))
        {
            WorkspaceInput.Text = initialWorkspace;
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
        if (admitting)
        {
            return;
        }
        admitting = true;
        StartButton.IsEnabled = false;
        SetAdmissionState(
            "VERIFYING RUNTIME",
            "Checking workspace, packaged hashes and the desktop-owned Pi sidecar.",
            "CyanBrush");
        try
        {
            ConversationProviderKind provider =
                OpenAiProviderOption.IsChecked == true
                    ? ConversationProviderKind.OpenAiResponses
                    : ConversationProviderKind.LocalDiagnostic;
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
            StartButton.IsEnabled =
                DesktopSessionLaunchAdmission.AdmitWorkspace(
                    WorkspaceInput.Text).Result == "passed";
        }
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
