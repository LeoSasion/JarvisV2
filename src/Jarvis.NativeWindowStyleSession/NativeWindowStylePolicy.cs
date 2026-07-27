namespace Jarvis.NativeWindowStyleSession;

internal sealed record NativeWindowColorPreset(
    string Id,
    string BorderHex,
    uint BorderColorRef,
    string CaptionHex,
    uint CaptionColorRef,
    string TextHex,
    uint TextColorRef);

internal static class NativeWindowStylePolicy
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;
    public const string ApplyConfirmation =
        "--confirm-live-native-window-style";
    public const string ResetConfirmation =
        "--confirm-live-native-window-style-rollback";
    public const string BaselineAcknowledgement =
        "--baseline-system-default";

    public static NativeWindowColorPreset GetPreset(string presetId) =>
        presetId switch
        {
            "signal" => new(
                "signal",
                "#00E5FF",
                ToColorRef(0x00, 0xE5, 0xFF),
                "#123840",
                ToColorRef(0x12, 0x38, 0x40),
                "#FFD166",
                ToColorRef(0xFF, 0xD1, 0x66)),
            _ => throw new ArgumentException(
                $"Unsupported preset '{presetId}'. Expected signal.",
                nameof(presetId)),
        };

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

    public static void RequireApplyConfirmation(
        bool confirmed,
        bool baselineAcknowledged)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Live preview requires {ApplyConfirmation}.");
        }

        if (!baselineAcknowledged)
        {
            throw new InvalidOperationException(
                "Live preview is limited to a newly opened system-default " +
                $"window and requires {BaselineAcknowledgement}.");
        }
    }

    public static void RequireResetConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Manual reset requires {ResetConfirmation}.");
        }
    }

    private static uint ToColorRef(byte red, byte green, byte blue) =>
        red | ((uint)green << 8) | ((uint)blue << 16);
}
