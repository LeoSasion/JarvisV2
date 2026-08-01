namespace Jarvis.DesktopStyleSession;

public sealed record DesktopTextColorPreset(
    string Id,
    string HexColor,
    uint ColorRef);

public static class DesktopStylePolicy
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
            ["orbital-cyan"] = new(
                "orbital-cyan",
                "#00E5FF",
                ToColorRef(0x00, 0xE5, 0xFF)),
            ["reactor-amber"] = new(
                "reactor-amber",
                "#FF6A00",
                ToColorRef(0xFF, 0x6A, 0x00)),
            ["neural-emerald"] = new(
                "neural-emerald",
                "#00FF9A",
                ToColorRef(0x00, 0xFF, 0x9A)),
        };

    public static DesktopTextColorPreset GetPreset(string presetId)
    {
        if (!Presets.TryGetValue(presetId, out DesktopTextColorPreset? preset))
        {
            throw new ArgumentException(
                $"Unsupported preset '{presetId}'. Expected graphite, amber, " +
                "orbital-cyan, reactor-amber or neural-emerald.",
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
