using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class DependencyMatrixBuilder
{
    public ArchitectureGraph BuildProjectMatrixGraph(IReadOnlyList<TypeDescriptor> types)
    {
        var graph = new ArchitectureGraph();
        var typeByName = types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        var projectByType = types.ToDictionary(
            t => t.FullName,
            t => InferProjectName(t.Namespace),
            StringComparer.Ordinal);

        var projects = projectByType.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var project in projects)
        {
            graph.Nodes.Add(new ArchitectureNode
            {
                Id = project,
                Name = project,
                Type = ArchitectureNodeType.Project,
                FullPath = project,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        var matrix = new Dictionary<(string Source, string Target), int>();
        foreach (var source in types)
        {
            var sourceProject = projectByType[source.FullName];
            foreach (var dep in source.BaseTypes.Concat(source.Interfaces).Concat(source.ReferencedTypes))
            {
                if (!typeByName.TryGetValue(dep, out var targetType))
                {
                    continue;
                }

                var targetProject = projectByType[targetType.FullName];
                if (string.Equals(sourceProject, targetProject, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = (sourceProject, targetProject);
                matrix[key] = matrix.TryGetValue(key, out var current) ? current + 1 : 1;
            }
        }

        foreach (var pair in matrix.OrderBy(x => x.Key.Source, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.Target, StringComparer.OrdinalIgnoreCase))
        {
            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = pair.Key.Source,
                ToNodeId = pair.Key.Target,
                Label = $"UsesType ({pair.Value})",
                Type = ArchitectureEdgeType.UsesType
            });
        }

        BuildMatrixMetadata(graph, projects, matrix);
        ApplyGridLayout(graph);
        return graph;
    }

    private static void BuildMatrixMetadata(
        ArchitectureGraph graph,
        IReadOnlyList<string> projects,
        IReadOnlyDictionary<(string Source, string Target), int> matrix)
    {
        foreach (var node in graph.Nodes)
        {
            var row = projects
                .Select(target =>
                {
                    if (string.Equals(target, node.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{target}: -";
                    }

                    var value = matrix.TryGetValue((node.Name, target), out var count) ? count : 0;
                    return $"{target}: {value}";
                });

            node.Metadata["MatrixRow"] = string.Join(Environment.NewLine, row);
        }
    }

    private static string InferProjectName(string ns)
    {
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }

        return ns;
    }

    private static void ApplyGridLayout(ArchitectureGraph graph)
    {
        const double xSpacing = 280;
        const double ySpacing = 120;
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            graph.Nodes[i].X = 80 + (i % 3) * xSpacing;
            graph.Nodes[i].Y = 80 + (i / 3) * ySpacing;
        }
    }
}
