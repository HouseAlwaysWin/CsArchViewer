using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Metrics;

public sealed class DependencyMetricsAnalyzer
{
    public IReadOnlyList<DependencyMetrics> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var result = new List<DependencyMetrics>
        {
            BuildForScope("Project", graphs, GraphType.ProjectDependencies, ArchitectureEdgeType.ProjectReference),
            BuildForScope("Namespace", graphs, GraphType.NamespaceDependencies, ArchitectureEdgeType.UsesNamespace),
            BuildForScope("Type", graphs, GraphType.TypeDependencies, ArchitectureEdgeType.UsesType, ArchitectureEdgeType.Inherits, ArchitectureEdgeType.Implements),
            BuildForScope("File", graphs, GraphType.FileDependencies, ArchitectureEdgeType.UsesFile)
        };

        return result;
    }

    private static DependencyMetrics BuildForScope(
        string scope,
        IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs,
        GraphType graphType,
        params ArchitectureEdgeType[] edgeTypes)
    {
        if (!graphs.TryGetValue(graphType, out var graph))
        {
            return new DependencyMetrics
            {
                Scope = scope,
                OutgoingDependencyCount = 0,
                IncomingDependencyCount = 0,
                CircularDependencyCount = 0,
                DependencyDepth = 0,
                LayerViolationCount = 0
            };
        }

        var edges = graph.Edges.Where(x => edgeTypes.Contains(x.Type)).ToList();
        var outgoing = edges
            .GroupBy(e => e.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .Sum(g => g.Count());
        var incoming = edges
            .GroupBy(e => e.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .Sum(g => g.Count());
        var circular = edges.Count(e => e.Type == ArchitectureEdgeType.CircularDependency);
        var depth = ComputeDepth(graph, edgeTypes);
        var layerViolations = graphs.TryGetValue(GraphType.ArchitectureViolations, out var violationGraph)
            ? violationGraph.Edges.Count(e => e.Type == ArchitectureEdgeType.ViolatesRule)
            : 0;

        return new DependencyMetrics
        {
            Scope = scope,
            OutgoingDependencyCount = outgoing,
            IncomingDependencyCount = incoming,
            CircularDependencyCount = circular,
            DependencyDepth = depth,
            LayerViolationCount = layerViolations
        };
    }

    private static int ComputeDepth(ArchitectureGraph graph, IReadOnlyCollection<ArchitectureEdgeType> edgeTypes)
    {
        var outgoing = graph.Nodes.ToDictionary(x => x.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges.Where(x => edgeTypes.Contains(x.Type)))
        {
            if (outgoing.TryGetValue(edge.FromNodeId, out var list))
            {
                list.Add(edge.ToNodeId);
            }
        }

        var maxDepth = 0;
        foreach (var node in graph.Nodes)
        {
            maxDepth = Math.Max(maxDepth, ComputeDepth(node.Id, outgoing, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        }

        return maxDepth;
    }

    private static int ComputeDepth(string nodeId, IReadOnlyDictionary<string, List<string>> outgoing, HashSet<string> visited)
    {
        if (!visited.Add(nodeId))
        {
            return 0;
        }

        var max = 0;
        if (outgoing.TryGetValue(nodeId, out var nextNodes))
        {
            foreach (var next in nextNodes)
            {
                max = Math.Max(max, 1 + ComputeDepth(next, outgoing, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase)));
            }
        }

        return max;
    }
}
