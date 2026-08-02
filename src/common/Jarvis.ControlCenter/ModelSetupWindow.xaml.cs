using System.IO;
using System.Windows;
using System.Windows.Input;
using Jarvis.PiAgentHost;

namespace Jarvis.ControlCenter;

public partial class ModelSetupWindow : Window
{
    private readonly OpenAiApiKeyCredentialStore? credentialStore;
    private bool saving;

    public ModelSetupWindow(
        OpenAiApiKeyCredentialStore credentialStore,
        bool credentialConfigured,
        bool replacementRequired = false)
    {
        this.credentialStore = credentialStore ??
            throw new ArgumentNullException(nameof(credentialStore));
        InitializeComponent();
        CredentialState.Text = replacementRequired
            ? UiText.Get("Loc.Setup.Unreadable")
            : credentialConfigured
                ? UiText.Get("Loc.Setup.Protected")
                : UiText.Get("Loc.Setup.NotConfigured");
        Loaded += (_, _) => ApiKeyInput.Focus();
    }

    private async void SaveButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (saving || credentialStore is null)
        {
            return;
        }
        string apiKey = ApiKeyInput.Password;
        try
        {
            OpenAiApiKeyCredentialStore.ValidateApiKey(apiKey);
        }
        catch (ArgumentException)
        {
            ErrorMessage.Text = UiText.Get("Loc.Setup.ValidationError");
            ApiKeyInput.Focus();
            return;
        }

        saving = true;
        SaveButton.IsEnabled = false;
        ErrorMessage.Text = string.Empty;
        try
        {
            await credentialStore.SaveAsync(apiKey);
            ApiKeyInput.Clear();
            DialogResult = true;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                InvalidDataException or
                UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException)
        {
            ErrorMessage.Text = UiText.Format(
                "Loc.Setup.SaveError",
                exception.Message);
        }
        finally
        {
            saving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!saving)
        {
            Close();
        }
    }

    private void Window_OnPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && !saving)
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
