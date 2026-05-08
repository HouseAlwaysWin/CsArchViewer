namespace CsArchViewer.Core.Models;

public sealed class AnalysisResult
{
    public required string RootPath { get; init; }
    public List<ProjectInfo> Projects { get; init; } = [];
    public Dictionary<GraphType, ArchitectureGraph> Graphs { get; init; } = [];
}
