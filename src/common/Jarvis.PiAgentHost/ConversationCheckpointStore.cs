using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.PiAgentHost;

public sealed record PiAgentConversationCheckpointStoreReceipt(
    int SchemaVersion,
    string ReceiptType,
    string WorkspaceId,
    string CheckpointPath,
    int TurnCount,
    int EnvelopeBytes,
    DateTimeOffset SavedAtUtc);

public sealed class PiAgentConversationCheckpointStore
{
    public const string StoreModel =
        "current-user-dpapi-atomic-workspace-bound";
    public const string SaveModel =
        "write-through-temp-then-atomic-replace";
    public const int MaximumEnvelopeBytes = 65_536;

    private const string EnvelopeReceiptType =
        "jarvisv2-pi-agent-conversation-checkpoint";
    private const string EntropyDomain =
        "JARVIS2/pi-agent-conversation-checkpoint/v1";

    private sealed class CheckpointEnvelope
    {
        public int SchemaVersion { get; init; }
        public string ReceiptType { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
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

    public PiAgentConversationCheckpointStore()
        : this(GetDefaultRootDirectory())
    {
    }

    public PiAgentConversationCheckpointStore(string rootDirectory)
    {
        if (
            string.IsNullOrWhiteSpace(rootDirectory) ||
            !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "Checkpoint storage requires an absolute root directory.",
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
                "Checkpoint storage may not use a filesystem root.",
                nameof(rootDirectory));
        }
    }

    public string RootDirectory => rootDirectory;

    public string GetCheckpointPath(string workspaceRoot)
    {
        WorkspaceBinding binding = BindWorkspace(workspaceRoot);
        try
        {
            return Path.Combine(
                rootDirectory,
                $"{binding.WorkspaceId}.j2checkpoint");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(binding.Entropy);
        }
    }

    public async Task<PiAgentConversationCheckpoint?> LoadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        WorkspaceBinding binding = BindWorkspace(workspaceRoot);
        bool gateHeld = false;
        try
        {
            await ioGate.WaitAsync(cancellationToken);
            gateHeld = true;
            string checkpointPath = Path.Combine(
                rootDirectory,
                $"{binding.WorkspaceId}.j2checkpoint");
            if (!Directory.Exists(rootDirectory))
            {
                if (File.Exists(rootDirectory))
                {
                    throw new InvalidDataException(
                        "The conversation checkpoint storage root is a " +
                        "file instead of a directory.");
                }
                return null;
            }
            EnsureNoReparsePoints(rootDirectory);
            if (!File.Exists(checkpointPath))
            {
                if (Directory.Exists(checkpointPath))
                {
                    throw new InvalidDataException(
                        "The conversation checkpoint path is a directory " +
                        "instead of an envelope.");
                }
                return null;
            }
            EnsureNoReparsePoints(checkpointPath);

            FileInfo checkpointFile = new(checkpointPath);
            if (
                checkpointFile.Length <= 0 ||
                checkpointFile.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The encrypted conversation checkpoint envelope " +
                    "failed its size boundary.");
            }

            byte[] envelopeBytes = await File.ReadAllBytesAsync(
                checkpointPath,
                cancellationToken);
            if (
                envelopeBytes.Length <= 0 ||
                envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The encrypted conversation checkpoint changed " +
                    "outside its admitted size boundary.");
            }

            CheckpointEnvelope envelope;
            try
            {
                envelope =
                    JsonSerializer.Deserialize<CheckpointEnvelope>(
                        envelopeBytes,
                        SerializerOptions) ??
                    throw new InvalidDataException(
                        "The encrypted conversation checkpoint envelope " +
                        "was empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The encrypted conversation checkpoint envelope " +
                    "was malformed.",
                    exception);
            }

