using System.Text.Json;

namespace Jarvis.DesktopStyleProbe;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 ||
            !string.Equals(
                args[0],
                "inspect",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: jarvis-desktop-style-probe inspect");
            return 2;
        }

        IReadOnlyList<DesktopHostCandidate> candidates =
            NativeDesktopHostProbe.Inspect();
        bool exactCandidate = candidates.Count == 1;
        object receipt = new
        {
            schemaVersion = 1,
            receiptType = "jarvisv2-desktop-style-host-probe",
            result = exactCandidate ? "passed-read-only" : "blocked",
            observedAtUtc = DateTimeOffset.UtcNow,
            selectionMode = "exact-shell-defview-child",
            candidateCount = candidates.Count,
            candidates,
            executionSupported = false,
            mutationSupported = false,
            activationPermitted = false,
            mutationPerformed = false,
            liveExplorer = "read-only-inspection",
        };
        Console.WriteLine(
            JsonSerializer.Serialize(
                receipt,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
        return exactCandidate ? 0 : 12;
    }
}
