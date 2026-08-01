namespace Jarvis.DesktopStyleSession;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                string.Equals(args[0], "model-test", StringComparison.Ordinal))
            {
                return DesktopStyleSessionController.RunModelTests();
            }

            if (args.Length == 0)
            {
                return Usage();
            }

            string command = args[0];
            IReadOnlyDictionary<string, string?> options =
                ParseOptions(args[1..]);
            DesktopStyleSessionController controller = new();
            return command switch
            {
                "inspect" => Inspect(controller, options),
                "plan-preview" => Plan(controller, options),
                "apply-preview" => await ApplyAsync(controller, options),
                "rollback" => Rollback(controller, options),
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
        DesktopStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(options, "--expected-explorer-pid");
        return controller.Inspect(
            GetUInt32(options, "--expected-explorer-pid"));
    }

    private static int Plan(
        DesktopStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--expected-explorer-pid",
            "--preset",
            "--ttl-seconds");
        return controller.Plan(
            GetUInt32(options, "--expected-explorer-pid"),
            GetRequired(options, "--preset"),
            GetInt32(options, "--ttl-seconds"));
    }

    private static Task<int> ApplyAsync(
        DesktopStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--expected-explorer-pid",
            "--preset",
            "--ttl-seconds",
            DesktopStylePolicy.ApplyConfirmation);
        return controller.ApplyAsync(
            GetUInt32(options, "--expected-explorer-pid"),
            GetRequired(options, "--preset"),
            GetInt32(options, "--ttl-seconds"),
            HasFlag(options, DesktopStylePolicy.ApplyConfirmation));
    }

    private static int Rollback(
        DesktopStyleSessionController controller,
        IReadOnlyDictionary<string, string?> options)
    {
        EnsureOnly(
            options,
            "--expected-explorer-pid",
            "--session",
            DesktopStylePolicy.RollbackConfirmation);
        return controller.Rollback(
            GetUInt32(options, "--expected-explorer-pid"),
            GetRequired(options, "--session"),
            HasFlag(options, DesktopStylePolicy.RollbackConfirmation));
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

            bool isConfirmation =
                option == DesktopStylePolicy.ApplyConfirmation ||
                option == DesktopStylePolicy.RollbackConfirmation;
            if (isConfirmation)
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
            "  jarvis-desktop-style-session inspect " +
            "--expected-explorer-pid <pid>\n" +
            "  jarvis-desktop-style-session plan-preview " +
            "--expected-explorer-pid <pid> --preset " +
            "graphite|amber|orbital-cyan|reactor-amber|neural-emerald " +
            "--ttl-seconds 10..60\n" +
            "  jarvis-desktop-style-session apply-preview " +
            "--expected-explorer-pid <pid> --preset " +
            "graphite|amber|orbital-cyan|reactor-amber|neural-emerald " +
            "--ttl-seconds 10..60 " +
            DesktopStylePolicy.ApplyConfirmation + "\n" +
            "  jarvis-desktop-style-session rollback " +
            "--session <path> --expected-explorer-pid <pid> " +
            DesktopStylePolicy.RollbackConfirmation + "\n" +
            "  jarvis-desktop-style-session model-test");
        return 2;
    }
}
