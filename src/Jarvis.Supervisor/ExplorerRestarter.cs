using System.Diagnostics;

namespace Jarvis.Supervisor;

internal static class ExplorerRestarter
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AutomaticReturnWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShellReturnTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(200);
    private const int StableProbeCount = 3;

    public static async Task<ExplorerRestartResult> RestartCurrentSessionAsync(
        StateGateLease lease,
        KillSwitchGuard guard)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(guard);
        if (!lease.IsHeld || !guard.IsHeld)
        {
            throw new InvalidOperationException(
                "Explorer recovery requires the state gate and emergency-flag guard.");
        }

        string expectedExplorerPath = GetExpectedExplorerPath();
        using Process currentProcess = Process.GetCurrentProcess();
        int currentSessionId = currentProcess.SessionId;

        List<string> errors = [];
        List<int> stoppedProcessIds = [];
        int? targetedShellProcessId = null;
        bool terminationAttempted = false;
        bool startedBySupervisor = false;
        int? startedProcessId = null;

        ShellIdentity initialShell = WindowsShell.Probe(
            currentSessionId,
            expectedExplorerPath);
        if (initialShell.State == ShellIdentityState.Invalid)
        {
            errors.Add($"Initial shell wasn't trusted: {initialShell.Error}");
        }

        if (initialShell.IsVerified && initialShell.ProcessId is int initialProcessId)
        {
            try
            {
                // Verify the window ownership again immediately before obtaining the
                // process handle. No other explorer.exe process is ever targeted.
                ShellIdentity target = WindowsShell.Probe(
                    currentSessionId,
                    expectedExplorerPath);
                if (!target.IsVerified || target.ProcessId != initialProcessId)
                {
                    throw new InvalidOperationException(
                        "The verified shell changed before termination; no process was stopped.");
                }

                using Process shellProcess = Process.GetProcessById(initialProcessId);
                string? targetImagePath = shellProcess.MainModule?.FileName;
                if (shellProcess.HasExited ||
                    shellProcess.SessionId != currentSessionId ||
                    string.IsNullOrWhiteSpace(targetImagePath) ||
                    !WindowsShell.PathsEqual(targetImagePath, expectedExplorerPath))
                {
                    throw new InvalidOperationException(
                        "The shell process handle failed the final path/session verification.");
                }

                terminationAttempted = true;
                targetedShellProcessId = initialProcessId;
                if (!shellProcess.HasExited)
                {
                    shellProcess.Kill(entireProcessTree: false);
                }

                if (!shellProcess.HasExited)
                {
                    using CancellationTokenSource timeout = new(ExitTimeout);
                    try
                    {
                        await shellProcess.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new TimeoutException(
                            $"Shell process {initialProcessId} did not exit within {ExitTimeout.TotalSeconds:0} seconds.",
                            exception);
                    }
                }

                if (shellProcess.HasExited)
                {
                    stoppedProcessIds.Add(initialProcessId);
                }
            }
            catch (Exception exception)
            {
                // Recovery below is unconditional after a termination attempt. This
                // prevents a partial kill or wait timeout from stranding the desktop.
                errors.Add(
                    $"Shell termination failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        ShellWaitResult recoveredShell;
        try
        {
            recoveredShell = await WaitForStableShellAsync(
                currentSessionId,
                expectedExplorerPath,
                AutomaticReturnWindow);

            if (!recoveredShell.Identity.IsVerified)
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = expectedExplorerPath,
                    UseShellExecute = false,
                };
                string userProfile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    startInfo.WorkingDirectory = userProfile;
                }

                using Process? started = Process.Start(startInfo);
                if (started is null)
                {
                    throw new InvalidOperationException(
                        "Windows did not return a process after starting Explorer.");
                }

                startedBySupervisor = true;
                startedProcessId = started.Id;
                recoveredShell = await WaitForStableShellAsync(
                    currentSessionId,
                    expectedExplorerPath,
                    ShellReturnTimeout);
            }
        }
        catch (Exception exception)
        {
            errors.Add(
                $"Shell recovery failed: {exception.GetType().Name}: {exception.Message}");
            recoveredShell = new ShellWaitResult(
                ShellIdentity.Invalid(exception.Message),
                exception.Message);
        }

        bool recoverySucceeded = recoveredShell.Identity.IsVerified;
        if (!recoverySucceeded)
        {
            errors.Add(
                $"A stable, trusted desktop shell didn't return: {recoveredShell.LastError ?? recoveredShell.Identity.Error ?? "unknown error"}");
        }

        bool succeeded = recoverySucceeded && errors.Count == 0;
        string status = !recoverySucceeded
            ? "recovery_failed"
            : errors.Count != 0
                ? "recovered_with_errors"
                : terminationAttempted
                    ? "restarted"
                    : "started_or_already_running";

        KillSwitchProbe killSwitch = KillSwitch.Probe();
        if (!lease.IsHeld || !guard.IsHeld ||
            killSwitch.State != KillSwitchState.Armed)
        {
            errors.Add(
                "The emergency-flag guard wasn't intact at the end of recovery.");
        }

        succeeded = recoverySucceeded && errors.Count == 0;
        if (recoverySucceeded && errors.Count != 0)
        {
            status = "recovered_with_errors";
        }

        return new ExplorerRestartResult(
            status,
            succeeded,
            recoverySucceeded,
            currentSessionId,
            expectedExplorerPath,
            initialShell.State,
            initialShell.ProcessId,
            initialShell.Error,
            targetedShellProcessId,
            stoppedProcessIds,
            recoveredShell.Identity.ProcessId,
            startedBySupervisor,
            startedProcessId,
            KillSwitch.FlagPath,
            killSwitch.State,
            errors,
            DateTimeOffset.UtcNow);
    }

    private static async Task<ShellWaitResult> WaitForStableShellAsync(
        int currentSessionId,
        string expectedExplorerPath,
        TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        int? candidateProcessId = null;
        int consecutiveMatches = 0;
        ShellIdentity lastProbe = ShellIdentity.Absent("Shell inspection hasn't started.");

        do
        {
            lastProbe = WindowsShell.Probe(currentSessionId, expectedExplorerPath);
            if (lastProbe.IsVerified && lastProbe.ProcessId is int processId)
            {
                if (candidateProcessId == processId)
                {
                    consecutiveMatches++;
                }
                else
                {
                    candidateProcessId = processId;
                    consecutiveMatches = 1;
                }

                if (consecutiveMatches >= StableProbeCount)
                {
                    return new ShellWaitResult(lastProbe, null);
                }
            }
            else
            {
                candidateProcessId = null;
                consecutiveMatches = 0;
            }

            await Task.Delay(ProbeInterval);
        }
        while (Environment.TickCount64 < deadline);

        return new ShellWaitResult(lastProbe, lastProbe.Error);
    }

    private static string GetExpectedExplorerPath()
    {
        string windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            throw new InvalidOperationException(
                "Windows did not provide its installation directory.");
        }

        string explorerPath = Path.GetFullPath(
            Path.Combine(windowsDirectory, "explorer.exe"));
        try
        {
            FileAttributes attributes = File.GetAttributes(explorerPath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new FileNotFoundException(
                    "The Windows Explorer path was a directory.",
                    explorerPath);
            }
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new FileNotFoundException(
                "The Windows Explorer executable was not found.",
                explorerPath,
                exception);
        }

        return explorerPath;
    }

}

internal sealed record ShellWaitResult(
    ShellIdentity Identity,
    string? LastError);

internal sealed record ExplorerRestartResult(
    string Status,
    bool Succeeded,
    bool RecoverySucceeded,
    int SessionId,
    string ExplorerPath,
    ShellIdentityState InitialShellState,
    int? InitialShellProcessId,
    string? InitialShellError,
    int? TargetedShellProcessId,
    IReadOnlyList<int> StoppedProcessIds,
    int? ReplacementProcessId,
    bool StartedBySupervisor,
    int? StartedProcessId,
    string KillSwitchPath,
    KillSwitchState KillSwitchState,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);
