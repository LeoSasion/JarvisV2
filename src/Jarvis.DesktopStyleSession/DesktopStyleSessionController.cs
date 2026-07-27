using System.Text.Json;

namespace Jarvis.DesktopStyleSession;

internal sealed class DesktopStyleSessionController
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly DesktopStyleSessionStore store = new();

    public int Inspect(uint expectedExplorerProcessId)
    {
        DesktopHostTarget target =
            NativeDesktopHost.LocateExact(expectedExplorerProcessId);
        uint textColor =
            DesktopListViewTransport.GetTextColor(target.FolderViewWindow);
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType = "jarvisv2-desktop-text-color-inspection",
                result = "passed-read-only",
                observedAtUtc = DateTimeOffset.UtcNow,
                target = target.Identity,
                textColorRef = textColor,
                textColorHex = FormatColorRef(textColor),
                transport = new
                {
                    message = "LVM_GETTEXTCOLOR",
                    timeoutMilliseconds = 250,
                    scalarOnly = true,
                },
                mutationSupported = false,
                activationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "read-only-inspection",
            });
        return 0;
    }

    public int Plan(
        uint expectedExplorerProcessId,
        string presetId,
        int ttlSeconds)
    {
        DesktopStylePolicy.ValidateTtl(ttlSeconds);
        DesktopTextColorPreset preset =
            DesktopStylePolicy.GetPreset(presetId);
        DesktopHostTarget target =
            NativeDesktopHost.LocateExact(expectedExplorerProcessId);
        uint originalColor =
            DesktopListViewTransport.GetTextColor(target.FolderViewWindow);
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType = "jarvisv2-desktop-style-preview-plan",
                result = "passed-read-only-plan",
                observedAtUtc = DateTimeOffset.UtcNow,
                target = target.Identity,
                originalColorRef = originalColor,
                originalColorHex = FormatColorRef(originalColor),
                preset = preset.Id,
                previewColorRef = preset.ColorRef,
                previewColorHex = preset.HexColor,
                ttlSeconds,
                exactApplyCommand =
                    "dotnet run --project " +
                    @".\src\Jarvis.DesktopStyleSession " +
                    "--configuration Release --no-build -- apply-preview " +
                    $"--expected-explorer-pid {expectedExplorerProcessId} " +
                    $"--preset {preset.Id} --ttl-seconds {ttlSeconds} " +
                    DesktopStylePolicy.ApplyConfirmation,
                exactEmergencyRollbackCommand =
                    "dotnet run --project " +
                    @".\src\Jarvis.DesktopStyleSession " +
                    "--configuration Release --no-build -- rollback " +
                    $"--session \"{store.ActiveSessionPath}\" " +
                    "--expected-explorer-pid " +
                    $"{expectedExplorerProcessId} " +
                    DesktopStylePolicy.RollbackConfirmation,
                limits = new
                {
                    scalarMessages = new[]
                    {
                        "LVM_GETTEXTCOLOR",
                        "LVM_SETTEXTCOLOR",
                    },
                    timeoutMilliseconds = 250,
                    maximumTtlSeconds = DesktopStylePolicy.MaximumTtlSeconds,
                    changesTextBackground = false,
                    changesWindowBackground = false,
                    changesIconLayout = false,
                    changesWallpaper = false,
                    changesRegistry = false,
                    restartsExplorer = false,
                },
                activationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "read-only-inspection",
            });
        return 0;
    }

    public async Task<int> ApplyAsync(
        uint expectedExplorerProcessId,
        string presetId,
        int ttlSeconds,
        bool confirmed)
    {
        DesktopStylePolicy.RequireApplyConfirmation(confirmed);
        DesktopStylePolicy.ValidateTtl(ttlSeconds);
        DesktopTextColorPreset preset =
            DesktopStylePolicy.GetPreset(presetId);
        DesktopHostTarget target =
            NativeDesktopHost.LocateExact(expectedExplorerProcessId);
        uint originalColor =
            DesktopListViewTransport.GetTextColor(target.FolderViewWindow);

        string runId =
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
            $"{Guid.NewGuid():N}"[..8];
        string sessionPath = store.NewSessionPath(runId);
        DateTimeOffset preparedAt = DateTimeOffset.UtcNow;
        DesktopStyleSessionJournal journal = new()
        {
            RunId = runId,
            Result = "prepared",
            State = "prepared",
            SessionPath = sessionPath,
            Target = target.Identity,
            Preset = preset.Id,
            PreviewColorHex = preset.HexColor,
            PreviewColorRef = preset.ColorRef,
            OriginalColorRef = originalColor,
            TtlSeconds = ttlSeconds,
            PreparedAtUtc = preparedAt,
            ExpiresAtUtc = preparedAt.AddSeconds(ttlSeconds),
            ActivationPermitted = false,
            ExplorerRestartRequested = false,
            ProcessTerminationRequested = false,
            RegistryMutationRequested = false,
        };

        // The rollback value and exact target identity are durable before SET.
        store.Prepare(journal);

        using CancellationTokenSource previewLifetime = new();
        previewLifetime.CancelAfter(TimeSpan.FromSeconds(ttlSeconds));
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            previewLifetime.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        Exception? operationError = null;
        Exception? rollbackError = null;
        try
        {
            DesktopHostTarget preApply =
                NativeDesktopHost.LocateExact(expectedExplorerProcessId);
            if (!NativeDesktopHost.IsSameTarget(preApply, journal.Target))
            {
                throw new InvalidOperationException(
                    "Desktop target identity changed before SET.");
            }

            journal.ApplyAttempted = true;
            journal.Result = "apply-attempt-recorded";
            store.Update(journal);

            DesktopListViewTransport.SetTextColor(
                preApply.FolderViewWindow,
                preset.ColorRef);
            journal.MutationPerformed = true;
            uint appliedColor =
                DesktopListViewTransport.GetTextColor(
                    preApply.FolderViewWindow);
            journal.LastObservedColorRef = appliedColor;
            if (appliedColor != preset.ColorRef)
            {
                throw new InvalidOperationException(
                    "SET completed but GET did not verify the preview color.");
            }

            journal.State = "active";
            journal.Result = "passed-preview-active";
            store.Update(journal);
            Console.Error.WriteLine(
                $"Desktop text-color preview active for at most " +
                $"{ttlSeconds}s. Session: {sessionPath}");
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    previewLifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // TTL expiration and Ctrl+C share the same rollback path.
            }
        }
        catch (Exception exception)
        {
            operationError = exception;
            journal.Detail = exception.Message;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (journal.ApplyAttempted)
            {
                try
                {
                    RollBackExactTarget(
                        journal,
                        expectedExplorerProcessId);
                }
                catch (Exception exception)
                {
                    rollbackError = exception;
                    journal.RollbackAttempted = true;
                    journal.RollbackVerified = false;
                    journal.State = "rollback-failed";
                    journal.Result = "emergency-rollback-failed";
                    journal.Detail =
                        $"{journal.Detail} Rollback error: " +
                        exception.Message;
                    store.Update(journal);
                }
            }
        }

        if (rollbackError is not null)
        {
            WriteJson(journal);
            Console.Error.WriteLine(
                "ROLLBACK FAILED. Run the exact rollback command against " +
                store.ActiveSessionPath + ".");
            return 41;
        }

        if (operationError is not null)
        {
            if (journal.RollbackVerified)
            {
                journal.Result = "failed-safe-rolled-back";
            }
            journal.Detail =
                $"{operationError.Message} {journal.Detail}".Trim();
            store.Update(journal);
            WriteJson(journal);
            return 31;
        }

        journal.Result = journal.State == "target-retired"
            ? "passed-preview-ended-target-retired-no-send"
            : "passed-preview-completed-and-rolled-back";
        store.Update(journal);
        WriteJson(journal);
        return 0;
    }

    public int Rollback(
        uint expectedExplorerProcessId,
        string sessionPath,
        bool confirmed)
    {
        DesktopStylePolicy.RequireRollbackConfirmation(confirmed);
        DesktopStyleSessionJournal journal = store.Load(sessionPath);
        if (journal.State is "recovered" or "target-retired")
        {
            WriteJson(journal);
            return 0;
        }

        RollBackExactTarget(journal, expectedExplorerProcessId);
        WriteJson(journal);
        return 0;
    }

    public static int RunModelTests()
    {
        List<object> scenarios = [];
        RunScenario(
            scenarios,
            "graphite-colorref",
            () => DesktopStylePolicy.GetPreset("graphite").ColorRef ==
                0x00EAEFBD);
        RunScenario(
            scenarios,
            "amber-colorref",
            () => DesktopStylePolicy.GetPreset("amber").ColorRef ==
                0x007BC7F0);
        RunScenario(
            scenarios,
            "ttl-minimum",
            () =>
            {
                DesktopStylePolicy.ValidateTtl(
                    DesktopStylePolicy.MinimumTtlSeconds);
                return true;
            });
        RunScenario(
            scenarios,
            "ttl-maximum",
            () =>
            {
                DesktopStylePolicy.ValidateTtl(
                    DesktopStylePolicy.MaximumTtlSeconds);
                return true;
            });
        RunExpectedFailure(
            scenarios,
            "ttl-below-minimum",
            () => DesktopStylePolicy.ValidateTtl(9));
        RunExpectedFailure(
            scenarios,
            "ttl-above-maximum",
            () => DesktopStylePolicy.ValidateTtl(61));
        RunExpectedFailure(
            scenarios,
            "unsupported-preset",
            () => DesktopStylePolicy.GetPreset("unknown"));
        RunExpectedFailure(
            scenarios,
            "apply-confirmation-required",
            () => DesktopStylePolicy.RequireApplyConfirmation(false));
        RunExpectedFailure(
            scenarios,
            "rollback-confirmation-required",
            () => DesktopStylePolicy.RequireRollbackConfirmation(false));

        bool passed = scenarios.All(
            scenario =>
                (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                    scenario) ?? false));
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType = "jarvisv2-desktop-style-session-model-tests",
                result = passed ? "passed" : "failed",
                scenarioCount = scenarios.Count,
                passedCount = scenarios.Count(
                    scenario =>
                        (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                            scenario) ?? false)),
                scenarios,
                activationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "not-run",
            });
        return passed ? 0 : 1;
    }

    private void RollBackExactTarget(
        DesktopStyleSessionJournal journal,
        uint expectedExplorerProcessId)
    {
        journal.RollbackAttempted = true;
        DesktopHostTarget current;
        try
        {
            current =
                NativeDesktopHost.LocateExact(expectedExplorerProcessId);
        }
        catch (InvalidOperationException exception)
        {
            journal.State = "target-retired";
            journal.Result = "passed-target-retired-no-send";
            journal.Detail =
                "The original Explorer target no longer exists; no message " +
                $"was sent. {exception.Message}";
            store.Update(journal);
            return;
        }

        if (!NativeDesktopHost.IsSameTarget(current, journal.Target))
        {
            journal.State = "target-retired";
            journal.Result = "passed-target-retired-no-send";
            journal.Detail =
                "The exact original desktop HWND identity changed; no " +
                "rollback message was sent to the replacement target.";
            store.Update(journal);
            return;
        }

        DesktopListViewTransport.SetTextColor(
            current.FolderViewWindow,
            journal.OriginalColorRef);
        uint restoredColor =
            DesktopListViewTransport.GetTextColor(
                current.FolderViewWindow);
        journal.LastObservedColorRef = restoredColor;
        journal.RollbackVerified =
            restoredColor == journal.OriginalColorRef;
        if (!journal.RollbackVerified)
        {
            throw new InvalidOperationException(
                "Rollback SET completed but GET did not verify the original " +
                "text color.");
        }

        journal.State = "recovered";
        journal.Result = "passed-rollback-verified";
        journal.Detail = "Original desktop text color restored and verified.";
        store.Update(journal);
    }

    private static void RunScenario(
        ICollection<object> scenarios,
        string name,
        Func<bool> test)
    {
        bool passed;
        string detail;
        try
        {
            passed = test();
            detail = passed ? "passed" : "returned false";
        }
        catch (Exception exception)
        {
            passed = false;
            detail = exception.Message;
        }

        scenarios.Add(new { Name = name, Passed = passed, Detail = detail });
    }

    private static void RunExpectedFailure(
        ICollection<object> scenarios,
        string name,
        Action test)
    {
        bool passed = false;
        string detail = "did not fail";
        try
        {
            test();
        }
        catch (Exception exception)
        {
            passed = true;
            detail = exception.GetType().Name;
        }

        scenarios.Add(new { Name = name, Passed = passed, Detail = detail });
    }

    private static string FormatColorRef(uint colorRef)
    {
        byte red = unchecked((byte)(colorRef & 0xFF));
        byte green = unchecked((byte)((colorRef >> 8) & 0xFF));
        byte blue = unchecked((byte)((colorRef >> 16) & 0xFF));
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions));
}
