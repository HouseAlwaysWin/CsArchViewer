namespace CsArchViewer.Metrics.Models;

public sealed class NamespaceMetrics
{
    public required string Namespace { get; init; }
    public required int FileCount { get; init; }
    public required int TypeCount { get; init; }
    public required int TotalLines { get; init; }
    public required int DependencyCount { get; init; }
    public required int ReferencedByCount { get; init; }
}
