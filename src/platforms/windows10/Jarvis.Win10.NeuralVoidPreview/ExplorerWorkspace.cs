namespace Jarvis.Win10.NeuralVoidPreview;

internal enum ExplorerWorkspaceItemKind
{
    Folder,
    File,
}

internal enum ExplorerOpenDisposition
{
    NoSelection,
    Navigated,
    PreviewRequested,
}

internal enum ExplorerRenameError
{
    None,
    NoSelection,
    NoActiveRename,
    EmptyName,
    NameTooLong,
    InvalidCharacter,
    TrailingSpaceOrPeriod,
    ReservedName,
    DuplicateName,
}

internal sealed record ExplorerBreadcrumbSegment(
    string Id,
    string Name);

internal sealed record ExplorerWorkspaceLocation(
    string Id,
    string Name,
    string IconKey);

internal sealed record ExplorerWorkspaceItem(
    string Id,
    string Name,
    ExplorerWorkspaceItemKind Kind,
    string TypeLabel,
    string IconKey,
    long? SizeBytes,
    DateTimeOffset ModifiedAt,
    bool IsSelected,
    bool IsRenameTarget,
    bool IsPreviewable);

internal sealed record ExplorerWorkspaceSnapshot(
    ExplorerWorkspaceLocation CurrentLocation,
    IReadOnlyList<ExplorerBreadcrumbSegment> Breadcrumb,
    IReadOnlyList<ExplorerWorkspaceItem> Items,
    string? SelectedItemId,
    string? RenameItemId,
    bool CanGoBack,
    bool CanGoForward,
    bool CanGoUp,
    bool IsEmpty);

internal sealed record ExplorerPreviewRequest(
    string ItemId,
    string DisplayName,
    string TypeLabel,
    string PreviewKind,
    string Owner,
    bool OwnProcessOnly,
    bool OperatingSystemLaunchSupported,
    bool RealFileSystemAccessSupported);

internal sealed record ExplorerOpenResult(
    ExplorerOpenDisposition Disposition,
    string CurrentLocationId,
    ExplorerPreviewRequest? PreviewRequest);

internal sealed record ExplorerRenameResult(
    bool Succeeded,
    ExplorerRenameError Error,
    string? ItemId,
    string? Name,
    string Message);

/// <summary>
/// Deterministic, memory-only Explorer session used by the own-process preview.
/// It contains no filesystem paths and never delegates an open request to Windows.
/// </summary>
internal sealed class ExplorerWorkspace
{
    public const int MaximumNameLength = 255;
    public const string PreviewOwner = "JARVIS2.NEURAL_VOID_PREVIEW";
    public const string ThisPcId = "location:this-pc";
    public const string NeuralStorageId = "location:neural-storage";
    public const string DocumentsId = "location:documents";
    public const string ProjectsId = "location:projects";
    public const string ArchiveId = "location:archive";
    public const string ProjectBriefId = "file:project-brief-0826";
    public const string SpecificationsId = "file:specifications-v2";
    public const string LongResearchFileId = "file:horizon-research-matrix";

    private static readonly DateTimeOffset SessionEpoch =
        new(2026, 8, 4, 14, 39, 0, TimeSpan.Zero);

    private static readonly string[] ReservedDeviceNames =
    [
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    ];

    private readonly Dictionary<string, Node> _nodes =
        new(StringComparer.Ordinal);
    private readonly List<string> _backHistory = [];
    private readonly List<string> _forwardHistory = [];
    private string _currentLocationId = DocumentsId;
    private string? _selectedItemId;
    private string? _renameItemId;
    private int _nextFolderSequence = 1;

    public ExplorerWorkspace()
    {
        SeedVirtualTree();
    }

    public event EventHandler? Changed;

    public string CurrentLocationId => _currentLocationId;

    public string? SelectedItemId => _selectedItemId;

    public string? RenameItemId => _renameItemId;

    public bool CanGoBack => _backHistory.Count > 0;

    public bool CanGoForward => _forwardHistory.Count > 0;

    public bool CanGoUp => CurrentNode.ParentId is not null;

