using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.VisualEffects;
using Jarvis.Win10.HostAdmission;
using Jarvis.Win10.RgbThemeModel;

namespace Jarvis.Win10.NativeStyleProbe;

public partial class MainWindow : Window
{
    private readonly HostProbeReceipt hostReceipt;
    private readonly DispatcherTimer rgbTimer;
    private readonly SolidColorBrush accentBrush;
    private readonly DateTimeOffset rgbStartedAt =
        DateTimeOffset.UtcNow;
    private string rgbEffectId = "signal-pulse";

    internal MainWindow(HostProbeReceipt hostReceipt)
    {
        this.hostReceipt = hostReceipt;
        InitializeComponent();
        accentBrush =
            (SolidColorBrush)Resources["AccentBrush"];
        rgbTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            (_, _) => RenderRgbFrame(),
            Dispatcher);
        PopulateHostEvidence();
        SourceInitialized += (_, _) =>
            ApplyPreset(NativeStylePreset.JarvisGraphite);
        Loaded += (_, _) =>
        {
            RenderRgbFrame();
            rgbTimer.Start();
        };
        Closed += (_, _) => rgbTimer.Stop();
    }

    private void PresetButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string presetName } ||
            !Enum.TryParse(presetName, out NativeStylePreset preset))
        {
            return;
        }

        ApplyPreset(preset);
    }

    private void RgbPresetButton_OnClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string hueText } &&
            double.TryParse(
                hueText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double hue))
        {
            RgbHueSlider.Value = hue;
        }
    }

    private void RgbHueSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (IsInitialized)
        {
            RenderRgbFrame();
        }
    }

    private void RgbEffectSelector_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (RgbEffectSelector.SelectedItem is
            ComboBoxItem { Tag: string effectId })
        {
            rgbEffectId = effectId;
            if (IsInitialized)
            {
                RenderRgbFrame();
            }
        }
    }

    private void PopulateHostEvidence()
    {
        WindowsHostIdentity host =
            hostReceipt.Host ??
            throw new InvalidOperationException(
                "The admitted host receipt does not contain host evidence.");
        SystemVisualIdentity visuals =
            hostReceipt.SystemVisuals ??
            throw new InvalidOperationException(
                "The admitted host receipt does not contain DWM evidence.");

        ProfileName.Text = hostReceipt.MatchedProfileId;
        HostSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{host.ProductName} {host.DisplayVersion} · {host.Build}.{host.Ubr} · {host.Architecture}");
        CompositionStatus.Text =
            visuals.CompositionEnabled ? "ENABLED" : "UNAVAILABLE";
        CompositionStatus.Foreground =
            FindBrush(
                visuals.CompositionEnabled
                    ? "AccentBrush"
                    : "AmberBrush");
        HighContrastStatus.Text =
            $"HIGH CONTRAST = {ToStatus(visuals.HighContrast)}";
        AnimationStatus.Text =
            $"CLIENT ANIMATION = {ToStatus(visuals.ClientAreaAnimation)}";
        ColorizationValue.Text = visuals.ColorizationColor;
        OpaqueBlendStatus.Text =
            $"OPAQUE BLEND = {ToStatus(visuals.ColorizationOpaqueBlend)}";

        if (ColorConverter.ConvertFromString(
                visuals.ColorizationColor) is Color color)
        {
            ColorizationSwatch.Background = new SolidColorBrush(color);
        }
    }

    private void ApplyPreset(NativeStylePreset preset)
    {
        OwnedWindowStyleResult result =
            OwnedWindowStyler.Apply(this, preset);
        DwmStyleCall call = result.Calls.Single();

        PresetName.Text = GetDisplayName(preset);
        PresetDescription.Text = GetDescription(preset);
        CallStatus.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{call.Name} · 0x{call.HResult:X8}");
        CallStatus.Foreground =
            FindBrush(result.Passed ? "AccentBrush" : "AmberBrush");

        ClientSurface.Background =
            new SolidColorBrush(GetSurfaceColor(preset));
    }

    private void RenderRgbFrame()
    {
        double seconds =
            (DateTimeOffset.UtcNow - rgbStartedAt).TotalSeconds;
        double cyclesPerSecond = rgbEffectId switch
        {
            "breathe" => 12.0 / 60.0,
            "spectrum" => 4.0 / 60.0,
            "signal-pulse" => 30.0 / 60.0,
            _ => 0.0,
        };
        double phase =
            cyclesPerSecond == 0.0
                ? 0.0
                : seconds * cyclesPerSecond;
        RgbFrame frame =
            RgbEffectEngine.Sample(
                RgbHueSlider.Value,
                1.0,
                1.0,
                rgbEffectId,
                phase);
        ApplyRgbFrame(frame);
    }

    internal void ApplyRgbFrame(RgbFrame frame)
    {
        Color accent =
            Color.FromRgb(
                frame.Red,
                frame.Green,
                frame.Blue);
        accentBrush.Color = accent;
        RgbHueValue.Text =
            $"{frame.HueDegrees.ToString("F1", CultureInfo.InvariantCulture)}°";
        RgbFrameStatus.Text =
            $"{frame.Hex} / {frame.EffectId.ToUpperInvariant()} / CLIENT ONLY";
    }

    private static Color GetSurfaceColor(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault =>
                Color.FromRgb(0x0D, 0x12, 0x15),
            NativeStylePreset.NativeAccent =>
                Color.FromRgb(0x08, 0x0E, 0x12),
            _ => Color.FromRgb(0x07, 0x0B, 0x0E),
        };

    private static string GetDisplayName(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault => "SYSTEM CAPTION",
            NativeStylePreset.JarvisGraphite => "JARVIS GRAPHITE",
            NativeStylePreset.NativeAccent => "NATIVE ACCENT",
            _ => preset.ToString().ToUpperInvariant(),
        };

    private static string GetDescription(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault =>
                "Returns the Win10 caption to its system-controlled light-mode preference.",
            NativeStylePreset.JarvisGraphite =>
                "Shared Jarvis visual intent translated to the one reviewed Win10 dark-caption attribute.",
            NativeStylePreset.NativeAccent =>
                "Keeps the reviewed dark caption while the host DWM accent remains read-only evidence beside the independent RGB frame.",
            _ => "Unknown Win10 style preset.",
        };

    private SolidColorBrush FindBrush(string key) =>
        (SolidColorBrush)FindResource(key);

    private static string ToStatus(bool value) =>
        value ? "ON" : "OFF";
}
