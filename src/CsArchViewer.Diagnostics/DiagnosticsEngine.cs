using CsArchViewer.Core.Models;

namespace CsArchViewer.Diagnostics;

public sealed class DiagnosticsEngine
{
    private readonly CircularDependencyAnalyzer _circular = new();
    private readonly LayerViolationAnalyzer _layer = new();
    private readonly DependencyDepthAnalyzer _depth = new();
    private readonly UnusedReferenceAnalyzer _unused = new();

    public IReadOnlyList<ArchitectureDiagnostic> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var result = new List<ArchitectureDiagnostic>();
        result.AddRange(_circular.Analyze(graphs));
        result.AddRange(_layer.Analyze(graphs));
        result.AddRange(_depth.Analyze(graphs));
        result.AddRange(_unused.Analyze(graphs));
        return result
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
