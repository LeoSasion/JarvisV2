using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Jarvis.Win10.OwnedTaskManager;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record TaskManagerSurfaceScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record TaskManagerSurfaceTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool OwnProcessOnly,
    bool ReadOnlyProcessTelemetry,
    bool ProcessControlSupported,
    bool SamplingStopsWhenHidden,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<TaskManagerSurfaceScenario> Scenarios);

internal static class TaskManagerSurfaceScenarios
{
    public static TaskManagerSurfaceTestReceipt Run()
    {
        List<TaskManagerSurfaceScenario> scenarios = [];
        Add(scenarios, "initial-surface-is-paused-and-empty", InitialState);
        Add(scenarios, "snapshot-renders-a-readable-process-ledger", SnapshotLedger);
        Add(scenarios, "measured-frame-updates-aggregate-telemetry", MeasuredFrame);
        Add(scenarios, "surface-sampling-starts-and-stops-explicitly", SamplingLifecycle);
        Add(scenarios, "system-button-owns-complete-window-lifecycle", OwnedWindowLifecycle);
        Add(scenarios, "deterministic-preview-is-labelled-and-non-live", DeterministicPreview);
        Add(scenarios, "same-pid-and-name-restart-replaces-row-and-selection", ProcessInstanceIdentity);
        Add(scenarios, "rapid-stop-start-refreshes-after-retired-capture", RapidSamplingRestart);
        Add(scenarios, "stop-after-read-fault-clears-retrying-copy", StopAfterReadFault);

        int passed = scenarios.Count(scenario => scenario.Passed);
        return new TaskManagerSurfaceTestReceipt(
            1,
            "jarvisv2-win10-owned-task-manager-surface-test",
            passed == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passed,
            true,
            true,
            false,
            true,
            "not-run",
            false,
            scenarios);
    }

