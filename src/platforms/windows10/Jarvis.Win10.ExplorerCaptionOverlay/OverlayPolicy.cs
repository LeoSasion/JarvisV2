namespace Jarvis.Win10.ExplorerCaptionOverlay;

internal static class OverlayPolicy
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;

    public const string RequiredCapability =
        "run-bounded-owned-explorer-caption-overlay-preview";

    public const string Confirmation =
        "--confirm-owned-explorer-caption-overlay-preview";

    public static void ValidateTtl(int ttlSeconds)
    {
        if (ttlSeconds is < MinimumTtlSeconds or > MaximumTtlSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttlSeconds),
                $"Overlay TTL must be {MinimumTtlSeconds}.." +
                $"{MaximumTtlSeconds} seconds.");
        }
    }

    public static void RequireConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Owned overlay requires {Confirmation}.");
        }
    }
}
