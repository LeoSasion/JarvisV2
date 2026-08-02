using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.ControlCenter;

public sealed record DesktopRecentSessionEntry(
    string WorkspaceRoot,
    ConversationProviderKind Provider,
    DateTimeOffset LastOpenedAtUtc);

public sealed record DesktopRecentSessionCatalog(
    int SchemaVersion,
    string ReceiptType,
    int Revision,
    IReadOnlyList<DesktopRecentSessionEntry> Entries);

public sealed record DesktopRecentSessionStoreReceipt(
    int SchemaVersion,
    string ReceiptType,
    string CatalogPath,
    int Revision,
    int EntryCount,
    int EnvelopeBytes,
    DateTimeOffset SavedAtUtc);

public sealed class DesktopRecentSessionStore
{
    public const string StoreModel =
        "current-user-dpapi-atomic-desktop-owned";
    public const int MaximumEntries = 8;
    public const int MaximumWorkspacePathCharacters = 1_024;
    public const int MaximumPayloadBytes = 16_384;
    public const int MaximumEnvelopeBytes = 32_768;

    private const string CatalogReceiptType =
        "jarvisv2-desktop-recent-session-catalog";
    private const string EnvelopeReceiptType =
        "jarvisv2-desktop-recent-session-envelope";
    private const string EntropyDomain =
        "JARVIS2/desktop-recent-session-catalog/v1";
    private const string CatalogFileName = "recent-sessions.j2catalog";

    private sealed class CatalogEnvelope
    {
        public int SchemaVersion { get; init; }
        public string ReceiptType { get; init; } = string.Empty;
        public int Revision { get; init; }
        public int EntryCount { get; init; }
        public DateTimeOffset SavedAtUtc { get; init; }
        public byte[] ProtectedPayload { get; init; } = [];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string rootDirectory;
    private readonly SemaphoreSlim ioGate = new(1, 1);

    public DesktopRecentSessionStore()
        : this(GetDefaultRootDirectory())
    {
    }

