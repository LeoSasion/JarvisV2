using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jarvis.Win10.RgbThemeModel;

internal sealed record EmbeddedTheme(
    ThemeDocument Document,
    string Sha256);

internal static class EmbeddedThemeReader
{
    private const string ThemeResource =
        "Jarvis.Win10.RgbThemeModel.neural-void-theme.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static EmbeddedTheme Read()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ThemeResource) ??
            throw new InvalidDataException(
                $"Missing embedded resource: {ThemeResource}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        ThemeDocument document =
            JsonSerializer.Deserialize<ThemeDocument>(
                bytes,
                JsonOptions) ??
            throw new InvalidDataException(
                "Embedded Neural Void theme is empty.");
        return new EmbeddedTheme(
            document,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
