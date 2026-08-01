namespace Jarvis.Win10.ExplorerCaptionSession;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "model-test", StringComparison.Ordinal))
            {
                return ExplorerCaptionSessionController.RunModelTests();
            }

            if (args.Length == 0)
            {
                return Usage();
            }

            string command = args[0];
            IReadOnlyDictionary<string, string?> options =
                ParseOptions(args[1..]);
            ExplorerCaptionSessionController controller = new();
            return command switch
            {
                "apply-preview" => await controller.ApplyAsync(
                    GetRequired(options, "--expected-window-handle"),
                    GetInt32(options, "--ttl-seconds"),
                    HasFlag(
                        options,
                        ExplorerCaptionSessionPolicy.ApplyConfirmation)),
                "rollback" => controller.Rollback(
                    GetRequired(options, "--session"),
                    HasFlag(
                        options,
                        ExplorerCaptionSessionPolicy.RollbackConfirmation)),
                _ => Usage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string?> options =
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

            if (option is
                ExplorerCaptionSessionPolicy.ApplyConfirmation or
                ExplorerCaptionSessionPolicy.RollbackConfirmation)
            {
                options.Add(option, null);
                continue;
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

    private static bool HasFlag(
        IReadOnlyDictionary<string, string?> options,
        string name) =>
        options.TryGetValue(name, out string? value) &&
        value is null;

    private static string GetRequired(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Missing required option '{name}'.");
        }

        return value;
    }

    private static int GetInt32(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        string value = GetRequired(options, name);
        if (!int.TryParse(value, out int result))
        {
            throw new ArgumentException(
                $"Option '{name}' must be an Int32.");
        }

        return result;
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  jarvis-win10-explorer-caption-session apply-preview " +
            "--expected-window-handle 0x... --ttl-seconds 10..60 " +
            ExplorerCaptionSessionPolicy.ApplyConfirmation + "\n" +
            "  jarvis-win10-explorer-caption-session rollback " +
            "--session <path> " +
            ExplorerCaptionSessionPolicy.RollbackConfirmation + "\n" +
            "  jarvis-win10-explorer-caption-session model-test");
        return 2;
    }
}
