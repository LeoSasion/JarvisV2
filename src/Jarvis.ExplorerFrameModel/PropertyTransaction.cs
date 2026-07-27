namespace Jarvis.ExplorerFrameModel;

internal sealed class PropertyTransaction
{
    private readonly TargetIdentity _target;
    private readonly VisualTreeFixture _tree;
    private readonly IReadOnlyList<SelectorSpec> _selectors;
    private readonly List<PropertySnapshot> _snapshots = [];
    private readonly List<PropertySnapshot> _applied = [];
    private readonly List<AuditEvent> _audit = [];
    private IReadOnlyDictionary<string, VisualNode> _resolved =
        new Dictionary<string, VisualNode>(StringComparer.Ordinal);

    public PropertyTransaction(
        TargetIdentity target,
        VisualTreeFixture tree,
        IReadOnlyList<SelectorSpec> selectors)
    {
        _target = target;
        _tree = tree;
        _selectors = selectors;
    }

    public FrameTransactionState State { get; private set; } =
        FrameTransactionState.Cold;

    public IReadOnlyList<PropertySnapshot> Snapshots => _snapshots;

    public IReadOnlyList<AuditEvent> Audit => _audit;

    public bool TryDiscover(out string error)
    {
        if (State != FrameTransactionState.Cold)
        {
            error = "discover-state-invalid";
            return false;
        }

        IReadOnlyList<string> admissionFailures =
            FrameAdmission.Validate(_target);
        if (admissionFailures.Count != 0)
        {
            State = FrameTransactionState.Blocked;
            error = admissionFailures[0];
            return false;
        }

        if (!GenerationMatches())
        {
            State = FrameTransactionState.Blocked;
            error = "visual-tree-generation-drift";
            return false;
        }

        if (!SelectorEngine.TryResolve(
                _tree,
                _selectors,
                out _resolved,
                out error))
        {
            State = FrameTransactionState.Blocked;
            return false;
        }

        State = FrameTransactionState.Discovered;
        return true;
    }

    public bool TryPrepare(
        IReadOnlyList<StyleIntent> intents,
        out string error)
    {
        if (State != FrameTransactionState.Discovered)
        {
            error = "prepare-state-invalid";
            return false;
        }

        if (!GenerationMatches())
        {
            State = FrameTransactionState.Blocked;
            error = "visual-tree-generation-drift";
            return false;
        }

        if (intents.Count == 0)
        {
            State = FrameTransactionState.Blocked;
            error = "style-intents-empty";
            return false;
        }

        HashSet<string> uniqueTargets = new(StringComparer.Ordinal);
        foreach (StyleIntent intent in intents
                     .OrderBy(
                         item => RoleOrder(item.Role))
                     .ThenBy(
                         item => PropertyOrder(item.Property)))
        {
            if (!SurfaceRoles.RequiredRoles.Contains(intent.Role))
            {
                State = FrameTransactionState.Blocked;
                error = $"style-role-not-allowed:{intent.Role}";
                return false;
            }

            if (!StyleProperties.AllowList.Contains(intent.Property))
            {
                State = FrameTransactionState.Blocked;
                error = $"style-property-not-allowed:{intent.Property}";
                return false;
            }

            string key = $"{intent.Role}\0{intent.Property}";
            if (!uniqueTargets.Add(key))
            {
                State = FrameTransactionState.Blocked;
                error = $"style-property-duplicated:{intent.Role}:{intent.Property}";
                return false;
            }

            VisualNode node = _resolved[intent.Role];
            if (!node.Properties.TryGetValue(
                    intent.Property,
                    out string? originalValue))
            {
                State = FrameTransactionState.Blocked;
                error = $"original-property-missing:{intent.Role}:{intent.Property}";
                return false;
            }

            _snapshots.Add(
                new PropertySnapshot(
                    intent.Role,
                    node.NodeId,
                    intent.Property,
                    originalValue,
                    intent.Value));
        }

        State = FrameTransactionState.Prepared;
        error = string.Empty;
        return true;
    }

    public bool TryApply(
        FaultProfile fault,
        out string error)
    {
        if (State != FrameTransactionState.Prepared)
        {
            error = "apply-state-invalid";
            return false;
        }

        if (!GenerationMatches())
        {
            State = FrameTransactionState.Blocked;
            error = "visual-tree-generation-drift";
            return false;
        }

        for (int index = 0; index < _snapshots.Count; index++)
        {
            if (fault.FailApplyAtIndex == index)
            {
                error = "apply-fault-simulated";
                if (_applied.Count == 0)
                {
                    State = FrameTransactionState.Blocked;
                    return false;
                }

                State = FrameTransactionState.RestoreRequired;
                bool restored = TryRestoreInternal(
                    fault.FailRestoreAtIndex,
                    out string restoreError);
                if (!restored)
                {
                    error = $"{error};{restoreError}";
                }

                return false;
            }

            PropertySnapshot snapshot = _snapshots[index];
            VisualNode node = _tree.GetRequiredNode(snapshot.NodeId);
            node.Properties[snapshot.Property] = snapshot.StyledValue;
            _applied.Add(snapshot);
            AddAudit("apply", snapshot);
        }

        State = FrameTransactionState.Applied;
        error = string.Empty;
        return true;
    }

    public bool TryRestore(
        FaultProfile fault,
        out string error)
    {
        if (State == FrameTransactionState.Restored)
        {
            error = string.Empty;
            return true;
        }

        if (State != FrameTransactionState.Applied &&
            State != FrameTransactionState.RestoreRequired)
        {
            error = "restore-state-invalid";
            return false;
        }

        if (!GenerationMatches())
        {
            State = FrameTransactionState.RestoreRequired;
            error = "visual-tree-generation-drift";
            return false;
        }

        return TryRestoreInternal(
            fault.FailRestoreAtIndex,
            out error);
    }

    private bool TryRestoreInternal(
        int? failRestoreAtIndex,
        out string error)
    {
        State = FrameTransactionState.Restoring;
        int restoreIndex = 0;
        while (_applied.Count != 0)
        {
            if (failRestoreAtIndex == restoreIndex)
            {
                State = FrameTransactionState.RestoreRequired;
                error = "restore-fault-simulated";
                return false;
            }

            int last = _applied.Count - 1;
            PropertySnapshot snapshot = _applied[last];
            VisualNode node = _tree.GetRequiredNode(snapshot.NodeId);
            node.Properties[snapshot.Property] = snapshot.OriginalValue;
            _applied.RemoveAt(last);
            AddAudit("restore", snapshot);
            restoreIndex++;
        }

        State = FrameTransactionState.Restored;
        error = string.Empty;
        return true;
    }

    private bool GenerationMatches()
    {
        return string.Equals(
            _target.VisualTreeGeneration,
            _tree.Generation,
            StringComparison.Ordinal);
    }

    private void AddAudit(
        string action,
        PropertySnapshot snapshot)
    {
        _audit.Add(
            new AuditEvent(
                _audit.Count,
                action,
                snapshot.Role,
                snapshot.NodeId,
                snapshot.Property));
    }

    private static int RoleOrder(string role)
    {
        return role switch
        {
            SurfaceRoles.TabStrip => 0,
            SurfaceRoles.CommandBar => 1,
            SurfaceRoles.NavigationPane => 2,
            _ => int.MaxValue,
        };
    }

    private static int PropertyOrder(string property)
    {
        return property switch
        {
            StyleProperties.Background => 0,
            StyleProperties.Foreground => 1,
            StyleProperties.BorderBrush => 2,
            _ => int.MaxValue,
        };
    }
}
