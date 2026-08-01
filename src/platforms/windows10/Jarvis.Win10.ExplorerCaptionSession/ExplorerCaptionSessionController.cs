using System.Text.Json;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionSession;

internal sealed class ExplorerCaptionSessionController
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ExplorerCaptionSessionStore store = new();

    public async Task<int> ApplyAsync(
        string expectedWindowHandle,
        int ttlSeconds,
        bool confirmed)
    {
        ExplorerCaptionSessionPolicy.RequireApplyConfirmation(
            confirmed);
        ExplorerCaptionSessionPolicy.ValidateTtl(ttlSeconds);
        ExplorerCaptionGateResult initial =
            ExplorerCaptionGate.Inspect(expectedWindowHandle);
        EnsureApplyGate(initial.Receipt);
        ExplorerCaptionGateReceipt initialReceipt =
            initial.Receipt;
        ExplorerCaptionTargetIdentity target =
            initialReceipt.Target ??
            throw new InvalidOperationException(
                "The apply gate did not return a target.");
        DwmCaptionObservation original =
            initialReceipt.CurrentCaption ??
            throw new InvalidOperationException(
                "The apply gate did not return the original caption value.");
        if (original.Value != 0)
        {
            throw new InvalidOperationException(
                "The selected Explorer caption is already dark; a visible " +
                "preview would be a no-op.");
        }

        string runId =
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-" +
            $"{Guid.NewGuid():N}"[..8];
        string sessionPath = store.NewSessionPath(runId);
        DateTimeOffset preparedAt = DateTimeOffset.UtcNow;
        ExplorerCaptionSessionJournal journal = new()
        {
            RunId = runId,
            Result = "prepared",
            State = "prepared",
            SessionPath = sessionPath,
            HostProfileId =
                initialReceipt.HostProfileId ??
                throw new InvalidOperationException(
                    "The apply gate did not return a host profile."),
            Target = target,
            OriginalValue = original.Value,
            TtlSeconds = ttlSeconds,
            PreparedAtUtc = preparedAt,
            ExpiresAtUtc = preparedAt.AddSeconds(ttlSeconds),
            InjectionRequested = false,
            ExplorerRestartRequested = false,
            ProcessTerminationRequested = false,
            RegistryMutationRequested = false,
            ModuleActivationPermitted = false,
        };

        // The original value and exact target identity are durable before SET.
        store.Prepare(journal);

        using CancellationTokenSource previewLifetime = new();
        previewLifetime.CancelAfter(
            TimeSpan.FromSeconds(ttlSeconds));
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
            ExplorerCaptionGateResult preApply =
                ExplorerCaptionGate.Inspect(target.WindowHandle);
            EnsureApplyGate(preApply.Receipt);
            if (preApply.Receipt.Target is null ||
                preApply.Receipt.CurrentCaption is null ||
                !NativeExplorerCaptionTarget.IsSameTarget(
                    preApply.Receipt.Target,
                    journal.Target) ||
                preApply.Receipt.CurrentCaption.Value !=
                    journal.OriginalValue)
            {
                throw new InvalidOperationException(
                    "Explorer caption target or original value changed " +
                    "before SET.");
            }

            journal.ApplyAttempted = true;
            journal.MutationMayHaveOccurred = true;
            journal.Result = "apply-attempt-recorded";
            store.Update(journal);

            DwmCaptionWriter.SetDarkCaption(
                preApply.WindowHandle,
                enabled: true);
            DwmCaptionWriter.RequestNonClientRefresh(
                preApply.WindowHandle);
            journal.ApplyNonClientRefreshRequested = true;
            DwmCaptionWriter.Flush();
            journal.MutationPerformed = true;
            DwmCaptionObservation applied =
                DwmCaptionReader.Read(preApply.WindowHandle);
            journal.LastObservedValue = applied.Value;
            journal.ApplyVerified =
                applied.Passed && applied.Value == 1;
            if (!journal.ApplyVerified)
            {
                throw new InvalidOperationException(
                    "SET completed but GET did not verify the dark caption.");
            }

            journal.State = "active";
            journal.Result = "passed-preview-active";
            store.Update(journal);
            Console.Error.WriteLine(
                "Single-Explorer dark-caption preview active for at most " +
                $"{ttlSeconds}s. Session: {sessionPath}");
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    previewLifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // TTL expiration and Ctrl+C share the exact rollback path.
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
                    RollBackExactTarget(journal);
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
            journal.Result = journal.RollbackVerified
                ? "failed-safe-rolled-back"
                : journal.Result;
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

    public int Rollback(string sessionPath, bool confirmed)
    {
        ExplorerCaptionSessionPolicy.RequireRollbackConfirmation(
            confirmed);
        ExplorerCaptionSessionJournal journal =
            store.Load(sessionPath);
        if (journal.State is "recovered" or "target-retired")
        {
            WriteJson(journal);
            return 0;
        }

        RollBackExactTarget(journal);
        WriteJson(journal);
        return 0;
    }

    public static int RunModelTests()
    {
        List<object> scenarios = [];
        RunScenario(
            scenarios,
            "ttl-minimum",
            () =>
            {
                ExplorerCaptionSessionPolicy.ValidateTtl(
                    ExplorerCaptionSessionPolicy.MinimumTtlSeconds);
                return true;
            });
        RunScenario(
            scenarios,
            "ttl-maximum",
            () =>
            {
                ExplorerCaptionSessionPolicy.ValidateTtl(
                    ExplorerCaptionSessionPolicy.MaximumTtlSeconds);
                return true;
            });
        RunExpectedFailure(
            scenarios,
            "ttl-below-minimum",
            () => ExplorerCaptionSessionPolicy.ValidateTtl(9));
        RunExpectedFailure(
            scenarios,
            "ttl-above-maximum",
            () => ExplorerCaptionSessionPolicy.ValidateTtl(61));
        RunScenario(
            scenarios,
            "apply-confirmation-accepted",
            () =>
            {
                ExplorerCaptionSessionPolicy.RequireApplyConfirmation(true);
                return true;
            });
        RunExpectedFailure(
            scenarios,
            "apply-confirmation-required",
            () =>
                ExplorerCaptionSessionPolicy.RequireApplyConfirmation(false));
        RunScenario(
            scenarios,
            "rollback-confirmation-accepted",
            () =>
            {
                ExplorerCaptionSessionPolicy.RequireRollbackConfirmation(
                    true);
                return true;
            });
        RunExpectedFailure(
            scenarios,
            "rollback-confirmation-required",
            () =>
                ExplorerCaptionSessionPolicy.RequireRollbackConfirmation(
                    false));
        RunScenario(
            scenarios,
            "capability-is-platform-specific",
            () => string.Equals(
                ExplorerCaptionSessionPolicy.RequiredCapability,
                "run-bounded-single-explorer-dark-caption-preview",
                StringComparison.Ordinal));

        int passedCount = scenarios.Count(
            scenario =>
                (bool)(scenario.GetType().GetProperty("Passed")?.GetValue(
                    scenario) ?? false));
        bool passed = passedCount == scenarios.Count;
        WriteJson(
            new
            {
                schemaVersion = 1,
                receiptType =
                    "jarvisv2-win10-explorer-caption-session-model-tests",
                result = passed ? "passed" : "failed",
                scenarioCount = scenarios.Count,
                passedCount,
                scenarios,
                moduleActivationPermitted = false,
                mutationPerformed = false,
                liveExplorer = "not-run",
            });
        return passed ? 0 : 1;
    }

    private static void EnsureApplyGate(
        ExplorerCaptionGateReceipt gate)
    {
        bool capabilityGranted =
            gate.Admission.Profile?.AllowedCapabilities.Contains(
                ExplorerCaptionSessionPolicy.RequiredCapability,
                StringComparer.Ordinal) == true;
        if (!gate.Passed || !capabilityGranted)
        {
            ExplorerCaptionGate.WriteJson(gate);
            throw new InvalidOperationException(
                "The exact profile does not grant the bounded single-" +
                "Explorer caption preview.");
        }
    }

    private void RollBackExactTarget(
        ExplorerCaptionSessionJournal journal)
    {
        journal.RollbackAttempted = true;
        if (!NativeExplorerCaptionTarget.TryValidateExact(
                journal.Target,
                out nint windowHandle,
                out string detail))
        {
            journal.State = "target-retired";
            journal.Result = "passed-target-retired-no-send";
            journal.Detail =
                $"{detail} No rollback call was sent.";
            store.Update(journal);
            return;
        }

        DwmCaptionWriter.SetDarkCaption(
            windowHandle,
            journal.OriginalValue != 0);
        DwmCaptionWriter.RequestNonClientRefresh(windowHandle);
        journal.RollbackNonClientRefreshRequested = true;
        DwmCaptionWriter.Flush();
        DwmCaptionObservation restored =
            DwmCaptionReader.Read(windowHandle);
        journal.LastObservedValue = restored.Value;
        journal.RollbackVerified =
            restored.Passed &&
            restored.Value == journal.OriginalValue;
        if (!journal.RollbackVerified)
        {
            throw new InvalidOperationException(
                "Rollback SET completed but GET did not verify the " +
                "original caption value.");
        }

        journal.State = "recovered";
        journal.Result = "passed-rollback-verified";
        journal.Detail =
            "Original Explorer caption value restored and verified.";
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
        Console.WriteLine(
            JsonSerializer.Serialize(value, SerializerOptions));
}
