namespace Jarvis.Win10.OwnedTaskManager;

public sealed record ProcessCounterSample(
    int ProcessId,
    string ProcessName,
    long StartTimeUtcTicks,
    long TotalProcessorTimeTicks,
    long WorkingSetBytes,
    int ThreadCount);

public sealed record ProcessCounterBatch(
    DateTimeOffset CapturedAtUtc,
    long MonotonicTimestamp,
    long MonotonicFrequency,
    int ProcessorCount,
    int EnumeratedProcessCount,
    int SkippedProcessCount,
    IReadOnlyList<ProcessCounterSample> Processes);

public interface IProcessCounterSource
{
    ProcessCounterBatch Capture();
}

public sealed record OwnedTaskProcessSnapshot(
    string ProcessName,
    int ProcessId,
    double CpuPercent,
    bool CpuPercentMeasured,
    long WorkingSetBytes,
    int ThreadCount)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long StartTimeUtcTicks { get; init; }
}

public sealed record OwnedTaskSnapshot(
    int SchemaVersion,
    string Result,
    DateTimeOffset CapturedAtUtc,
    int ProcessorCount,
    int EnumeratedProcessCount,
    int SkippedProcessCount,
    int ProcessCount,
    int MaximumProcessCount,
    int TruncatedProcessCount,
    bool ReadOnlyProcessTelemetry,
    bool ProcessControlSupported,
    bool WindowControlSupported,
    bool InjectionSupported,
    IReadOnlyList<OwnedTaskProcessSnapshot> Processes);

internal sealed record OwnedTaskManagerScenario(
    string Name,
    bool Passed,
    string Detail);

internal sealed record OwnedTaskManagerTestReceipt(
    int SchemaVersion,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool SourceInjectable,
    bool ReadOnlyProcessTelemetry,
    bool ProcessControlSupported,
    bool WindowControlSupported,
    bool InjectionSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<OwnedTaskManagerScenario> Scenarios);
