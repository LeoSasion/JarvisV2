using System.Globalization;
using System.Text.Json;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.RgbThemeModel;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        EmbeddedTheme embedded = EmbeddedThemeReader.Read();
        if (args.Length == 1 && args[0] == "compile")
        {
            ThemeCompilationReceipt receipt =
                ThemeCompiler.Compile(
                    embedded.Document,
                    embedded.Sha256);
            Write(receipt);
            return receipt.Result ==
                    "compiled-approved-offline-intent"
                ? 0
                : 12;
        }

        if (args.Length == 1 && args[0] == "test")
        {
            ThemeModelTestReceipt receipt =
                ModelScenarios.Run(embedded.Document);
            Write(receipt);
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 && args[0] == "compile-vfx")
        {
            EmbeddedVfxContract vfx =
                EmbeddedVfxContractReader.Read();
            VfxCompilationReceipt receipt =
                VfxContractCompiler.Compile(
                    vfx.Document,
                    vfx.Sha256);
            Write(receipt);
            return receipt.Result ==
                    "compiled-parameter-contract"
                ? 0
                : 12;
        }

        if (args.Length == 1 && args[0] == "test-vfx")
        {
            EmbeddedVfxContract vfx =
                EmbeddedVfxContractReader.Read();
            EmbeddedVfxPreset preset =
                EmbeddedVfxPresetReader.Read();
            VfxModelTestReceipt receipt =
                VfxContractScenarios.Run(
                    vfx.Document,
                    preset.Document);
            Write(receipt);
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (args.Length == 1 && args[0] == "compile-vfx-preset")
        {
            EmbeddedVfxContract vfx =
                EmbeddedVfxContractReader.Read();
            EmbeddedVfxPreset preset =
                EmbeddedVfxPresetReader.Read();
            VfxPresetCompilationReceipt receipt =
                VfxPresetCompiler.Compile(
                    vfx.Document,
                    vfx.Sha256,
                    preset.Document,
                    preset.Sha256);
            Write(receipt);
            return receipt.Result == "compiled-inert-preset"
                ? 0
                : 12;
        }

        if (args.Length == 6 && args[0] == "sample")
        {
            if (!TryDouble(args[1], out double hue) ||
                !TryDouble(args[2], out double saturation) ||
                !TryDouble(args[3], out double value) ||
                !TryDouble(args[5], out double phase))
            {
                Console.Error.WriteLine(
                    "Hue, saturation, value and phase must be numbers.");
                return 2;
            }

            try
            {
                Write(
                    RgbEffectEngine.Sample(
                        hue,
                        saturation,
                        value,
                        args[4],
                        phase));
                return 0;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
        }

        Console.Error.WriteLine(
            "Usage: jarvis-win10-rgb-theme-model " +
            "<compile|test|compile-vfx|compile-vfx-preset|test-vfx> " +
            "or sample <hue> <saturation> <value> <effect> <phase>");
        return 2;
    }

    private static bool TryDouble(
        string value,
        out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);

    private static void Write<T>(T value) =>
        Console.WriteLine(
            JsonSerializer.Serialize(value, JsonOptions));
}
