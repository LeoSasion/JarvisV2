using System.Text.Json;

namespace Jarvis.DesktopStyleSession;

internal sealed class DesktopStyleSessionJournal
{
    public int SchemaVersion { get; init; } = 1;

    public string ReceiptType { get; init; } =
        "jarvisv2-desktop-style-session";

    public required string RunId { get; init; }

    public required string Result { get; set; }

    public required string State { get; set; }

    public required string SessionPath { get; init; }

    public required DesktopHostIdentity Target { get; init; }

    public required string Preset { get; init; }

    public required string PreviewColorHex { get; init; }

    public required uint PreviewColorRef { get; init; }

    public required uint OriginalColorRef { get; init; }

    public required int TtlSeconds { get; init; }

    public required DateTimeOffset PreparedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public bool ApplyAttempted { get; set; }

    public bool MutationPerformed { get; set; }

    public bool RollbackAttempted { get; set; }

    public bool RollbackVerified { get; set; }

    public uint? LastObservedColorRef { get; set; }

    public string? Detail { get; set; }

    public bool ActivationPermitted { get; init; }

    public bool ExplorerRestartRequested { get; init; }

    public bool ProcessTerminationRequested { get; init; }

    public bool RegistryMutationRequested { get; init; }

    public string LiveExplorer { get; init; } = "controlled-desktop-session";
}

internal sealed class DesktopStyleSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string rootDirectory;
    private readonly string sessionsDirectory;

    public DesktopStyleSessionStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        rootDirectory = Path.GetFullPath(
            Path.Combine(localAppData, "JARVIS2", "DesktopStyle"));
        sessionsDirectory = Path.Combine(rootDirectory, "sessions");
    }

    public string ActiveSessionPath =>
        Path.Combine(rootDirectory, "active-session.json");

    public string NewSessionPath(string runId) =>
        Path.Combine(sessionsDirectory, $"{runId}.json");

    public void Prepare(DesktopStyleSessionJournal journal)
    {
        EnsureStorage();
        if (File.Exists(ActiveSessionPath))
        {
            DesktopStyleSessionJournal active = Load(ActiveSessionPath);
            if (active.State is "prepared" or "active" or "rollback-failed")
            {
                throw new InvalidOperationException(
                    "A non-terminal desktop style session already exists at " +
                    $"{ActiveSessionPath}.");
            }
        }

        WriteAtomic(journal.SessionPath, journal);
        WriteAtomic(ActiveSessionPath, journal);
    }

    public void Update(DesktopStyleSessionJournal journal)
    {
        EnsureStorage();
        WriteAtomic(journal.SessionPath, journal);
        WriteAtomic(ActiveSessionPath, journal);
    }

    public DesktopStyleSessionJournal Load(string path)
    {
        string safePath = ResolveSessionPath(path);
        string json = File.ReadAllText(safePath);
        DesktopStyleSessionJournal? journal =
            JsonSerializer.Deserialize<DesktopStyleSessionJournal>(
                json,
                SerializerOptions);
        return journal ??
            throw new InvalidDataException(
                "The desktop style session journal is empty.");
    }

    public string ResolveSessionPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string allowedPrefix = rootDirectory.TrimEnd(
            Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(
                allowedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Session paths must remain under the JARVIS2 DesktopStyle " +
                "directory.");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private void EnsureStorage()
    {
        Directory.CreateDirectory(sessionsDirectory);
        EnsureNoReparsePoints(sessionsDirectory);
    }

    private static void WriteAtomic(
        string path,
        DesktopStyleSessionJournal journal)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            journal,
            SerializerOptions);
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureNoReparsePoints(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ??
            throw new InvalidOperationException("Path has no root.");
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Desktop style evidence path is a reparse point: {current}");
            }
        }
    }
}
