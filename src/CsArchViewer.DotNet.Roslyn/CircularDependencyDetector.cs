using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class CircularDependencyDetector
{
    public sealed record NamespaceCycle(IReadOnlyList<string> Nodes);

    public IReadOnlyList<NamespaceCycle> DetectCycles(ArchitectureGraph namespaceGraph)
    {
        var adjacency = namespaceGraph.Nodes
            .ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var edge in namespaceGraph.Edges.Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace))
        {
            if (adjacency.TryGetValue(edge.FromNodeId, out var neighbors))
            {
                neighbors.Add(edge.ToNodeId);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cycleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<NamespaceCycle>();

        foreach (var node in adjacency.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            Dfs(node);
        }

        return result;

        void Dfs(string node)
        {
            if (visited.Contains(node))
            {
                return;
            }

            visited.Add(node);
            onStack.Add(node);
            stack.Add(node);

            foreach (var neighbor in adjacency[node])
            {
                if (!visited.Contains(neighbor))
                {
                    Dfs(neighbor);
                    continue;
                }

                if (!onStack.Contains(neighbor))
                {
                    continue;
                }

                var startIndex = stack.FindLastIndex(item => string.Equals(item, neighbor, StringComparison.OrdinalIgnoreCase));
                if (startIndex < 0)
                {
                    continue;
                }

                var cycleNodes = stack.Skip(startIndex).ToList();
                cycleNodes.Add(neighbor);
                if (cycleNodes.Count < 3)
                {
                    continue;
                }

                var normalized = NormalizeCycle(cycleNodes);
                var key = string.Join("->", normalized);
                if (!cycleKeys.Add(key))
                {
                    continue;
                }

                result.Add(new NamespaceCycle(normalized));
            }

            onStack.Remove(node);
            stack.RemoveAt(stack.Count - 1);
        }
    }

    public void AnnotateGraphWithCircularEdges(ArchitectureGraph namespaceGraph, IReadOnlyList<NamespaceCycle> cycles)
    {
        var edgeKeys = namespaceGraph.Edges
            .Select(edge => $"{edge.Type}:{edge.FromNodeId}->{edge.ToNodeId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var cycle in cycles)
        {
            for (var i = 0; i < cycle.Nodes.Count - 1; i++)
            {
                var from = cycle.Nodes[i];
                var to = cycle.Nodes[i + 1];
                var key = $"{ArchitectureEdgeType.CircularDependency}:{from}->{to}";
                if (!edgeKeys.Add(key))
                {
                    continue;
                }

                namespaceGraph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = from,
                    ToNodeId = to,
                    Label = "CircularDependency",
                    Type = ArchitectureEdgeType.CircularDependency
                });
            }
        }
    }

    private static IReadOnlyList<string> NormalizeCycle(IReadOnlyList<string> cycleNodes)
    {
        var ring = cycleNodes.Take(cycleNodes.Count - 1).ToList();
        if (ring.Count == 0)
        {
            return cycleNodes;
        }

        var minIndex = 0;
        for (var i = 1; i < ring.Count; i++)
        {
            if (string.Compare(ring[i], ring[minIndex], StringComparison.OrdinalIgnoreCase) < 0)
            {
                minIndex = i;
            }
        }

        var normalized = ring.Skip(minIndex).Concat(ring.Take(minIndex)).ToList();
        normalized.Add(normalized[0]);
        return normalized;
    }
}