    public bool IsCurrentLocationEmpty => CurrentNode.Children.Count == 0;

    public ExplorerWorkspaceLocation CurrentLocation =>
        new(CurrentNode.Id, CurrentNode.Name, CurrentNode.IconKey);

    public IReadOnlyList<ExplorerBreadcrumbSegment> Breadcrumb
    {
        get
        {
            List<ExplorerBreadcrumbSegment> segments = [];
            Node? cursor = CurrentNode;
            while (cursor is not null)
            {
                segments.Add(new(cursor.Id, cursor.Name));
                cursor = cursor.ParentId is null
                    ? null
                    : RequireNode(cursor.ParentId);
            }

            segments.Reverse();
            return segments;
        }
    }

    public IReadOnlyList<ExplorerWorkspaceItem> Items =>
        CurrentNode.Children
            .Select(id => ToItem(RequireNode(id)))
            .ToArray();

    private Node CurrentNode => RequireNode(_currentLocationId);

    public ExplorerWorkspaceSnapshot CaptureSnapshot()
    {
        IReadOnlyList<ExplorerWorkspaceItem> items = Items;
        return new(
            CurrentLocation,
            Breadcrumb,
            items,
            _selectedItemId,
            _renameItemId,
            CanGoBack,
            CanGoForward,
            CanGoUp,
            items.Count == 0);
    }

    public bool TryGetItem(
        string? itemId,
        out ExplorerWorkspaceItem? item)
    {
        if (itemId is not null &&
            _nodes.TryGetValue(itemId, out Node? node))
        {
            item = ToItem(node);
            return true;
        }

        item = null;
        return false;
    }

    public bool SelectItem(string? itemId)
    {
        bool isChild = itemId is not null &&
            CurrentNode.Children.Contains(itemId, StringComparer.Ordinal);
        if (!isChild)
        {
            ClearSelection();
            return false;
        }

        bool selectionChanged = !StringComparer.Ordinal.Equals(
            _selectedItemId,
            itemId);
        if (selectionChanged)
        {
            _selectedItemId = itemId;
            _renameItemId = null;
            NotifyChanged();
        }

        return true;
    }

    public void ClearSelection()
    {
        bool changed = _selectedItemId is not null ||
            _renameItemId is not null;
        ClearInteractionState();
        NotifyChangedIf(changed);
    }

    public bool TryNavigateTo(string? locationId)
    {
        if (locationId is null ||
            !_nodes.TryGetValue(locationId, out Node? destination) ||
            destination.Kind != ExplorerWorkspaceItemKind.Folder ||
            StringComparer.Ordinal.Equals(locationId, _currentLocationId))
        {
            return false;
        }

        _backHistory.Add(_currentLocationId);
        _forwardHistory.Clear();
        SetCurrentLocation(locationId);
        return true;
    }

    public bool TryGoBack() =>
        TryMoveHistory(_backHistory, _forwardHistory);

    public bool TryGoForward() =>
        TryMoveHistory(_forwardHistory, _backHistory);

    private bool TryMoveHistory(
        List<string> source,
        List<string> destinationHistory)
    {
        if (source.Count == 0)
        {
            return false;
        }

        string destination = Pop(source);
        destinationHistory.Add(_currentLocationId);
        SetCurrentLocation(destination);
        return true;
    }

    public bool TryGoUp()
    {
        string? parentId = CurrentNode.ParentId;
        return parentId is not null && TryNavigateTo(parentId);
    }

    public ExplorerOpenResult OpenItem(string? itemId)
    {
        return SelectItem(itemId)
            ? OpenSelected()
            : NoSelectionResult();
    }

    public ExplorerOpenResult OpenSelected()
    {
        if (_selectedItemId is null ||
            !CurrentNode.Children.Contains(
                _selectedItemId,
                StringComparer.Ordinal) ||
            !_nodes.TryGetValue(_selectedItemId, out Node? selected))
        {
            ClearSelection();
            return NoSelectionResult();
        }

        if (selected.Kind == ExplorerWorkspaceItemKind.Folder)
        {
            _ = TryNavigateTo(selected.Id);
            return new(
                ExplorerOpenDisposition.Navigated,
                _currentLocationId,
                null);
        }

        ExplorerPreviewRequest request = new(
            selected.Id,
            selected.Name,
            selected.TypeLabel,
            selected.PreviewKind,
            PreviewOwner,
            true,
            false,
            false);
        return new(
            ExplorerOpenDisposition.PreviewRequested,
            _currentLocationId,
            request);
    }

