namespace CsArchViewer.Metrics.Models;

public sealed class ProjectMetrics
{
    public required string ProjectName { get; init; }
    public required int TotalFiles { get; init; }
    public required int TotalLines { get; init; }
    public required int TotalCodeLines { get; init; }
    public required int TotalCommentLines { get; init; }
    public required int TotalBlankLines { get; init; }
    public required double AverageFileSize { get; init; }
    public required string LargestFile { get; init; }
    public required int DependencyCount { get; init; }
    public required int CircularDependencyCount { get; init; }
}
