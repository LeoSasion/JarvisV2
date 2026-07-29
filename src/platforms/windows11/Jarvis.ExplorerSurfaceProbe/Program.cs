using System.Globalization;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Automation;

namespace Jarvis.ExplorerSurfaceProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryParse(args, out ExactTargetRequest? request))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-explorer-surface-probe inspect-exact " +
                "--hwnd <hex> --pid <id> --tid <id> --title <exact> " +
                "--process-start-utc <ISO-8601> " +
                "--desktop-shell-pid <id>");
            return 2;
        }

        DateTime inspectedAtUtc = DateTime.UtcNow;
        if (!ExactExplorerTarget.TryInspect(
                request,
                out ExactTargetObservation? target,
                out IReadOnlyList<string> failures))
        {
            Write(
                new SurfaceProbeReceipt(
                    1,
                    "jarvisv2-explorer-surface-readonly-probe",
                    "blocked",
                    inspectedAtUtc,
                    target,
                    null,
                    ReadyForXamlSelectorVerification: false,
                    ReadyForPreview: false,
                    ExecutionSupported: false,
                    MutationSupported: false,
                    ActivationPermitted: false,
                    LiveExplorer: "read-only-inspection",
                    MutationPerformed: false,
                    Failures: failures));
            return 12;
        }

        try
        {
            AutomationTreeSnapshot tree =
                AutomationTreeReader.Read(request.WindowHandle);
            Write(
                new SurfaceProbeReceipt(
                    1,
                    "jarvisv2-explorer-surface-readonly-probe",
                    "passed-read-only",
                    inspectedAtUtc,
                    target,
                    tree,
                    ReadyForXamlSelectorVerification: false,
                    ReadyForPreview: false,
                    ExecutionSupported: false,
                    MutationSupported: false,
                    ActivationPermitted: false,
                    LiveExplorer: "read-only-inspection",
                    MutationPerformed: false,
                    Failures: []));
            return 0;
        }
        catch (
            Exception exception) when (
            exception is InvalidOperationException ||
            exception is ElementNotAvailableException ||
            exception is System.Runtime.InteropServices.COMException)
        {
            Write(
                new SurfaceProbeReceipt(
                    1,
                    "jarvisv2-explorer-surface-readonly-probe",
                    "blocked",
                    inspectedAtUtc,
                    target,
                    null,
                    ReadyForXamlSelectorVerification: false,
                    ReadyForPreview: false,
                    ExecutionSupported: false,
                    MutationSupported: false,
                    ActivationPermitted: false,
                    LiveExplorer: "read-only-inspection",
                    MutationPerformed: false,
                    Failures:
                    [
                        $"automation-tree-unavailable:" +
                        exception.GetType().Name,
                    ]));
            return 12;
        }
    }

    private static bool TryParse(
        string[] args,
        [NotNullWhen(true)]
        out ExactTargetRequest? request)
    {
        request = null;
        if (args.Length != 13 || args[0] != "inspect-exact")
        {
            return false;
        }

        Dictionary<string, string> options =
            new(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(args[index], args[index + 1]))
            {
                return false;
            }
        }

        string[] required =
        [
            "--hwnd",
            "--pid",
            "--tid",
            "--title",
            "--process-start-utc",
            "--desktop-shell-pid",
        ];
        if (options.Count != required.Length ||
            required.Any(key => !options.ContainsKey(key)) ||
            !TryParseWindowHandle(options["--hwnd"], out nint handle) ||
            !uint.TryParse(
                options["--pid"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint processId) ||
            !uint.TryParse(
                options["--tid"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint threadId) ||
            !uint.TryParse(
                options["--desktop-shell-pid"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint shellProcessId) ||
            !DateTime.TryParse(
                options["--process-start-utc"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                out DateTime startUtc))
        {
            return false;
        }

        request = new ExactTargetRequest(
            handle,
            processId,
            threadId,
            options["--title"],
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            shellProcessId);
        return processId > 0 &&
            threadId > 0 &&
            shellProcessId > 0 &&
            string.Equals(
                options["--title"],
                "C:\\",
                StringComparison.Ordinal);
    }

    private static bool TryParseWindowHandle(
        string value,
        out nint handle)
    {
        handle = nint.Zero;
        if (!value.StartsWith("0x", StringComparison.Ordinal) ||
            !ulong.TryParse(
                value.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong raw) ||
            raw == 0)
        {
            return false;
        }

        handle = unchecked((nint)(long)raw);
        return true;
    }

    private static void Write<T>(T value)
    {
        Console.Out.WriteLine(
            JsonSerializer.Serialize(value, OutputOptions));
    }
}
