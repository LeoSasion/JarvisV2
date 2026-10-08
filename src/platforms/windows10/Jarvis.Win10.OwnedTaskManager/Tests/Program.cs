using System.Text.Json;
using Jarvis.Win10.OwnedTaskManager;

List<ContractScenario> scenarios = [];
Add("external-first-frame-baseline", FirstFrame);
Add("external-cpu-delta", CpuDelta);
Add("external-pid-reuse", PidReuse);
Add("external-read-failure-tolerance", ReadFailureTolerance);
Add("external-stable-sort", StableSort);
Add("external-hard-limit", HardLimit);

int passed = scenarios.Count(scenario => scenario.Passed);
ContractTestReceipt receipt = new(
    1,
    passed == scenarios.Count ? "passed" : "failed",
    scenarios.Count,
    passed,
    scenarios);
Console.WriteLine(JsonSerializer.Serialize(
    receipt,
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    }));
return receipt.Result == "passed" ? 0 : 1;

bool FirstFrame()
{
    TaskSnapshotSampler sampler = new(
        new ExternalSource(
            Batch(1_000, 4, Sample(7, "alpha", 100, 8.0, 64)),
            Batch(2_000, 4, Sample(7, "alpha", 100, 9.0, 64)),
            Batch(62_000, 4, Sample(7, "alpha", 100, 29.0, 64)),
            Batch(63_000, 4, Sample(7, "alpha", 100, 30.0, 64))));
    OwnedTaskProcessSnapshot first = sampler.Capture().Processes.Single();
    OwnedTaskProcessSnapshot measured = sampler.Capture().Processes.Single();
    sampler.ResetBaseline();
    OwnedTaskProcessSnapshot restoredFirst =
        sampler.Capture().Processes.Single();
    OwnedTaskProcessSnapshot restoredMeasured =
        sampler.Capture().Processes.Single();
    return first.CpuPercent == 0.0 &&
        !first.CpuPercentMeasured &&
        measured.CpuPercentMeasured &&
        measured.CpuPercent == 25.0 &&
        restoredFirst.CpuPercent == 0.0 &&
        !restoredFirst.CpuPercentMeasured &&
        restoredMeasured.CpuPercentMeasured &&
        restoredMeasured.CpuPercent == 25.0;
}

bool CpuDelta()
{
    TaskSnapshotSampler sampler = new(
        new ExternalSource(
            Batch(1_000, 4, Sample(7, "alpha", 100, 8.0, 64)),
            Batch(2_000, 4, Sample(7, "alpha", 100, 8.5, 64))));
    _ = sampler.Capture();
    OwnedTaskProcessSnapshot row = sampler.Capture().Processes.Single();
    return row.CpuPercentMeasured && row.CpuPercent == 12.5;
}

bool PidReuse()
{
    TaskSnapshotSampler sampler = new(
        new ExternalSource(
            Batch(1_000, 2, Sample(42, "old", 100, 1.0, 32)),
            Batch(2_000, 2, Sample(42, "new", 200, 9.0, 48))));
    _ = sampler.Capture();
    OwnedTaskProcessSnapshot row = sampler.Capture().Processes.Single();
    return row.ProcessName == "new" &&
        row.CpuPercent == 0.0 &&
        !row.CpuPercentMeasured;
}

bool ReadFailureTolerance()
{
    FakeProbe[] probes =
    [
        new(Sample(5, "readable", 100, 1.0, 64)),
        new(new UnauthorizedAccessException("external-access-denied")),
        new(new SystemException("external-process-exited")),
    ];
    WindowsProcessSnapshotSource source = new(
        new FakeCatalog(probes),
        () => DateTimeOffset.UnixEpoch,
        () => 1_000,
        1_000,
        2);
    ProcessCounterBatch batch = source.Capture();
    return batch.EnumeratedProcessCount == 3 &&
        batch.SkippedProcessCount == 2 &&
        batch.Processes.Single().ProcessId == 5 &&
        probes.All(probe => probe.Disposed);
}

bool StableSort()
{
    ProcessCounterSample[] first =
    [
        Sample(9, "zeta", 90, 2.0, 100),
        Sample(4, "beta", 40, 2.0, 200),
        Sample(3, "Alpha", 30, 2.0, 200),
        Sample(2, "alpha", 20, 2.0, 200),
    ];
    ProcessCounterSample[] second =
    [
        Sample(9, "zeta", 90, 2.8, 100),
        Sample(4, "beta", 40, 2.4, 200),
        Sample(3, "Alpha", 30, 2.4, 200),
        Sample(2, "alpha", 20, 2.4, 200),
    ];
    TaskSnapshotSampler sampler = new(
        new ExternalSource(
            Batch(1_000, 2, first),
            Batch(2_000, 2, second),
            Batch(3_000, 2, second)));
    _ = sampler.Capture();
    int[] measured = sampler.Capture().Processes
        .Select(row => row.ProcessId)
        .ToArray();
    int[] replay = sampler.Capture().Processes
        .Select(row => row.ProcessId)
        .ToArray();
    return measured.SequenceEqual([9, 3, 2, 4]) &&
        replay.SequenceEqual([3, 2, 4, 9]);
}

bool HardLimit()
{
    ProcessCounterSample[] samples = Enumerable.Range(1, 7)
        .Select(index =>
            Sample(index, $"process-{index}", 100 + index, index, index * 100))
        .ToArray();
    OwnedTaskSnapshot frame = new TaskSnapshotSampler(
        new ExternalSource(Batch(1_000, 4, samples)),
        3).Capture();
    return frame.ProcessCount == 3 &&
        frame.Processes.Count == 3 &&
        frame.MaximumProcessCount == 3 &&
        frame.TruncatedProcessCount == 4;
}

void Add(string name, Func<bool> execute)
{
    try
    {
        bool passed = execute();
        scenarios.Add(new(
            name,
            passed,
            passed ? "passed" : "predicate-returned-false"));
    }
    catch (Exception exception)
    {
        scenarios.Add(new(
            name,
            false,
            $"{exception.GetType().Name}: {exception.Message}"));
    }
}

static ProcessCounterBatch Batch(
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

static ProcessCounterSample Sample(
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

internal sealed record ContractScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record ContractTestReceipt(
    int SchemaVersion,
    string Result,
    int ScenarioCount,
    int PassedCount,
    IReadOnlyList<ContractScenario> Scenarios);

internal sealed class ExternalSource(params ProcessCounterBatch[] batches)
    : IProcessCounterSource
{
    private readonly Queue<ProcessCounterBatch> _batches = new(batches);

    public ProcessCounterBatch Capture() => _batches.Dequeue();
}

internal sealed class FakeCatalog(IReadOnlyList<FakeProbe> probes)
    : IProcessCounterCatalog
{
    public IReadOnlyList<IProcessCounterProbe> Enumerate() => probes;
}

internal sealed class FakeProbe : IProcessCounterProbe
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

        return _sample ?? throw new InvalidOperationException("sample-absent");
    }

    public void Dispose() => Disposed = true;
}
