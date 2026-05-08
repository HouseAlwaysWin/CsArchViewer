namespace CsArchViewer.Core.Models;

public sealed class ArchitectureEdge
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required string Label { get; init; }
    public required ArchitectureEdgeType Type { get; init; }
}
