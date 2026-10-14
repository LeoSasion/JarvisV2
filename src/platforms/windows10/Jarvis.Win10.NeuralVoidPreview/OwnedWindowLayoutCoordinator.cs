using System.Collections.ObjectModel;
using System.Windows;

namespace Jarvis.Win10.NeuralVoidPreview;

internal readonly record struct OwnedWindowId
{
    public OwnedWindowId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "An owned window id is required.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}

internal enum OwnedWindowLifecycle
{
    Closed,
    Minimized,
    Visible,
    Suppressed,
}

internal enum OwnedLayoutApplyStatus
{
    Applied,
    RestoredFreeform,
    NoVisibleWindow,
    UnknownOwnedWindow,
    UnknownLayout,
    InvalidWorkArea,
    ModeConflict,
}

internal sealed record OwnedWindowState(
    OwnedWindowId Id,
    OwnedWindowLifecycle Lifecycle,
    Rect Bounds,
    Rect FloatingBounds,
    long ActivationOrdinal,
    int RegistrationOrder,
    int? AssignedPaneIndex);

internal sealed record OwnedWindowLayoutSnapshot(
    LayoutPreset? AppliedPreset,
    LayoutPreset? LastTiledPreset,
    OwnedWindowId? ActiveWindowId,
    IReadOnlyList<OwnedWindowState> Windows);

internal sealed record OwnedLayoutAvailability(
    LayoutPreset Preset,
    int RequiredPaneCount,
    int VisibleWindowCount,
    bool IsEnabled,
    string StatusText);

internal sealed record OwnedWindowPlacement(
    OwnedWindowId WindowId,
    int? PaneIndex,
    Rect Bounds,
    int ZRank,
    bool IsActive);

internal sealed record OwnedLayoutApplyResult(
    OwnedLayoutApplyStatus Status,
    LayoutPreset? AppliedPreset,
    int RequiredPaneCount,
    int VisibleWindowCount,
    OwnedWindowId? ActiveWindowId,
    IReadOnlyList<OwnedWindowPlacement> Placements)
{
    public bool Succeeded =>
        Status is
            OwnedLayoutApplyStatus.Applied or
            OwnedLayoutApplyStatus.RestoredFreeform;
}

/// <summary>
/// Coordinates geometry for explicitly registered, own-process surfaces.
/// It never discovers or controls operating-system or third-party windows.
/// </summary>
internal sealed partial class OwnedWindowLayoutCoordinator
{
    internal const double InternalGap = 12.0;

    private readonly Dictionary<OwnedWindowId, WindowEntry> _windows = [];
    private long _activationOrdinal;
    private int _registrationOrder;
    private LayoutPreset? _appliedPreset;
    private LayoutPreset? _lastTiledPreset;
    private OwnedWindowId? _activeWindowId;
    private IReadOnlyList<Rect> _committedPaneBounds = [];

    public LayoutPreset? AppliedPreset => _appliedPreset;

    public LayoutPreset? LastTiledPreset => _lastTiledPreset;

    public OwnedWindowId? ActiveWindowId => _activeWindowId;

    public int VisibleWindowCount =>
        _windows.Values.Count(
            window => IsWindowShown(window.Id));

    public bool TryRegister(
        OwnedWindowId id,
        OwnedWindowLifecycle lifecycle,
        Rect initialBounds)
    {
        if (IsPartition || id.IsEmpty ||
            !IsValidBounds(initialBounds) ||
            _windows.ContainsKey(id))
        {
            return false;
        }

        WindowEntry entry = new(
            id,
            lifecycle,
            initialBounds,
            ++_registrationOrder);
        _windows.Add(id, entry);
        if (lifecycle == OwnedWindowLifecycle.Visible)
        {
            ActivateCore(entry);
        }

        return true;
    }

    public bool TryGetWindow(
        OwnedWindowId id,
        out OwnedWindowState? state)
    {
        if (!_windows.TryGetValue(id, out WindowEntry? entry))
        {
            state = null;
            return false;
        }

        state = ToState(entry);
        return true;
    }

    public OwnedWindowLayoutSnapshot CaptureSnapshot() =>
        new(
            _appliedPreset,
            _lastTiledPreset,
            _activeWindowId,
            new ReadOnlyCollection<OwnedWindowState>(
                _windows.Values
                    .OrderBy(window => window.RegistrationOrder)
                    .Select(ToState)
                    .ToArray()));

    public OwnedLayoutAvailability GetAvailability(LayoutPreset preset)
    {
        if (!Enum.IsDefined(preset))
        {
            return new(
                preset,
                0,
                VisibleWindowCount,
                false,
                "UNKNOWN LAYOUT");
        }

        int required = LayoutCatalog.Get(preset).PaneCount;
        int visible = VisibleWindowCount;
        return new(
            preset,
            required,
            visible,
            true,
            visible >= required
                ? "AVAILABLE"
                : $"FILLS {visible} OF {required} PANES");
    }

    public bool TryActivate(OwnedWindowId id)
    {
        if (!_windows.TryGetValue(id, out WindowEntry? entry) ||
            !IsWindowShown(id))
        {
            return false;
        }

        ActivateCore(entry);
        return true;
    }

    public bool TrySetLifecycle(
        OwnedWindowId id,
        OwnedWindowLifecycle lifecycle)
    {
        if (IsPartition || !_windows.TryGetValue(id, out WindowEntry? entry))
        {
            return false;
        }

        if (entry.Lifecycle == lifecycle)
        {
            return true;
        }

        if (entry.Lifecycle == OwnedWindowLifecycle.Closed &&
            lifecycle == OwnedWindowLifecycle.Minimized)
        {
            return false;
        }

        OwnedWindowLifecycle previous = entry.Lifecycle;
        if (lifecycle == OwnedWindowLifecycle.Closed)
        {
            BreakLayoutCore();
            entry.Lifecycle = lifecycle;
            entry.AssignedPaneIndex = null;
            PromoteVisibleWindowIfNeeded(id);
            return true;
        }

        entry.Lifecycle = lifecycle;
        if (lifecycle == OwnedWindowLifecycle.Minimized)
        {
            PromoteVisibleWindowIfNeeded(id);
            return true;
        }

        if (previous == OwnedWindowLifecycle.Closed)
        {
            entry.Bounds = entry.FloatingBounds;
            entry.AssignedPaneIndex = null;
        }
        else if (previous == OwnedWindowLifecycle.Minimized)
        {
            // A visible-only reapply may have cleared the old association.
            // Recover it only from unoccupied, currently committed geometry.
            entry.AssignedPaneIndex = null;
            for (int paneIndex = 0; paneIndex < _committedPaneBounds.Count; paneIndex++)
            {
                if (entry.Bounds == _committedPaneBounds[paneIndex] &&
                    !_windows.Values.Any(window =>
                        window.Lifecycle == OwnedWindowLifecycle.Visible &&
                        window.AssignedPaneIndex == paneIndex))
                {
                    entry.AssignedPaneIndex = paneIndex;
                    break;
                }
            }
        }

        ActivateCore(entry);
        return true;
    }

    public bool TrySetManualBounds(
        OwnedWindowId id,
        Rect bounds)
    {
        if (!_windows.TryGetValue(id, out WindowEntry? entry) ||
            entry.Lifecycle != OwnedWindowLifecycle.Visible ||
            IsPartition || !IsValidBounds(bounds))
        {
            return false;
        }

        BreakLayoutCore();
        _lastTiledPreset = null;
        entry.Bounds = bounds;
        entry.FloatingBounds = bounds;
        ActivateCore(entry);
        return true;
    }

    public OwnedLayoutApplyResult TryApply(
        LayoutPreset preset,
        Rect workArea,
        bool restoreHiddenWindows = false) =>
        TryApplyCore(preset, workArea, null, restoreHiddenWindows);

    public OwnedLayoutApplyResult TryToggleMaximize(
        OwnedWindowId id,
        Rect workArea)
    {
        if (IsPartition) return Result(OwnedLayoutApplyStatus.ModeConflict, 1, []);
        if (!_windows.TryGetValue(id, out WindowEntry? entry) ||
            entry.Lifecycle != OwnedWindowLifecycle.Visible)
        {
            return Result(
                OwnedLayoutApplyStatus.UnknownOwnedWindow,
                1,
                []);
        }

        if (!IsValidBounds(workArea))
        {
            return Result(
                OwnedLayoutApplyStatus.InvalidWorkArea,
                1,
                []);
        }

        bool targetOwnsMaximizedPane =
            _appliedPreset == LayoutPreset.Maximized &&
            entry.AssignedPaneIndex == 0;
        if (!targetOwnsMaximizedPane)
        {
            return TryApplyCore(LayoutPreset.Maximized, workArea, id);
        }

        if (_lastTiledPreset is LayoutPreset lastTiled)
        {
            return TryApplyCore(lastTiled, workArea, id);
        }

        BreakLayoutCore();
        entry.Bounds = entry.FloatingBounds;
        ActivateCore(entry);
        return Result(
            OwnedLayoutApplyStatus.RestoredFreeform,
            1,
            [
                new(
                    id,
                    null,
                    entry.Bounds,
                    1,
                    true),
            ]);
    }

    internal static int PrimaryPaneIndex(LayoutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Panes
            .Select(
                (pane, index) => (Pane: pane, Index: index, Area: pane.Width * pane.Height))
            .OrderByDescending(candidate => candidate.Area)
            .ThenBy(candidate => candidate.Pane.Y)
            .ThenBy(candidate => candidate.Pane.X)
            .ThenBy(candidate => candidate.Index)
            .First()
            .Index;
    }

    internal static IReadOnlyList<Rect> ScaleWithInternalGap(
        LayoutPreset preset,
        Rect workArea)
    {
        if (!Enum.IsDefined(preset) || !IsValidBounds(workArea))
        {
            return [];
        }

        double left = Math.Ceiling(workArea.Left);
        double top = Math.Ceiling(workArea.Top);
        double right = Math.Floor(workArea.Right);
        double bottom = Math.Floor(workArea.Bottom);
        if (right <= left || bottom <= top)
        {
            return [];
        }

        double width = right - left;
        double height = bottom - top;
        const double halfGap = InternalGap / 2.0;
        LayoutDefinition definition = LayoutCatalog.Get(preset);
        List<Rect> result = new(definition.PaneCount);
        foreach (LayoutPane pane in definition.Panes)
        {
            double paneLeft = GridCoordinate(left, width, pane.X) +
                (pane.X == 0 ? 0.0 : halfGap);
            double paneTop = GridCoordinate(top, height, pane.Y) +
                (pane.Y == 0 ? 0.0 : halfGap);
            double paneRight = GridCoordinate(left, width, pane.Right) -
                (pane.Right == LayoutCatalog.GridSize ? 0.0 : halfGap);
            double paneBottom = GridCoordinate(top, height, pane.Bottom) -
                (pane.Bottom == LayoutCatalog.GridSize ? 0.0 : halfGap);
            if (paneRight <= paneLeft || paneBottom <= paneTop)
            {
                return [];
            }

            Rect bounds = new(
                paneLeft,
                paneTop,
                paneRight - paneLeft,
                paneBottom - paneTop);
            if (!IsValidBounds(bounds))
            {
                return [];
            }

            result.Add(bounds);
        }

        return new ReadOnlyCollection<Rect>(result);
    }

    private OwnedLayoutApplyResult TryApplyCore(
        LayoutPreset preset,
        Rect workArea,
        OwnedWindowId? requestedActive,
        bool restoreHiddenWindows = false)
    {
        if (IsPartition) return Result(OwnedLayoutApplyStatus.ModeConflict, 0, []);
        if (!Enum.IsDefined(preset))
        {
            return Result(
                OwnedLayoutApplyStatus.UnknownLayout,
                0,
                []);
        }

        LayoutDefinition definition = LayoutCatalog.Get(preset);
        int required = definition.PaneCount;
        List<WindowEntry> visible = _windows.Values
            .Where(window => window.Lifecycle == OwnedWindowLifecycle.Visible)
            .ToList();
        if (restoreHiddenWindows && visible.Count < required)
        {
            // Plan against registered own-process windows only. Do not change
            // lifecycle until the work area and complete geometry are valid.
            IEnumerable<WindowEntry> hidden = OrderByMostRecent(
                _windows.Values.Where(window =>
                    window.Lifecycle == OwnedWindowLifecycle.Minimized))
                .Concat(OrderByMostRecent(_windows.Values.Where(window =>
                    window.Lifecycle == OwnedWindowLifecycle.Closed)));
            visible.AddRange(hidden.Take(required - visible.Count));
        }

        if (visible.Count == 0)
        {
            return Result(
                OwnedLayoutApplyStatus.NoVisibleWindow,
                required,
                []);
        }

        if (!IsValidBounds(workArea))
        {
            return Result(
                OwnedLayoutApplyStatus.InvalidWorkArea,
                required,
                []);
        }

        WindowEntry? active = null;
        if (requestedActive is OwnedWindowId requested)
        {
            if (!_windows.TryGetValue(requested, out active) ||
                active.Lifecycle != OwnedWindowLifecycle.Visible)
            {
                return Result(
                    OwnedLayoutApplyStatus.UnknownOwnedWindow,
                    required,
                    []);
            }
        }
        else if (_activeWindowId is OwnedWindowId current)
        {
            _ = _windows.TryGetValue(current, out active);
            if (active?.Lifecycle != OwnedWindowLifecycle.Visible)
            {
                active = null;
            }
        }

        active ??= OrderByMostRecent(visible).First();
        List<WindowEntry> participants =
        [
            active,
            .. OrderByMostRecent(
                visible.Where(window => !ReferenceEquals(window, active)))
                .Take(required - 1),
        ];

        IReadOnlyList<Rect> paneBounds =
            ScaleWithInternalGap(preset, workArea);
        if (paneBounds.Count != required)
        {
            return Result(
                OwnedLayoutApplyStatus.InvalidWorkArea,
                required,
                []);
        }

        int primaryPane = PrimaryPaneIndex(definition);
        List<int> paneOrder = definition.Panes
            .Select((pane, index) => (Pane: pane, Index: index))
            .Where(candidate => candidate.Index != primaryPane)
            .OrderBy(candidate => candidate.Pane.Y)
            .ThenBy(candidate => candidate.Pane.X)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Index)
            .Prepend(primaryPane)
            .ToList();

        List<OwnedWindowPlacement> placements = new(required);
        for (int index = 0; index < participants.Count; index++)
        {
            WindowEntry participant = participants[index];
            int paneIndex = paneOrder[index];
            placements.Add(
                new(
                    participant.Id,
                    paneIndex,
                    paneBounds[paneIndex],
                    index == 0 ? required : required - index,
                    index == 0));
        }

        foreach (WindowEntry window in _windows.Values)
        {
            window.AssignedPaneIndex = null;
        }

        foreach (OwnedWindowPlacement placement in placements)
        {
            WindowEntry participant = _windows[placement.WindowId];
            participant.Lifecycle = OwnedWindowLifecycle.Visible;
            participant.Bounds = placement.Bounds;
            participant.AssignedPaneIndex = placement.PaneIndex;
        }

        _activeWindowId = active.Id;
        _appliedPreset = preset;
        _committedPaneBounds = paneBounds;
        if (preset != LayoutPreset.Maximized)
        {
            _lastTiledPreset = preset;
        }

        return Result(
            OwnedLayoutApplyStatus.Applied,
            required,
            placements);
    }

    private OwnedLayoutApplyResult Result(
        OwnedLayoutApplyStatus status,
        int requiredPaneCount,
        IReadOnlyList<OwnedWindowPlacement> placements)
    {
        return new(
            status,
            _appliedPreset,
            requiredPaneCount,
            VisibleWindowCount,
            _activeWindowId,
            new ReadOnlyCollection<OwnedWindowPlacement>(placements.ToArray()));
    }

    private void ActivateCore(WindowEntry entry)
    {
        entry.ActivationOrdinal = ++_activationOrdinal;
        _activeWindowId = entry.Id;
    }

    private void PromoteVisibleWindowIfNeeded(OwnedWindowId changedWindow)
    {
        if (_activeWindowId != changedWindow)
        {
            return;
        }

        WindowEntry? replacement = OrderByMostRecent(
            _windows.Values.Where(
                window => window.Lifecycle == OwnedWindowLifecycle.Visible))
            .FirstOrDefault();
        _activeWindowId = replacement?.Id;
    }

    private static IOrderedEnumerable<WindowEntry> OrderByMostRecent(
        IEnumerable<WindowEntry> windows) =>
        windows
            .OrderByDescending(window => window.ActivationOrdinal)
            .ThenBy(window => window.RegistrationOrder);

    private void BreakLayoutCore()
    {
        _appliedPreset = null;
        _committedPaneBounds = [];
        foreach (WindowEntry window in _windows.Values)
        {
            window.AssignedPaneIndex = null;
        }
    }

    private static OwnedWindowState ToState(WindowEntry entry) =>
        new(
            entry.Id,
            entry.Lifecycle,
            entry.Bounds,
            entry.FloatingBounds,
            entry.ActivationOrdinal,
            entry.RegistrationOrder,
            entry.AssignedPaneIndex);

    private static double GridCoordinate(
        double start,
        double length,
        int coordinate) =>
        Math.Round(
            start + length * coordinate / LayoutCatalog.GridSize,
            MidpointRounding.AwayFromZero);

    private static bool IsValidBounds(Rect bounds) =>
        !bounds.IsEmpty &&
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height) &&
        double.IsFinite(bounds.Right) &&
        double.IsFinite(bounds.Bottom) &&
        bounds.Width > 0.0 &&
        bounds.Height > 0.0;

    private sealed class WindowEntry(
        OwnedWindowId id,
        OwnedWindowLifecycle lifecycle,
        Rect initialBounds,
        int registrationOrder)
    {
        public OwnedWindowId Id { get; } = id;

        public OwnedWindowLifecycle Lifecycle { get; set; } = lifecycle;

        public Rect Bounds { get; set; } = initialBounds;

        public Rect FloatingBounds { get; set; } = initialBounds;

        public long ActivationOrdinal { get; set; }

        public int RegistrationOrder { get; } = registrationOrder;

        public int? AssignedPaneIndex { get; set; }
    }
}
