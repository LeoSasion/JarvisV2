using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.ExplorerPreviewModel;

internal static class Program
{
    private static readonly JsonSerializerOptions StrictInputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions CompatibilityOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 &&
                args[0] == "model-test")
            {
                PreviewModelTestReceipt receipt =
                    PreviewModelScenarios.Run();
                Write(receipt);
                return receipt.Result == "passed" ? 0 : 12;
            }

            if (args.Length == 3 &&
                args[0] == "compile-candidate")
            {
                byte[] profileBytes = File.ReadAllBytes(args[1]);
                byte[] compatibilityBytes = File.ReadAllBytes(args[2]);
                CandidateProfileDocument profile =
                    JsonSerializer.Deserialize<CandidateProfileDocument>(
                        profileBytes,
                        StrictInputOptions) ??
                    throw new JsonException(
                        "Candidate profile root is null.");
                CompatibilityDocument compatibility =
                    JsonSerializer.Deserialize<CompatibilityDocument>(
                        compatibilityBytes,
                        CompatibilityOptions) ??
                    throw new JsonException(
                        "Compatibility root is null.");
                CandidateCompilationReceipt receipt =
                    CandidateProfileCompiler.Compile(
                        profile,
                        compatibility,
                        Convert.ToHexString(
                            SHA256.HashData(profileBytes)),
                        Convert.ToHexString(
                            SHA256.HashData(compatibilityBytes)));
                Write(receipt);
                return receipt.Result == "compiled-offline-candidate"
                    ? 0
                    : 12;
            }

            Console.Error.WriteLine(
                "Usage: jarvis-explorer-preview-model model-test | " +
                "compile-candidate <profile.json> <compatibility.json>");
            return 2;
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is JsonException)
        {
            Write(
                new
                {
                    error = "invalid-offline-input",
                    message = exception.Message,
                    executionSupported = false,
                    activationPermitted = false,
                    liveExplorer = "not-run",
                    mutationPerformed = false,
                });
            return 2;
        }
    }

    private static void Write<T>(T value)
    {
        Console.Out.WriteLine(
            JsonSerializer.Serialize(value, OutputOptions));
    }
}
