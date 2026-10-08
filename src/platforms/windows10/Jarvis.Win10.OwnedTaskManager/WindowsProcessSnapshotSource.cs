using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Win10.OwnedTaskManager;

public sealed class WindowsProcessSnapshotSource : IProcessCounterSource
{
    private readonly IProcessCounterCatalog _catalog;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<long> _timestamp;
    private readonly long _timestampFrequency;
    private readonly int _processorCount;

    public WindowsProcessSnapshotSource()
        : this(
            new WindowsProcessCounterCatalog(),
            () => DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency,
            Environment.ProcessorCount)
    {
    }

    internal WindowsProcessSnapshotSource(
        IProcessCounterCatalog catalog,
        Func<DateTimeOffset> utcNow,
        Func<long> timestamp,
        long timestampFrequency,
        int processorCount)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentNullException.ThrowIfNull(timestamp);
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        if (processorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processorCount));
        }

        _catalog = catalog;
        _utcNow = utcNow;
        _timestamp = timestamp;
        _timestampFrequency = timestampFrequency;
        _processorCount = processorCount;
    }

    public ProcessCounterBatch Capture()
    {
        DateTimeOffset capturedAtUtc = _utcNow();
        long monotonicTimestamp = _timestamp();
        IReadOnlyList<IProcessCounterProbe> probes = _catalog.Enumerate();
        List<ProcessCounterSample> samples = new(probes.Count);
        int skipped = 0;
        foreach (IProcessCounterProbe probe in probes)
        {
            try
            {
                samples.Add(probe.Read());
            }
            catch (Exception exception) when (IsExpectedReadFailure(exception))
            {
                skipped++;
            }
            finally
            {
                probe.Dispose();
            }
        }

        return new ProcessCounterBatch(
            capturedAtUtc,
            monotonicTimestamp,
            _timestampFrequency,
            _processorCount,
            probes.Count,
            skipped,
            samples);
    }

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is Win32Exception or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException ||
        exception.GetType() == typeof(SystemException);
}

internal interface IProcessCounterCatalog
{
    IReadOnlyList<IProcessCounterProbe> Enumerate();
}

internal interface IProcessCounterProbe : IDisposable
{
    ProcessCounterSample Read();
}

internal sealed class WindowsProcessCounterCatalog : IProcessCounterCatalog
{
    public IReadOnlyList<IProcessCounterProbe> Enumerate() =>
        Process.GetProcesses()
            .Select(process =>
                (IProcessCounterProbe)new WindowsProcessCounterProbe(process))
            .ToArray();
}

internal sealed class WindowsProcessCounterProbe(Process process)
    : IProcessCounterProbe
{
    private readonly Process _process = process;

    public ProcessCounterSample Read() =>
        new(
            _process.Id,
            _process.ProcessName,
            _process.StartTime.ToUniversalTime().Ticks,
            _process.TotalProcessorTime.Ticks,
            _process.WorkingSet64,
            _process.Threads.Count);

    public void Dispose() => _process.Dispose();
}
