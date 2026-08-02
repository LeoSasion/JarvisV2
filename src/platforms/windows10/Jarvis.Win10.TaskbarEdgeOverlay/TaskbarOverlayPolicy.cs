namespace Jarvis.Win10.TaskbarEdgeOverlay;

internal static class TaskbarOverlayPolicy
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;
    public const double EdgeHeightDips = 8.0;
    public const string RequiredCapability =
        "run-bounded-owned-taskbar-edge-overlay-preview";
    public const string Confirmation =
        "--confirm-owned-taskbar-edge-overlay-preview";

    public static void ValidateTtl(int ttlSeconds)
    {
        if (ttlSeconds is < MinimumTtlSeconds or > MaximumTtlSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttlSeconds),
                $"Taskbar overlay TTL must be {MinimumTtlSeconds}.." +
                $"{MaximumTtlSeconds} seconds.");
        }
    }

    public static void RequireConfirmation(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                $"Owned taskbar overlay requires {Confirmation}.");
        }
    }
}
