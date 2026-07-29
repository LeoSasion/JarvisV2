using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jarvis.Win10.SurfaceSelectorModel;

internal sealed record EmbeddedModelInputs(
    SelectorCandidateDocument Candidate,
    TopologyFixtureDocument Evidence,
    string CandidateSha256,
    string EvidenceSha256);

internal static class EmbeddedModelInputReader
{
    private const string CandidateResource =
        "Jarvis.Win10.SurfaceSelectorModel.selector-candidate.json";
    private const string EvidenceResource =
        "Jarvis.Win10.SurfaceSelectorModel.topology-evidence.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static EmbeddedModelInputs Read()
    {
        byte[] candidateBytes = ReadResource(CandidateResource);
        byte[] evidenceBytes = ReadResource(EvidenceResource);
        SelectorCandidateDocument candidate =
            JsonSerializer.Deserialize<SelectorCandidateDocument>(
                candidateBytes,
                JsonOptions) ??
            throw new InvalidDataException(
                "Embedded selector candidate is empty.");
        TopologyFixtureDocument evidence =
            JsonSerializer.Deserialize<TopologyFixtureDocument>(
                evidenceBytes,
                JsonOptions) ??
            throw new InvalidDataException(
                "Embedded topology evidence is empty.");

        return new EmbeddedModelInputs(
            candidate,
            evidence,
            Convert.ToHexString(SHA256.HashData(candidateBytes)),
            Convert.ToHexString(SHA256.HashData(evidenceBytes)));
    }

    private static byte[] ReadResource(string resourceName)
    {
        using Stream stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException(
                $"Missing embedded resource: {resourceName}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
