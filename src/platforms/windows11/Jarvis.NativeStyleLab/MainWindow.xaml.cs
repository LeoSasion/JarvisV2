using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Jarvis.NativeStyleLab;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
            ApplyPreset(NativeStylePreset.GraphiteMica);
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

    private void ApplyPreset(NativeStylePreset preset)
    {
        DwmStyleResult result = DwmWindowStyler.Apply(this, preset);
        PresetName.Text = GetDisplayName(preset);
        PresetDescription.Text = GetDescription(preset);

        int passedCount = result.HResults.Values.Count(value => value >= 0);
        ReceiptSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{passedCount} / {result.HResults.Count} S_OK");
        ReceiptDetails.Text = string.Join(
            Environment.NewLine,
            result.HResults.Select(pair =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{pair.Key,-34} 0x{pair.Value:X8}")));
    }

    private static string GetDisplayName(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault => "SYSTEM DEFAULT",
            NativeStylePreset.GraphiteMica => "GRAPHITE MICA",
            NativeStylePreset.NightAcrylic => "NIGHT ACRYLIC",
            NativeStylePreset.MicaAlt => "MICA ALT",
            _ => preset.ToString().ToUpperInvariant(),
        };

    private static string GetDescription(NativeStylePreset preset) =>
        preset switch
        {
            NativeStylePreset.SystemDefault =>
                "All reviewed DWM attributes are returned to their documented default values.",
            NativeStylePreset.GraphiteMica =>
                "Long-lived Windows system backdrop, dark native caption, rounded corners and a restrained cyan border.",
            NativeStylePreset.NightAcrylic =>
                "Transient Windows system backdrop, compact rounded corners and a low-glare amber frame.",
            NativeStylePreset.MicaAlt =>
                "Alternate tabbed system backdrop with a quiet indigo frame and native caption controls.",
            _ => "Unknown style preset.",
        };
}
