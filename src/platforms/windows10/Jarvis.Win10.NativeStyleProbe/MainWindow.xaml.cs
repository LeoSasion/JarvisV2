using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jarvis.Win10.HostAdmission;

namespace Jarvis.Win10.NativeStyleProbe;

public partial class MainWindow : Window
{
    private readonly HostProbeReceipt hostReceipt;

    internal MainWindow(HostProbeReceipt hostReceipt)
    {
        this.hostReceipt = hostReceipt;
        InitializeComponent();
        PopulateHostEvidence();
        SourceInitialized += (_, _) =>
            ApplyPreset(NativeStylePreset.JarvisGraphite);
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
                    ? "CyanBrush"
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
            FindBrush(result.Passed ? "CyanBrush" : "AmberBrush");

        Color accent = GetAccentColor(preset);
        AccentRail.Background = new SolidColorBrush(accent);
        ClientSurface.Background =
            new SolidColorBrush(GetSurfaceColor(preset));
    }

    private Color GetAccentColor(NativeStylePreset preset)
    {
        if (preset == NativeStylePreset.NativeAccent &&
            hostReceipt.SystemVisuals is not null &&
            ColorConverter.ConvertFromString(
                hostReceipt.SystemVisuals.ColorizationColor) is Color color)
        {
            return color;
        }

        return preset == NativeStylePreset.SystemDefault
            ? Color.FromRgb(0x82, 0x95, 0x9D)
            : Color.FromRgb(0x52, 0xD9, 0xCF);
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
                "Keeps the reviewed dark caption while reflecting the host DWM colorization color in this client surface.",
            _ => "Unknown Win10 style preset.",
        };

    private SolidColorBrush FindBrush(string key) =>
        (SolidColorBrush)FindResource(key);

    private static string ToStatus(bool value) =>
        value ? "ON" : "OFF";
}
