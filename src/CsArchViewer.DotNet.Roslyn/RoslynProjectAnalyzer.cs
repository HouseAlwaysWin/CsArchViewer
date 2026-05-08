using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class RoslynProjectAnalyzer
{
    private readonly RoslynSolutionLoader _solutionLoader = new();
    private readonly NamespaceDependencyAnalyzer _namespaceAnalyzer = new();
    private readonly ArchitectureRuleEngine _ruleEngine = new();
    private readonly CircularDependencyDetector _cycleDetector = new();

    public async Task<Dictionary<GraphType, ArchitectureGraph>> AnalyzeAsync(
        string rootPath,
        string rulesFilePath,
        CancellationToken cancellationToken = default)
    {
        var graphs = new Dictionary<GraphType, ArchitectureGraph>();
        var solution = await _solutionLoader.LoadAsync(rootPath, cancellationToken);
        if (solution is null)
        {
            graphs[GraphType.NamespaceDependencies] = new ArchitectureGraph();
            graphs[GraphType.ArchitectureViolations] = new ArchitectureGraph();
            return graphs;
        }

        var namespaceGraph = await _namespaceAnalyzer.AnalyzeAsync(solution, cancellationToken);
        var cycles = _cycleDetector.DetectCycles(namespaceGraph);
        _cycleDetector.AnnotateGraphWithCircularEdges(namespaceGraph, cycles);
        ApplyLayeredLayout(namespaceGraph);

        var rules = _ruleEngine.LoadRules(rulesFilePath);
        var ruleViolations = _ruleEngine.Evaluate(namespaceGraph, rules);
        var violationsGraph = BuildViolationGraph(ruleViolations, cycles);
        ApplyGroupedViolationLayout(violationsGraph);

        graphs[GraphType.NamespaceDependencies] = namespaceGraph;
        graphs[GraphType.ArchitectureViolations] = violationsGraph;
        return graphs;
    }

    private static ArchitectureGraph BuildViolationGraph(
        IReadOnlyList<ArchitectureRuleEngine.RuleViolation> ruleViolations,
        IReadOnlyList<CircularDependencyDetector.NamespaceCycle> cycles)
    {
        var graph = new ArchitectureGraph();
        var namespaceNodes = new Dictionary<string, ArchitectureNode>(StringComparer.OrdinalIgnoreCase);

        var violationIndex = 0;
        foreach (var violation in ruleViolations)
        {
            var source = GetOrCreateNamespaceNode(namespaceNodes, violation.SourceNamespace);
            var target = GetOrCreateNamespaceNode(namespaceNodes, violation.TargetNamespace);

            var violationId = $"violation:rule:{violationIndex++}";
            var violationNode = new ArchitectureNode
            {
                Id = violationId,
                Name = "Rule Violation",
                Type = ArchitectureNodeType.Violation,
                FullPath = violationId,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Rule"] = $"{violation.Rule.SourcePattern} -> !{violation.Rule.ForbiddenTargetPattern}",
                    ["Severity"] = violation.Rule.Severity,
                    ["RuleCategory"] = violation.Rule.RuleCategory,
                    ["Source"] = violation.SourceNamespace,
                    ["Target"] = violation.TargetNamespace,
                    ["Message"] = violation.Rule.Message
                }
            };
            graph.Nodes.Add(violationNode);
            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = source.Id,
                ToNodeId = violationId,
                Label = "ViolatesRule",
                Type = ArchitectureEdgeType.ViolatesRule
            });
            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = violationId,
                ToNodeId = target.Id,
                Label = "ViolatesRule",
                Type = ArchitectureEdgeType.ViolatesRule
            });
        }

        foreach (var cycle in cycles)
        {
            if (cycle.Nodes.Count < 2)
            {
                continue;
            }

            var sourceNamespace = cycle.Nodes[0];
            var targetNamespace = cycle.Nodes[1];
            var cyclePath = string.Join(" -> ", cycle.Nodes);

            var source = GetOrCreateNamespaceNode(namespaceNodes, sourceNamespace);
            var target = GetOrCreateNamespaceNode(namespaceNodes, targetNamespace);
            var violationId = $"violation:cycle:{graph.Nodes.Count}";

            var cycleNode = new ArchitectureNode
            {
                Id = violationId,
                Name = "Circular Dependency",
                Type = ArchitectureNodeType.Violation,
                FullPath = violationId,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Rule"] = "No circular namespace dependencies",
                    ["Severity"] = "Error",
                    ["RuleCategory"] = "CircularDependency",
                    ["Source"] = sourceNamespace,
                    ["Target"] = targetNamespace,
                    ["Message"] = cyclePath
                }
            };

            graph.Nodes.Add(cycleNode);
            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = source.Id,
                ToNodeId = cycleNode.Id,
                Label = "CircularDependency",
                Type = ArchitectureEdgeType.CircularDependency
            });
            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = cycleNode.Id,
                ToNodeId = target.Id,
                Label = "CircularDependency",
                Type = ArchitectureEdgeType.CircularDependency
            });
        }

        graph.Nodes.AddRange(namespaceNodes.Values.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase));
        PopulateNamespaceViolationMetadata(graph);
        return graph;
    }

    private static ArchitectureNode GetOrCreateNamespaceNode(
        IDictionary<string, ArchitectureNode> namespaceNodes,
        string namespaceName)
    {
        if (namespaceNodes.TryGetValue(namespaceName, out var existing))
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
                ["ReferencedNamespaces"] = "(see namespace graph)",
                ["ReferencedBy"] = "(see namespace graph)"
            }
        };

        namespaceNodes[namespaceName] = node;
        return node;
    }

    private static void PopulateNamespaceViolationMetadata(ArchitectureGraph graph)
    {
        var outgoing = graph.Edges
            .Where(edge => edge.FromNodeId != edge.ToNodeId && !edge.FromNodeId.StartsWith("violation:", StringComparison.OrdinalIgnoreCase))
            .GroupBy(edge => edge.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var incoming = graph.Edges
            .Where(edge => edge.ToNodeId != edge.FromNodeId && !edge.ToNodeId.StartsWith("violation:", StringComparison.OrdinalIgnoreCase))
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
            node.Metadata["ReferencedBy"] = incoming.TryGetValue(node.Id, out var by) && by.Count > 0
                ? string.Join(Environment.NewLine, by)
                : "(none)";
        }
    }

    private static void ApplyLayeredLayout(ArchitectureGraph graph)
    {
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges.Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace || edge.Type == ArchitectureEdgeType.CircularDependency))
        {
            if (incoming.ContainsKey(edge.ToNodeId))
            {
                incoming[edge.ToNodeId]++;
            }

            if (outgoing.TryGetValue(edge.FromNodeId, out var targets))
            {
                targets.Add(edge.ToNodeId);
            }
        }

        var levels = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in outgoing[current])
            {
                levels[next] = Math.Max(levels[next], levels[current] + 1);
                incoming[next]--;
                if (incoming[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        var groups = graph.Nodes
            .GroupBy(node => levels[node.Id])
            .OrderBy(group => group.Key)
            .ToList();

        const double xSpacing = 320;
        const double ySpacing = 110;
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].X = 80 + group.Key * xSpacing;
                ordered[i].Y = 80 + i * ySpacing;
            }
        }
    }

    private static void ApplyGroupedViolationLayout(ArchitectureGraph graph)
    {
        var namespaces = graph.Nodes.Where(node => node.Type == ArchitectureNodeType.Namespace)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var violations = graph.Nodes.Where(node => node.Type == ArchitectureNodeType.Violation)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const double leftX = 80;
        const double centerX = 460;
        const double rightX = 840;
        const double ySpacing = 100;

        for (var i = 0; i < namespaces.Count; i++)
        {
            namespaces[i].X = (i % 2 == 0) ? leftX : rightX;
            namespaces[i].Y = 80 + (i / 2) * ySpacing;
        }

        for (var i = 0; i < violations.Count; i++)
        {
            violations[i].X = centerX;
            violations[i].Y = 80 + i * ySpacing;
        }
    }
}
