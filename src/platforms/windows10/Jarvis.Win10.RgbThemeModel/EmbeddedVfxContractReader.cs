using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Jarvis.VisualEffects;

namespace Jarvis.Win10.RgbThemeModel;

internal sealed record EmbeddedVfxContract(
    VfxContractDocument Document,
    string Sha256);

internal sealed record EmbeddedVfxPreset(
    VfxPresetDocument Document,
    string Sha256);

internal static class EmbeddedVfxContractReader
{
    private const string ContractResource =
        "Jarvis.Win10.RgbThemeModel.global-vfx-contract.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static EmbeddedVfxContract Read()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ContractResource) ??
            throw new InvalidDataException(
                $"Missing embedded resource: {ContractResource}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        VfxContractDocument document =
            JsonSerializer.Deserialize<VfxContractDocument>(
                bytes,
                JsonOptions) ??
            throw new InvalidDataException(
                "Embedded Neural Void global VFX contract is empty.");
        return new EmbeddedVfxContract(
            document,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}

internal static class EmbeddedVfxPresetReader
{
    private const string PresetResource =
        "Jarvis.Win10.RgbThemeModel.vfx-preset.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static EmbeddedVfxPreset Read()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(PresetResource) ??
            throw new InvalidDataException(
                $"Missing embedded resource: {PresetResource}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        VfxPresetDocument document =
            JsonSerializer.Deserialize<VfxPresetDocument>(
                bytes,
                JsonOptions) ??
            throw new InvalidDataException(
                "Embedded Neural Void VFX preset is empty.");
        return new EmbeddedVfxPreset(
            document,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
