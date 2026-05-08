namespace CsArchViewer.Core.Models;

public sealed class DependencyPathResult
{
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public IReadOnlyList<string> NodeIds { get; init; } = [];
    public IReadOnlyList<string> EdgeKeys { get; init; } = [];
    public bool Found { get; init; }
    public bool IsCycle { get; init; }
    public string Summary { get; init; } = string.Empty;
}
