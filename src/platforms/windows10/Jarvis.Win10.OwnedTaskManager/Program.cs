using System.Globalization;
using System.Text.Json;

namespace Jarvis.Win10.OwnedTaskManager;

internal static class Program
{
    private const int DefaultSnapshotIntervalMilliseconds = 250;
    private const int MinimumSnapshotIntervalMilliseconds = 100;
    private const int MaximumSnapshotIntervalMilliseconds = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "test-model")
        {
            OwnedTaskManagerTestReceipt receipt = ModelScenarios.Run();
            Write(receipt);
            return receipt.Result == "passed" ? 0 : 12;
        }

        if (TryParseSnapshotOptions(
                args,
                out int maximumProcessCount,
                out int intervalMilliseconds))
        {
            TaskSnapshotSampler sampler = new(
                new WindowsProcessSnapshotSource(),
                maximumProcessCount);
            _ = sampler.Capture();
            Thread.Sleep(intervalMilliseconds);
            OwnedTaskSnapshot snapshot = sampler.Capture();
            Write(snapshot);
            return snapshot.Result == "captured-read-only-process-snapshot"
                ? 0
                : 12;
        }

        WriteUsage();
        return 2;
    }

    private static bool TryParseSnapshotOptions(
        string[] args,
        out int maximumProcessCount,
        out int intervalMilliseconds)
    {
        maximumProcessCount = TaskSnapshotSampler.DefaultMaximumProcessCount;
        intervalMilliseconds = DefaultSnapshotIntervalMilliseconds;
        if (args.Length == 0 || args[0] != "snapshot")
        {
            return false;
        }

        bool limitSeen = false;
        bool intervalSeen = false;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length ||
                !int.TryParse(
                    args[index + 1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                return false;
            }

            if (args[index] == "--limit" &&
                !limitSeen &&
                value is >= 1 and <=
                    TaskSnapshotSampler.AbsoluteMaximumProcessCount)
            {
                maximumProcessCount = value;
                limitSeen = true;
            }
            else if (args[index] == "--interval-ms" &&
                !intervalSeen &&
                value is >= MinimumSnapshotIntervalMilliseconds and <=
                    MaximumSnapshotIntervalMilliseconds)
            {
                intervalMilliseconds = value;
                intervalSeen = true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static void Write<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteUsage() =>
        Console.Error.WriteLine(
            "Usage: jarvis-win10-owned-task-manager test-model " +
            "or snapshot [--limit <1-256>] [--interval-ms <100-2000>]");
}
