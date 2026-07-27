namespace Jarvis.DesktopStyleSession;

internal sealed record DesktopTextColorPreset(
    string Id,
    string HexColor,
    uint ColorRef);

internal static class DesktopStylePolicy
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;
    public const string ApplyConfirmation =
        "--confirm-live-desktop-text-color";
    public const string RollbackConfirmation =
        "--confirm-live-desktop-text-color-rollback";

    private static readonly IReadOnlyDictionary<
        string,
        DesktopTextColorPreset> Presets =
        new Dictionary<string, DesktopTextColorPreset>(
            StringComparer.Ordinal)
        {
            ["graphite"] = new(
                "graphite",
                "#BDEFEA",
                ToColorRef(0xBD, 0xEF, 0xEA)),
            ["amber"] = new(
                "amber",
                "#F0C77B",
                ToColorRef(0xF0, 0xC7, 0x7B)),
        };

    public static DesktopTextColorPreset GetPreset(string presetId)
    {
        if (!Presets.TryGetValue(presetId, out DesktopTextColorPreset? preset))
        {
            throw new ArgumentException(
                $"Unsupported preset '{presetId}'. Expected graphite or amber.",
                nameof(presetId));
        }

        return preset;
    }

    public static void ValidateTtl(int ttlSeconds)
    {
        if (ttlSeconds < MinimumTtlSeconds ||
            ttlSeconds > MaximumTtlSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttlSeconds),
                ttlSeconds,
                $"TTL must be between {MinimumTtlSeconds} and " +
                $"{MaximumTtlSeconds} seconds.");
        }
    }

    public static void RequireApplyConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Live preview requires {ApplyConfirmation}.");
        }
    }

    public static void RequireRollbackConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Manual rollback requires {RollbackConfirmation}.");
        }
    }

    private static uint ToColorRef(byte red, byte green, byte blue) =>
        red | ((uint)green << 8) | ((uint)blue << 16);
}
