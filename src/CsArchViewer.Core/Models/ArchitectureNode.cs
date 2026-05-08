namespace CsArchViewer.Core.Models;

public sealed class ArchitectureNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ArchitectureNodeType Type { get; init; }
    public required string FullPath { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
    public double X { get; set; }
    public double Y { get; set; }
}
