namespace Jarvis.NativeWindowStyleSession;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "model-test", StringComparison.Ordinal))
            {
                return NativeWindowStyleSessionController.RunModelTests();
            }

            if (args.Length == 0)
            {
                return Usage();
            }

            string command = args[0];
            IReadOnlyDictionary<string, string?> options =
                ParseOptions(args[1..]);
            NativeWindowStyleSessionController controller = new();
            return command switch
            {
                "inspect" => Inspect(controller, options),
                "plan-preview" => Plan(controller, options),
                "apply-preview" => await ApplyAsync(controller, options),
                "reset-default" => ResetDefault(controller, options),
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
        NativeWindowStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--window-handle",
            "--expected-process-id",
            "--expected-title");
        return controller.Inspect(
            GetRequired(options, "--window-handle"),
            GetUInt32(options, "--expected-process-id"),
            GetRequired(options, "--expected-title"));
    }

    private static int Plan(
        NativeWindowStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--window-handle",
            "--expected-process-id",
            "--expected-title",
            "--preset",
            "--ttl-seconds");
        return controller.Plan(
            GetRequired(options, "--window-handle"),
            GetUInt32(options, "--expected-process-id"),
            GetRequired(options, "--expected-title"),
            GetRequired(options, "--preset"),
            GetInt32(options, "--ttl-seconds"));
    }

    private static Task<int> ApplyAsync(
        NativeWindowStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--window-handle",
            "--expected-process-id",
            "--expected-title",
            "--preset",
            "--ttl-seconds",
            NativeWindowStylePolicy.BaselineAcknowledgement,
            NativeWindowStylePolicy.ApplyConfirmation);
        return controller.ApplyAsync(
            GetRequired(options, "--window-handle"),
            GetUInt32(options, "--expected-process-id"),
            GetRequired(options, "--expected-title"),
            GetRequired(options, "--preset"),
            GetInt32(options, "--ttl-seconds"),
            HasFlag(
                options,
                NativeWindowStylePolicy.ApplyConfirmation),
            HasFlag(
                options,
                NativeWindowStylePolicy.BaselineAcknowledgement));
    }

    private static int ResetDefault(
        NativeWindowStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--window-handle",
            "--expected-process-id",
            "--expected-title",
            NativeWindowStylePolicy.ResetConfirmation);
        return controller.ResetDefault(
            GetRequired(options, "--window-handle"),
            GetUInt32(options, "--expected-process-id"),
            GetRequired(options, "--expected-title"),
            HasFlag(options, NativeWindowStylePolicy.ResetConfirmation));
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        Dictionary<string, string?> options = new(StringComparer.Ordinal);
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

            bool isFlag =
                option == NativeWindowStylePolicy.ApplyConfirmation ||
                option == NativeWindowStylePolicy.ResetConfirmation ||
                option == NativeWindowStylePolicy.BaselineAcknowledgement;
            if (isFlag)
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

    private static void EnsureOnly(
        IReadOnlyDictionary<string, string?> options,
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

    private static bool HasFlag(
        IReadOnlyDictionary<string, string?> options,
        string name) =>
        options.TryGetValue(name, out string? value) && value is null;

    private static string GetRequired(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '{name}'.");
        }

        return value;
    }

    private static uint GetUInt32(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        string value = GetRequired(options, name);
        if (!uint.TryParse(value, out uint result) || result == 0)
        {
            throw new ArgumentException(
                $"Option '{name}' must be a positive UInt32.");
        }

        return result;
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
            "  jarvis-native-window-style-session inspect " +
            "--window-handle <0xHWND> --expected-process-id <pid> " +
            "--expected-title <title>\n" +
            "  jarvis-native-window-style-session plan-preview " +
            "--window-handle <0xHWND> --expected-process-id <pid> " +
            "--expected-title <title> --preset signal " +
            "--ttl-seconds 10..60\n" +
            "  jarvis-native-window-style-session apply-preview " +
            "--window-handle <0xHWND> --expected-process-id <pid> " +
            "--expected-title <title> --preset signal " +
            "--ttl-seconds 10..60 " +
            NativeWindowStylePolicy.BaselineAcknowledgement + " " +
            NativeWindowStylePolicy.ApplyConfirmation + "\n" +
            "  jarvis-native-window-style-session reset-default " +
            "--window-handle <0xHWND> --expected-process-id <pid> " +
            "--expected-title <title> " +
            NativeWindowStylePolicy.ResetConfirmation + "\n" +
            "  jarvis-native-window-style-session model-test");
        return 2;
    }
}
