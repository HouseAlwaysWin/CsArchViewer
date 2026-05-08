using CsArchViewer.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class NamespaceDependencyAnalyzer
{
    public async Task<ArchitectureGraph> AnalyzeAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var graph = new ArchitectureGraph();
        var nodeByNamespace = new Dictionary<string, ArchitectureNode>(StringComparer.OrdinalIgnoreCase);
        var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.SourceCodeKind != SourceCodeKind.Regular || document.FilePath is null)
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                {
                    continue;
                }

                var declaredNamespaces = root.DescendantNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .Select(ns => ns.Name.ToString())
                    .Where(static ns => !string.IsNullOrWhiteSpace(ns))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (declaredNamespaces.Count == 0)
                {
                    continue;
                }

                var usings = root.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Where(static directive => directive.Alias is null && directive.Name is not null)
                    .Select(directive => directive.Name!.ToString())
                    .Where(static ns => !string.IsNullOrWhiteSpace(ns))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var sourceNamespace in declaredNamespaces)
                {
                    var sourceNode = GetOrCreateNamespaceNode(nodeByNamespace, sourceNamespace);
                    sourceNode.Metadata["DocumentCount"] = IncrementCounter(sourceNode.Metadata, "DocumentCount");

                    foreach (var usingNamespace in usings)
                    {
                        if (string.Equals(sourceNamespace, usingNamespace, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        _ = GetOrCreateNamespaceNode(nodeByNamespace, usingNamespace);
                        var key = $"{sourceNamespace}->{usingNamespace}";
                        if (!edgeKeys.Add(key))
                        {
                            continue;
                        }

                        graph.Edges.Add(new ArchitectureEdge
                        {
                            FromNodeId = sourceNamespace,
                            ToNodeId = usingNamespace,
                            Label = "UsesNamespace",
                            Type = ArchitectureEdgeType.UsesNamespace
                        });
                    }
                }
            }
        }

        graph.Nodes.AddRange(nodeByNamespace.Values.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase));
        PopulateNamespaceMetadata(graph);
        return graph;
    }

    private static ArchitectureNode GetOrCreateNamespaceNode(
        IDictionary<string, ArchitectureNode> nodeByNamespace,
        string namespaceName)
    {
        if (nodeByNamespace.TryGetValue(namespaceName, out var existing))
        {
            return existing;
        }

        var node = new ArchitectureNode
        {
            Id = namespaceName,
            Name = namespaceName,
            Type = ArchitectureNodeType.Namespace,
            FullPath = namespaceName,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReferencedNamespaces"] = "(none)",
                ["ReferencedBy"] = "(none)"
            }
        };
        nodeByNamespace[namespaceName] = node;
        return node;
    }

    private static void PopulateNamespaceMetadata(ArchitectureGraph graph)
    {
        var outgoing = graph.Edges
            .Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace)
            .GroupBy(edge => edge.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var incoming = graph.Edges
            .Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace)
            .GroupBy(edge => edge.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.FromNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes.Where(node => node.Type == ArchitectureNodeType.Namespace))
        {
            node.Metadata["ReferencedNamespaces"] = outgoing.TryGetValue(node.Id, out var refs) && refs.Count > 0
                ? string.Join(Environment.NewLine, refs)
                : "(none)";

            node.Metadata["ReferencedBy"] = incoming.TryGetValue(node.Id, out var dependents) && dependents.Count > 0
                ? string.Join(Environment.NewLine, dependents)
                : "(none)";
        }
    }

    private static string IncrementCounter(IDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || !int.TryParse(value, out var current))
        {
            current = 0;
        }

        current++;
        return current.ToString();
    }
}
