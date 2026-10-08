namespace Jarvis.Win10.OwnedTaskManager;

public sealed class TaskSnapshotSampler
{
    public const int DefaultMaximumProcessCount = 64;
    public const int AbsoluteMaximumProcessCount = 256;
    public const int MaximumProcessNameLength = 128;

    private readonly IProcessCounterSource _source;
    private readonly int _maximumProcessCount;
    private Dictionary<int, ProcessCounterSample> _previousByProcessId = [];
    private long _previousTimestamp;
    private long _previousFrequency;
    private bool _hasPreviousBatch;

    public TaskSnapshotSampler(
        IProcessCounterSource source,
        int maximumProcessCount = DefaultMaximumProcessCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumProcessCount is < 1 or > AbsoluteMaximumProcessCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumProcessCount),
                maximumProcessCount,
                $"Process limit must be between 1 and {AbsoluteMaximumProcessCount}.");
        }

        _source = source;
        _maximumProcessCount = maximumProcessCount;
    }

    public OwnedTaskSnapshot Capture()
    {
        ProcessCounterBatch batch = _source.Capture() ??
            throw new InvalidOperationException("Process source returned no batch.");
        if (batch.MonotonicFrequency <= 0 || batch.ProcessorCount <= 0)
        {
            throw new InvalidOperationException(
                "Process source returned an invalid clock or processor count.");
        }

        double elapsedSeconds =
            _hasPreviousBatch &&
            batch.MonotonicFrequency == _previousFrequency &&
            batch.MonotonicTimestamp > _previousTimestamp
                ? (batch.MonotonicTimestamp - _previousTimestamp) /
                    (double)batch.MonotonicFrequency
                : 0.0;

        Dictionary<int, ProcessCounterSample> currentByProcessId = [];
        List<OwnedTaskProcessSnapshot> rows = [];
        foreach (ProcessCounterSample current in batch.Processes)
        {
            if (!IsValid(current) ||
                !currentByProcessId.TryAdd(current.ProcessId, current))
            {
                continue;
            }

            bool measured = false;
            double cpuPercent = 0.0;
            if (elapsedSeconds > 0.0 &&
                _previousByProcessId.TryGetValue(
                    current.ProcessId,
                    out ProcessCounterSample? previous) &&
                previous.StartTimeUtcTicks == current.StartTimeUtcTicks &&
                current.TotalProcessorTimeTicks >=
                    previous.TotalProcessorTimeTicks)
            {
                long processorDeltaTicks =
                    current.TotalProcessorTimeTicks -
                    previous.TotalProcessorTimeTicks;
                double processorSeconds =
                    processorDeltaTicks / (double)TimeSpan.TicksPerSecond;
                cpuPercent = Math.Clamp(
                    processorSeconds /
                        elapsedSeconds /
                        batch.ProcessorCount * 100.0,
                    0.0,
                    100.0);
                measured = true;
            }

            rows.Add(new OwnedTaskProcessSnapshot(
                NormalizeName(current.ProcessName),
                current.ProcessId,
                Math.Round(cpuPercent, 2, MidpointRounding.AwayFromZero),
                measured,
                current.WorkingSetBytes,
                current.ThreadCount)
            {
                StartTimeUtcTicks = current.StartTimeUtcTicks,
            });
        }

        _previousByProcessId = currentByProcessId;
        _previousTimestamp = batch.MonotonicTimestamp;
        _previousFrequency = batch.MonotonicFrequency;
        _hasPreviousBatch = true;

        OwnedTaskProcessSnapshot[] ordered = rows
            .OrderByDescending(row => row.CpuPercent)
            .ThenByDescending(row => row.WorkingSetBytes)
            .ThenBy(row => row.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProcessName, StringComparer.Ordinal)
            .ThenBy(row => row.ProcessId)
            .Take(_maximumProcessCount)
            .ToArray();

        return new OwnedTaskSnapshot(
            1,
            "captured-read-only-process-snapshot",
            batch.CapturedAtUtc,
            batch.ProcessorCount,
            Math.Max(batch.EnumeratedProcessCount, 0),
            Math.Max(batch.SkippedProcessCount, 0),
            ordered.Length,
            _maximumProcessCount,
            Math.Max(0, rows.Count - ordered.Length),
            true,
            false,
            false,
            false,
            ordered);
    }

    public void ResetBaseline()
    {
        _previousByProcessId = [];
        _previousTimestamp = 0;
        _previousFrequency = 0;
        _hasPreviousBatch = false;
    }

    private static bool IsValid(ProcessCounterSample sample) =>
        sample.ProcessId > 0 &&
        !string.IsNullOrWhiteSpace(sample.ProcessName) &&
        sample.StartTimeUtcTicks > 0 &&
        sample.TotalProcessorTimeTicks >= 0 &&
        sample.WorkingSetBytes >= 0 &&
        sample.ThreadCount >= 0;

    private static string NormalizeName(string processName)
    {
        string normalized = processName.Trim();
        return normalized.Length <= MaximumProcessNameLength
            ? normalized
            : normalized[..MaximumProcessNameLength];
    }
}
