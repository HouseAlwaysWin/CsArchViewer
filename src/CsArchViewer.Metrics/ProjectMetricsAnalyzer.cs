using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Metrics;

public sealed class ProjectMetricsAnalyzer
{
    public IReadOnlyList<ProjectMetrics> Analyze(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyList<FileMetrics> files,
        IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var circularByProject = BuildCircularDependencyLookup(graphs);

        var result = new List<ProjectMetrics>();
        foreach (var project in projects)
        {
            var projectDir = Path.GetDirectoryName(project.CsProjPath) ?? string.Empty;
            var projectFiles = files
                .Where(file => file.FilePath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var largest = projectFiles.OrderByDescending(f => f.TotalLines).FirstOrDefault();
            result.Add(new ProjectMetrics
            {
                ProjectName = project.Name,
                TotalFiles = projectFiles.Count,
                TotalLines = projectFiles.Sum(f => f.TotalLines),
                TotalCodeLines = projectFiles.Sum(f => f.CodeLines),
                TotalCommentLines = projectFiles.Sum(f => f.CommentLines),
                TotalBlankLines = projectFiles.Sum(f => f.BlankLines),
                AverageFileSize = projectFiles.Count == 0 ? 0 : projectFiles.Average(f => f.FileSizeBytes),
                LargestFile = largest?.FileName ?? "-",
                DependencyCount = project.ProjectReferences.Count,
                CircularDependencyCount = circularByProject.TryGetValue(project.CsProjPath, out var circularCount) ? circularCount : 0
            });
        }

        return result
            .OrderByDescending(x => x.TotalLines)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> BuildCircularDependencyLookup(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!graphs.TryGetValue(GraphType.ArchitectureViolations, out var graph))
        {
            return result;
        }

        foreach (var edge in graph.Edges.Where(x => x.Type == ArchitectureEdgeType.CircularDependency))
        {
            result[edge.FromNodeId] = result.TryGetValue(edge.FromNodeId, out var count) ? count + 1 : 1;
        }

        return result;
    }
}