            if (
                envelope.SchemaVersion != 1 ||
                envelope.ReceiptType != EnvelopeReceiptType ||
                envelope.WorkspaceId != binding.WorkspaceId ||
                envelope.ProtectedPayload is not { Length: > 0 } ||
                envelope.SavedAtUtc == default)
            {
                throw new InvalidDataException(
                    "The encrypted conversation checkpoint envelope " +
                    "failed workspace or schema admission.");
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
                    "The encrypted conversation checkpoint could not be " +
                    "opened for this Windows user and workspace.",
                    exception);
            }

            try
            {
                PiAgentConversationCheckpoint checkpoint;
                try
                {
                    checkpoint =
                        JsonSerializer.Deserialize<
                            PiAgentConversationCheckpoint>(
                            plaintext,
                            SerializerOptions) ??
                        throw new InvalidDataException(
                            "The decrypted conversation checkpoint was " +
                            "empty.");
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        "The decrypted conversation checkpoint was " +
                        "malformed.",
                        exception);
                }
                return PiAgentConversationState.AdmitCheckpoint(
                    checkpoint);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
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

    public async Task<PiAgentConversationCheckpointStoreReceipt> SaveAsync(
        string workspaceRoot,
        PiAgentConversationCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        PiAgentConversationCheckpoint admitted =
            PiAgentConversationState.AdmitCheckpoint(checkpoint) ??
            throw new ArgumentException(
                "A non-null conversation checkpoint is required.",
                nameof(checkpoint));
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
            Directory.CreateDirectory(rootDirectory);
            EnsureNoReparsePoints(rootDirectory);
            protectedPayload = ProtectedData.Protect(
                plaintext,
                binding.Entropy,
                DataProtectionScope.CurrentUser);
            DateTimeOffset savedAtUtc = DateTimeOffset.UtcNow;
            CheckpointEnvelope envelope = new()
            {
                SchemaVersion = 1,
                ReceiptType = EnvelopeReceiptType,
                WorkspaceId = binding.WorkspaceId,
                SavedAtUtc = savedAtUtc,
                ProtectedPayload = protectedPayload,
            };
            byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                SerializerOptions);
            if (envelopeBytes.Length > MaximumEnvelopeBytes)
            {
                throw new InvalidOperationException(
                    "The encrypted conversation checkpoint envelope " +
                    "exceeded its reviewed size boundary.");
            }

            string checkpointPath = Path.Combine(
                rootDirectory,
                $"{binding.WorkspaceId}.j2checkpoint");
            if (Directory.Exists(checkpointPath))
            {
                throw new InvalidDataException(
                    "The conversation checkpoint path is a directory " +
                    "instead of an envelope.");
            }
            if (File.Exists(checkpointPath))
            {
                EnsureNoReparsePoints(checkpointPath);
            }
            string temporaryPath = Path.Combine(
                rootDirectory,
                $".{binding.WorkspaceId}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous |
                        FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(
                        envelopeBytes,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(
                    temporaryPath,
                    checkpointPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new PiAgentConversationCheckpointStoreReceipt(
                1,
                "jarvisv2-pi-agent-conversation-checkpoint-store",
                binding.WorkspaceId,
                checkpointPath,
                admitted.Turns.Count,
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

    private static WorkspaceBinding BindWorkspace(string workspaceRoot)
    {
        if (
            string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Path.IsPathFullyQualified(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "Checkpoint storage requires an existing absolute " +
                "workspace root.",
                nameof(workspaceRoot));
        }
        string canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        string bindingText =
            $"{EntropyDomain}\0{canonicalRoot.ToUpperInvariant()}";
        byte[] bindingBytes =
            Encoding.UTF8.GetBytes(bindingText);
        byte[] workspaceBytes = Encoding.UTF8.GetBytes(
            canonicalRoot.ToUpperInvariant());
        try
        {
            byte[] entropy = SHA256.HashData(bindingBytes);
            string workspaceId = Convert.ToHexString(
                SHA256.HashData(workspaceBytes))
                .ToLowerInvariant();
            return new WorkspaceBinding(
                workspaceId,
                entropy);
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
            "conversations");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ??
            throw new InvalidOperationException(
                "Checkpoint storage path has no filesystem root.");
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
                    "Conversation checkpoint storage may not traverse " +
                    $"a reparse point: {current}");
            }
        }
    }
}
