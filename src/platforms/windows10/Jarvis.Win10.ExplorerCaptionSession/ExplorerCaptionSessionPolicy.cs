namespace Jarvis.Win10.ExplorerCaptionSession;

internal static class ExplorerCaptionSessionPolicy
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;
    public const string RequiredCapability =
        "run-bounded-single-explorer-dark-caption-preview";
    public const string ApplyConfirmation =
        "--confirm-live-single-explorer-dark-caption";
    public const string RollbackConfirmation =
        "--confirm-live-single-explorer-dark-caption-rollback";

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
                $"Live apply requires {ApplyConfirmation}.");
        }
    }

    public static void RequireRollbackConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Emergency rollback requires {RollbackConfirmation}.");
        }
    }
}
