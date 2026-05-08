namespace CsArchViewer.Metrics.Models;

public sealed class DependencyMetrics
{
    public required string Scope { get; init; }
    public required int OutgoingDependencyCount { get; init; }
    public required int IncomingDependencyCount { get; init; }
    public required int CircularDependencyCount { get; init; }
    public required int DependencyDepth { get; init; }
    public required int LayerViolationCount { get; init; }
}
