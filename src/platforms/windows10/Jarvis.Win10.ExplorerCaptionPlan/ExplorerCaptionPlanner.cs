namespace Jarvis.Win10.ExplorerCaptionPlan;

public static class ExplorerCaptionPlanner
{
    public const int MinimumTtlSeconds = 10;
    public const int MaximumTtlSeconds = 60;

    public static int WritePlan(
        ExplorerCaptionGateReceipt gate,
        int ttlSeconds)
    {
        if (!gate.Passed ||
            gate.Target is null ||
            gate.CurrentCaption is null)
        {
            ExplorerCaptionGate.WriteJson(gate);
            return 12;
        }

        ValidateTtl(ttlSeconds);
        ExplorerCaptionGate.WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-win10-explorer-caption-preview-plan",
                result = "passed-read-only-plan",
                observedAtUtc = DateTimeOffset.UtcNow,
                hostProfileId = gate.HostProfileId,
                target = gate.Target,
                original = new
                {
                    attribute =
                        "DWMWA_USE_IMMERSIVE_DARK_MODE",
                    attributeId = gate.CurrentCaption.Attribute,
                    value = gate.CurrentCaption.Value,
                    hResult = gate.CurrentCaption.HResult,
                },
                requested = new
                {
                    value = 1,
                    description = "dark-caption-request",
                    wouldChange =
                        gate.CurrentCaption.Value != 1,
                },
                ttlSeconds,
                requiredBeforeExecution = new[]
                {
                    "fresh exact-host and single-window compatibility report",
                    "durable original-value and HWND/PID/TID journal",
                    "exact target revalidation immediately before SET",
                    "documented DWM write limited to this one CabinetWClass HWND",
                    "finally rollback to the recorded original value",
                    "DwmGetWindowAttribute rollback readback verification",
                    "current-task approval of the exact apply command",
                },
                proposedFutureConfirmation =
                    "--confirm-live-single-explorer-dark-caption",
                exactFutureApplyCommand =
                    "dotnet run --project " +
                    @".\src\platforms\windows10\" +
                    "Jarvis.Win10.ExplorerCaptionSession " +
                    "--configuration Release --no-build -- apply-preview " +
                    $"--expected-window-handle {gate.Target.WindowHandle} " +
                    $"--ttl-seconds {ttlSeconds} " +
                    "--confirm-live-single-explorer-dark-caption",
                exactFutureEmergencyRollbackCommand =
                    "dotnet run --project " +
                    @".\src\platforms\windows10\" +
                    "Jarvis.Win10.ExplorerCaptionSession " +
                    "--configuration Release --no-build -- rollback " +
                    "--session \"%LOCALAPPDATA%\\JARVIS2\\" +
                    "ExplorerCaption\\active-session.json\" " +
                    "--confirm-live-single-explorer-dark-caption-rollback",
                previewExecutionSupported = false,
                mutationSupported = false,
                activationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "read-only-inspection",
            });
        return 0;
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
}
