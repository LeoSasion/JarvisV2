using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.PiAgentHost;

public interface IOpenAiApiKeySource
{
    ValueTask<string?> GetApiKeyAsync(
        CancellationToken cancellationToken = default);
}

public sealed record OpenAiApiKeyStoreReceipt(
    int SchemaVersion,
    string ReceiptType,
    string CredentialPath,
    int EnvelopeBytes,
    DateTimeOffset SavedAtUtc);

public sealed class OpenAiApiKeyCredentialStore : IOpenAiApiKeySource
{
    public const string StoreModel =
        "desktop-current-user-dpapi-atomic-no-sidecar-transport";
    public const int MaximumApiKeyCharacters = 512;
    public const int MaximumEnvelopeBytes = 16_384;

    private const string EnvelopeReceiptType =
        "jarvisv2-openai-api-key";
    private const string EntropyDomain =
        "JARVIS2/openai-api-key/v1";
    private const string CredentialFileName =
        "openai-api-key.j2secret";

    private sealed class CredentialEnvelope
    {
        public int SchemaVersion { get; init; }
        public string ReceiptType { get; init; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; init; }
        public byte[] ProtectedPayload { get; init; } = [];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly SemaphoreSlim ioGate = new(1, 1);
    private readonly string rootDirectory;

    public OpenAiApiKeyCredentialStore()
        : this(GetDefaultRootDirectory())
    {
    }

    public OpenAiApiKeyCredentialStore(string rootDirectory)
    {
        if (
            string.IsNullOrWhiteSpace(rootDirectory) ||
            !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "Credential storage requires an absolute root directory.",
                nameof(rootDirectory));
        }

