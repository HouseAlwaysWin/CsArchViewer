using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Metrics;

public sealed class NamespaceMetricsAnalyzer
{
    public IReadOnlyList<NamespaceMetrics> Analyze(
        IReadOnlyList<FileMetrics> files,
        IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        if (!graphs.TryGetValue(GraphType.NamespaceDependencies, out var namespaceGraph))
        {
            return [];
        }

        var typeCountByNamespace = BuildTypeCountLookup(graphs);
        var result = new List<NamespaceMetrics>();
        foreach (var node in namespaceGraph.Nodes.Where(x => x.Type == ArchitectureNodeType.Namespace))
        {
            var namespaceName = node.Name;
            var namespaceFiles = files.Where(f => FileContainsNamespace(f.FilePath, namespaceName)).ToList();
            var outgoing = namespaceGraph.Edges.Count(e =>
                e.Type == ArchitectureEdgeType.UsesNamespace &&
                string.Equals(e.FromNodeId, node.Id, StringComparison.OrdinalIgnoreCase));
            var incoming = namespaceGraph.Edges.Count(e =>
                e.Type == ArchitectureEdgeType.UsesNamespace &&
                string.Equals(e.ToNodeId, node.Id, StringComparison.OrdinalIgnoreCase));

            result.Add(new NamespaceMetrics
            {
                Namespace = namespaceName,
                FileCount = namespaceFiles.Count,
                TypeCount = typeCountByNamespace.TryGetValue(namespaceName, out var typeCount) ? typeCount : 0,
                TotalLines = namespaceFiles.Sum(f => f.TotalLines),
                DependencyCount = outgoing,
                ReferencedByCount = incoming
            });
        }

        return result
            .OrderByDescending(x => x.TotalLines)
            .ThenBy(x => x.Namespace, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> BuildTypeCountLookup(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!graphs.TryGetValue(GraphType.TypeDependencies, out var typeGraph))
        {
            return lookup;
        }

        foreach (var node in typeGraph.Nodes.Where(x => x.Type is ArchitectureNodeType.Type or ArchitectureNodeType.Interface or ArchitectureNodeType.Struct or ArchitectureNodeType.Enum or ArchitectureNodeType.Record))
        {
            var ns = node.Metadata.TryGetValue("Namespace", out var value) ? value : string.Empty;
            if (string.IsNullOrWhiteSpace(ns))
            {
                continue;
            }

            lookup[ns] = lookup.TryGetValue(ns, out var count) ? count + 1 : 1;
        }

        return lookup;
    }

    private static bool FileContainsNamespace(string filePath, string namespaceName)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            return text.Contains($"namespace {namespaceName}", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
