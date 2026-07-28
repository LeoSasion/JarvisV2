using System.Text.Json;

namespace Jarvis.NativeWindowStyleSession;

internal sealed class NativeWindowStyleSessionController
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly NativeWindowStyleSessionStore store = new();

    public int Inspect(
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle)
    {
        NativeExplorerWindowTarget target =
            NativeExplorerWindowTarget.Bind(
                windowHandle,
                expectedProcessId,
                expectedTitle);
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-native-window-style-inspection",
                result = "passed-read-only",
                observedAtUtc = DateTimeOffset.UtcNow,
                target = target.Identity,
                allowedDwmAttributes = new[]
                {
                    "DWMWA_BORDER_COLOR",
                    "DWMWA_CAPTION_COLOR",
                    "DWMWA_TEXT_COLOR",
                },
                baselineReadable = false,
                baselineContract =
                    "new-window-system-default-colors",
                resetValue = "DWMWA_COLOR_DEFAULT (0xFFFFFFFF)",
                mutationPerformed = false,
                liveExplorer = "read-only-window-inspection",
            });
        return 0;
    }

    public int Plan(
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle,
        string presetId,
        int ttlSeconds)
    {
        NativeWindowStylePolicy.ValidateTtl(ttlSeconds);
        NativeWindowColorPreset preset =
            NativeWindowStylePolicy.GetPreset(presetId);
        NativeExplorerWindowTarget target =
            NativeExplorerWindowTarget.Bind(
                windowHandle,
                expectedProcessId,
                expectedTitle);
        string escapedTitle = expectedTitle.Replace("\"", "\\\"");
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-native-window-style-preview-plan",
                result = "passed-read-only-plan",
                observedAtUtc = DateTimeOffset.UtcNow,
                target = target.Identity,
                preset = new
                {
                    preset.Id,
                    preset.BorderHex,
                    preset.CaptionHex,
                    preset.TextHex,
                },
                ttlSeconds,
                baselineContract =
                    "new-window-system-default-colors",
                exactApplyCommand =
                    "dotnet run --project " +
                    @".\src\Jarvis.NativeWindowStyleSession " +
                    "--configuration Release --no-build -- " +
                    $"apply-preview --window-handle {windowHandle} " +
                    $"--expected-process-id {expectedProcessId} " +
                    $"--expected-title \"{escapedTitle}\" " +
                    $"--preset {preset.Id} --ttl-seconds {ttlSeconds} " +
                    NativeWindowStylePolicy.BaselineAcknowledgement +
                    " " +
                    NativeWindowStylePolicy.ApplyConfirmation,
                exactEmergencyResetCommand =
                    "dotnet run --project " +
                    @".\src\Jarvis.NativeWindowStyleSession " +
                    "--configuration Release --no-build -- " +
                    $"reset-default --window-handle {windowHandle} " +
                    $"--expected-process-id {expectedProcessId} " +
                    $"--expected-title \"{escapedTitle}\" " +
                    NativeWindowStylePolicy.ResetConfirmation,
                limits = new
                {
                    changesClientArea = false,
                    injectsCode = false,
                    startsWindhawk = false,
                    restartsExplorer = false,
                    terminatesProcess = false,
                    changesRegistry = false,
                    maximumTtlSeconds =
                        NativeWindowStylePolicy.MaximumTtlSeconds,
                },
                mutationPerformed = false,
                liveExplorer = "read-only-window-inspection",
            });
        return 0;
    }

    public async Task<int> ApplyAsync(
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle,
        string presetId,
        int ttlSeconds,
        bool confirmed,
        bool baselineAcknowledged)
    {
        NativeWindowStylePolicy.RequireApplyConfirmation(
            confirmed,
            baselineAcknowledged);
        NativeWindowStylePolicy.ValidateTtl(ttlSeconds);
        NativeWindowColorPreset preset =
            NativeWindowStylePolicy.GetPreset(presetId);
        NativeExplorerWindowTarget target =
            NativeExplorerWindowTarget.Bind(
                windowHandle,
                expectedProcessId,
                expectedTitle);

        string runId =
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
            $"{Guid.NewGuid():N}"[..8];
        DateTimeOffset preparedAtUtc = DateTimeOffset.UtcNow;
        NativeWindowStyleSessionJournal journal = new()
        {
            RunId = runId,
            Result = "prepared",
            State = "prepared",
            SessionPath = store.NewSessionPath(runId),
            Target = target.Identity,
            Preset = preset.Id,
            BorderHex = preset.BorderHex,
            CaptionHex = preset.CaptionHex,
            TextHex = preset.TextHex,
            TtlSeconds = ttlSeconds,
            PreparedAtUtc = preparedAtUtc,
            ExpiresAtUtc = preparedAtUtc.AddSeconds(ttlSeconds),
            InjectionRequested = false,
            ExplorerRestartRequested = false,
            ProcessTerminationRequested = false,
            RegistryMutationRequested = false,
        };

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
        Exception? resetError = null;
        try
        {
            NativeExplorerWindowTarget preApply =
                NativeExplorerWindowTarget.Bind(
                    windowHandle,
                    expectedProcessId,
                    expectedTitle);
            if (!NativeExplorerWindowTarget.IsSameTarget(
                    preApply,
                    journal.Target))
            {
                throw new InvalidOperationException(
                    "Native Explorer window identity changed before SET.");
            }

            journal.ApplyAttempted = true;
            journal.Result = "apply-attempt-recorded";
            store.Update(journal);

            DwmColorOperation apply = DwmWindowColorTransport.Apply(
                preApply.WindowHandle,
                preset);
            journal.ApplyHResults = apply.HResults;
            journal.MutationPerformed =
                apply.ColorMutationMayHaveOccurred;
            if (!apply.Passed)
            {
                throw new InvalidOperationException(
                    "One or more DWM color calls failed.");
            }

            journal.State = "active";
            journal.Result = "passed-preview-active";
            store.Update(journal);
            Console.Error.WriteLine(
                $"Native Explorer window preview active for at most " +
                $"{ttlSeconds}s. Session: {journal.SessionPath}");
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    previewLifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // TTL expiration and Ctrl+C share the same reset path.
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
                    ResetExactTarget(
                        journal,
                        windowHandle,
                        expectedProcessId,
                        expectedTitle);
                }
                catch (Exception exception)
                {
                    resetError = exception;
                    journal.ResetAttempted = true;
                    journal.ResetApiSucceeded = false;
                    journal.State = "reset-failed";
                    journal.Result = "emergency-reset-failed";
                    journal.Detail =
                        $"{journal.Detail} Reset error: " +
                        exception.Message;
                    store.Update(journal);
                }
            }
        }

        if (resetError is not null)
        {
            WriteJson(journal);
            Console.Error.WriteLine(
                "DEFAULT RESET FAILED. Run the exact reset-default command.");
            return 41;
        }

        if (operationError is not null)
        {
            if (journal.ResetApiSucceeded)
            {
                journal.Result = "failed-safe-default-reset-succeeded";
            }
            journal.Detail =
                $"{operationError.Message} {journal.Detail}".Trim();
            store.Update(journal);
            WriteJson(journal);
            return 31;
        }

        journal.Result = journal.State == "target-retired"
            ? "passed-preview-ended-target-retired-no-send"
            : "passed-preview-completed-default-reset-succeeded";
        store.Update(journal);
        WriteJson(journal);
        return 0;
    }

    public int ResetDefault(
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle,
        bool confirmed)
    {
        NativeWindowStylePolicy.RequireResetConfirmation(confirmed);
        NativeExplorerWindowTarget target =
            NativeExplorerWindowTarget.Bind(
                windowHandle,
                expectedProcessId,
                expectedTitle);
        DwmColorOperation reset =
            DwmWindowColorTransport.ResetSystemDefault(
                target.WindowHandle);
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-native-window-style-manual-reset",
                result = reset.Passed
                    ? "passed-default-reset-api-succeeded"
                    : "failed",
                target = target.Identity,
                hResults = reset.HResults,
                resetValue = "DWMWA_COLOR_DEFAULT (0xFFFFFFFF)",
                injectionRequested = false,
                explorerRestartRequested = false,
                processTerminationRequested = false,
                registryMutationRequested = false,
            });
        return reset.Passed ? 0 : 41;
    }

    public static int RunModelTests()
    {
        List<object> scenarios = [];
        NativeWindowColorPreset signal =
            NativeWindowStylePolicy.GetPreset("signal");
        RunScenario(
            scenarios,
            "signal-border-colorref",
            () => signal.BorderColorRef == 0x00FFE500);
        RunScenario(
            scenarios,
            "signal-caption-colorref",
            () => signal.CaptionColorRef == 0x00403812);
        RunScenario(
            scenarios,
            "signal-text-colorref",
            () => signal.TextColorRef == 0x0066D1FF);
        RunScenario(
            scenarios,
            "ttl-minimum",
            () =>
            {
                NativeWindowStylePolicy.ValidateTtl(10);
                return true;
            });
        RunScenario(
            scenarios,
            "ttl-maximum",
            () =>
            {
                NativeWindowStylePolicy.ValidateTtl(60);
                return true;
            });
        RunExpectedFailure(
            scenarios,
            "ttl-below-minimum",
            () => NativeWindowStylePolicy.ValidateTtl(9));
        RunExpectedFailure(
            scenarios,
            "ttl-above-maximum",
            () => NativeWindowStylePolicy.ValidateTtl(61));
        RunExpectedFailure(
            scenarios,
            "unsupported-preset",
            () => NativeWindowStylePolicy.GetPreset("unknown"));
        RunExpectedFailure(
            scenarios,
            "apply-confirmation-required",
            () => NativeWindowStylePolicy.RequireApplyConfirmation(
                false,
                true));
        RunExpectedFailure(
            scenarios,
            "baseline-acknowledgement-required",
            () => NativeWindowStylePolicy.RequireApplyConfirmation(
                true,
                false));
        RunExpectedFailure(
            scenarios,
            "reset-confirmation-required",
            () => NativeWindowStylePolicy.RequireResetConfirmation(false));

        bool passed = scenarios.All(
            scenario =>
                (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                    scenario) ?? false));
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-native-window-style-model-tests",
                result = passed ? "passed" : "failed",
                scenarioCount = scenarios.Count,
                passedCount = scenarios.Count(
                    scenario =>
                        (bool)(scenario.GetType().GetProperty("Passed")
                            ?.GetValue(scenario) ?? false)),
                scenarios,
                mutationPerformed = false,
                liveExplorer = "not-run",
            });
        return passed ? 0 : 1;
    }

    private void ResetExactTarget(
        NativeWindowStyleSessionJournal journal,
        string windowHandle,
        uint expectedProcessId,
        string expectedTitle)
    {
        journal.ResetAttempted = true;
        NativeExplorerWindowTarget current;
        try
        {
            current = NativeExplorerWindowTarget.Bind(
                windowHandle,
                expectedProcessId,
                expectedTitle);
        }
        catch (InvalidOperationException exception)
        {
            journal.State = "target-retired";
            journal.Result = "passed-target-retired-no-send";
            journal.Detail =
                "The exact temporary Explorer window no longer exists; " +
                $"no DWM call was sent. {exception.Message}";
            store.Update(journal);
            return;
        }

        if (!NativeExplorerWindowTarget.IsSameTarget(
                current,
                journal.Target))
        {
            journal.State = "target-retired";
            journal.Result = "passed-target-retired-no-send";
            journal.Detail =
                "The exact Explorer HWND identity changed; no reset was " +
                "sent to the replacement target.";
            store.Update(journal);
            return;
        }

        DwmColorOperation reset =
            DwmWindowColorTransport.ResetSystemDefault(
                current.WindowHandle);
        journal.ResetHResults = reset.HResults;
        journal.ResetApiSucceeded = reset.Passed;
        if (!reset.Passed)
        {
            throw new InvalidOperationException(
                "One or more DWM default-reset calls failed.");
        }

        journal.State = "reset-succeeded";
        journal.Result = "passed-default-reset-api-succeeded";
        journal.Detail =
            "DWM default reset returned success; before/after screenshot " +
            "comparison remains the external visual verification.";
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

    private static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, SerializerOptions));
}
