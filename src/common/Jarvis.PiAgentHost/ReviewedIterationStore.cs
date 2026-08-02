using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentReviewedIterationStoreReceipt(
    int SchemaVersion,
    string ReceiptType,
    string WorkspaceId,
    string IterationId,
    string ReceiptPath,
    int Revision,
    int StepCount,
    int EnvelopeBytes,
    DateTimeOffset SavedAtUtc);

public sealed class PiAgentReviewedIterationStore
{
    public const string StoreModel =
        "current-user-dpapi-atomic-workspace-bound-durable-receipts";
    public const int MaximumPayloadBytes = 196_608;
    public const int MaximumEnvelopeBytes = 262_144;

    private const string EnvelopeReceiptType =
        "jarvisv2-pi-agent-reviewed-iteration";
    private const string EntropyDomain =
        "JARVIS2/pi-agent-reviewed-iteration/v1";

    private sealed class IterationEnvelope
    {
        public int SchemaVersion { get; init; }
        public string ReceiptType { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public string IterationId { get; init; } = string.Empty;
        public int Revision { get; init; }
        public DateTimeOffset SavedAtUtc { get; init; }
        public byte[] ProtectedPayload { get; init; } = [];
    }

    private sealed record WorkspaceBinding(
        string WorkspaceId,
        byte[] Entropy);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string rootDirectory;
    private readonly SemaphoreSlim ioGate = new(1, 1);

    public PiAgentReviewedIterationStore()
        : this(GetDefaultRootDirectory())
    {
    }

    public PiAgentReviewedIterationStore(string rootDirectory)
    {
        if (
            string.IsNullOrWhiteSpace(rootDirectory) ||
            !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "Reviewed iteration storage requires an absolute root.",
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
                "Reviewed iteration storage may not use a filesystem root.",
                nameof(rootDirectory));
        }
    }

    public string RootDirectory => rootDirectory;

    public async Task<PiAgentReviewedIterationSnapshot?> LoadLatestAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        WorkspaceBinding binding = BindWorkspace(workspaceRoot);
        bool gateHeld = false;
        try
        {
            await ioGate.WaitAsync(cancellationToken);
            gateHeld = true;
            string workspaceDirectory = Path.Combine(
                rootDirectory,
                binding.WorkspaceId);
            if (!Directory.Exists(workspaceDirectory))
            {
                if (File.Exists(workspaceDirectory))
                {
                    throw new InvalidDataException(
                        "The reviewed iteration workspace store is a file.");
                }
                return null;
            }
            EnsureNoReparsePoints(workspaceDirectory);
            string[] candidates = Directory.EnumerateFiles(
                    workspaceDirectory,
                    "review-loop-*.j2iteration",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Take(257)
                .ToArray();
            if (candidates.Length > 256)
            {
                throw new InvalidDataException(
                    "The reviewed iteration receipt count exceeded its admission boundary.");
            }
            return candidates.Length == 0
                ? null
                : await LoadPathAsync(
                    candidates[0],
                    binding,
                    cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(binding.Entropy);
            if (gateHeld)
            {
                ioGate.Release();
            }
        }
    }

    public async Task<PiAgentReviewedIterationStoreReceipt> SaveAsync(
        string workspaceRoot,
        PiAgentReviewedIterationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        PiAgentReviewedIterationSnapshot admitted =
            PiAgentReviewedIterationAdmission.Admit(snapshot);
        WorkspaceBinding binding = BindWorkspace(workspaceRoot);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            admitted,
            SerializerOptions);
        byte[] protectedPayload = [];
        bool gateHeld = false;
        try
        {
            await ioGate.WaitAsync(cancellationToken);
            gateHeld = true;
            string workspaceDirectory = Path.Combine(
                rootDirectory,
                binding.WorkspaceId);
            Directory.CreateDirectory(workspaceDirectory);
            EnsureNoReparsePoints(workspaceDirectory);
            protectedPayload = ProtectedData.Protect(
                plaintext,
                binding.Entropy,
                DataProtectionScope.CurrentUser);
            DateTimeOffset savedAtUtc = DateTimeOffset.UtcNow;
            IterationEnvelope envelope = new()
            {
                SchemaVersion = 1,
                ReceiptType = EnvelopeReceiptType,
                WorkspaceId = binding.WorkspaceId,
                IterationId = admitted.IterationId,
                Revision = admitted.Revision,
                SavedAtUtc = savedAtUtc,
                ProtectedPayload = protectedPayload,
            };
            byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                SerializerOptions);
            if (envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidOperationException(
                    "The encrypted reviewed iteration envelope exceeded its size boundary.");
            }

            string receiptPath = Path.Combine(
                workspaceDirectory,
                $"{admitted.IterationId}.j2iteration");
            if (Directory.Exists(receiptPath))
            {
                throw new InvalidDataException(
                    "The reviewed iteration receipt path is a directory.");
            }
            if (File.Exists(receiptPath))
            {
                EnsureNoReparsePoints(receiptPath);
            }
            string temporaryPath = Path.Combine(
                workspaceDirectory,
                $".{admitted.IterationId}.{Guid.NewGuid():N}.tmp");
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
                File.Move(temporaryPath, receiptPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new PiAgentReviewedIterationStoreReceipt(
                1,
                "jarvisv2-pi-agent-reviewed-iteration-store",
                binding.WorkspaceId,
                admitted.IterationId,
                receiptPath,
                admitted.Revision,
                admitted.Steps.Count,
                envelopeBytes.Length,
                savedAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedPayload);
            CryptographicOperations.ZeroMemory(binding.Entropy);
            if (gateHeld)
            {
                ioGate.Release();
            }
        }
    }

