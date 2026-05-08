namespace CsArchViewer.Core.Models;

public sealed class ArchitectureGraph
{
    public List<ArchitectureNode> Nodes { get; init; } = [];
    public List<ArchitectureEdge> Edges { get; init; } = [];
}
