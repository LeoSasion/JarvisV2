namespace Jarvis.Win10.RgbThemeModel;

internal static class RgbEffectEngine
{
    public static RgbFrame Sample(
        double hueDegrees,
        double saturation,
        double value,
        string effectId,
        double phase)
    {
        if (!ThemeContract.RequiredEffects.ContainsKey(effectId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectId),
                effectId,
                "Unknown RGB effect.");
        }

        if (!double.IsFinite(hueDegrees) ||
            !double.IsFinite(saturation) ||
            !double.IsFinite(value) ||
            !double.IsFinite(phase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(hueDegrees),
                "RGB inputs must be finite.");
        }

        double normalizedPhase = NormalizeUnit(phase);
        double resolvedHue = NormalizeHue(hueDegrees);
        double brightnessScale = 1.0;
        switch (effectId)
        {
            case "breathe":
                brightnessScale =
                    0.65 +
                    (0.35 * ((Math.Sin(
                        (normalizedPhase * Math.PI * 2.0) -
                        (Math.PI / 2.0)) + 1.0) / 2.0));
                break;
            case "spectrum":
                resolvedHue =
                    NormalizeHue(
                        resolvedHue +
                        (normalizedPhase * 360.0));
                break;
            case "signal-pulse":
                double signal =
                    Math.Max(
                        0.0,
                        Math.Sin(normalizedPhase * Math.PI * 2.0));
                brightnessScale =
                    0.55 + (0.45 * Math.Pow(signal, 4.0));
                break;
        }

        double resolvedSaturation = Math.Clamp(saturation, 0.0, 1.0);
        double resolvedValue =
            Math.Clamp(value, 0.1, 1.0) * brightnessScale;
        (byte red, byte green, byte blue) =
            HsvToRgb(
                resolvedHue,
                resolvedSaturation,
                resolvedValue);

        return new RgbFrame(
            1,
            effectId,
            normalizedPhase,
            resolvedHue,
            resolvedSaturation,
            resolvedValue,
            brightnessScale,
            red,
            green,
            blue,
            $"#{red:X2}{green:X2}{blue:X2}");
    }

    public static (byte Red, byte Green, byte Blue) HsvToRgb(
        double hueDegrees,
        double saturation,
        double value)
    {
        double hue = NormalizeHue(hueDegrees);
        double sat = Math.Clamp(saturation, 0.0, 1.0);
        double val = Math.Clamp(value, 0.0, 1.0);
        double chroma = val * sat;
        double hueSector = hue / 60.0;
        double secondary =
            chroma *
            (1.0 - Math.Abs((hueSector % 2.0) - 1.0));
        (double r1, double g1, double b1) =
            ((int)Math.Floor(hueSector)) switch
            {
                0 => (chroma, secondary, 0.0),
                1 => (secondary, chroma, 0.0),
                2 => (0.0, chroma, secondary),
                3 => (0.0, secondary, chroma),
                4 => (secondary, 0.0, chroma),
                _ => (chroma, 0.0, secondary),
            };
        double match = val - chroma;
        return (
            ToByte(r1 + match),
            ToByte(g1 + match),
            ToByte(b1 + match));
    }

    private static byte ToByte(double value) =>
        checked((byte)Math.Round(
            Math.Clamp(value, 0.0, 1.0) * 255.0,
            MidpointRounding.AwayFromZero));

    private static double NormalizeHue(double hueDegrees)
    {
        double normalized = hueDegrees % 360.0;
        return normalized < 0.0
            ? normalized + 360.0
            : normalized;
    }

    private static double NormalizeUnit(double value)
    {
        double normalized = value % 1.0;
        return normalized < 0.0
            ? normalized + 1.0
            : normalized;
    }
}