    private static async Task<PiAgentReviewedIterationSnapshot> LoadPathAsync(
        string receiptPath,
        WorkspaceBinding binding,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(receiptPath);
        FileInfo file = new(receiptPath);
        if (file.Length <= 0 || file.Length > MaximumEnvelopeBytes)
        {
            throw new InvalidDataException(
                "The reviewed iteration envelope failed its size boundary.");
        }
        byte[] bytes = await File.ReadAllBytesAsync(
            receiptPath,
            cancellationToken);
        IterationEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IterationEnvelope>(
                    bytes,
                    SerializerOptions) ??
                throw new InvalidDataException(
                    "The reviewed iteration envelope was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The reviewed iteration envelope was malformed.",
                exception);
        }
        if (
            envelope.SchemaVersion != 1 ||
            envelope.ReceiptType != EnvelopeReceiptType ||
            envelope.WorkspaceId != binding.WorkspaceId ||
            envelope.ProtectedPayload is not { Length: > 0 } ||
            envelope.SavedAtUtc == default ||
            !string.Equals(
                Path.GetFileNameWithoutExtension(receiptPath),
                envelope.IterationId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The reviewed iteration envelope failed workspace or schema admission.");
        }

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(
                envelope.ProtectedPayload,
                binding.Entropy,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "The reviewed iteration receipt could not be opened for this user and workspace.",
                exception);
        }
        try
        {
            PiAgentReviewedIterationSnapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<
                        PiAgentReviewedIterationSnapshot>(
                        plaintext,
                        SerializerOptions) ??
                    throw new InvalidDataException(
                        "The reviewed iteration payload was empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The reviewed iteration payload was malformed.",
                    exception);
            }
            PiAgentReviewedIterationSnapshot admitted =
                PiAgentReviewedIterationAdmission.Admit(snapshot);
            if (
                admitted.IterationId != envelope.IterationId ||
                admitted.Revision != envelope.Revision)
            {
                throw new InvalidDataException(
                    "The reviewed iteration envelope diverged from its payload.");
            }
            return admitted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static WorkspaceBinding BindWorkspace(string workspaceRoot)
    {
        if (
            string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Path.IsPathFullyQualified(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "Reviewed iteration storage requires an existing absolute workspace.",
                nameof(workspaceRoot));
        }
        string canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        byte[] bindingBytes = Encoding.UTF8.GetBytes(
            $"{EntropyDomain}\0{canonicalRoot.ToUpperInvariant()}");
        byte[] workspaceBytes = Encoding.UTF8.GetBytes(
            canonicalRoot.ToUpperInvariant());
        try
        {
            return new WorkspaceBinding(
                Convert.ToHexString(SHA256.HashData(workspaceBytes))
                    .ToLowerInvariant(),
                SHA256.HashData(bindingBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bindingBytes);
            CryptographicOperations.ZeroMemory(workspaceBytes);
        }
    }

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
            "reviewed-iterations");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ??
            throw new InvalidOperationException(
                "Reviewed iteration storage path has no root.");
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
                throw new InvalidOperationException(
                    "Reviewed iteration storage may not traverse a reparse point: " +
                    current);
            }
        }
    }
}
