using CsArchViewer.Core.Models;

namespace CsArchViewer.Diagnostics;

public sealed class UnusedReferenceAnalyzer
{
    public IReadOnlyList<ArchitectureDiagnostic> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var diagnostics = new List<ArchitectureDiagnostic>();
        if (graphs.TryGetValue(GraphType.ProjectDependencies, out var projectGraph) &&
            graphs.TryGetValue(GraphType.DependencyMatrix, out var matrixGraph))
        {
            var matrixLookup = matrixGraph.Edges.ToDictionary(
                edge => $"{edge.FromNodeId}->{edge.ToNodeId}",
                edge => edge.Label,
                StringComparer.OrdinalIgnoreCase);

            foreach (var edge in projectGraph.Edges.Where(edge => edge.Type == ArchitectureEdgeType.ProjectReference))
            {
                var key = $"{edge.FromNodeId}->{edge.ToNodeId}";
                if (!matrixLookup.TryGetValue(key, out var label) || label.EndsWith("(0)", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new ArchitectureDiagnostic
                    {
                        Type = "UnusedReference",
                        Severity = DiagnosticSeverity.Info,
                        Source = edge.FromNodeId,
                        Target = edge.ToNodeId,
                        Message = "ProjectReference appears unused by current type dependency heuristic."
                    });
                }
            }
        }

        if (graphs.TryGetValue(GraphType.NamespaceDependencies, out var nsGraph))
        {
            var incoming = nsGraph.Edges
                .Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace)
                .GroupBy(edge => edge.ToNodeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var node in nsGraph.Nodes.Where(node => node.Type == ArchitectureNodeType.Namespace))
            {
                if (incoming.ContainsKey(node.Id))
                {
                    continue;
                }

                diagnostics.Add(new ArchitectureDiagnostic
                {
                    Type = "UnusedReference",
                    Severity = DiagnosticSeverity.Info,
                    Source = node.FullPath,
                    Message = "Namespace is not referenced by other namespaces."
                });
            }
        }

        return diagnostics;
    }
}
