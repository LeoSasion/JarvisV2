using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Jarvis.Win10.NativeStyleProbe;

internal static class HostProfileCatalog
{
    private const string ResourceName =
        "Jarvis.Win10.NativeStyleProbe.windows10-host-profiles.json";

    public static Windows10HostProfileCatalog Load()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(
                ResourceName) ??
            throw new InvalidOperationException(
                $"Embedded host profile resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<Windows10HostProfileCatalog>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ??
            throw new InvalidOperationException(
                "The embedded Windows 10 host profile catalog is empty.");
    }
}
