using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CsArchViewer.Analysis;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool CanDrillIntoFolder(ArchitectureNode node)
    {
        if (!_graphs.TryGetValue(GraphType.FileStructure, out var fileGraph))
        {
            return false;
        }

        var prefix = node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;

        return fileGraph.Nodes.Any(candidate =>
            !string.Equals(candidate.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase) &&
            candidate.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplySearch()
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var keyword = SearchText?.Trim() ?? string.Empty;
        var relationVisibleNodeIds = BuildRelationVisibleNodeIds();
        var filtered = _allProjects
            .Where(project => MatchesTypeFilter(project) &&
                              (keyword.Length == 0 ||
                               project.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                               project.CsProjPath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Projects.Clear();
        foreach (var project in filtered)
        {
            Projects.Add(project);
        }

        ListedNodes.Clear();
        foreach (var node in Graph.Nodes.Where(MatchesTypeFilter))
        {
            if (!MatchesRelationFilter(node, relationVisibleNodeIds))
            {
                continue;
            }

            if (keyword.Length == 0 ||
                node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                node.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                ListedNodes.Add(node);
            }
        }

        MarkSearchMatches(relationVisibleNodeIds);
        ApplyMetricsOverlay();
        Graph.Touch();
    }

    private void MarkSearchMatches(HashSet<string>? relationVisibleNodeIds)
    {
        var keyword = SearchText?.Trim() ?? string.Empty;
        var relationVisibleEdges = BuildRelationVisibleEdges();

        foreach (var node in Graph.Nodes)
        {
            var typeVisible = MatchesTypeFilter(node);
            var drillVisible = MatchesDrillFilter(node);
            var relationVisible = MatchesRelationFilter(node, relationVisibleNodeIds);
            var matched = typeVisible &&
                          drillVisible &&
                          relationVisible &&
                          keyword.Length > 0 &&
                          (node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           node.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            var metricsVisible = MatchesMetricsFilter(node);
            node.Metadata["IsSearchHit"] = matched ? "true" : "false";
            node.Metadata["IsTypeVisible"] = (typeVisible && drillVisible && metricsVisible && relationVisible) ? "true" : "false";
        }

        foreach (var edge in Graph.Edges)
        {
            var key = $"{edge.FromNodeId}->{edge.ToNodeId}";
            var isVisible = relationVisibleEdges is null || relationVisibleEdges.Contains(key);
            edge.Metadata["IsRelationVisible"] = isVisible ? "true" : "false";
        }
    }

    private HashSet<string>? BuildRelationVisibleNodeIds()
    {
        if (!ShowSelectedNodeNeighborhoodOnly || Graph.SelectedNode is null)
        {
            return null;
        }

        var selectedId = Graph.SelectedNode.Id;
        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectedId };
        foreach (var edge in Graph.Edges)
        {
            if (string.Equals(edge.FromNodeId, selectedId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edge.ToNodeId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                related.Add(edge.FromNodeId);
                related.Add(edge.ToNodeId);
            }
        }

        return related;
    }

    private HashSet<string>? BuildRelationVisibleEdges()
    {
        if (!ShowSelectedNodeNeighborhoodOnly || Graph.SelectedNode is null)
        {
            return null;
        }

        var selectedId = Graph.SelectedNode.Id;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in Graph.Edges)
        {
            if (string.Equals(edge.FromNodeId, selectedId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edge.ToNodeId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                keys.Add($"{edge.FromNodeId}->{edge.ToNodeId}");
            }
        }

        return keys;
    }

    private static bool MatchesRelationFilter(ArchitectureNode node, HashSet<string>? relationVisibleNodeIds)
    {
        return relationVisibleNodeIds is null || relationVisibleNodeIds.Contains(node.Id);
    }

    private static void UpdateRecentSearchHistory(ObservableCollection<string> collection, string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (collection.Count > 0 &&
            normalized.StartsWith(collection[0], StringComparison.OrdinalIgnoreCase))
        {
            collection[0] = normalized;
        }
        else
        {
            var existingIndex = collection
                .Select((item, index) => new { item, index })
                .FirstOrDefault(x => string.Equals(x.item, normalized, StringComparison.OrdinalIgnoreCase))
                ?.index;

            if (existingIndex.HasValue)
            {
                collection.RemoveAt(existingIndex.Value);
            }

            collection.Insert(0, normalized);
        }

        while (collection.Count > 10)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private bool MatchesTypeFilter(ProjectInfo project)
    {
        return SelectedTypeFilter switch
        {
            "Library" => IsLibrary(project.OutputType),
            "Exe" => IsExecutable(project.OutputType),
            _ => true
        };
    }

    private static bool IsExecutable(string outputType)
    {
        return outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
               outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLibrary(string outputType)
    {
        return !IsExecutable(outputType);
    }

    private bool MatchesTypeFilter(ArchitectureNode node)
    {
        if (SelectedTypeFilter == "All")
        {
            return true;
        }

        if (node.Type != ArchitectureNodeType.Project)
        {
            return true;
        }

        var outputType = node.Metadata.TryGetValue("OutputType", out var typeValue) ? typeValue : string.Empty;
        return SelectedTypeFilter switch
        {
            "Library" => IsLibrary(outputType),
            "Exe" => IsExecutable(outputType),
            _ => true
        };
    }

    private bool MatchesDrillFilter(ArchitectureNode node)
    {
        if (SelectedGraphType == GraphType.ProjectDependencies ||
            SelectedGraphType == GraphType.PackageDependencies ||
            SelectedGraphType == GraphType.NamespaceDependencies ||
            SelectedGraphType == GraphType.ArchitectureViolations ||
            SelectedGraphType == GraphType.TypeDependencies ||
            SelectedGraphType == GraphType.TypeInheritance ||
            SelectedGraphType == GraphType.FileDependencies ||
            SelectedGraphType == GraphType.DependencyMatrix)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_drillProjectPath))
        {
            return true;
        }

        var projectDir = Path.GetDirectoryName(_drillProjectPath) ?? string.Empty;
        var inProject = node.Type == ArchitectureNodeType.Solution ||
                        string.Equals(node.FullPath, _drillProjectPath, StringComparison.OrdinalIgnoreCase) ||
                        node.FullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase);

        if (!inProject)
        {
            return false;
        }

        if (SelectedGraphType != GraphType.FileStructure || string.IsNullOrWhiteSpace(_drillFolderPath))
        {
            return true;
        }

        if (node.Type == ArchitectureNodeType.Solution || node.Type == ArchitectureNodeType.Project)
        {
            return true;
        }

        return node.FullPath.StartsWith(_drillFolderPath, StringComparison.OrdinalIgnoreCase) ||
               _drillFolderPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase);
    }
}
