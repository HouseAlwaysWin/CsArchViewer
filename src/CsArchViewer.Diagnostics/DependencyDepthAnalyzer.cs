using CsArchViewer.Core.Models;

namespace CsArchViewer.Diagnostics;

public sealed class DependencyDepthAnalyzer
{
    public IReadOnlyList<ArchitectureDiagnostic> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs, int depthThreshold = 4)
    {
        if (!graphs.TryGetValue(GraphType.TypeDependencies, out var graph))
        {
            return [];
        }

        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges.Where(edge => edge.Type is ArchitectureEdgeType.UsesType or ArchitectureEdgeType.Inherits or ArchitectureEdgeType.Implements))
        {
            if (outgoing.TryGetValue(edge.FromNodeId, out var list))
            {
                list.Add(edge.ToNodeId);
            }
        }

        var diagnostics = new List<ArchitectureDiagnostic>();
        foreach (var node in graph.Nodes)
        {
            var depth = ComputeMaxDepth(node.Id, outgoing, []);
            if (depth <= depthThreshold)
            {
                continue;
            }

            diagnostics.Add(new ArchitectureDiagnostic
            {
                Type = "DependencyDepthWarning",
                Severity = DiagnosticSeverity.Warning,
                Source = node.FullPath,
                Message = $"Dependency depth is {depth}, exceeds threshold {depthThreshold}."
            });
        }

        return diagnostics;
    }

    private static int ComputeMaxDepth(string nodeId, IReadOnlyDictionary<string, List<string>> outgoing, HashSet<string> visited)
    {
        if (!visited.Add(nodeId))
        {
            return 0;
        }

        var max = 0;
        if (outgoing.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var next in neighbors)
            {
                max = Math.Max(max, 1 + ComputeMaxDepth(next, outgoing, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase)));
            }
        }

        return max;
    }
}
