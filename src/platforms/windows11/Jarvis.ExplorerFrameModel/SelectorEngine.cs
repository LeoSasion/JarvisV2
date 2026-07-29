namespace Jarvis.ExplorerFrameModel;

internal static class SelectorEngine
{
    public const string OfflineCandidateOrigin =
        "offline-fixture-candidate-pending-live-discovery";

    public static bool TryResolve(
        VisualTreeFixture tree,
        IReadOnlyList<SelectorSpec> selectors,
        out IReadOnlyDictionary<string, VisualNode> resolved,
        out string error)
    {
        Dictionary<string, VisualNode> selected =
            new(StringComparer.Ordinal);

        if (selectors.Count != SurfaceRoles.RequiredRoles.Count ||
            !selectors.Select(selector => selector.Role)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(SurfaceRoles.RequiredRoles))
        {
            resolved = selected;
            error = "selector-role-set-not-exact";
            return false;
        }

        foreach (SelectorSpec selector in selectors)
        {
            if (selector.ExpectedMatchCount != 1)
            {
                resolved = selected;
                error = $"selector-expected-count-not-one:{selector.Role}";
                return false;
            }

            if (!string.Equals(
                    selector.Origin,
                    OfflineCandidateOrigin,
                    StringComparison.Ordinal))
            {
                resolved = selected;
                error = $"selector-origin-not-offline-candidate:{selector.Role}";
                return false;
            }

            List<VisualNode> matches = tree.Nodes
                .Where(node => Matches(tree, node, selector))
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)
                .ToList();

            if (matches.Count == 0)
            {
                resolved = selected;
                error = $"selector-match-missing:{selector.Role}";
                return false;
            }

            if (matches.Count != selector.ExpectedMatchCount)
            {
                resolved = selected;
                error = $"selector-match-not-unique:{selector.Role}";
                return false;
            }

            if (selected.Values.Any(
                    node => string.Equals(
                        node.NodeId,
                        matches[0].NodeId,
                        StringComparison.Ordinal)))
            {
                resolved = selected;
                error = $"selector-instance-reused:{selector.Role}";
                return false;
            }

            selected.Add(selector.Role, matches[0]);
        }

        resolved = selected;
        error = string.Empty;
        return true;
    }

    private static bool Matches(
        VisualTreeFixture tree,
        VisualNode node,
        SelectorSpec selector)
    {
        if (!string.Equals(node.Role, selector.Role, StringComparison.Ordinal) ||
            !string.Equals(
                node.RuntimeClass,
                selector.RuntimeClass,
                StringComparison.Ordinal) ||
            !string.Equals(node.Name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        VisualNode? ancestor = tree.GetParent(node);
        while (ancestor is not null)
        {
            if (string.Equals(
                    ancestor.RuntimeClass,
                    selector.AncestorRuntimeClass,
                    StringComparison.Ordinal))
            {
                return true;
            }

            ancestor = tree.GetParent(ancestor);
        }

        return false;
    }
}
