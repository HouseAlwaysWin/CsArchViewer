using CsArchViewer.Core.Models;

namespace CsArchViewer.Diagnostics;

public sealed class CircularDependencyAnalyzer
{
    public IReadOnlyList<ArchitectureDiagnostic> Analyze(
        IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var diagnostics = new List<ArchitectureDiagnostic>();
        foreach (var pair in graphs)
        {
            var circularEdges = pair.Value.Edges
                .Where(edge => edge.Type == ArchitectureEdgeType.CircularDependency)
                .ToList();

            foreach (var edge in circularEdges)
            {
                diagnostics.Add(new ArchitectureDiagnostic
                {
                    Type = "CircularDependency",
                    Severity = DiagnosticSeverity.Error,
                    Source = edge.FromNodeId,
                    Target = edge.ToNodeId,
                    Message = $"Detected circular dependency in {pair.Key}: {edge.FromNodeId} -> {edge.ToNodeId}"
                });
            }
        }

        return diagnostics;
    }
}