    public ExplorerRenameResult BeginRenameSelected()
    {
        if (_selectedItemId is null ||
            !CurrentNode.Children.Contains(
                _selectedItemId,
                StringComparer.Ordinal))
        {
            return RenameFailure(
                ExplorerRenameError.NoSelection,
                "Select an item before renaming it.");
        }

        bool changed = !StringComparer.Ordinal.Equals(
            _renameItemId,
            _selectedItemId);
        _renameItemId = _selectedItemId;
        NotifyChangedIf(changed);
        Node target = RequireNode(_renameItemId);
        return new(
            true,
            ExplorerRenameError.None,
            target.Id,
            target.Name,
            "Rename started.");
    }

    public ExplorerRenameResult CommitRename(string? proposedName)
    {
        if (_renameItemId is null ||
            !_nodes.TryGetValue(_renameItemId, out Node? target) ||
            target.ParentId is null)
        {
            return RenameFailure(
                ExplorerRenameError.NoActiveRename,
                "No rename operation is active.");
        }

        ExplorerRenameError error = ValidateName(target, proposedName);
        if (error != ExplorerRenameError.None)
        {
            return RenameFailure(error, RenameErrorMessage(error));
        }

        target.Name = proposedName!;
        if (target.Kind == ExplorerWorkspaceItemKind.File)
        {
            ApplyFilePresentation(target, target.Name);
        }

        _renameItemId = null;
        NotifyChanged();
        return new(
            true,
            ExplorerRenameError.None,
            target.Id,
            target.Name,
            "Rename committed.");
    }

    public bool CancelRename()
    {
        if (_renameItemId is null)
        {
            return false;
        }

        _renameItemId = null;
        NotifyChanged();
        return true;
    }

    public ExplorerWorkspaceItem CreateUniqueFolder()
    {
        string name = UniqueFolderName(CurrentNode);
        int sequence = _nextFolderSequence++;
        string id = $"session:new-folder:{sequence:D4}";
        Node folder = AddFolder(
            id,
            name,
            CurrentNode.Id,
            "FOLDER",
            "folder",
            SessionEpoch.AddSeconds(sequence));
        _selectedItemId = folder.Id;
        _renameItemId = folder.Id;
        NotifyChanged();
        return ToItem(folder);
    }

    private void SeedVirtualTree()
    {
        AddFolder(
            ThisPcId,
            "THIS PC",
            null,
            "ROOT",
            "this-pc",
            Utc(2026, 8, 1, 9, 0));
        AddFolder(
            NeuralStorageId,
            "NEURAL STORAGE",
            ThisPcId,
            "STORAGE",
            "neural-storage",
            Utc(2026, 8, 1, 9, 1));
        AddFolder(
            DocumentsId,
            "DOCUMENTS",
            NeuralStorageId,
            "FOLDER",
            "documents",
            Utc(2026, 8, 4, 13, 42));
        AddFolder(
            ProjectsId,
            "PROJECTS",
            NeuralStorageId,
            "FOLDER",
            "projects",
            Utc(2026, 8, 3, 18, 20));
        AddFolder(
            ArchiveId,
            "ARCHIVE",
            NeuralStorageId,
            "FOLDER",
            "archive",
            Utc(2026, 7, 29, 16, 18));

        AddFile(
            ProjectBriefId,
            "PROJECT_BRIEF_0826.DOCX",
            DocumentsId,
            2_400_000,
            Utc(2026, 8, 4, 10, 21));
        AddFile(
            SpecificationsId,
            "SPECIFICATIONS_V2.PDF",
            DocumentsId,
            1_800_000,
            Utc(2026, 8, 3, 15, 42));
        AddFile(
            "file:report-draft",
            "REPORT_DRAFT.PPTX",
            DocumentsId,
            5_700_000,
            Utc(2026, 8, 2, 11, 9));
        AddFile(
            "file:data-sheet",
            "DATA_SHEET.XLSX",
            DocumentsId,
            1_200_000,
            Utc(2026, 8, 1, 9, 33));
        AddFile(
            "file:meeting-notes",
            "MEETING_NOTES_0804.TXT",
            DocumentsId,
            18_000,
            Utc(2026, 8, 4, 8, 47));
        AddFile(
            "file:site-map",
            "SITE_MAP.VIS",
            DocumentsId,
            640_000,
            Utc(2026, 8, 2, 17, 26));

        AddFolder(
            "folder:aurora-engine",
            "AURORA ENGINE",
            ProjectsId,
            "FOLDER",
            "project-folder",
            Utc(2026, 8, 4, 12, 4));
        AddFile(
            LongResearchFileId,
            "HORIZON_MEMBRANE_SPATIAL_COMPUTE_RESEARCH_NOTES_" +
                "AND_IMPLEMENTATION_MATRIX_2026-08-04.MD",
            ProjectsId,
            84_000,
            Utc(2026, 8, 4, 12, 28));
    }

