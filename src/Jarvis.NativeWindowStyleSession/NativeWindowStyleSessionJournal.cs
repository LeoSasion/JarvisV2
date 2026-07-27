using System.Text.Json;

namespace Jarvis.NativeWindowStyleSession;

internal sealed class NativeWindowStyleSessionJournal
{
    public int SchemaVersion { get; init; } = 1;

    public string ReceiptType { get; init; } =
        "jarvisv2-native-window-style-session";

    public required string RunId { get; init; }

    public required string Result { get; set; }

    public required string State { get; set; }

    public required string SessionPath { get; init; }

    public required NativeWindowIdentity Target { get; init; }

    public required string Preset { get; init; }

    public required string BorderHex { get; init; }

    public required string CaptionHex { get; init; }

    public required string TextHex { get; init; }

    public required int TtlSeconds { get; init; }

    public required DateTimeOffset PreparedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public string BaselineContract { get; init; } =
        "new-window-system-default-colors";

    public bool ApplyAttempted { get; set; }

    public bool MutationPerformed { get; set; }

    public bool ResetAttempted { get; set; }

    public bool ResetApiSucceeded { get; set; }

    public IReadOnlyDictionary<string, int>? ApplyHResults { get; set; }

    public IReadOnlyDictionary<string, int>? ResetHResults { get; set; }

    public string? Detail { get; set; }

    public bool InjectionRequested { get; init; }

    public bool ExplorerRestartRequested { get; init; }

    public bool ProcessTerminationRequested { get; init; }

    public bool RegistryMutationRequested { get; init; }

    public string LiveExplorer { get; init; } =
        "controlled-external-window-session";
}

internal sealed class NativeWindowStyleSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string rootDirectory;
    private readonly string sessionsDirectory;

    public NativeWindowStyleSessionStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        rootDirectory = Path.GetFullPath(
            Path.Combine(
                localAppData,
                "JARVIS2",
                "NativeWindowStyle"));
        sessionsDirectory = Path.Combine(rootDirectory, "sessions");
    }

    public string ActiveSessionPath =>
        Path.Combine(rootDirectory, "active-session.json");

    public string NewSessionPath(string runId) =>
        Path.Combine(sessionsDirectory, $"{runId}.json");

    public void Prepare(NativeWindowStyleSessionJournal journal)
    {
        EnsureStorage();
        if (File.Exists(ActiveSessionPath))
        {
            NativeWindowStyleSessionJournal active = Load(ActiveSessionPath);
            if (active.State is "prepared" or "active" or "reset-failed")
            {
                throw new InvalidOperationException(
                    "A non-terminal native window style session already " +
                    $"exists at {ActiveSessionPath}.");
            }
        }

        WriteAtomic(journal.SessionPath, journal);
        WriteAtomic(ActiveSessionPath, journal);
    }

    public void Update(NativeWindowStyleSessionJournal journal)
    {
        EnsureStorage();
        WriteAtomic(journal.SessionPath, journal);
        WriteAtomic(ActiveSessionPath, journal);
    }

    public NativeWindowStyleSessionJournal Load(string path)
    {
        string safePath = ResolveSessionPath(path);
        NativeWindowStyleSessionJournal? journal =
            JsonSerializer.Deserialize<NativeWindowStyleSessionJournal>(
                File.ReadAllText(safePath),
                SerializerOptions);
        return journal ??
            throw new InvalidDataException(
                "The native window style journal is empty.");
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
                "Session paths must remain under the JARVIS2 " +
                "NativeWindowStyle directory.");
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
        NativeWindowStyleSessionJournal journal)
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
                    $"Native window evidence path is a reparse point: {current}");
            }
        }
    }
}