    public DesktopRecentSessionStore(string rootDirectory)
    {
        if (
            string.IsNullOrWhiteSpace(rootDirectory) ||
            !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "Recent-session storage requires an absolute root directory.",
                nameof(rootDirectory));
        }
        this.rootDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootDirectory));
        string? volumeRoot = Path.GetPathRoot(this.rootDirectory);
        if (
            string.IsNullOrWhiteSpace(volumeRoot) ||
            string.Equals(
                this.rootDirectory,
                Path.TrimEndingDirectorySeparator(volumeRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Recent-session storage may not use a filesystem root.",
                nameof(rootDirectory));
        }
    }

    public string RootDirectory => rootDirectory;

    public string CatalogPath => Path.Combine(
        rootDirectory,
        CatalogFileName);

    public static DesktopRecentSessionCatalog EmptyCatalog() =>
        new(1, CatalogReceiptType, 0, []);

    public async Task<DesktopRecentSessionCatalog> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await ioGate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            ioGate.Release();
        }
    }

    public async Task<DesktopRecentSessionStoreReceipt> RememberAsync(
        string workspaceRoot,
        ConversationProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                "The recent-session provider is not admitted.");
        }
        DesktopWorkspaceAdmissionReceipt workspace =
            DesktopSessionLaunchAdmission.AdmitWorkspace(workspaceRoot);
        if (workspace.Result != "passed" || workspace.WorkspaceRoot is null)
        {
            throw new ArgumentException(
                workspace.Failure ??
                    "The recent-session workspace is not admitted.",
                nameof(workspaceRoot));
        }

        await ioGate.WaitAsync(cancellationToken);
        try
        {
            DesktopRecentSessionCatalog current =
                await LoadCoreAsync(cancellationToken);
            DateTimeOffset openedAtUtc = DateTimeOffset.UtcNow;
            List<DesktopRecentSessionEntry> entries =
            [
                new(
                    workspace.WorkspaceRoot,
                    provider,
                    openedAtUtc),
            ];
            entries.AddRange(current.Entries.Where(entry =>
                !string.Equals(
                    entry.WorkspaceRoot,
                    workspace.WorkspaceRoot,
                    StringComparison.OrdinalIgnoreCase)));
            if (entries.Count > MaximumEntries)
            {
                entries.RemoveRange(
                    MaximumEntries,
                    entries.Count - MaximumEntries);
            }
            DesktopRecentSessionCatalog catalog = AdmitCatalog(
                new DesktopRecentSessionCatalog(
                    1,
                    CatalogReceiptType,
                    checked(current.Revision + 1),
                    entries));
            return await SaveCoreAsync(catalog, openedAtUtc, cancellationToken);
        }
        finally
        {
            ioGate.Release();
        }
    }

    private async Task<DesktopRecentSessionCatalog> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory))
        {
            if (File.Exists(rootDirectory))
            {
                throw new InvalidDataException(
                    "The recent-session storage root is a file.");
            }
            return EmptyCatalog();
        }
        EnsureNoReparsePoints(rootDirectory);
        string catalogPath = CatalogPath;
        if (!File.Exists(catalogPath))
        {
            if (Directory.Exists(catalogPath))
            {
                throw new InvalidDataException(
                    "The recent-session catalog path is a directory.");
            }
            return EmptyCatalog();
        }
        EnsureNoReparsePoints(catalogPath);
        FileInfo file = new(catalogPath);
        if (file.Length <= 0 || file.Length > MaximumEnvelopeBytes)
        {
            throw new InvalidDataException(
                "The encrypted recent-session envelope failed its size boundary.");
        }
        byte[] envelopeBytes = await File.ReadAllBytesAsync(
            catalogPath,
            cancellationToken);
        if (
            envelopeBytes.Length <= 0 ||
            envelopeBytes.Length > MaximumEnvelopeBytes)
        {
            throw new InvalidDataException(
                "The encrypted recent-session envelope changed outside its size boundary.");
        }

        CatalogEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CatalogEnvelope>(
                    envelopeBytes,
                    SerializerOptions) ??
                throw new InvalidDataException(
                    "The encrypted recent-session envelope was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The encrypted recent-session envelope was malformed.",
                exception);
        }
        if (
            envelope.SchemaVersion != 1 ||
            envelope.ReceiptType != EnvelopeReceiptType ||
            envelope.Revision <= 0 ||
            envelope.EntryCount is < 1 or > MaximumEntries ||
            envelope.SavedAtUtc == default ||
            envelope.SavedAtUtc.Offset != TimeSpan.Zero ||
            envelope.SavedAtUtc < DateTimeOffset.UnixEpoch ||
            envelope.SavedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
            envelope.ProtectedPayload is not { Length: > 0 })
        {
            throw new InvalidDataException(
                "The encrypted recent-session envelope failed schema admission.");
        }

        byte[] entropy = CreateEntropy();
        byte[] plaintext;
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
                "The recent-session catalog could not be opened for this Windows user.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
        try
        {
            if (plaintext.Length > MaximumPayloadBytes)
            {
                throw new InvalidDataException(
                    "The decrypted recent-session catalog exceeded its size boundary.");
            }
            DesktopRecentSessionCatalog catalog;
            try
            {
                catalog = JsonSerializer.Deserialize<
                        DesktopRecentSessionCatalog>(
                        plaintext,
                        SerializerOptions) ??
                    throw new InvalidDataException(
                        "The decrypted recent-session catalog was empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The decrypted recent-session catalog was malformed.",
                    exception);
            }
            DesktopRecentSessionCatalog admitted = AdmitCatalog(catalog);
            if (
                admitted.Revision != envelope.Revision ||
                admitted.Entries.Count != envelope.EntryCount)
            {
                throw new InvalidDataException(
                    "The recent-session envelope diverged from its protected payload.");
            }
            return admitted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<DesktopRecentSessionStoreReceipt> SaveCoreAsync(
        DesktopRecentSessionCatalog catalog,
        DateTimeOffset savedAtUtc,
        CancellationToken cancellationToken)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            catalog,
            SerializerOptions);
        if (plaintext.Length > MaximumPayloadBytes)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidOperationException(
                "The recent-session catalog exceeded its payload boundary.");
        }
        byte[] entropy = CreateEntropy();
        byte[] protectedPayload = [];
        try
        {
            Directory.CreateDirectory(rootDirectory);
            EnsureNoReparsePoints(rootDirectory);
            protectedPayload = ProtectedData.Protect(
                plaintext,
                entropy,
                DataProtectionScope.CurrentUser);
            CatalogEnvelope envelope = new()
            {
                SchemaVersion = 1,
                ReceiptType = EnvelopeReceiptType,
                Revision = catalog.Revision,
                EntryCount = catalog.Entries.Count,
                SavedAtUtc = savedAtUtc,
                ProtectedPayload = protectedPayload,
            };
            byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                SerializerOptions);
            if (envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidOperationException(
                    "The recent-session envelope exceeded its size boundary.");
            }

            string catalogPath = CatalogPath;
            if (Directory.Exists(catalogPath))
            {
                throw new InvalidDataException(
                    "The recent-session catalog path is a directory.");
            }
            if (File.Exists(catalogPath))
            {
                EnsureNoReparsePoints(catalogPath);
            }
            string temporaryPath = Path.Combine(
                rootDirectory,
                $".recent-sessions.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(
                        envelopeBytes,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, catalogPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new DesktopRecentSessionStoreReceipt(
                1,
                "jarvisv2-desktop-recent-session-store",
                catalogPath,
                catalog.Revision,
                catalog.Entries.Count,
                envelopeBytes.Length,
                savedAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
            CryptographicOperations.ZeroMemory(protectedPayload);
        }
    }

    private static DesktopRecentSessionCatalog AdmitCatalog(
        DesktopRecentSessionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (
            catalog.SchemaVersion != 1 ||
            catalog.ReceiptType != CatalogReceiptType ||
            catalog.Revision < 0 ||
            catalog.Entries is null ||
            catalog.Entries.Count > MaximumEntries ||
            (catalog.Revision == 0) != (catalog.Entries.Count == 0))
        {
            throw new InvalidDataException(
                "The recent-session catalog failed schema admission.");
        }

        List<DesktopRecentSessionEntry> entries = [];
        HashSet<string> workspaceRoots =
            new(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? priorOpenedAtUtc = null;
        foreach (DesktopRecentSessionEntry entry in catalog.Entries)
        {
            string workspaceRoot = NormalizeStoredWorkspaceRoot(
                entry.WorkspaceRoot);
            if (
                !Enum.IsDefined(entry.Provider) ||
                entry.LastOpenedAtUtc == default ||
                entry.LastOpenedAtUtc.Offset != TimeSpan.Zero ||
                entry.LastOpenedAtUtc < DateTimeOffset.UnixEpoch ||
                entry.LastOpenedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
                !workspaceRoots.Add(workspaceRoot) ||
                (priorOpenedAtUtc is not null &&
                    entry.LastOpenedAtUtc > priorOpenedAtUtc.Value))
            {
                throw new InvalidDataException(
                    "The recent-session catalog contains an invalid or unordered entry.");
            }
            priorOpenedAtUtc = entry.LastOpenedAtUtc;
            entries.Add(entry with { WorkspaceRoot = workspaceRoot });
        }
        return catalog with { Entries = entries };
    }

    private static string NormalizeStoredWorkspaceRoot(string workspaceRoot)
    {
        if (
            string.IsNullOrWhiteSpace(workspaceRoot) ||
            workspaceRoot.Trim() != workspaceRoot ||
            workspaceRoot.Length > MaximumWorkspacePathCharacters ||
            !Path.IsPathFullyQualified(workspaceRoot) ||
            workspaceRoot.StartsWith("\\\\", StringComparison.Ordinal) ||
            workspaceRoot.StartsWith("//", StringComparison.Ordinal) ||
            workspaceRoot.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            workspaceRoot.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A recent-session workspace path failed shape admission.");
        }
        string fullPath;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspaceRoot));
        }
        catch (Exception exception)
            when (exception is
                ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "A recent-session workspace path was invalid.",
                exception);
        }
        string? volumeRoot = Path.GetPathRoot(fullPath);
        if (
            string.IsNullOrWhiteSpace(volumeRoot) ||
            string.Equals(
                fullPath,
                Path.TrimEndingDirectorySeparator(volumeRoot),
                StringComparison.OrdinalIgnoreCase) ||
            fullPath[volumeRoot.Length..].Contains(':'))
        {
            throw new InvalidDataException(
                "A recent-session workspace path named a forbidden root or stream.");
        }
        return fullPath;
    }

    private static byte[] CreateEntropy() => SHA256.HashData(
        Encoding.UTF8.GetBytes(EntropyDomain));

    private static string GetDefaultRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "Windows LocalApplicationData is unavailable.");
        }
        return Path.Combine(
            localAppData,
            "JARVIS2",
            "PiAgent",
            "session-launcher");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ??
            throw new InvalidOperationException(
                "Recent-session storage path has no filesystem root.");
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     [
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar,
                     ],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }
            if (
                (File.GetAttributes(current) &
                    FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Recent-session storage may not traverse a reparse point: " +
                    current);
            }
        }
    }
}
