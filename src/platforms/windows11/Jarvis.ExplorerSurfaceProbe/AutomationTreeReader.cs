using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using System.Diagnostics.CodeAnalysis;

namespace Jarvis.ExplorerSurfaceProbe;

internal static class AutomationTreeReader
{
    private const int MaximumNodes = 2048;
    private const int MaximumDepth = 14;

    public static AutomationTreeSnapshot Read(nint windowHandle)
    {
        List<AutomationNodeObservation> nodes = [];
        List<SurfaceHint> hints = [];
        List<string> errors = [];
        bool truncated = false;

        AutomationElement root =
            AutomationElement.FromHandle(windowHandle);
        TreeWalker walker = TreeWalker.RawViewWalker;
        Queue<QueuedElement> queue = new();
        queue.Enqueue(
            new QueuedElement(
                root,
                Depth: 0,
                ParentKey: null,
                SiblingOrdinal: 0));

        while (queue.Count > 0)
        {
            if (nodes.Count >= MaximumNodes)
            {
                truncated = true;
                break;
            }

            QueuedElement queued = queue.Dequeue();
            if (!TryReadNode(
                    queued,
                    out AutomationNodeObservation? node,
                    out string? error))
            {
                if (error is not null)
                {
                    errors.Add(error);
                }

                continue;
            }

            nodes.Add(node);
            string? role = ClassifySurface(node);
            if (role is not null)
            {
                hints.Add(
                    new SurfaceHint(
                        role,
                        node.NodeKey,
                        node.ClassName,
                        node.AutomationId,
                        "uia-topology-hint-not-xaml-proof"));
            }

            if (queued.Depth >= MaximumDepth)
            {
                truncated = true;
                continue;
            }

            try
            {
                AutomationElement? child =
                    walker.GetFirstChild(queued.Element);
                int siblingOrdinal = 0;
                while (child is not null)
                {
                    if (nodes.Count + queue.Count >= MaximumNodes)
                    {
                        truncated = true;
                        break;
                    }

                    queue.Enqueue(
                        new QueuedElement(
                            child,
                            queued.Depth + 1,
                            node.NodeKey,
                            siblingOrdinal));
                    siblingOrdinal++;
                    child = walker.GetNextSibling(child);
                }
            }
            catch (
                Exception exception) when (
                exception is ElementNotAvailableException ||
                exception is InvalidOperationException ||
                exception is COMException)
            {
                errors.Add(
                    $"child-enumeration-unavailable:{node.NodeKey}");
            }
        }

        string topologyMaterial = string.Join(
            "\n",
            nodes.Select(
                node =>
                    $"{node.NodeKey}|{node.ParentKey}|{node.Depth}|" +
                    $"{node.SiblingOrdinal}|{node.ClassName}|" +
                    $"{node.AutomationId}|{node.ControlType}"));
        string topologySha256 = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(topologyMaterial)));

        return new AutomationTreeSnapshot(
            NodeCount: nodes.Count,
            MaximumDepthObserved:
                nodes.Count == 0 ? 0 : nodes.Max(node => node.Depth),
            Truncated: truncated,
            TopologySha256: topologySha256,
            Nodes: nodes,
            SurfaceHints: hints,
            Errors: errors);
    }

    private static bool TryReadNode(
        QueuedElement queued,
        [NotNullWhen(true)]
        out AutomationNodeObservation? node,
        out string? error)
    {
        node = null;
        error = null;
        try
        {
            AutomationElement.AutomationElementInformation current =
                queued.Element.Current;
            string className = Bound(current.ClassName, 256);
            string automationId = Bound(current.AutomationId, 256);
            string controlType = Bound(
                current.ControlType?.ProgrammaticName ?? string.Empty,
                128);
            string keyMaterial =
                $"{queued.ParentKey}|{queued.Depth}|" +
                $"{queued.SiblingOrdinal}|{className}|" +
                $"{automationId}|{controlType}";
            string nodeKey = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(keyMaterial)))[..24];
            node = new AutomationNodeObservation(
                NodeKey: nodeKey,
                ParentKey: queued.ParentKey,
                Depth: queued.Depth,
                SiblingOrdinal: queued.SiblingOrdinal,
                ClassName: className,
                AutomationId: automationId,
                ControlType: controlType,
                IsControlElement: current.IsControlElement,
                IsContentElement: current.IsContentElement,
                IsOffscreen: current.IsOffscreen);
            return true;
        }
        catch (
            Exception exception) when (
            exception is ElementNotAvailableException ||
            exception is InvalidOperationException ||
            exception is COMException)
        {
            error = "node-properties-unavailable";
            return false;
        }
    }

    private static string? ClassifySurface(
        AutomationNodeObservation node)
    {
        string material =
            $"{node.ClassName}\0{node.AutomationId}";
        if (material.Contains("TabContainer", StringComparison.Ordinal) ||
            material.Contains(
                "FileExplorerTab",
                StringComparison.Ordinal))
        {
            return "tab-strip";
        }

        if (material.Contains("CommandBar", StringComparison.Ordinal))
        {
            return "command-bar";
        }

        if (material.Contains("NavigationView", StringComparison.Ordinal) ||
            material.Contains("NavigationPane", StringComparison.Ordinal))
        {
            return "navigation-pane";
        }

        return null;
    }

    private static string Bound(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
    }

    private sealed record QueuedElement(
        AutomationElement Element,
        int Depth,
        string? ParentKey,
        int SiblingOrdinal);
}
