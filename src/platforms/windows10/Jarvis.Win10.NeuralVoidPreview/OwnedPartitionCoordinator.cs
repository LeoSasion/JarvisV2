using System.Windows;

namespace Jarvis.Win10.NeuralVoidPreview;

internal sealed record OwnedWindowAdmission(OwnedWindowId Id, Rect FloatingBounds);

internal sealed partial class OwnedWindowLayoutCoordinator
{
    private PartitionGeometry? _partitionGeometry;
    private OwnedWindowId? _partitionZoom;
    private readonly Dictionary<LayoutPreset,IReadOnlyDictionary<PartitionCutKey,double>> _partitionRatios = [];
    internal bool IsPartitionZoomed => _partitionZoom.HasValue;
    internal bool IsWindowShown(OwnedWindowId id) => _windows.TryGetValue(id,out WindowEntry? entry) &&
        entry.Lifecycle == OwnedWindowLifecycle.Visible && (_partitionZoom is null || _partitionZoom == id);
    private OwnedWindowId[] _partitionAssignments = [];
    private readonly Dictionary<OwnedWindowId,(LayoutPreset Preset,int Pane)> _previousPartitionPane = [];
    private long _partitionGeneration;
    internal bool IsPartition => _partitionGeometry is not null;
    internal PartitionGeometry? Partition => _partitionGeometry;
    internal long PartitionGeneration => _partitionGeneration;

    internal bool CanProjectPartition(LayoutPreset preset, Rect workArea) =>
        PartitionGeometry.TryCreate(preset,workArea,_partitionRatios.GetValueOrDefault(preset),out _);

