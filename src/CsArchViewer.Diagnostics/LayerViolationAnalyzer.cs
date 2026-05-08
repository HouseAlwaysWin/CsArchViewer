using CsArchViewer.Core.Models;

namespace CsArchViewer.Diagnostics;

public sealed class LayerViolationAnalyzer
{
    public IReadOnlyList<ArchitectureDiagnostic> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var diagnostics = new List<ArchitectureDiagnostic>();
        if (!graphs.TryGetValue(GraphType.ArchitectureViolations, out var graph))
        {
            return diagnostics;
        }

        foreach (var node in graph.Nodes.Where(node => node.Type == ArchitectureNodeType.Violation))
        {
            var severity = node.Metadata.TryGetValue("Severity", out var severityRaw) &&
                           Enum.TryParse<DiagnosticSeverity>(severityRaw, ignoreCase: true, out var parsed)
                ? parsed
                : DiagnosticSeverity.Warning;

            diagnostics.Add(new ArchitectureDiagnostic
            {
                Type = "LayerViolation",
                Severity = severity,
                Source = node.Metadata.TryGetValue("Source", out var source) ? source : node.Name,
                Target = node.Metadata.TryGetValue("Target", out var target) ? target : "-",
                Message = node.Metadata.TryGetValue("Message", out var message) ? message : "Layer rule violation"
            });
        }

        return diagnostics;
    }
}
