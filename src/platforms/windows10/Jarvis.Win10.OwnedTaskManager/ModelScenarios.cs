namespace Jarvis.Win10.OwnedTaskManager;

internal static class ModelScenarios
{
    public static OwnedTaskManagerTestReceipt Run()
    {
        List<OwnedTaskManagerScenario> scenarios = [];
        Add(scenarios, "first-frame-establishes-cpu-baseline", FirstFrame);
        Add(scenarios, "cpu-delta-is-normalized-by-core-count", CpuDelta);
        Add(scenarios, "pid-reuse-requires-a-new-baseline", PidReuse);
        Add(
            scenarios,
            "access-denied-and-process-exit-are-skipped",
            ExpectedReadFailures);
        Add(scenarios, "sort-order-is-stable-and-deterministic", StableSort);
        Add(scenarios, "result-count-is-strictly-bounded", BoundedResult);

        int passed = scenarios.Count(scenario => scenario.Passed);
        return new OwnedTaskManagerTestReceipt(
            1,
            passed == scenarios.Count ? "passed" : "failed",
            scenarios.Count,
            passed,
            true,
            true,
            false,
            false,
            false,
            false,
            "not-run",
            false,
            scenarios);
    }

    private static bool FirstFrame()
    {
        QueueProcessCounterSource source = new(
            Batch(
                timestamp: 1_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 8, workingSet: 64)),
            Batch(
                timestamp: 2_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 9, workingSet: 64)),
            Batch(
                timestamp: 62_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 29, workingSet: 64)),
            Batch(
                timestamp: 63_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 30, workingSet: 64)));
        TaskSnapshotSampler sampler = new(source);
        OwnedTaskSnapshot firstFrame = sampler.Capture();
        OwnedTaskProcessSnapshot firstRow = firstFrame.Processes.Single();
        OwnedTaskProcessSnapshot measuredRow =
            sampler.Capture().Processes.Single();
        sampler.ResetBaseline();
        OwnedTaskProcessSnapshot restoredFirstRow =
            sampler.Capture().Processes.Single();
        OwnedTaskProcessSnapshot restoredMeasuredRow =
            sampler.Capture().Processes.Single();
        return firstRow.ProcessId == 7 &&
            firstRow.CpuPercent == 0.0 &&
            !firstRow.CpuPercentMeasured &&
            measuredRow.CpuPercentMeasured &&
            measuredRow.CpuPercent == 25.0 &&
            restoredFirstRow.CpuPercent == 0.0 &&
            !restoredFirstRow.CpuPercentMeasured &&
            restoredMeasuredRow.CpuPercentMeasured &&
            restoredMeasuredRow.CpuPercent == 25.0 &&
            firstFrame.ReadOnlyProcessTelemetry &&
            !firstFrame.ProcessControlSupported &&
            !firstFrame.WindowControlSupported &&
            !firstFrame.InjectionSupported;
    }

    private static bool CpuDelta()
    {
        QueueProcessCounterSource source = new(
            Batch(
                timestamp: 1_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 8.0, workingSet: 64)),
            Batch(
                timestamp: 2_000,
                processors: 4,
                Sample(7, "alpha", 100, cpuSeconds: 8.5, workingSet: 64)));
        TaskSnapshotSampler sampler = new(source);
        _ = sampler.Capture();
        OwnedTaskProcessSnapshot row = sampler.Capture().Processes.Single();
        return row.CpuPercentMeasured && row.CpuPercent == 12.5;
    }

    private static bool PidReuse()
    {
        QueueProcessCounterSource source = new(
            Batch(
                timestamp: 1_000,
                processors: 2,
                Sample(42, "old", 100, cpuSeconds: 1.0, workingSet: 32)),
            Batch(
                timestamp: 2_000,
                processors: 2,
                Sample(42, "new", 200, cpuSeconds: 9.0, workingSet: 48)));
        TaskSnapshotSampler sampler = new(source);
        _ = sampler.Capture();
        OwnedTaskProcessSnapshot row = sampler.Capture().Processes.Single();
        return row.ProcessName == "new" &&
            row.CpuPercent == 0.0 &&
            !row.CpuPercentMeasured;
    }

    private static bool ExpectedReadFailures()
    {
        IProcessCounterProbe[] probes =
        [
            new FakeProbe(
                Sample(5, "readable", 100, cpuSeconds: 1.0, workingSet: 64)),
            new FakeProbe(new UnauthorizedAccessException("synthetic")),
            new FakeProbe(new InvalidOperationException("synthetic")),
            new FakeProbe(new SystemException("synthetic-process-exited")),
        ];
        WindowsProcessSnapshotSource source = new(
            new FakeCatalog(probes),
            () => DateTimeOffset.UnixEpoch,
            () => 1_000,
            1_000,
            2);
        ProcessCounterBatch batch = source.Capture();
        return batch.EnumeratedProcessCount == 4 &&
            batch.SkippedProcessCount == 3 &&
            batch.Processes.Count == 1 &&
            batch.Processes[0].ProcessId == 5 &&
            probes.All(probe => ((FakeProbe)probe).Disposed);
    }

    private static bool StableSort()
    {
        ProcessCounterSample[] first =
        [
            Sample(9, "zeta", 90, cpuSeconds: 2.0, workingSet: 100),
            Sample(4, "beta", 40, cpuSeconds: 2.0, workingSet: 200),
            Sample(3, "Alpha", 30, cpuSeconds: 2.0, workingSet: 200),
            Sample(2, "alpha", 20, cpuSeconds: 2.0, workingSet: 200),
        ];
        ProcessCounterSample[] second =
        [
            Sample(9, "zeta", 90, cpuSeconds: 2.8, workingSet: 100),
            Sample(4, "beta", 40, cpuSeconds: 2.4, workingSet: 200),
            Sample(3, "Alpha", 30, cpuSeconds: 2.4, workingSet: 200),
            Sample(2, "alpha", 20, cpuSeconds: 2.4, workingSet: 200),
        ];
        QueueProcessCounterSource source = new(
            Batch(1_000, 2, first),
            Batch(2_000, 2, second),
            Batch(3_000, 2, second));
        TaskSnapshotSampler sampler = new(source);
        _ = sampler.Capture();
        int[] measuredOrder = sampler.Capture().Processes
            .Select(row => row.ProcessId)
            .ToArray();
        int[] replayOrder = sampler.Capture().Processes
            .Select(row => row.ProcessId)
            .ToArray();
        return measuredOrder.SequenceEqual([9, 3, 2, 4]) &&
            replayOrder.SequenceEqual([3, 2, 4, 9]);
    }

    private static bool BoundedResult()
    {
        ProcessCounterSample[] samples = Enumerable.Range(1, 7)
            .Select(index =>
                Sample(
                    index,
                    $"process-{index}",
                    100 + index,
                    cpuSeconds: index,
                    workingSet: index * 100))
            .ToArray();
        TaskSnapshotSampler sampler = new(
            new QueueProcessCounterSource(Batch(1_000, 4, samples)),
            maximumProcessCount: 3);
        OwnedTaskSnapshot frame = sampler.Capture();
        return frame.ProcessCount == 3 &&
            frame.Processes.Count == 3 &&
            frame.MaximumProcessCount == 3 &&
            frame.TruncatedProcessCount == 4;
    }

    private static ProcessCounterBatch Batch(
        long timestamp,
        int processors,
        params ProcessCounterSample[] samples) =>
        new(
            DateTimeOffset.UnixEpoch.AddMilliseconds(timestamp),
            timestamp,
            1_000,
            processors,
            samples.Length,
            0,
            samples);

    private static ProcessCounterSample Sample(
        int processId,
        string name,
        long startTicks,
        double cpuSeconds,
        long workingSet) =>
        new(
            processId,
            name,
            startTicks,
            checked((long)(cpuSeconds * TimeSpan.TicksPerSecond)),
            workingSet,
            Math.Max(processId % 17, 1));

    private static void Add(
        ICollection<OwnedTaskManagerScenario> scenarios,
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

        scenarios.Add(new OwnedTaskManagerScenario(name, passed, detail));
    }

    private sealed class QueueProcessCounterSource(
        params ProcessCounterBatch[] batches) : IProcessCounterSource
    {
        private readonly Queue<ProcessCounterBatch> _batches = new(batches);

        public ProcessCounterBatch Capture() =>
            _batches.Count > 0
                ? _batches.Dequeue()
                : throw new InvalidOperationException("No synthetic batch remains.");
    }

    private sealed class FakeCatalog(IReadOnlyList<IProcessCounterProbe> probes)
        : IProcessCounterCatalog
    {
        public IReadOnlyList<IProcessCounterProbe> Enumerate() => probes;
    }

    private sealed class FakeProbe : IProcessCounterProbe
    {
        private readonly ProcessCounterSample? _sample;
        private readonly Exception? _exception;

        public FakeProbe(ProcessCounterSample sample)
        {
            _sample = sample;
        }

        public FakeProbe(Exception exception)
        {
            _exception = exception;
        }

        public bool Disposed { get; private set; }

        public ProcessCounterSample Read()
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return _sample ??
                throw new InvalidOperationException("Synthetic sample is absent.");
        }

        public void Dispose() => Disposed = true;
    }
}