    private void AddFile(
        string id,
        string name,
        string parentId,
        long sizeBytes,
        DateTimeOffset modifiedAt)
    {
        Node file = AddNode(
            id,
            name,
            ExplorerWorkspaceItemKind.File,
            parentId,
            string.Empty,
            string.Empty,
            sizeBytes,
            modifiedAt,
            string.Empty);
        ApplyFilePresentation(file, name);
    }

    private Node AddFolder(
        string id,
        string name,
        string? parentId,
        string typeLabel,
        string iconKey,
        DateTimeOffset modifiedAt) =>
        AddNode(
            id,
            name,
            ExplorerWorkspaceItemKind.Folder,
            parentId,
            typeLabel,
            iconKey,
            null,
            modifiedAt,
            string.Empty);

    private Node AddNode(
        string id,
        string name,
        ExplorerWorkspaceItemKind kind,
        string? parentId,
        string typeLabel,
        string iconKey,
        long? sizeBytes,
        DateTimeOffset modifiedAt,
        string previewKind)
    {
        Node node = new(
            id,
            name,
            kind,
            parentId,
            typeLabel,
            iconKey,
            sizeBytes,
            modifiedAt,
            previewKind);
        _nodes.Add(id, node);
        if (parentId is not null)
        {
            RequireNode(parentId).Children.Add(id);
        }

        return node;
    }

    private static void ApplyFilePresentation(Node target, string name)
    {
        int separator = name.LastIndexOf('.');
        string extension = separator > 0 && separator < name.Length - 1
            ? name[(separator + 1)..].ToUpperInvariant()
            : string.Empty;
        (string typeLabel, string iconKey, string previewKind) = extension switch
        {
            "DOCX" => ("DOCX", "document", "document-outline"),
            "PDF" => ("PDF", "pdf", "pdf-outline"),
            "PPTX" => ("PPTX", "presentation", "presentation-outline"),
            "XLSX" => ("XLSX", "spreadsheet", "spreadsheet-outline"),
            "TXT" => ("TXT", "text", "text-outline"),
            "MD" => ("MD", "text", "text-outline"),
            "VIS" => ("VISUAL", "visual", "visual-outline"),
            _ => (
                extension.Length is > 0 and <= 12 ? extension : "FILE",
                "document",
                "document-outline"),
        };
        target.TypeLabel = typeLabel;
        target.IconKey = iconKey;
        target.PreviewKind = previewKind;
    }

