namespace Jarvis.ExplorerFrameModel;

internal enum FrameTransactionState
{
    Cold,
    Discovered,
    Prepared,
    Applied,
    RestoreRequired,
    Restoring,
    Restored,
    Blocked,
}

internal static class SurfaceRoles
{
    public const string TabStrip = "tab-strip";
    public const string CommandBar = "command-bar";
    public const string NavigationPane = "navigation-pane";

    public static readonly IReadOnlySet<string> RequiredRoles =
        new HashSet<string>(
            [TabStrip, CommandBar, NavigationPane],
            StringComparer.Ordinal);
}

internal static class StyleProperties
{
    public const string Background = "Background";
    public const string Foreground = "Foreground";
    public const string BorderBrush = "BorderBrush";

    public static readonly IReadOnlySet<string> AllowList =
        new HashSet<string>(
            [Background, Foreground, BorderBrush],
            StringComparer.Ordinal);
}

internal sealed record TargetIdentity(
    int ProcessId,
    int DesktopShellProcessId,
    int ThreadId,
    string WindowHandle,
    string WindowClass,
    string WindowTitle,
    string ExpectedWindowTitle,
    bool SeparateProcess,
    DateTime ProcessStartTimeUtc,
    string VisualTreeGeneration);

internal sealed record SelectorSpec(
    string Role,
    string RuntimeClass,
    string Name,
    string AncestorRuntimeClass,
    int ExpectedMatchCount,
    string Origin);

internal sealed record StyleIntent(
    string Role,
    string Property,
    string Value);

internal sealed record PropertySnapshot(
    string Role,
    string NodeId,
    string Property,
    string OriginalValue,
    string StyledValue);

internal sealed record FaultProfile(
    int? FailApplyAtIndex = null,
    int? FailRestoreAtIndex = null);

internal sealed record AuditEvent(
    int Sequence,
    string Action,
    string Role,
    string NodeId,
    string Property);

internal sealed class VisualNode
{
    public VisualNode(
        string nodeId,
        string? parentId,
        string runtimeClass,
        string name,
        string role,
        IReadOnlyDictionary<string, string> properties)
    {
        NodeId = nodeId;
        ParentId = parentId;
        RuntimeClass = runtimeClass;
        Name = name;
        Role = role;
        Properties = new Dictionary<string, string>(
            properties,
            StringComparer.Ordinal);
    }

    public string NodeId { get; }

    public string? ParentId { get; }

    public string RuntimeClass { get; }

    public string Name { get; }

    public string Role { get; }

    public Dictionary<string, string> Properties { get; }
}

internal sealed class VisualTreeFixture
{
    private readonly Dictionary<string, VisualNode> _nodes;

    public VisualTreeFixture(
        string generation,
        IEnumerable<VisualNode> nodes)
    {
        Generation = generation;
        _nodes = nodes.ToDictionary(
            node => node.NodeId,
            StringComparer.Ordinal);
    }

    public string Generation { get; set; }

    public IReadOnlyCollection<VisualNode> Nodes => _nodes.Values;

    public VisualNode GetRequiredNode(string nodeId)
    {
        return _nodes[nodeId];
    }

    public VisualNode? GetParent(VisualNode node)
    {
        if (node.ParentId is null)
        {
            return null;
        }

        return _nodes.GetValueOrDefault(node.ParentId);
    }
}

internal sealed record ModelScenarioResult(
    string Name,
    bool Passed,
    string Detail);

internal sealed record ModelTestReceipt(
    int SchemaVersion,
    string ReceiptType,
    string Result,
    int ScenarioCount,
    int PassedCount,
    bool ExecutionSupported,
    bool ActivationPermitted,
    string LiveExplorer,
    bool MutationPerformed,
    IReadOnlyList<ModelScenarioResult> Scenarios);
