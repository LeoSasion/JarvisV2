namespace Jarvis.Win10.ExplorerCaptionPlan;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "model-test", StringComparison.Ordinal))
            {
                return ExplorerCaptionGate.RunModelTests();
            }

            if (args.Length == 0)
            {
                return Usage();
            }

            string command = args[0];
            IReadOnlyDictionary<string, string> options =
                ParseOptions(args[1..]);
            string? expectedWindowHandle =
                GetOptional(options, "--expected-window-handle");
            ExplorerCaptionGateResult gate =
                ExplorerCaptionGate.Inspect(expectedWindowHandle);
            return command switch
            {
                "inspect" => Inspect(gate.Receipt, options),
                "plan-preview" => Plan(gate.Receipt, options),
                _ => Usage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Inspect(
        ExplorerCaptionGateReceipt gate,
        IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "--expected-window-handle");
        ExplorerCaptionGate.WriteJson(gate);
        return gate.Passed ? 0 : 12;
    }

    private static int Plan(
        ExplorerCaptionGateReceipt gate,
        IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(
            options,
            "--expected-window-handle",
            "--ttl-seconds");
        return ExplorerCaptionPlanner.WritePlan(
            gate,
            GetInt32(options, "--ttl-seconds"));
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string> options =
            new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Unexpected positional argument '{option}'.");
            }

            if (options.ContainsKey(option))
            {
                throw new ArgumentException(
                    $"Duplicate option '{option}'.");
            }

            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Option '{option}' requires a value.");
            }

            options.Add(option, arguments[++index]);
        }

        return options;
    }

    private static void EnsureOnly(
        IReadOnlyDictionary<string, string> options,
        params string[] allowed)
    {
        string[] unexpected = options.Keys
            .Where(option => !allowed.Contains(
                option,
                StringComparer.Ordinal))
            .ToArray();
        if (unexpected.Length != 0)
        {
            throw new ArgumentException(
                $"Unexpected option(s): {string.Join(", ", unexpected)}.");
        }
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value) ||
            !int.TryParse(value, out int result))
        {
            throw new ArgumentException(
                $"Option '{name}' must be an Int32.");
        }

        return result;
    }

    private static string? GetOptional(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out string? value)
            ? value
            : null;

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  jarvis-win10-explorer-caption-plan inspect " +
            "[--expected-window-handle 0x...]\n" +
            "  jarvis-win10-explorer-caption-plan plan-preview " +
            "[--expected-window-handle 0x...] --ttl-seconds 10..60\n" +
            "  jarvis-win10-explorer-caption-plan model-test");
        return 2;
    }
}
