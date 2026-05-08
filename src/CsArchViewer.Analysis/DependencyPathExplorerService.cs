using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class DependencyPathExplorerService
{
    public DependencyPathResult FindShortestPath(ArchitectureGraph graph, string sourceNodeId, string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeId) || string.IsNullOrWhiteSpace(targetNodeId))
        {
            return new DependencyPathResult
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                Summary = "Source or target is empty."
            };
        }

        var parents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [sourceNodeId] = null
        };
        var queue = new Queue<string>();
        queue.Enqueue(sourceNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, targetNodeId, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            foreach (var next in GetOutgoing(graph, current))
            {
                if (parents.ContainsKey(next))
                {
                    continue;
                }

                parents[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!parents.ContainsKey(targetNodeId))
        {
            return new DependencyPathResult
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                Summary = "No dependency path found."
            };
        }

        var nodeIds = RebuildNodePath(targetNodeId, parents);
        return new DependencyPathResult
        {
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            NodeIds = nodeIds,
            EdgeKeys = BuildEdgeKeys(nodeIds),
            Found = true,
            Summary = $"Shortest path length: {Math.Max(0, nodeIds.Count - 1)}"
        };
    }

    public DependencyPathResult FindCycle(ArchitectureGraph graph, string sourceNodeId)
    {
        foreach (var next in GetOutgoing(graph, sourceNodeId))
        {
            var path = FindShortestPath(graph, next, sourceNodeId);
            if (!path.Found)
            {
                continue;
            }

            var cycleNodes = new List<string> { sourceNodeId };
            cycleNodes.AddRange(path.NodeIds);
            return new DependencyPathResult
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = sourceNodeId,
                NodeIds = cycleNodes,
                EdgeKeys = BuildEdgeKeys(cycleNodes),
                Found = true,
                IsCycle = true,
                Summary = $"Cycle length: {Math.Max(0, cycleNodes.Count - 1)}"
            };
        }

        return new DependencyPathResult
        {
            SourceNodeId = sourceNodeId,
            TargetNodeId = sourceNodeId,
            Summary = "No circular path found from selected node."
        };
    }

    private static IReadOnlyList<string> GetOutgoing(ArchitectureGraph graph, string nodeId)
    {
        return graph.Edges
            .Where(edge => string.Equals(edge.FromNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.ToNodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> RebuildNodePath(
        string targetNodeId,
        IReadOnlyDictionary<string, string?> parents)
    {
        var result = new List<string>();
        for (var current = targetNodeId; current is not null; current = parents[current])
        {
            result.Add(current);
        }

        result.Reverse();
        return result;
    }

    private static IReadOnlyList<string> BuildEdgeKeys(IReadOnlyList<string> nodeIds)
    {
        var result = new List<string>();
        for (var i = 0; i < nodeIds.Count - 1; i++)
        {
            result.Add($"{nodeIds[i]}->{nodeIds[i + 1]}");
        }

        return result;
    }
}
