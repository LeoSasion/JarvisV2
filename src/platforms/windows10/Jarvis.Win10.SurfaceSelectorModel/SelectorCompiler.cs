using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Win10.SurfaceSelectorModel;

internal static class SelectorCompiler
{
    public static SelectorCompilationReceipt Compile(
        SelectorCandidateDocument candidate,
        TopologyFixtureDocument evidence,
        string candidateSha256,
        string evidenceSha256)
    {
        List<string> failures = [];
        List<SelectorResolution> resolutions = [];

        ValidateCandidateIdentity(candidate, failures);
        ValidateEvidenceIdentity(evidence, failures);
        Dictionary<string, SurfaceFixture> surfaceByKind =
            ValidateFixtureSurfaces(evidence, failures);

        ValidateRoleAndIdSet(candidate, failures);
        HashSet<string> resolvedNodes = new(StringComparer.Ordinal);
        foreach (SurfaceSelectorCandidate selector in
                     candidate.Selectors.OrderBy(
                         selector => selector.Role,
                         StringComparer.Ordinal))
        {
            ValidateSelectorShape(selector, failures);
            if (selector.ExpectedMatchCount != 1)
            {
                failures.Add(
                    $"selector-match-count-not-one:{selector.Role}");
            }

            if (!surfaceByKind.TryGetValue(
                    selector.SurfaceKind,
                    out SurfaceFixture? surface))
            {
                failures.Add(
                    $"selector-surface-missing:{selector.Role}");
                continue;
            }

            List<FixtureNode> matches = surface.Nodes
                .Where(node =>
                    node.Visible == selector.RequiredVisible &&
                    BuildClassPath(node, surface, failures)
                        .SequenceEqual(
                            selector.ClassPath,
                            StringComparer.Ordinal))
                .ToList();
            if (matches.Count != selector.ExpectedMatchCount)
            {
                failures.Add(
                    $"selector-observed-match-count-invalid:" +
                    $"{selector.Role}:{matches.Count}");
                continue;
            }

            FixtureNode match = matches[0];
            string resolvedIdentity =
                $"{surface.SurfaceKind}:{match.NodeKey}";
            if (!resolvedNodes.Add(resolvedIdentity))
            {
                failures.Add(
                    $"resolved-node-reused:{selector.Role}");
                continue;
            }

            string classPath = string.Join("/", selector.ClassPath);
            resolutions.Add(
                new SelectorResolution(
                    selector.Id,
                    selector.Role,
                    selector.SurfaceKind,
                    classPath,
                    match.NodeKey,
                    Fingerprint(
                        $"{selector.SurfaceKind}|{classPath}|" +
                        $"{selector.RequiredVisible}"),
                    selector.RequiredVisible));
        }

        bool passed =
            failures.Count == 0 &&
            resolutions.Count == SelectorContract.RequiredShapes.Count;
        return new SelectorCompilationReceipt(
            1,
            "jarvisv2-win10-surface-selector-compilation",
            passed
                ? "compiled-offline-selector-candidates"
                : "blocked",
            candidate.ProfileId,
            candidate.SelectorSetId,
            candidateSha256,
            evidenceSha256,
            resolutions,
            false,
            false,
            false,
            false,
            false,
            "not-run",
            false,
            failures.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateCandidateIdentity(
        SelectorCandidateDocument candidate,
        ICollection<string> failures)
    {
        Require(
            candidate.SchemaVersion == 1,
            "candidate-schema-version-invalid",
            failures);
        Require(
            candidate.Platform == SelectorContract.Platform,
            "candidate-platform-invalid",
            failures);
        Require(
            candidate.ProfileId == SelectorContract.ProfileId,
            "candidate-profile-id-invalid",
            failures);
        Require(
            candidate.SelectorSetId == SelectorContract.SelectorSetId,
            "candidate-selector-set-id-invalid",
            failures);
        Require(
            candidate.Status == SelectorContract.CandidateStatus,
            "candidate-status-invalid",
            failures);
        Require(
            candidate.Origin == SelectorContract.EvidenceOrigin,
            "candidate-origin-invalid",
            failures);
        Require(
            !candidate.StyleValuesDefined,
            "candidate-style-values-must-be-absent",
            failures);
        Require(
            !candidate.ExecutionSupported &&
            !candidate.MutationSupported &&
            !candidate.ActivationPermitted &&
            candidate.LiveExplorer == "not-run",
            "candidate-offline-boundary-invalid",
            failures);
    }

    private static void ValidateEvidenceIdentity(
        TopologyFixtureDocument evidence,
        ICollection<string> failures)
    {
        Require(
            evidence.SchemaVersion == 1,
            "evidence-schema-version-invalid",
            failures);
        Require(
            evidence.FixtureType ==
                "sanitized-selector-evidence-excerpt",
            "evidence-fixture-type-invalid",
            failures);
        Require(
            evidence.ProfileId == SelectorContract.ProfileId,
            "evidence-profile-id-invalid",
            failures);
        Require(
            evidence.Source == SelectorContract.EvidenceOrigin,
            "evidence-origin-invalid",
            failures);
        Require(
            !evidence.WindowTextCollected &&
            !evidence.ContainsUserContent,
            "evidence-privacy-boundary-invalid",
            failures);
    }

    private static Dictionary<string, SurfaceFixture>
        ValidateFixtureSurfaces(
            TopologyFixtureDocument evidence,
            ICollection<string> failures)
    {
        Dictionary<string, SurfaceFixture> result =
            new(StringComparer.Ordinal);
        foreach (SurfaceFixture surface in evidence.Surfaces)
        {
            if (!result.TryAdd(surface.SurfaceKind, surface))
            {
                failures.Add(
                    $"fixture-surface-duplicated:{surface.SurfaceKind}");
                continue;
            }

            if (surface.SourceNodeCount < surface.Nodes.Length)
            {
                failures.Add(
                    $"fixture-source-node-count-invalid:" +
                    surface.SurfaceKind);
            }

            if (surface.ObservedTopologySha256.Length != 64 ||
                surface.ObservedTopologySha256.Any(
                    character => !Uri.IsHexDigit(character)))
            {
                failures.Add(
                    $"fixture-topology-hash-invalid:" +
                    surface.SurfaceKind);
            }

            Dictionary<string, FixtureNode> nodes =
                new(StringComparer.Ordinal);
            foreach (FixtureNode node in surface.Nodes)
            {
                if (!nodes.TryAdd(node.NodeKey, node))
                {
                    failures.Add(
                        $"fixture-node-key-duplicated:" +
                        $"{surface.SurfaceKind}:{node.NodeKey}");
                }
            }

            if (!nodes.TryGetValue("root", out FixtureNode? root) ||
                root.ParentKey is not null ||
                root.ClassName != surface.RootClass)
            {
                failures.Add(
                    $"fixture-root-invalid:{surface.SurfaceKind}");
            }

            foreach (FixtureNode node in surface.Nodes)
            {
                if (node.ParentKey is not null &&
                    !nodes.ContainsKey(node.ParentKey))
                {
                    failures.Add(
                        $"fixture-node-parent-missing:" +
                        $"{surface.SurfaceKind}:{node.NodeKey}");
                }
            }
        }

        string[] requiredSurfaces =
            SelectorContract.RequiredShapes.Values
                .Select(shape => shape.SurfaceKind)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        if (result.Count != requiredSurfaces.Length ||
            requiredSurfaces.Any(surface => !result.ContainsKey(surface)))
        {
            failures.Add("fixture-surface-set-not-exact");
        }

        return result;
    }

    private static void ValidateRoleAndIdSet(
        SelectorCandidateDocument candidate,
        ICollection<string> failures)
    {
        if (candidate.Selectors.Length !=
                SelectorContract.RequiredShapes.Count ||
            !candidate.Selectors
                .Select(selector => selector.Role)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(SelectorContract.RequiredShapes.Keys))
        {
            failures.Add("selector-role-set-not-exact");
        }

        if (candidate.Selectors
            .GroupBy(selector => selector.Role, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            failures.Add("selector-role-duplicated");
        }

        if (candidate.Selectors
            .GroupBy(selector => selector.Id, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            failures.Add("selector-id-duplicated");
        }
    }

    private static void ValidateSelectorShape(
        SurfaceSelectorCandidate selector,
        ICollection<string> failures)
    {
        if (!SelectorContract.RequiredShapes.TryGetValue(
                selector.Role,
                out SelectorShape? expected))
        {
            failures.Add($"selector-role-unknown:{selector.Role}");
            return;
        }

        if (selector.Id != expected.Id ||
            selector.SurfaceKind != expected.SurfaceKind ||
            selector.RequiredVisible != expected.RequiredVisible ||
            !selector.ClassPath.SequenceEqual(
                expected.ClassPath,
                StringComparer.Ordinal))
        {
            failures.Add($"selector-shape-invalid:{selector.Role}");
        }
    }

    private static IReadOnlyList<string> BuildClassPath(
        FixtureNode node,
        SurfaceFixture surface,
        ICollection<string> failures)
    {
        Dictionary<string, FixtureNode> nodes =
            surface.Nodes
                .GroupBy(item => item.NodeKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
        List<string> reversePath = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        FixtureNode? current = node;
        while (current is not null)
        {
            if (!visited.Add(current.NodeKey))
            {
                failures.Add(
                    $"fixture-parent-cycle:" +
                    $"{surface.SurfaceKind}:{node.NodeKey}");
                return [];
            }

            reversePath.Add(current.ClassName);
            if (current.ParentKey is null)
            {
                break;
            }

            if (!nodes.TryGetValue(
                    current.ParentKey,
                    out FixtureNode? parent))
            {
                return [];
            }

            current = parent;
        }

        reversePath.Reverse();
        return reversePath;
    }

    private static string Fingerprint(string text) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Require(
        bool condition,
        string failure,
        ICollection<string> failures)
    {
        if (!condition)
        {
            failures.Add(failure);
        }
    }
}