        this.rootDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootDirectory));
        string? root = Path.GetPathRoot(this.rootDirectory);
        if (
            string.IsNullOrWhiteSpace(root) ||
            string.Equals(
                this.rootDirectory,
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Credential storage may not use a filesystem root.",
                nameof(rootDirectory));
        }
    }

    public string RootDirectory => rootDirectory;
    public string CredentialPath =>
        Path.Combine(rootDirectory, CredentialFileName);

    public async ValueTask<string?> GetApiKeyAsync(
        CancellationToken cancellationToken = default)
    {
        bool gateHeld = false;
        byte[]? entropy = null;
        byte[]? plaintext = null;
        try
        {
            await ioGate.WaitAsync(cancellationToken);
            gateHeld = true;
            if (!Directory.Exists(rootDirectory))
            {
                if (File.Exists(rootDirectory))
                {
                    throw new InvalidDataException(
                        "The credential root is a file instead of a directory.");
                }
                return null;
            }

            EnsureNoReparsePoints(rootDirectory);
            string credentialPath = CredentialPath;
            if (!File.Exists(credentialPath))
            {
                if (Directory.Exists(credentialPath))
                {
                    throw new InvalidDataException(
                        "The credential path is a directory instead of an envelope.");
                }
                return null;
            }
            EnsureNoReparsePoints(credentialPath);

            FileInfo info = new(credentialPath);
            if (info.Length <= 0 || info.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The encrypted credential envelope failed its size boundary.");
            }

            byte[] envelopeBytes = await File.ReadAllBytesAsync(
                credentialPath,
                cancellationToken);
            if (
                envelopeBytes.Length <= 0 ||
                envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The encrypted credential envelope changed outside its boundary.");
            }

            CredentialEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<CredentialEnvelope>(
                    envelopeBytes,
                    SerializerOptions) ??
                    throw new InvalidDataException(
                        "The encrypted credential envelope was empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The encrypted credential envelope was malformed.",
                    exception);
            }

            if (
                envelope.SchemaVersion != 1 ||
                envelope.ReceiptType != EnvelopeReceiptType ||
                envelope.SavedAtUtc == default ||
                envelope.ProtectedPayload is not { Length: > 0 })
            {
                throw new InvalidDataException(
                    "The encrypted credential envelope failed admission.");
            }

            entropy = CreateEntropy(credentialPath);
            try
            {
                plaintext = ProtectedData.Unprotect(
                    envelope.ProtectedPayload,
                    entropy,
                    DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidDataException(
                    "The credential could not be opened for this Windows user.",
                    exception);
            }

            string apiKey;
            try
            {
                apiKey = new UTF8Encoding(false, true).GetString(plaintext);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(
                    "The decrypted credential was not valid UTF-8.",
                    exception);
            }
            ValidateApiKey(apiKey);
            return apiKey;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            if (entropy is not null)
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
            if (gateHeld)
            {
                ioGate.Release();
            }
        }
    }

    public async Task<OpenAiApiKeyStoreReceipt> SaveAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateApiKey(apiKey);
        bool gateHeld = false;
        byte[]? plaintext = null;
        byte[]? protectedPayload = null;
        byte[]? entropy = null;
        string? temporaryPath = null;
        try
        {
            await ioGate.WaitAsync(cancellationToken);
            gateHeld = true;
            EnsureNoReparsePoints(rootDirectory);
            Directory.CreateDirectory(rootDirectory);
            EnsureNoReparsePoints(rootDirectory);
            string credentialPath = CredentialPath;
            if (Directory.Exists(credentialPath))
            {
                throw new InvalidDataException(
                    "The credential path is a directory instead of an envelope.");
            }

            plaintext = Encoding.UTF8.GetBytes(apiKey);
            entropy = CreateEntropy(credentialPath);
            protectedPayload = ProtectedData.Protect(
                plaintext,
                entropy,
                DataProtectionScope.CurrentUser);
            DateTimeOffset savedAtUtc = DateTimeOffset.UtcNow;
            byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
                new CredentialEnvelope
                {
                    SchemaVersion = 1,
                    ReceiptType = EnvelopeReceiptType,
                    SavedAtUtc = savedAtUtc,
                    ProtectedPayload = protectedPayload,
                },
                SerializerOptions);
            if (envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The encrypted credential envelope exceeded its boundary.");
            }

            temporaryPath = Path.Combine(
                rootDirectory,
                $".{CredentialFileName}.{Guid.NewGuid():N}.tmp");
            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4_096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(envelopeBytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            EnsureNoReparsePoints(temporaryPath);
            EnsureNoReparsePoints(credentialPath);
            if (File.Exists(credentialPath))
            {
                File.Replace(
                    temporaryPath,
                    credentialPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, credentialPath);
            }
            temporaryPath = null;
            return new OpenAiApiKeyStoreReceipt(
                1,
                EnvelopeReceiptType,
                credentialPath,
                envelopeBytes.Length,
                savedAtUtc);
        }
        finally
        {
            if (
                temporaryPath is not null &&
                File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            if (protectedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
            }
            if (entropy is not null)
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
            if (gateHeld)
            {
                ioGate.Release();
            }
        }
    }

    public static void ValidateApiKey(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (
            apiKey.Length < 20 ||
            apiKey.Length > MaximumApiKeyCharacters ||
            apiKey.Any(character => character < 0x21 || character > 0x7e))
        {
            throw new ArgumentException(
                "The OpenAI API key failed its length or character boundary.",
                nameof(apiKey));
        }
    }

    private static byte[] CreateEntropy(string credentialPath)
    {
        string binding = string.Concat(
            EntropyDomain,
            "\n",
            Path.GetFullPath(credentialPath).ToUpperInvariant());
        return SHA256.HashData(Encoding.UTF8.GetBytes(binding));
    }

    private static string GetDefaultRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "Local application data is unavailable for credential storage.");
        }
        return Path.Combine(localAppData, "JARVIS2", "credentials");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException(
                "The credential path has no filesystem root.");
        }

        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (
                (File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Credential storage may not traverse a reparse point.");
            }
        }
    }
}
