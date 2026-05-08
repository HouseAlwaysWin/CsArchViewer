namespace CsArchViewer.Metrics.Models;

public sealed class MetricsSummary
{
    public required string RootPath { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<FileMetrics> Files { get; init; } = [];
    public IReadOnlyList<ProjectMetrics> Projects { get; init; } = [];
    public IReadOnlyList<NamespaceMetrics> Namespaces { get; init; } = [];
    public IReadOnlyList<DependencyMetrics> Dependencies { get; init; } = [];
    public IReadOnlyList<HealthWarning> HealthWarnings { get; init; } = [];

    public int TotalFiles => Files.Count;
    public int TotalLoc => Files.Sum(x => x.TotalLines);
    public int TotalCodeLoc => Files.Sum(x => x.CodeLines);
    public int TotalCommentLoc => Files.Sum(x => x.CommentLines);
    public int TotalBlankLoc => Files.Sum(x => x.BlankLines);

    public FileMetrics? LargestFile => Files.OrderByDescending(x => x.TotalLines).FirstOrDefault();
    public NamespaceMetrics? LargestNamespace => Namespaces.OrderByDescending(x => x.TotalLines).FirstOrDefault();
    public int CircularDependencyCount => Dependencies.Sum(x => x.CircularDependencyCount);
    public int LayerViolationCount => Dependencies.Sum(x => x.LayerViolationCount);
}
