using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class DependencyExplorerService
{
    public DependencyExplorerResult Explore(ArchitectureGraph graph, string nodeId)
    {
        var outgoing = graph.Edges
            .Where(edge => string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.ToNodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var incoming = graph.Edges
            .Where(edge => string.Equals(edge.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.FromNodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var circularCount = graph.Edges.Count(edge =>
            edge.Type == ArchitectureEdgeType.CircularDependency &&
            (string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(edge.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase)));

        var violationCount = graph.Edges.Count(edge =>
            edge.Type == ArchitectureEdgeType.ViolatesRule &&
            (string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(edge.ToNodeId, nodeId, StringComparison.OrdinalIgnoreCase)));

        return new DependencyExplorerResult
        {
            NodeId = nodeId,
            Outgoing = outgoing,
            Incoming = incoming,
            CircularDependencyCount = circularCount,
            ViolationCount = violationCount
        };
    }
}

public sealed class DependencyExplorerResult
{
    public required string NodeId { get; init; }
    public IReadOnlyList<string> Outgoing { get; init; } = [];
    public IReadOnlyList<string> Incoming { get; init; } = [];
    public int CircularDependencyCount { get; init; }
    public int ViolationCount { get; init; }
}