    private static bool InitialState()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            Batch(1_000, Sample(7, "alpha", 100, 1.0, 32 * MiB, 4)));
        TaskManagerSurface surface = hosted.Surface;
        return
            !surface.IsSamplingActiveForTest &&
            surface.SamplingStateForTest == "PAUSED" &&
            surface.ProcessRows.Count == 0 &&
            surface.EmptyStatePanel.Visibility == Visibility.Visible &&
            AutomationProperties.GetName(surface.ProcessList) ==
                "Read-only process telemetry";
    }

    private static bool SnapshotLedger()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            Batch(
                1_000,
                Sample(7, "alpha", 100, 1.0, 320 * MiB, 14),
                Sample(9, "beta", 200, 2.0, 96 * MiB, 8)),
            Batch(
                2_000,
                Sample(7, "alpha", 100, 1.4, 321 * MiB, 15),
                Sample(9, "beta", 200, 2.1, 98 * MiB, 9)));
        TaskManagerSurface surface = hosted.Surface;
        surface.RefreshSynchronouslyForTest();
        TaskManagerProcessRow first = surface.ProcessRows[0];
        bool firstFrameRendered =
            surface.ProcessRows.Count == 2 &&
            surface.ProcessList.Items.Count == 2 &&
            first.ProcessName == "ALPHA" &&
            first.ProcessIdText == "7" &&
            first.CpuPercentText == "LEARNING" &&
            first.WorkingSetText == "320.0 MB" &&
            first.ThreadCountText == "14" &&
            surface.CpuSummaryForTest == "LEARNING" &&
            surface.MemorySummaryForTest == "416.0 MB" &&
            surface.EmptyStatePanel.Visibility == Visibility.Collapsed &&
            AutomationProperties.GetName(surface.ProcessList).Contains(
                "2 visible rows",
                StringComparison.Ordinal);
        surface.ProcessList.SelectedItem = first;
        surface.RefreshSynchronouslyForTest();
        return
            firstFrameRendered &&
            ReferenceEquals(surface.ProcessRows[0], first) &&
            ReferenceEquals(surface.ProcessList.SelectedItem, first) &&
            first.CpuPercentText == "10.00 %" &&
            first.WorkingSetText == "321.0 MB" &&
            first.ThreadCountText == "15";
    }

    private static bool MeasuredFrame()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            Batch(
                1_000,
                Sample(7, "alpha", 100, 1.0, 320 * MiB, 14),
                Sample(9, "beta", 200, 2.0, 96 * MiB, 8)),
            Batch(
                2_000,
                Sample(7, "alpha", 100, 1.8, 321 * MiB, 15),
                Sample(9, "beta", 200, 2.2, 98 * MiB, 9)));
        TaskManagerSurface surface = hosted.Surface;
        surface.RefreshSynchronouslyForTest();
        surface.RefreshSynchronouslyForTest();
        return
            surface.ProcessRows.Select(row => row.ProcessName)
                .SequenceEqual(["ALPHA", "BETA"]) &&
            surface.ProcessRows[0].CpuPercentText == "20.00 %" &&
            surface.ProcessRows[1].CpuPercentText == "5.00 %" &&
            surface.CpuSummaryForTest == "25.0 %" &&
            surface.ReadStatusForTest == "ALL ENUMERATED PROCESSES READ";
    }

    private static bool SamplingLifecycle()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            Batch(1_000, Sample(7, "alpha", 100, 1.0, 32 * MiB, 4)),
            Batch(2_000, Sample(7, "alpha", 100, 2.0, 32 * MiB, 4)),
            Batch(62_000, Sample(7, "alpha", 100, 32.0, 32 * MiB, 4)),
            Batch(63_000, Sample(7, "alpha", 100, 33.0, 32 * MiB, 4)));
        TaskManagerSurface surface = hosted.Surface;
        surface.StartSamplingWithoutRefreshForTest();
        bool started =
            surface.IsSamplingActiveForTest &&
            surface.SamplingStateForTest == "SAMPLING";
        surface.RefreshSynchronouslyForTest();
        bool firstFrameLearning =
            surface.ProcessRows.Single().CpuPercentText == "LEARNING";
        surface.RefreshSynchronouslyForTest();
        bool measuredBeforePause =
            surface.ProcessRows.Single().CpuPercentText == "25.00 %";
        surface.StopSampling();
        bool stopped =
            started &&
            !surface.IsSamplingActiveForTest &&
            surface.SamplingStateForTest == "PAUSED";
        surface.StartSamplingWithoutRefreshForTest();
        surface.RefreshSynchronouslyForTest();
        bool restoredFirstFrameLearning =
            surface.ProcessRows.Single().CpuPercentText == "LEARNING";
        surface.RefreshSynchronouslyForTest();
        bool measuredAfterRestore =
            surface.ProcessRows.Single().CpuPercentText == "25.00 %";
        surface.StopSampling();

        using HostedTaskManager delayedHost = CreateHostedTaskManager(
            new ScriptedSource(
                () => throw new InvalidOperationException("synthetic-delay"),
                () => Batch(
                    1_000,
                    Sample(9, "recovered", 200, 1.0, 48 * MiB, 6))));
        TaskManagerSurface delayed = delayedHost.Surface;
        delayed.StartSamplingWithoutRefreshForTest();
        delayed.RefreshSynchronouslyForTest();
        bool delayedButRetrying =
            delayed.IsSamplingActiveForTest &&
            delayed.SamplingStateForTest == "READ DELAYED / RETRYING" &&
            delayed.ReadStatusForTest.EndsWith(
                "/ RETRYING",
                StringComparison.Ordinal) &&
            AutomationProperties.GetName(delayed.SamplingStateText) ==
                "Task Manager state: read delayed; retrying";
        delayed.RefreshSynchronouslyForTest();
        bool recovered =
            delayed.IsSamplingActiveForTest &&
            delayed.SamplingStateForTest == "SAMPLING" &&
            delayed.ProcessRows.Single().ProcessName == "RECOVERED" &&
            AutomationProperties.GetName(delayed.ReadStatusText) ==
                "ALL ENUMERATED PROCESSES READ";
        delayed.StopSampling();

        return stopped && firstFrameLearning && measuredBeforePause &&
            restoredFirstFrameLearning && measuredAfterRestore &&
            delayedButRetrying && recovered;
    }

    private static bool OwnedWindowLifecycle()
    {
        DesktopShellSurface shell = new();
        Window host = CreateHost(shell);
        try
        {
            host.Show();
            PumpDispatcher();
            bool initiallyClosed =
                !shell.IsTaskManagerVisibleForTest &&
                !shell.IsTaskManagerRunningForTest &&
                !shell.IsTaskManagerSamplingForTest;

            Invoke(shell.TaskbarSystemButton);
            bool opened =
                shell.IsTaskManagerVisibleForTest &&
                shell.IsTaskManagerRunningForTest &&
                shell.IsTaskManagerSamplingForTest &&
                shell.TaskManagerIndicatorOpacityForTest == 1.0;

            host.WindowState = WindowState.Minimized;
            PumpDispatcher();
            bool outerHostPaused = !shell.IsTaskManagerSamplingForTest;
            host.WindowState = WindowState.Normal;
            PumpDispatcher();
            bool outerHostResumed = shell.IsTaskManagerSamplingForTest;

            Invoke(shell.TaskManagerMinimizeButton);
            bool minimized =
                !shell.IsTaskManagerVisibleForTest &&
                shell.IsTaskManagerRunningForTest &&
                !shell.IsTaskManagerSamplingForTest &&
                shell.TaskManagerIndicatorOpacityForTest == 0.45;

            Invoke(shell.TaskbarSystemButton);
            bool restored =
                shell.IsTaskManagerVisibleForTest &&
                shell.IsTaskManagerRunningForTest &&
                shell.IsTaskManagerSamplingForTest;

            Invoke(shell.TaskManagerCloseButton);
            bool closed =
                !shell.IsTaskManagerVisibleForTest &&
                !shell.IsTaskManagerRunningForTest &&
                !shell.IsTaskManagerSamplingForTest;
            return initiallyClosed && opened && outerHostPaused &&
                outerHostResumed && minimized && restored && closed;
        }
        finally
        {
            shell.TaskManagerPanel.StopSampling();
            host.Content = null;
            host.Close();
        }
    }

    private static bool DeterministicPreview()
    {
        DesktopShellSurface shell = new();
        Window host = CreateHost(shell);
        try
        {
            host.Show();
            PumpDispatcher();
            bool prepared = shell.PrepareTaskManagerForSnapshot();
            shell.UpdateLayout();
            return
                prepared &&
                shell.IsTaskManagerVisibleForTest &&
                shell.IsTaskManagerRunningForTest &&
                !shell.IsTaskManagerSamplingForTest &&
                shell.TaskManagerPanel.ProcessRows.Count == 8 &&
                shell.TaskManagerPanel.ProcessList.Items.Count == 8 &&
                shell.TaskManagerPanel.SamplingStateForTest ==
                    "DETERMINISTIC PREVIEW" &&
                shell.TaskManagerPanel.ProcessRows[0].ProcessName == "JARVIS2";
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static bool ProcessInstanceIdentity()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            Batch(1_000, Sample(7, "alpha", 100, 1.0, 32 * MiB, 4)),
            Batch(2_000, Sample(7, "alpha", 200, 2.0, 48 * MiB, 6)));
        TaskManagerSurface surface = hosted.Surface;
        surface.RefreshSynchronouslyForTest();
        TaskManagerProcessRow old = surface.ProcessRows.Single();
        surface.ProcessList.SelectedItem = old;
        surface.RefreshSynchronouslyForTest();
        TaskManagerProcessRow restarted = surface.ProcessRows.Single();
        return !ReferenceEquals(old, restarted) &&
            surface.ProcessList.SelectedItem is null &&
            restarted.StartTimeUtcTicks == 200 &&
            restarted.CpuPercentText == "LEARNING";
    }

    private static bool RapidSamplingRestart()
    {
        BlockingSource source = new();
        using HostedTaskManager hosted = CreateHostedTaskManager(source);
        TaskManagerSurface surface = hosted.Surface;
        try
        {
            surface.StartSampling();
            if (!PumpUntil(() => source.Started.Task.IsCompleted))
            {
                return false;
            }

            surface.StopSampling();
            surface.StartSampling();
            source.Release.TrySetResult(true);
            bool refreshed = PumpUntil(() => surface.ProcessRows.Count == 1);
            return refreshed && source.CaptureCount == 2 &&
                surface.ProcessRows.Single().ProcessName == "CURRENT" &&
                surface.ProcessRows.Single().CpuPercentText == "LEARNING" &&
                surface.SamplingStateForTest == "SAMPLING";
        }
        finally
        {
            source.Release.TrySetResult(true);
            surface.StopSampling();
        }
    }

    private static bool StopAfterReadFault()
    {
        using HostedTaskManager hosted = CreateHostedTaskManager(
            new ScriptedSource(() => throw new InvalidOperationException("synthetic-delay")));
        TaskManagerSurface surface = hosted.Surface;
        surface.StartSamplingWithoutRefreshForTest();
        surface.RefreshSynchronouslyForTest();
        surface.StopSampling();
        return surface.SamplingStateForTest == "PAUSED" &&
            !surface.ReadStatusForTest.Contains("RETRYING", StringComparison.Ordinal) &&
            AutomationProperties.GetName(surface.ReadStatusText) == surface.ReadStatusForTest;
    }

    private static bool PumpUntil(Func<bool> completed)
    {
        DispatcherFrame frame = new();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        DispatcherTimer timer = new(TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = !completed() && DateTime.UtcNow < deadline,
            Dispatcher.CurrentDispatcher);
        try
        {
            Dispatcher.PushFrame(frame);
            return completed();
        }
        finally
        {
            timer.Stop();
        }
    }

    private sealed class BlockingSource : IProcessCounterSource
    {
        private int _captures;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CaptureCount => Volatile.Read(ref _captures);

        public ProcessCounterBatch Capture()
        {
            int capture = Interlocked.Increment(ref _captures);
            if (capture == 1)
            {
                Started.TrySetResult(true);
                _ = Release.Task.GetAwaiter().GetResult();
            }

            return Batch(capture * 1_000, Sample(7, capture == 1 ? "retired" : "current",
                100, capture, 32 * MiB, 4));
        }
    }

    private static HostedTaskManager CreateHostedTaskManager(
        params ProcessCounterBatch[] batches)
        => CreateHostedTaskManager(new QueueSource(batches));

    private static HostedTaskManager CreateHostedTaskManager(
        IProcessCounterSource source)
    {
        TaskManagerSurface surface = new(
            new TaskSnapshotSampler(source),
            TimeSpan.FromHours(1));
        Window host = CreateHost(surface);
        host.Show();
        PumpDispatcher();
        return new HostedTaskManager(surface, host);
    }

    private static Window CreateHost(UIElement content) =>
        new()
        {
            Width = 760,
            Height = 600,
            Left = -32000,
            Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Background = Brushes.Black,
            Content = content,
        };

    private static ProcessCounterBatch Batch(
        long timestamp,
        params ProcessCounterSample[] samples) =>
        new(
            DateTimeOffset.UnixEpoch.AddMilliseconds(timestamp),
            timestamp,
            1_000,
            4,
            samples.Length,
            0,
            samples);

    private static ProcessCounterSample Sample(
        int processId,
        string name,
        long startTicks,
        double cpuSeconds,
        long workingSet,
        int threads) =>
        new(
            processId,
            name,
            startTicks,
            checked((long)(cpuSeconds * TimeSpan.TicksPerSecond)),
            workingSet,
            threads);

    private static void Invoke(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void PumpDispatcher()
    {
        DispatcherFrame frame = new();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Add(
        ICollection<TaskManagerSurfaceScenario> scenarios,
        string name,
        Func<bool> execute)
    {
        bool passed;
        string detail;
        try
        {
            passed = execute();
            detail = passed ? "passed" : "predicate-returned-false";
        }
        catch (Exception exception)
        {
            passed = false;
            detail = $"{exception.GetType().Name}: {exception.Message}";
        }

        scenarios.Add(new TaskManagerSurfaceScenario(name, passed, detail));
    }

    private sealed class QueueSource(IEnumerable<ProcessCounterBatch> batches)
        : IProcessCounterSource
    {
        private readonly Queue<ProcessCounterBatch> _batches = new(batches);

        public ProcessCounterBatch Capture() =>
            _batches.Count > 0
                ? _batches.Dequeue()
                : throw new InvalidOperationException("No test batch remains.");
    }

    private sealed class ScriptedSource(
        params Func<ProcessCounterBatch>[] captures) : IProcessCounterSource
    {
        private readonly Queue<Func<ProcessCounterBatch>> _captures =
            new(captures);

        public ProcessCounterBatch Capture() =>
            _captures.Count > 0
                ? _captures.Dequeue()()
                : throw new InvalidOperationException("No test capture remains.");
    }

    private sealed class HostedTaskManager(
        TaskManagerSurface surface,
        Window host) : IDisposable
    {
        public TaskManagerSurface Surface { get; } = surface;

        public void Dispose()
        {
            Surface.StopSampling();
            host.Content = null;
            host.Close();
        }
    }

    private const long MiB = 1024L * 1024L;
}
