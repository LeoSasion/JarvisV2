using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Jarvis.DesktopPresence;

namespace Jarvis.ControlCenter;

public partial class App : Application
{
    private readonly IReadOnlyList<string> launchArguments;
    private readonly ControlCenterSingleInstance? singleInstance;
    private int activationListenerStarted;

    public App()
        : this(Environment.GetCommandLineArgs().Skip(1).ToArray())
    {
    }

    internal App(IReadOnlyList<string> launchArguments)
        : this(launchArguments, singleInstance: null)
    {
    }

    internal App(
        IReadOnlyList<string> launchArguments,
        ControlCenterSingleInstance? singleInstance)
    {
        this.launchArguments = launchArguments;
        this.singleInstance = singleInstance;
    }

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        UiText.ApplyWindowsLanguage(this, CultureInfo.CurrentUICulture);
        base.OnStartup(eventArgs);
        IReadOnlyList<string> arguments = launchArguments;

        if (arguments.Count == 0)
        {
            MainWindow = new MainWindow(
                ConversationSurfaceViewModel.CreateIdle());
            MainWindow.Show();
            StartActivationListener();
            return;
        }

        if (TryParseCapture(
                arguments,
                out string? outputPath,
                out int captureWidth,
                out int captureHeight))
        {
            MainWindow preview = new MainWindow(
                ConversationSurfaceViewModel.CreatePreview())
            {
                Width = captureWidth,
                Height = captureHeight,
                ResizeMode = ResizeMode.NoResize,
            };
            MainWindow = preview;
            preview.Show();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () => CaptureAndClose(
                    preview,
                    outputPath,
                    captureWidth,
                    captureHeight));
            return;
        }

        if (TryParseSessionLauncherCapture(
                arguments,
                out outputPath,
                out string? previewWorkspace))
        {
            DateTimeOffset previewTime = new(
                2026,
                8,
                2,
                9,
                42,
                0,
                TimeSpan.Zero);
            DesktopRecentSessionEntry[] recentSessions =
            [
                new(
                    previewWorkspace,
                    ConversationProviderKind.OpenAiResponses,
                    previewTime),
                new(
                    Path.Combine(previewWorkspace, "archived-sandbox"),
                    ConversationProviderKind.LocalDiagnostic,
                    previewTime.AddDays(-1)),
            ];
            SessionLaunchWindow preview = new(
                previewWorkspace,
                recentSessions)
            {
                ShowInTaskbar = true,
            };
            MainWindow = preview;
            preview.Show();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () => CaptureAndClose(preview, outputPath, 760, 730));
            return;
        }

        if (TryParseResumeLatest(arguments, out bool startMinimized))
        {
            MainWindow window = new(
                ConversationSurfaceViewModel.CreateIdle())
            {
                ShowActivated = !startMinimized,
                WindowState = startMinimized
                    ? WindowState.Minimized
                    : WindowState.Normal,
            };
            MainWindow = window;
            window.Show();
            StartActivationListener();
            await window.ResumeLatestSessionAsync();
            return;
        }

        if (TryParseConversation(
                arguments,
                out ConversationLaunchOptions? options))
        {
            MainWindow window = new(
                ConversationSurfaceViewModel.Create(options));
            MainWindow = window;
            window.Show();
            StartActivationListener();
            await window.InitializeConversationAsync();
            return;
        }

        Shutdown(2);
    }

    internal static bool IsCaptureLaunch(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 &&
        (string.Equals(
             arguments[0],
             "--capture-preview",
             StringComparison.Ordinal) ||
         string.Equals(
             arguments[0],
             "--capture-session-launcher-preview",
             StringComparison.Ordinal));

    private void StartActivationListener()
    {
        if (
            singleInstance is null ||
            Interlocked.Exchange(ref activationListenerStarted, 1) != 0)
        {
            return;
        }
        singleInstance.StartListening(() =>
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(ActivateMainWindow));
        });
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not Window window)
        {
            return;
        }
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        if (!window.IsVisible)
        {
            window.Show();
        }
        _ = window.Activate();
        window.Focus();
    }

    private static bool TryParseCapture(
        IReadOnlyList<string> arguments,
        out string outputPath,
        out int width,
        out int height)
    {
        outputPath = string.Empty;
        width = 1440;
        height = 900;
        if ((arguments.Count != 2 && arguments.Count != 4) ||
            !string.Equals(
                arguments[0],
                "--capture-preview",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (arguments.Count == 4)
        {
            if (
                !string.Equals(
                    arguments[2],
                    "--size",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    arguments[3],
                    "compact",
                    StringComparison.Ordinal))
            {
                return false;
            }
            width = 1180;
            height = 760;
        }

        outputPath = Path.GetFullPath(arguments[1]);
        return string.Equals(
                Path.GetExtension(outputPath),
                ".png",
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.GetDirectoryName(outputPath));
    }

    private static bool TryParseSessionLauncherCapture(
        IReadOnlyList<string> arguments,
        out string outputPath,
        out string workspaceRoot)
    {
        outputPath = string.Empty;
        workspaceRoot = string.Empty;
        if (
            arguments.Count != 4 ||
            !string.Equals(
                arguments[0],
                "--capture-session-launcher-preview",
                StringComparison.Ordinal) ||
            !string.Equals(
                arguments[2],
                "--workspace",
                StringComparison.Ordinal))
        {
            return false;
        }
        outputPath = Path.GetFullPath(arguments[1]);
        workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(arguments[3]));
        return string.Equals(
                Path.GetExtension(outputPath),
                ".png",
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.GetDirectoryName(outputPath)) &&
            DesktopSessionLaunchAdmission.AdmitWorkspace(workspaceRoot)
                .Result == "passed";
    }

    private static bool TryParseConversation(
        IReadOnlyList<string> arguments,
        out ConversationLaunchOptions options)
    {
        options = null!;
        if (
            arguments.Count >= 3 &&
            string.Equals(
                arguments[0],
                "--conversation",
                StringComparison.Ordinal))
        {
            return TryParseBootstrappedConversation(arguments, out options);
        }
        if (arguments.Count != 7 ||
            !string.Equals(
                arguments[0],
                "--diagnostic-conversation",
                StringComparison.Ordinal))
        {
            return false;
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (index + 1 >= arguments.Count ||
                !name.StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(name, arguments[index + 1]))
            {
                return false;
            }
        }

        if (!values.TryGetValue("--node", out string? node) ||
            !values.TryGetValue("--sidecar", out string? sidecar) ||
            !values.TryGetValue("--workspace", out string? workspace) ||
            values.Count != 3)
        {
            return false;
        }

        options = new ConversationLaunchOptions(
            Path.GetFullPath(node),
            Path.GetFullPath(sidecar),
            Path.GetFullPath(workspace));
        return true;
    }

    private static bool TryParseResumeLatest(
        IReadOnlyList<string> arguments,
        out bool startMinimized)
    {
        startMinimized = false;
        if (
            arguments.Count is not 1 and not 2 ||
            !string.Equals(
                arguments[0],
                "--resume-latest",
                StringComparison.Ordinal))
        {
            return false;
        }
        if (arguments.Count == 2)
        {
            if (!string.Equals(
                    arguments[1],
                    "--minimized",
                    StringComparison.Ordinal))
            {
                return false;
            }
            startMinimized = true;
        }
        return true;
    }

    private static bool TryParseBootstrappedConversation(
        IReadOnlyList<string> arguments,
        out ConversationLaunchOptions options)
    {
        options = null!;
        if (
            arguments.Count is not 3 and not 5 ||
            arguments.Count % 2 == 0)
        {
            return false;
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < arguments.Count; index += 2)
        {
            if (
                index + 1 >= arguments.Count ||
                !values.TryAdd(arguments[index], arguments[index + 1]))
            {
                return false;
            }
        }
        if (
            !values.TryGetValue("--workspace", out string? workspace) ||
            values.Count is < 1 or > 2)
        {
            return false;
        }
        ConversationProviderKind provider =
            ConversationProviderKind.LocalDiagnostic;
        if (values.TryGetValue("--provider", out string? providerName))
        {
            provider = providerName switch
            {
                "local" => ConversationProviderKind.LocalDiagnostic,
                "openai" => ConversationProviderKind.OpenAiResponses,
                _ => (ConversationProviderKind)(-1),
            };
            if (!Enum.IsDefined(provider))
            {
                return false;
            }
        }
        DesktopSessionLaunchAdmissionReceipt admission =
            DesktopSessionLaunchAdmission.Admit(workspace, provider);
        if (admission.Result != "passed" || admission.Options is null)
        {
            return false;
        }
        options = admission.Options;
        return true;
    }

    private void CaptureAndClose(
        Window preview,
        string outputPath,
        int width,
        int height)
    {
        try
        {
            preview.UpdateLayout();
            RenderTargetBitmap bitmap = new(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(preview);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream output = new(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            encoder.Save(output);
            preview.Close();
            Shutdown(0);
        }
        catch
        {
            preview.Close();
            Shutdown(3);
        }
    }
}