    internal OwnedLayoutApplyResult TryCommitPartition(LayoutPreset preset, Rect workArea,
        IReadOnlyList<OwnedWindowAdmission> admissions)
    {
        if (!Enum.IsDefined(preset)) return Result(OwnedLayoutApplyStatus.UnknownLayout, 0, []);
        int required = LayoutCatalog.Get(preset).PaneCount;
        if (!PartitionGeometry.TryCreate(preset, workArea, _partitionRatios.GetValueOrDefault(preset), out PartitionGeometry? geometry))
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea, required, []);
        if (!ValidAdmissions(admissions) || admissions.Count != Math.Max(0,required-_windows.Count))
            return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow, required, []);
        WindowEntry[] fresh = admissions.Select((item,index) => new WindowEntry(item.Id,
            OwnedWindowLifecycle.Closed,item.FloatingBounds,_registrationOrder+index+1)).ToArray();
        WindowEntry[] candidates = PartitionCandidates().Concat(fresh).ToArray();
        WindowEntry active = candidates.FirstOrDefault(entry => entry.Id == _activeWindowId) ?? candidates[0];
        WindowEntry[] participants = new[] { active }.Concat(candidates.Where(entry => entry != active)).Take(required).ToArray();
        int primary = PrimaryPaneIndex(LayoutCatalog.Get(preset));
        int[] paneOrder = Enumerable.Range(0,required).Where(index => index != primary).Prepend(primary).ToArray();
        OwnedWindowId[] assignments = new OwnedWindowId[required];
        for (int index=0;index<required;index++) assignments[paneOrder[index]] = participants[index].Id;
        // Complete geometry and detached admission are validated before counters,
        // retained map, lifecycle or assignment changes.
        foreach (WindowEntry entry in fresh) _windows.Add(entry.Id,entry);
        _registrationOrder += fresh.Length;
        RememberPartitionPanes();
        _partitionGeometry = geometry;
        _partitionZoom = null;
        _partitionRatios[preset] = geometry!.Ratios;
        _partitionAssignments = assignments;
        _partitionGeneration++;
        foreach (WindowEntry entry in _windows.Values)
        {
            if (!assignments.Contains(entry.Id) && entry.Lifecycle == OwnedWindowLifecycle.Visible)
                entry.Lifecycle = OwnedWindowLifecycle.Suppressed;
            entry.AssignedPaneIndex = null;
        }
        _activeWindowId = active.Id;
        _appliedPreset = preset;
        if (preset != LayoutPreset.Maximized) _lastTiledPreset = preset;
        _committedPaneBounds = geometry!.Panes;
        for (int index=0;index<required;index++)
        {
            WindowEntry entry = _windows[assignments[index]];
            entry.Lifecycle = OwnedWindowLifecycle.Visible;
            entry.AssignedPaneIndex = index;
            entry.Bounds = geometry.Panes[index];
            _previousPartitionPane[entry.Id] = (preset,index);
        }
        return PartitionResult();
    }

    internal OwnedLayoutApplyResult TryResizePartition(PartitionCutKey key, double position, long generation)
    {
        if (_partitionGeometry is not PartitionGeometry geometry || _partitionZoom is not null || generation != _partitionGeneration)
            return Result(OwnedLayoutApplyStatus.ModeConflict,0,[]);
        if (!geometry.TryResize(key,position,out PartitionGeometry? candidate))
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea,_partitionAssignments.Length,[]);
        List<OwnedWindowPlacement> changed = [];
        for (int index=0;index<_partitionAssignments.Length;index++)
        {
            WindowEntry entry = _windows[_partitionAssignments[index]];
            if (entry.Bounds != candidate!.Panes[index])
                changed.Add(new(entry.Id,index,candidate.Panes[index],0,entry.Id == _activeWindowId));
        }
        _partitionGeometry = candidate;
        _partitionRatios[geometry.Preset] = candidate!.Ratios;
        _committedPaneBounds = candidate.Panes;
        foreach (OwnedWindowPlacement placement in changed) _windows[placement.WindowId].Bounds = placement.Bounds;
        return Result(OwnedLayoutApplyStatus.Applied,_partitionAssignments.Length,changed);
    }

    internal OwnedLayoutApplyResult TryRestorePartitionCuts(PartitionGeometry saved, long generation)
    {
        if (_partitionGeometry is not PartitionGeometry current || _partitionZoom is not null ||
            generation != _partitionGeneration || saved.Preset != current.Preset || saved.WorkArea != current.WorkArea)
            return Result(OwnedLayoutApplyStatus.ModeConflict,0,[]);
        if (!PartitionGeometry.TryCreate(saved.Preset,saved.WorkArea,saved.Ratios,out PartitionGeometry? staged))
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea,_partitionAssignments.Length,[]);
        _partitionGeometry = staged;
        _partitionRatios[saved.Preset] = staged!.Ratios;
        _committedPaneBounds = staged.Panes;
        for (int index=0;index<_partitionAssignments.Length;index++) _windows[_partitionAssignments[index]].Bounds = staged.Panes[index];
        return PartitionResult();
    }

    private WindowEntry? RefillCandidate(OwnedWindowId excluded) => PartitionCandidates()
        .FirstOrDefault(entry => entry.Id != excluded && !_partitionAssignments.Contains(entry.Id));

    internal int PartitionRefillDeficit(OwnedWindowId target) =>
        IsPartition && _partitionAssignments.Contains(target) && RefillCandidate(target) is null ? 1 : 0;

    internal OwnedLayoutApplyResult TryHidePartition(OwnedWindowId id, OwnedWindowLifecycle lifecycle, OwnedWindowAdmission? admission = null)
    {
        if (_partitionGeometry is not PartitionGeometry geometry) return Result(OwnedLayoutApplyStatus.ModeConflict,0,[]);
        int pane = Array.IndexOf(_partitionAssignments,id);
        if (!_windows.TryGetValue(id,out WindowEntry? target) || lifecycle is not (OwnedWindowLifecycle.Minimized or OwnedWindowLifecycle.Closed) ||
            target.Lifecycle is not (OwnedWindowLifecycle.Visible or OwnedWindowLifecycle.Suppressed))
            return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,_partitionAssignments.Length,[]);
        if (!PartitionGeometry.TryCreate(geometry.Preset,geometry.WorkArea,geometry.Ratios,out _) ||
            _partitionAssignments.Distinct().Count() != _partitionAssignments.Length)
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea,_partitionAssignments.Length,[]);
        WindowEntry? replacement = RefillCandidate(id);
        if (pane >= 0 && replacement is null)
        {
            if (admission is null || !ValidAdmissions(new[] { admission }))
                return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,_partitionAssignments.Length,[]);
            replacement = new WindowEntry(admission.Id,OwnedWindowLifecycle.Closed,admission.FloatingBounds,_registrationOrder+1);
        }
        else if (admission is not null) return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,_partitionAssignments.Length,[]);
        if (admission is not null) { _windows.Add(admission.Id,replacement!); _registrationOrder++; }
        if (pane >= 0)
        {
            _previousPartitionPane[id] = (geometry.Preset,pane);
            _partitionAssignments[pane] = replacement!.Id;
            replacement.Lifecycle = OwnedWindowLifecycle.Visible;
            replacement.Bounds = geometry.Panes[pane];
            replacement.AssignedPaneIndex = pane;
            _previousPartitionPane[replacement.Id] = (geometry.Preset,pane);
        }
        if (_partitionZoom == id)
        {
            // Refill the same underlying pane while the temporary zoom follows
            // its replacement. An explicit restore can swap the original back.
            _partitionZoom = replacement!.Id;
            replacement.Bounds = geometry.WorkArea;
            _activeWindowId = replacement.Id;
        }
        target.Lifecycle = lifecycle;
        target.AssignedPaneIndex = null;
        _partitionGeneration++;
        PromoteVisibleWindowIfNeeded(id);
        return PartitionResult();
    }

    private bool ValidAdmissions(IReadOnlyList<OwnedWindowAdmission> admissions) =>
        admissions.All(item => item.Id.Value is "explorer-2" or "explorer-3" or "explorer-4" &&
            !_windows.ContainsKey(item.Id) && IsValidBounds(item.FloatingBounds)) &&
        admissions.Select(item => item.Id).Distinct().Count() == admissions.Count &&
        _windows.Keys.Count(id => id.Value is "explorer" or "explorer-2" or "explorer-3" or "explorer-4") + admissions.Count <= 4;

    private void RememberPartitionPanes()
    {
        if (_partitionGeometry is null) return;
        foreach (WindowEntry entry in _windows.Values.Where(entry => entry.AssignedPaneIndex.HasValue))
            _previousPartitionPane[entry.Id] = (_partitionGeometry.Preset,entry.AssignedPaneIndex!.Value);
    }

    internal OwnedLayoutApplyResult TryShowPartition(OwnedWindowId id, OwnedWindowAdmission? admission = null)
    {
        if (_partitionGeometry is not PartitionGeometry geometry) return Result(OwnedLayoutApplyStatus.ModeConflict,0,[]);
        if (admission is not null && (admission.Id != id || !ValidAdmissions(new[] { admission })))
            return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,_partitionAssignments.Length,[]);
        WindowEntry? entry = _windows.GetValueOrDefault(id);
        if (entry is null && admission is null) return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,_partitionAssignments.Length,[]);
        if (!PartitionGeometry.IsExactCover(geometry.Panes,geometry.WorkArea))
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea,_partitionAssignments.Length,[]);
        entry ??= new WindowEntry(id,OwnedWindowLifecycle.Closed,admission!.FloatingBounds,_registrationOrder+1);
        int assigned = Array.IndexOf(_partitionAssignments,id);
        if (assigned < 0)
        {
            int pane = _previousPartitionPane.TryGetValue(id,out var previous) && previous.Preset == geometry.Preset && previous.Pane < _partitionAssignments.Length
                ? previous.Pane : Array.IndexOf(_partitionAssignments,_activeWindowId ?? default);
            if (pane < 0) pane = PrimaryPaneIndex(LayoutCatalog.Get(geometry.Preset));
            WindowEntry displaced = _windows[_partitionAssignments[pane]];
            if (admission is not null) { _windows.Add(id,entry); _registrationOrder++; }
            _previousPartitionPane[displaced.Id] = (geometry.Preset,pane);
            displaced.Lifecycle = OwnedWindowLifecycle.Suppressed;
            displaced.AssignedPaneIndex = null;
            _partitionAssignments[pane] = id;
            entry.Lifecycle = OwnedWindowLifecycle.Visible;
            entry.AssignedPaneIndex = pane;
            entry.Bounds = geometry.Panes[pane];
            _previousPartitionPane[id] = (geometry.Preset,pane);
            _partitionGeneration++;
        }
        if (_partitionZoom is not null)
        {
            _partitionZoom = id; entry.Bounds = geometry.WorkArea;
            _partitionGeneration++;
        }
        ActivateCore(entry);
        return PartitionResult();
    }

    internal OwnedLayoutApplyResult TryTogglePartitionZoom(OwnedWindowId id)
    {
        if (_partitionGeometry is not PartitionGeometry geometry || !_partitionAssignments.Contains(id))
            return Result(OwnedLayoutApplyStatus.UnknownOwnedWindow,1,[]);
        if (!PartitionGeometry.TryCreate(geometry.Preset,geometry.WorkArea,geometry.Ratios,out _))
            return Result(OwnedLayoutApplyStatus.InvalidWorkArea,1,[]);
        bool restore = _partitionZoom == id;
        _partitionGeneration++;
        _partitionZoom = restore ? null : id;
        _appliedPreset = restore ? geometry.Preset : LayoutPreset.Maximized;
        _activeWindowId = id;
        for (int index=0;index<_partitionAssignments.Length;index++)
            _windows[_partitionAssignments[index]].Bounds = _partitionZoom == _partitionAssignments[index] ? geometry.WorkArea : geometry.Panes[index];
        return PartitionResult();
    }

    internal OwnedLayoutApplyResult TryExitPartition()
    {
        if (!IsPartition && _appliedPreset is null) return Result(OwnedLayoutApplyStatus.RestoredFreeform,VisibleWindowCount,[]);
        _partitionGeneration++;
        _partitionGeometry = null;
        _partitionZoom = null;
        _partitionAssignments = [];
        _lastTiledPreset = null;
        BreakLayoutCore();
        foreach (WindowEntry entry in _windows.Values)
        {
            if (entry.Lifecycle == OwnedWindowLifecycle.Suppressed) entry.Lifecycle = OwnedWindowLifecycle.Visible;
            entry.Bounds = entry.FloatingBounds;
        }
        return Result(OwnedLayoutApplyStatus.RestoredFreeform,VisibleWindowCount,_windows.Values
            .Where(entry => entry.Lifecycle == OwnedWindowLifecycle.Visible)
            .Select(entry => new OwnedWindowPlacement(entry.Id,null,entry.Bounds,0,entry.Id == _activeWindowId)).ToArray());
    }

    private IEnumerable<WindowEntry> PartitionCandidates() =>
        OrderByMostRecent(_windows.Values.Where(entry => entry.Lifecycle == OwnedWindowLifecycle.Visible))
        .Concat(OrderByMostRecent(_windows.Values.Where(entry => entry.Lifecycle == OwnedWindowLifecycle.Suppressed)))
        .Concat(OrderByMostRecent(_windows.Values.Where(entry => entry.Lifecycle == OwnedWindowLifecycle.Minimized)))
        .Concat(OrderByMostRecent(_windows.Values.Where(entry => entry.Lifecycle == OwnedWindowLifecycle.Closed)));

    private OwnedLayoutApplyResult PartitionResult() => Result(OwnedLayoutApplyStatus.Applied,_partitionZoom is null ? _partitionAssignments.Length : 1,
        _partitionAssignments.Select((id,index) => new OwnedWindowPlacement(id,index,_windows[id].Bounds,
            id == _activeWindowId ? _partitionAssignments.Length : _partitionAssignments.Length-index-1,id == _activeWindowId))
            .Where(placement => IsWindowShown(placement.WindowId)).ToArray());
}
