using System.Text.Json;
using Jarvis.Win10.ExplorerCaptionPlan;

namespace Jarvis.Win10.ExplorerCaptionSession;

internal sealed class ExplorerCaptionSessionJournal
{
    public int SchemaVersion { get; init; } = 1;

    public string ReceiptType { get; init; } =
        "jarvisv2-win10-explorer-caption-session";

    public required string RunId { get; init; }

    public required string Result { get; set; }

    public required string State { get; set; }

    public required string SessionPath { get; init; }

    public required string HostProfileId { get; init; }

    public required ExplorerCaptionTargetIdentity Target { get; init; }

    public required int OriginalValue { get; init; }

    public int PreviewValue { get; init; } = 1;

    public required int TtlSeconds { get; init; }

    public required DateTimeOffset PreparedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public bool ApplyAttempted { get; set; }

    public bool MutationMayHaveOccurred { get; set; }

    public bool MutationPerformed { get; set; }

    public bool ApplyVerified { get; set; }

    public bool ApplyNonClientRefreshRequested { get; set; }

    public bool RollbackAttempted { get; set; }

    public bool RollbackVerified { get; set; }

    public bool RollbackNonClientRefreshRequested { get; set; }

    public int? LastObservedValue { get; set; }

    public string? Detail { get; set; }

    public bool InjectionRequested { get; init; }

    public bool ExplorerRestartRequested { get; init; }

    public bool ProcessTerminationRequested { get; init; }

    public bool RegistryMutationRequested { get; init; }

    public bool ModuleActivationPermitted { get; init; }

    public string LiveExplorer { get; init; } =
        "controlled-single-window-session";
}

internal sealed class ExplorerCaptionSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string rootDirectory;
    private readonly string sessionsDirectory;

    public ExplorerCaptionSessionStore()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        rootDirectory = Path.GetFullPath(
            Path.Combine(
                localAppData,
                "JARVIS2",
                "ExplorerCaption"));
        sessionsDirectory =
            Path.Combine(rootDirectory, "sessions");
    }

    public string ActiveSessionPath =>
        Path.Combine(rootDirectory, "active-session.json");

    public string NewSessionPath(string runId) =>
        Path.Combine(sessionsDirectory, $"{runId}.json");

    public void Prepare(ExplorerCaptionSessionJournal journal)
    {
        EnsureStorage();
        string sessionPath = ResolveJournalSessionPath(journal);
        string activePath = ResolveSessionPath(ActiveSessionPath);
        if (File.Exists(activePath))
        {
            ExplorerCaptionSessionJournal active =
                Load(activePath);
            if (active.State is
                "prepared" or "active" or "rollback-failed")
            {
                throw new InvalidOperationException(
                    "A non-terminal Explorer caption session already " +
                    $"exists at {activePath}.");
            }
        }

        WriteAtomic(sessionPath, journal);
        WriteAtomic(activePath, journal);
    }

    public void Update(ExplorerCaptionSessionJournal journal)
    {
        EnsureStorage();
        string sessionPath = ResolveJournalSessionPath(journal);
        string activePath = ResolveSessionPath(ActiveSessionPath);
        WriteAtomic(sessionPath, journal);
        WriteAtomic(activePath, journal);
    }

    public ExplorerCaptionSessionJournal Load(string path)
    {
        string safePath = ResolveSessionPath(path);
        string json = File.ReadAllText(safePath);
        ExplorerCaptionSessionJournal? journal =
            JsonSerializer.Deserialize<ExplorerCaptionSessionJournal>(
                json,
                SerializerOptions);
        if (journal is null)
        {
            throw new InvalidDataException(
                "The Explorer caption session journal is empty.");
        }
        string journalSessionPath = ResolveJournalSessionPath(journal);
        string activePath = ResolveSessionPath(ActiveSessionPath);
        if (!string.Equals(
                safePath,
                activePath,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                safePath,
                journalSessionPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Explorer caption journal path does not match its " +
                "canonical session identity.");
        }

        return journal;
    }

    private string ResolveJournalSessionPath(
        ExplorerCaptionSessionJournal journal)
    {
        if (string.IsNullOrWhiteSpace(journal.RunId) ||
            journal.RunId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            journal.RunId.Contains(Path.DirectorySeparatorChar) ||
            journal.RunId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                "The Explorer caption journal run ID is not a file name.");
        }

        string declaredPath = ResolveSessionPath(journal.SessionPath);
        string expectedPath = ResolveSessionPath(
            Path.Combine(sessionsDirectory, $"{journal.RunId}.json"));
        if (!string.Equals(
                declaredPath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Explorer caption journal session path is not bound " +
                "to its run ID.");
        }

        return expectedPath;
    }

    private string ResolveSessionPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string allowedPrefix = rootDirectory.TrimEnd(
            Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(
                allowedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Session paths must remain under the JARVIS2 " +
                "ExplorerCaption directory.");
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
        ExplorerCaptionSessionJournal journal)
    {
        string temporaryPath =
            $"{path}.{Guid.NewGuid():N}.tmp";
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

            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Explorer caption evidence path is a reparse point: " +
                    current);
            }
        }
    }
}