    private ExplorerRenameError ValidateName(
        Node target,
        string? proposedName)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            return ExplorerRenameError.EmptyName;
        }

        if (proposedName.Length > MaximumNameLength)
        {
            return ExplorerRenameError.NameTooLong;
        }

        if (proposedName is "." or "..")
        {
            return ExplorerRenameError.ReservedName;
        }

        if (proposedName.EndsWith(' ') || proposedName.EndsWith('.'))
        {
            return ExplorerRenameError.TrailingSpaceOrPeriod;
        }

        if (proposedName.Any(character =>
                char.IsControl(character) ||
                "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal)))
        {
            return ExplorerRenameError.InvalidCharacter;
        }

        int separator = proposedName.IndexOf('.');
        string deviceStem = separator < 0
            ? proposedName
            : proposedName[..separator];
        if (ReservedDeviceNames.Contains(
                deviceStem,
                StringComparer.OrdinalIgnoreCase))
        {
            return ExplorerRenameError.ReservedName;
        }

        Node parent = RequireNode(target.ParentId!);
        bool duplicate = parent.Children
            .Where(id => !StringComparer.Ordinal.Equals(id, target.Id))
            .Select(RequireNode)
            .Any(sibling => StringComparer.OrdinalIgnoreCase.Equals(
                sibling.Name,
                proposedName));
        return duplicate
            ? ExplorerRenameError.DuplicateName
            : ExplorerRenameError.None;
    }

    private static string RenameErrorMessage(ExplorerRenameError error) =>
        error switch
        {
            ExplorerRenameError.EmptyName => "A name is required.",
            ExplorerRenameError.NameTooLong =>
                $"Names may contain at most {MaximumNameLength} characters.",
            ExplorerRenameError.InvalidCharacter =>
                "The name contains a control or reserved character.",
            ExplorerRenameError.TrailingSpaceOrPeriod =>
                "Names cannot end with a space or period.",
            ExplorerRenameError.ReservedName =>
                "The name is reserved by Windows.",
            ExplorerRenameError.DuplicateName =>
                "An item with that name already exists in this location.",
            _ => "Rename could not be completed.",
        };

    private static ExplorerRenameResult RenameFailure(
        ExplorerRenameError error,
        string message) =>
        new(false, error, null, null, message);

    private string UniqueFolderName(Node parent)
    {
        HashSet<string> names = parent.Children
            .Select(RequireNode)
            .Select(node => node.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        const string baseName = "New Folder";
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseName} ({suffix})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void SetCurrentLocation(string locationId)
    {
        _currentLocationId = locationId;
        ClearInteractionState();
        NotifyChanged();
    }

    private void ClearInteractionState()
    {
        _selectedItemId = null;
        _renameItemId = null;
    }

    private ExplorerOpenResult NoSelectionResult() =>
        new(
            ExplorerOpenDisposition.NoSelection,
            _currentLocationId,
            null);

    private ExplorerWorkspaceItem ToItem(Node node) =>
        new(
            node.Id,
            node.Name,
            node.Kind,
            node.TypeLabel,
            node.IconKey,
            node.SizeBytes,
            node.ModifiedAt,
            StringComparer.Ordinal.Equals(node.Id, _selectedItemId),
            StringComparer.Ordinal.Equals(node.Id, _renameItemId),
            node.Kind == ExplorerWorkspaceItemKind.File);

    private Node RequireNode(string id) =>
        _nodes.TryGetValue(id, out Node? node)
            ? node
            : throw new InvalidOperationException(
                $"Virtual Explorer node '{id}' does not exist.");

    private static string Pop(List<string> history)
    {
        int index = history.Count - 1;
        string value = history[index];
        history.RemoveAt(index);
        return value;
    }

    private static DateTimeOffset Utc(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private void NotifyChangedIf(bool changed)
    {
        if (changed)
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class Node(
        string id,
        string name,
        ExplorerWorkspaceItemKind kind,
        string? parentId,
        string typeLabel,
        string iconKey,
        long? sizeBytes,
        DateTimeOffset modifiedAt,
        string previewKind)
    {
        public string Id { get; } = id;

        public string Name { get; set; } = name;

        public ExplorerWorkspaceItemKind Kind { get; } = kind;

        public string? ParentId { get; } = parentId;

        public string TypeLabel { get; set; } = typeLabel;

        public string IconKey { get; set; } = iconKey;

        public long? SizeBytes { get; } = sizeBytes;

        public DateTimeOffset ModifiedAt { get; } = modifiedAt;

        public string PreviewKind { get; set; } = previewKind;

        public List<string> Children { get; } = [];
    }
}
