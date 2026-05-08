using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class FileDependencyAnalyzer
{
    public ArchitectureGraph BuildGraph(IReadOnlyList<TypeDescriptor> types)
    {
        var graph = new ArchitectureGraph();
        var fileNodes = new Dictionary<string, ArchitectureNode>(StringComparer.OrdinalIgnoreCase);
        var typesByName = types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.FilePath))
            {
                continue;
            }

            if (!fileNodes.ContainsKey(type.FilePath))
            {
                fileNodes[type.FilePath] = new ArchitectureNode
                {
                    Id = type.FilePath,
                    Name = Path.GetFileName(type.FilePath),
                    Type = ArchitectureNodeType.File,
                    FullPath = type.FilePath,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineCount"] = GetLineCount(type.FilePath).ToString(),
                        ["ReferencedFiles"] = "(none)",
                        ["ReferencedBy"] = "(none)"
                    }
                };
            }
        }

        graph.Nodes.AddRange(fileNodes.Values.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase));

        foreach (var source in types)
        {
            foreach (var dependency in source.BaseTypes.Concat(source.Interfaces).Concat(source.ReferencedTypes))
            {
                if (!typesByName.TryGetValue(dependency, out var target))
                {
                    continue;
                }

                if (string.Equals(source.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!fileNodes.ContainsKey(source.FilePath) || !fileNodes.ContainsKey(target.FilePath))
                {
                    continue;
                }

                var key = $"{source.FilePath}->{target.FilePath}";
                if (!edgeKeys.Add(key))
                {
                    continue;
                }

                graph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = source.FilePath,
                    ToNodeId = target.FilePath,
                    Label = "UsesFile",
                    Type = ArchitectureEdgeType.UsesFile
                });
            }
        }

        PopulateMetadata(graph);
        ApplyLayeredLayout(graph);
        return graph;
    }

    private static void PopulateMetadata(ArchitectureGraph graph)
    {
        var outgoing = graph.Edges
            .GroupBy(edge => edge.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var incoming = graph.Edges
            .GroupBy(edge => edge.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.FromNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            node.Metadata["ReferencedFiles"] = outgoing.TryGetValue(node.Id, out var refs) && refs.Count > 0
                ? string.Join(Environment.NewLine, refs.Select(Path.GetFileName))
                : "(none)";
            node.Metadata["ReferencedBy"] = incoming.TryGetValue(node.Id, out var by) && by.Count > 0
                ? string.Join(Environment.NewLine, by.Select(Path.GetFileName))
                : "(none)";
        }
    }

    private static void ApplyLayeredLayout(ArchitectureGraph graph)
    {
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            incoming[edge.ToNodeId]++;
            outgoing[edge.FromNodeId].Add(edge.ToNodeId);
        }

        var levels = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(incoming.Where(x => x.Value == 0).Select(x => x.Key));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in outgoing[current])
            {
                levels[next] = Math.Max(levels[next], levels[current] + 1);
                incoming[next]--;
                if (incoming[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        var groups = graph.Nodes.GroupBy(node => levels[node.Id]).OrderBy(g => g.Key).ToList();
        const double xSpacing = 280;
        const double ySpacing = 90;
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

    private static int GetLineCount(string filePath)
    {
        try
        {
            return File.ReadLines(filePath).Count();
        }
        catch
        {
            return 0;
        }
    }
}
