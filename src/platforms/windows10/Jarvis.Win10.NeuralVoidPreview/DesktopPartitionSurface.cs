namespace Jarvis.Win10.NeuralVoidPreview;

public partial class DesktopShellSurface
{
    private readonly List<PartitionDivider> _partitionDividers = [];
    private bool _applyingPartitionGeometry;
    internal PartitionGeometry? PartitionState => _ownedWindowLayouts.Partition;
    internal long PartitionEpoch => _ownedWindowLayouts.PartitionGeneration;

    internal void BeginPartitionGesture(PartitionDivider owner)
    {
        CancelExplorerPendingFocus();
        foreach (PartitionDivider divider in _partitionDividers.Where(divider => !ReferenceEquals(divider,owner))) divider.FinishGesture();
    }

    internal bool ResizePartition(PartitionCutKey key, double position, long generation, bool pointerGesture = false)
    {
        OwnedLayoutApplyResult result = _ownedWindowLayouts.TryResizePartition(key,position,generation);
        if (!result.Succeeded) return false;
        if (!pointerGesture) foreach (PartitionDivider divider in _partitionDividers) divider.FinishGesture();
        _applyingPartitionGeometry = true;
        try { ApplyOwnedLayoutResult(result); }
        finally { _applyingPartitionGeometry = false; }
        return true;
    }

    internal void RestorePartitionCuts(PartitionGeometry geometry, long generation)
    {
        OwnedLayoutApplyResult result = _ownedWindowLayouts.TryRestorePartitionCuts(geometry,generation);
        if (!result.Succeeded) return;
        _applyingPartitionGeometry = true;
        try { ApplyOwnedLayoutResult(result); }
        finally { _applyingPartitionGeometry = false; }
    }

    private void PartitionSurface_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key != System.Windows.Input.Key.Escape) return;
        PartitionDivider? divider = _partitionDividers.FirstOrDefault(divider => divider.HasGesture);
        if (divider is null) return;
        divider.FinishGesture(rollback:true);
        args.Handled = true;
    }

    private void FinishPartitionGestures()
    {
        foreach (PartitionDivider divider in _partitionDividers) divider.FinishGesture();
    }

    private void RefreshPartitionDividers()
    {
        PartitionGeometry? geometry = _ownedWindowLayouts.IsPartitionZoomed ? null : PartitionState;
        if (geometry is null || _partitionDividers.Any(divider => divider.Generation != PartitionEpoch))
        {
            foreach (PartitionDivider divider in _partitionDividers) { divider.FinishGesture(); DesktopCanvas.Children.Remove(divider); }
            _partitionDividers.Clear();
        }
        if (geometry is null) return;
        if (_partitionDividers.Count == 0)
        {
            foreach (PartitionDividerSpan span in geometry.Dividers)
            {
                PartitionDivider divider = new(this,span,PartitionEpoch);
                _partitionDividers.Add(divider);
                DesktopCanvas.Children.Add(divider);
            }
        }
        foreach (PartitionDivider divider in _partitionDividers)
            divider.UpdateSpan(geometry.Dividers.Single(span => span.Key == divider.Key));
    }
}
