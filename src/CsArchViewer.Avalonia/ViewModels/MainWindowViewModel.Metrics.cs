using System;
using System.Globalization;
using System.Linq;
using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void SetMetricsSummary(MetricsSummary summary)
    {
        _metricsSummary = summary;

        TopFilesByLineCount.Clear();
        foreach (var file in summary.Files.OrderByDescending(x => x.TotalLines).Take(20))
        {
            TopFilesByLineCount.Add(new FileLineRankItem
            {
                FileName = file.FileName,
                FilePath = file.FilePath,
                LineCount = file.TotalLines
            });
        }

        TopCoupledNamespaces.Clear();
        foreach (var ns in summary.Namespaces
                     .OrderByDescending(x => x.DependencyCount + x.ReferencedByCount)
                     .Take(20))
        {
            TopCoupledNamespaces.Add(ns);
        }

        HealthWarnings.Clear();
        foreach (var warning in summary.HealthWarnings)
        {
            HealthWarnings.Add(warning);
        }

        ApplyMetricsToNodeMetadata(summary);
        ApplyMetricsOverlay();
        ApplyFilters();

        OnPropertyChanged(nameof(MetricsTotalLoc));
        OnPropertyChanged(nameof(MetricsTotalFiles));
        OnPropertyChanged(nameof(MetricsLargestFile));
        OnPropertyChanged(nameof(MetricsLargestNamespace));
        OnPropertyChanged(nameof(MetricsCircularDependencies));
        OnPropertyChanged(nameof(MetricsLayerViolations));
    }

    private bool MatchesMetricsFilter(ArchitectureNode node)
    {
        return SelectedMetricsFilter switch
        {
            "Large Files" => node.Type == ArchitectureNodeType.File &&
                             node.Metadata.TryGetValue("TotalLines", out var lines) &&
                             int.TryParse(lines, out var parsedLines) &&
                             parsedLines >= 2000,
            "Highly Coupled" => node.Metadata.TryGetValue("DependencyCount", out var depCount) &&
                                int.TryParse(depCount, out var parsedDepCount) &&
                                parsedDepCount >= 20,
            "Circular Dependencies" => node.Metadata.TryGetValue("CircularDependencyCount", out var circular) &&
                                       int.TryParse(circular, out var parsedCircular) &&
                                       parsedCircular > 0,
            "High Dependency Depth" => node.Metadata.TryGetValue("DependencyDepth", out var depth) &&
                                       int.TryParse(depth, out var parsedDepth) &&
                                       parsedDepth >= 10,
            _ => true
        };
    }

    private void UpdateTopFileLineRanking()
    {
        TopFilesByLineCount.Clear();
        if (!_graphs.TryGetValue(GraphType.FileStructure, out var fileGraph))
        {
            return;
        }

        var ranked = fileGraph.Nodes
            .Where(node => node.Type == ArchitectureNodeType.File &&
                           node.Metadata.TryGetValue("LineCount", out var lineValue) &&
                           int.TryParse(lineValue, out _))
            .Select(node => new
            {
                Node = node,
                LineCount = int.TryParse(node.Metadata["LineCount"], out var parsed) ? parsed : 0
            })
            .OrderByDescending(x => x.LineCount)
            .ThenBy(x => x.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        foreach (var item in ranked)
        {
            TopFilesByLineCount.Add(new FileLineRankItem
            {
                FileName = item.Node.Name,
                FilePath = item.Node.FullPath,
                LineCount = item.LineCount
            });
        }
    }

    private void ApplyMetricsToNodeMetadata(MetricsSummary summary)
    {
        var fileLookup = summary.Files.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        var projectLookup = summary.Projects.ToDictionary(x => x.ProjectName, StringComparer.OrdinalIgnoreCase);
        var namespaceLookup = summary.Namespaces.ToDictionary(x => x.Namespace, StringComparer.OrdinalIgnoreCase);
        var dependencyDepth = summary.Dependencies.ToDictionary(x => x.Scope, x => x.DependencyDepth, StringComparer.OrdinalIgnoreCase);

        var allNodes = _graphs.Values
            .SelectMany(graph => graph.Nodes)
            .DistinctBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var node in allNodes)
        {
            switch (node.Type)
            {
                case ArchitectureNodeType.File when fileLookup.TryGetValue(node.FullPath, out var file):
                    node.Metadata["TotalLines"] = file.TotalLines.ToString();
                    node.Metadata["CodeLines"] = file.CodeLines.ToString();
                    node.Metadata["CommentLines"] = file.CommentLines.ToString();
                    node.Metadata["BlankLines"] = file.BlankLines.ToString();
                    node.Metadata["FileSize"] = file.FileSizeBytes.ToString();
                    node.Metadata["DependencyCount"] = file.DependencyCount.ToString();
                    node.Metadata["ReferencedByCount"] = file.ReferencedByCount.ToString();
                    break;
                case ArchitectureNodeType.Project when projectLookup.TryGetValue(node.Name, out var project):
                    node.Metadata["TotalLines"] = project.TotalLines.ToString();
                    node.Metadata["TotalFiles"] = project.TotalFiles.ToString();
                    node.Metadata["LargestFile"] = project.LargestFile;
                    node.Metadata["CircularDependencyCount"] = project.CircularDependencyCount.ToString();
                    node.Metadata["DependencyCount"] = project.DependencyCount.ToString();
                    node.Metadata["DependencyDepth"] = dependencyDepth.TryGetValue("Project", out var projectDepth) ? projectDepth.ToString() : "0";
                    break;
                case ArchitectureNodeType.Namespace when namespaceLookup.TryGetValue(node.Name, out var ns):
                    node.Metadata["TypeCount"] = ns.TypeCount.ToString();
                    node.Metadata["TotalLines"] = ns.TotalLines.ToString();
                    node.Metadata["DependencyCount"] = ns.DependencyCount.ToString();
                    node.Metadata["ReferencedByCount"] = ns.ReferencedByCount.ToString();
                    node.Metadata["DependencyDepth"] = dependencyDepth.TryGetValue("Namespace", out var nsDepth) ? nsDepth.ToString() : "0";
                    break;
            }
        }
    }

    private void ApplyMetricsOverlay()
    {
        var depMax = Graph.Nodes
            .Select(node => node.Metadata.TryGetValue("DependencyCount", out var value) && int.TryParse(value, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();
        var lineMax = Graph.Nodes
            .Select(node => node.Metadata.TryGetValue("TotalLines", out var value) && int.TryParse(value, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var node in Graph.Nodes)
        {
            node.Metadata.Remove("OverlayColor");
            node.Metadata.Remove("OverlayScale");

            switch (SelectedOverlayMode)
            {
                case "Dependency Count":
                    if (node.Metadata.TryGetValue("DependencyCount", out var dep) && int.TryParse(dep, out var depCount))
                    {
                        var ratio = depMax <= 0 ? 0 : depCount / (double)depMax;
                        node.Metadata["OverlayScale"] = (1 + (ratio * 0.8)).ToString("F2", CultureInfo.InvariantCulture);
                    }
                    break;
                case "LOC Heatmap":
                    if (node.Metadata.TryGetValue("TotalLines", out var line) && int.TryParse(line, out var lineCount))
                    {
                        var ratio = lineMax <= 0 ? 0 : lineCount / (double)lineMax;
                        node.Metadata["OverlayColor"] = InterpolateColor("#22C55E", "#EF4444", ratio);
                    }
                    break;
                case "Project Size":
                    if (node.Type == ArchitectureNodeType.Project &&
                        node.Metadata.TryGetValue("TotalLines", out var total) &&
                        int.TryParse(total, out var totalLines))
                    {
                        var ratio = lineMax <= 0 ? 0 : totalLines / (double)lineMax;
                        node.Metadata["OverlayScale"] = (1 + (ratio * 0.9)).ToString("F2", CultureInfo.InvariantCulture);
                    }
                    break;
                case "Diagnostics Severity":
                    if (node.Metadata.TryGetValue("ViolationCount", out var violations) &&
                        int.TryParse(violations, out var count) &&
                        count > 0)
                    {
                        node.Metadata["OverlayColor"] = "#DC2626";
                    }
                    break;
            }
        }
    }

    private static string InterpolateColor(string startHex, string endHex, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var start = ParseHex(startHex);
        var end = ParseHex(endHex);
        var r = (byte)(start.r + ((end.r - start.r) * ratio));
        var g = (byte)(start.g + ((end.g - start.g) * ratio));
        var b = (byte)(start.b + ((end.b - start.b) * ratio));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (byte r, byte g, byte b) ParseHex(string hex)
    {
        var normalized = hex.TrimStart('#');
        return (
            Convert.ToByte(normalized.Substring(0, 2), 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16));
    }
}
