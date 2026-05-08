using System.Text;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class GraphGroupingService
{
    public ArchitectureGraph Group(
        ArchitectureGraph graph,
        GraphGroupingMode mode,
        IReadOnlyList<ProjectInfo> projects)
    {
        if (mode == GraphGroupingMode.None)
        {
            return graph;
        }

        var projectDirectories = projects
            .Select(project => new ProjectDirectory
            {
                Project = project,
                Directory = Path.GetDirectoryName(project.CsProjPath) ?? string.Empty
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Directory))
            .ToList();

        var grouped = new ArchitectureGraph();
        var groupMap = new Dictionary<string, ArchitectureNode>(StringComparer.OrdinalIgnoreCase);
        var memberMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            var key = BuildGroupKey(node, mode, projectDirectories);
            memberMap[node.Id] = key;

            if (groupMap.ContainsKey(key))
            {
                continue;
            }

            groupMap[key] = new ArchitectureNode
            {
                Id = $"group:{mode}:{key}",
                Name = key,
                Type = ArchitectureNodeType.Group,
                FullPath = key,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GroupMode"] = mode.ToString(),
                    ["SummaryCount"] = "0",
                    ["GroupSummary"] = string.Empty
                }
            };
        }

        var membersByGroup = graph.Nodes
            .GroupBy(node => memberMap[node.Id], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in membersByGroup)
        {
            var summary = BuildSummary(pair.Value);
            var groupNode = groupMap[pair.Key];
            groupNode.Metadata["SummaryCount"] = pair.Value.Count.ToString();
            groupNode.Metadata["GroupSummary"] = summary;
            grouped.Nodes.Add(groupNode);
        }

        var edgeMap = new Dictionary<string, ArchitectureEdge>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!memberMap.TryGetValue(edge.FromNodeId, out var fromKey) ||
                !memberMap.TryGetValue(edge.ToNodeId, out var toKey) ||
                string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var aggregatedKey = $"{fromKey}->{toKey}";
            if (!edgeMap.TryGetValue(aggregatedKey, out var aggregated))
            {
                aggregated = new ArchitectureEdge
                {
                    FromNodeId = groupMap[fromKey].Id,
                    ToNodeId = groupMap[toKey].Id,
                    Label = "Aggregated",
                    Type = edge.Type,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AggregatedCount"] = "0"
                    }
                };
                edgeMap[aggregatedKey] = aggregated;
            }

            var count = int.TryParse(aggregated.Metadata["AggregatedCount"], out var parsed) ? parsed : 0;
            aggregated.Metadata["AggregatedCount"] = (count + 1).ToString();
        }

        grouped.Edges.AddRange(edgeMap.Values.OrderBy(x => x.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ToNodeId, StringComparer.OrdinalIgnoreCase));
        ApplySimpleLayout(grouped);
        return grouped;
    }

    private static string BuildGroupKey(
        ArchitectureNode node,
        GraphGroupingMode mode,
        IReadOnlyList<ProjectDirectory> projectDirectories)
    {
        return mode switch
        {
            GraphGroupingMode.Project => InferProject(node, projectDirectories),
            GraphGroupingMode.Namespace => InferNamespaceGroup(node),
            GraphGroupingMode.Folder => InferFolder(node),
            GraphGroupingMode.Layer => InferLayer(node, projectDirectories),
            _ => node.Name
        };
    }

    private static string InferProject(ArchitectureNode node, IReadOnlyList<ProjectDirectory> projectDirectories)
    {
        if (node.Type == ArchitectureNodeType.Project)
        {
            return node.Name;
        }

        var path = node.Metadata.TryGetValue("File", out var filePath) && !string.IsNullOrWhiteSpace(filePath)
            ? filePath
            : node.FullPath;

        foreach (var entry in projectDirectories)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                path.StartsWith(entry.Directory, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Project.Name;
            }
        }

        return "Unmapped Project";
    }

    private static string InferNamespaceGroup(ArchitectureNode node)
    {
        var ns = node.Metadata.TryGetValue("Namespace", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : node.Type == ArchitectureNodeType.Namespace
                ? node.Name
                : string.Empty;

        if (string.IsNullOrWhiteSpace(ns))
        {
            return "Global Namespace";
        }

        var first = ns.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? ns : first;
    }

    private static string InferFolder(ArchitectureNode node)
    {
        var path = node.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Root";
        }

        if (Directory.Exists(path))
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var dir = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(dir) ? "Root" : Path.GetFileName(dir);
    }

    private static string InferLayer(ArchitectureNode node, IReadOnlyList<ProjectDirectory> projectDirectories)
    {
        var seed = string.Join(" ", new[]
        {
            node.Name,
            node.FullPath,
            node.Metadata.TryGetValue("Namespace", out var ns) ? ns : string.Empty,
            InferProject(node, projectDirectories)
        }).ToLowerInvariant();

        if (seed.Contains("infra"))
        {
            return "Infrastructure";
        }

        if (seed.Contains("application") || seed.Contains("app"))
        {
            return "Application";
        }

        if (seed.Contains("ui") || seed.Contains("avalonia") || seed.Contains("web"))
        {
            return "Presentation";
        }

        if (seed.Contains("domain") || seed.Contains("core"))
        {
            return "Core";
        }

        if (seed.Contains("test"))
        {
            return "Tests";
        }

        return "Other";
    }

    private static string BuildSummary(IReadOnlyList<ArchitectureNode> members)
    {
        var sb = new StringBuilder();
        var grouped = members
            .GroupBy(node => node.Type)
            .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {group.Count()}");
        sb.Append(string.Join(", ", grouped));
        return sb.ToString();
    }

    private static void ApplySimpleLayout(ArchitectureGraph graph)
    {
        const double xSpacing = 260;
        const double ySpacing = 110;
        var ordered = graph.Nodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].X = 80 + ((i % 5) * xSpacing);
            ordered[i].Y = 80 + ((i / 5) * ySpacing);
        }
    }

    private sealed record ProjectDirectory
    {
        public required ProjectInfo Project { get; init; }
        public required string Directory { get; init; }
    }
}
