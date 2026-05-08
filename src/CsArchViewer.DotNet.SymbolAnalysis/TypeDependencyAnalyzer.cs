using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class TypeDependencyAnalyzer
{
    public ArchitectureGraph BuildGraph(IReadOnlyList<TypeDescriptor> types)
    {
        var graph = new ArchitectureGraph();
        var nodeByType = new Dictionary<string, ArchitectureNode>(StringComparer.Ordinal);
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var node = new ArchitectureNode
            {
                Id = type.FullName,
                Name = type.Name,
                Type = MapNodeType(type.Kind),
                FullPath = type.FullName,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FullTypeName"] = type.FullName,
                    ["Namespace"] = type.Namespace,
                    ["File"] = type.FilePath,
                    ["BaseType"] = type.BaseTypes.Count == 0 ? "(none)" : string.Join(Environment.NewLine, type.BaseTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    ["ImplementedInterfaces"] = type.Interfaces.Count == 0 ? "(none)" : string.Join(Environment.NewLine, type.Interfaces.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    ["ReferencedTypes"] = type.ReferencedTypes.Count == 0 ? "(none)" : string.Join(Environment.NewLine, type.ReferencedTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    ["ReferencedBy"] = "(none)"
                }
            };
            nodeByType[type.FullName] = node;
        }

        graph.Nodes.AddRange(nodeByType.Values.OrderBy(node => node.FullPath, StringComparer.OrdinalIgnoreCase));

        foreach (var type in types)
        {
            AddEdges(type.FullName, type.BaseTypes, ArchitectureEdgeType.Inherits, "Inherits");
            AddEdges(type.FullName, type.Interfaces, ArchitectureEdgeType.Implements, "Implements");
            AddEdges(type.FullName, type.ReferencedTypes, ArchitectureEdgeType.UsesType, "UsesType");
        }

        PopulateReferencedByMetadata(graph);
        DetectCycles(graph);
        ApplyLayeredLayout(graph);
        return graph;

        void AddEdges(string sourceType, IEnumerable<string> targets, ArchitectureEdgeType edgeType, string label)
        {
            foreach (var target in targets)
            {
                if (!nodeByType.ContainsKey(target) || string.Equals(sourceType, target, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = $"{edgeType}:{sourceType}->{target}";
                if (!edgeKeys.Add(key))
                {
                    continue;
                }

                graph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = sourceType,
                    ToNodeId = target,
                    Label = label,
                    Type = edgeType
                });
            }
        }
    }

    private static ArchitectureNodeType MapNodeType(ArchitectureTypeKind kind)
    {
        return kind switch
        {
            ArchitectureTypeKind.Interface => ArchitectureNodeType.Interface,
            ArchitectureTypeKind.Struct => ArchitectureNodeType.Struct,
            ArchitectureTypeKind.Enum => ArchitectureNodeType.Enum,
            ArchitectureTypeKind.Record => ArchitectureNodeType.Record,
            _ => ArchitectureNodeType.Type
        };
    }

    private static void PopulateReferencedByMetadata(ArchitectureGraph graph)
    {
        var incoming = graph.Edges
            .Where(edge => edge.Type is ArchitectureEdgeType.UsesType or ArchitectureEdgeType.Inherits or ArchitectureEdgeType.Implements)
            .GroupBy(edge => edge.ToNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.FromNodeId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            node.Metadata["ReferencedBy"] = incoming.TryGetValue(node.Id, out var refs) && refs.Count > 0
                ? string.Join(Environment.NewLine, refs)
                : "(none)";
        }
    }

    private static void DetectCycles(ArchitectureGraph graph)
    {
        var adjacency = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(edge => edge.Type is ArchitectureEdgeType.UsesType or ArchitectureEdgeType.Inherits or ArchitectureEdgeType.Implements))
        {
            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cycleEdgeKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in adjacency.Keys)
        {
            Dfs(node);
        }

        void Dfs(string current)
        {
            if (!visited.Add(current))
            {
                return;
            }

            onStack.Add(current);
            stack.Add(current);
            foreach (var next in adjacency[current])
            {
                if (!visited.Contains(next))
                {
                    Dfs(next);
                }
                else if (onStack.Contains(next))
                {
                    var start = stack.FindLastIndex(x => string.Equals(x, next, StringComparison.Ordinal));
                    if (start >= 0)
                    {
                        var cycle = stack.Skip(start).ToList();
                        cycle.Add(next);
                        for (var i = 0; i < cycle.Count - 1; i++)
                        {
                            var key = $"{cycle[i]}->{cycle[i + 1]}";
                            if (!cycleEdgeKeys.Add(key))
                            {
                                continue;
                            }

                            graph.Edges.Add(new ArchitectureEdge
                            {
                                FromNodeId = cycle[i],
                                ToNodeId = cycle[i + 1],
                                Label = "CircularDependency",
                                Type = ArchitectureEdgeType.CircularDependency
                            });
                        }
                    }
                }
            }

            onStack.Remove(current);
            stack.RemoveAt(stack.Count - 1);
        }
    }

    private static void ApplyLayeredLayout(ArchitectureGraph graph)
    {
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(edge => edge.Type is ArchitectureEdgeType.UsesType or ArchitectureEdgeType.Inherits or ArchitectureEdgeType.Implements))
        {
            if (incoming.ContainsKey(edge.ToNodeId))
            {
                incoming[edge.ToNodeId]++;
            }

            outgoing[edge.FromNodeId].Add(edge.ToNodeId);
        }

        var level = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var queue = new Queue<string>(incoming.Where(x => x.Value == 0).Select(x => x.Key));
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var target in outgoing[n])
            {
                level[target] = Math.Max(level[target], level[n] + 1);
                incoming[target]--;
                if (incoming[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        var groups = graph.Nodes.GroupBy(node => level[node.Id]).OrderBy(g => g.Key).ToList();
        const double xSpacing = 300;
        const double ySpacing = 96;
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].X = 80 + group.Key * xSpacing;
                ordered[i].Y = 80 + i * ySpacing;
            }
        }
    }
}
