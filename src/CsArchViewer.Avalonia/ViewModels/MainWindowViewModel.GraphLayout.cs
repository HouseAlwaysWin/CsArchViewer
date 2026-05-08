using System;
using System.Collections.Generic;
using System.Linq;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void BuildActiveGraph()
    {
        ClearDependencyPathPresentation();
        Graph.Nodes.Clear();
        Graph.Edges.Clear();
        _activeGraph = null;

        if (!_graphs.TryGetValue(SelectedGraphType, out var graph))
        {
            return;
        }

        var sourceGraph = SelectedGroupingMode == GraphGroupingMode.None
            ? graph
            : _groupingService.Group(graph, SelectedGroupingMode, _allProjects);
        ApplyGraphLayout(sourceGraph, SelectedGraphType, SelectedGroupingMode, SelectedGraphLayout);
        _activeGraph = sourceGraph;

        foreach (var node in sourceGraph.Nodes)
        {
            Graph.Nodes.Add(node);
        }

        foreach (var edge in sourceGraph.Edges)
        {
            Graph.Edges.Add(edge);
        }

        Graph.SelectedNode = null;
    }

    private void UpdateGraphStatus()
    {
        Status = string.Format(L("StatusGraphTemplate"), SelectedGraphType, Graph.Nodes.Count, Graph.Edges.Count);
    }

    private void PrimeAutoLayoutsFromSourceGraphs()
    {
        foreach (var graphEntry in _graphs)
        {
            var key = BuildGraphLayoutKey(graphEntry.Key, GraphGroupingMode.None, "Auto");
            if (_persistedGraphLayouts.ContainsKey(key))
            {
                continue;
            }

            _persistedGraphLayouts[key] = graphEntry.Value.Nodes.ToDictionary(
                node => node.Id,
                node => new NodeLayoutState
                {
                    X = node.X,
                    Y = node.Y
                },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void CaptureGraphLayout(GraphType graphType, GraphGroupingMode groupingMode, string layoutMode)
    {
        if (Graph.Nodes.Count == 0)
        {
            return;
        }

        _persistedGraphLayouts[BuildGraphLayoutKey(graphType, groupingMode, layoutMode)] = Graph.Nodes
            .ToDictionary(
                node => node.Id,
                node => new NodeLayoutState
                {
                    X = node.X,
                    Y = node.Y
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyGraphLayout(
        ArchitectureGraph graph,
        GraphType graphType,
        GraphGroupingMode groupingMode,
        string layoutMode)
    {
        if (string.Equals(layoutMode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            TryApplyStoredGraphLayout(graph, graphType, groupingMode, layoutMode);
            return;
        }

        if (TryApplyStoredGraphLayout(graph, graphType, groupingMode, layoutMode))
        {
            return;
        }

        switch (layoutMode)
        {
            case "Tree":
                ApplyTreeLayout(graph);
                break;
            case "Layered":
                ApplyLayeredLayout(graph);
                break;
        }
    }

    private bool TryApplyStoredGraphLayout(
        ArchitectureGraph graph,
        GraphType graphType,
        GraphGroupingMode groupingMode,
        string layoutMode)
    {
        if (!_persistedGraphLayouts.TryGetValue(BuildGraphLayoutKey(graphType, groupingMode, layoutMode), out var layouts))
        {
            return false;
        }

        foreach (var node in graph.Nodes)
        {
            if (layouts.TryGetValue(node.Id, out var layout))
            {
                node.X = layout.X;
                node.Y = layout.Y;
            }
        }

        return true;
    }

    private static void ApplyTreeLayout(ArchitectureGraph graph)
    {
        ApplyDirectedLayout(graph, horizontal: false);
    }

    private static void ApplyLayeredLayout(ArchitectureGraph graph)
    {
        ApplyDirectedLayout(graph, horizontal: true);
    }

    private static void ApplyDirectedLayout(ArchitectureGraph graph, bool horizontal)
    {
        const double primarySpacing = 260;
        const double secondarySpacing = 120;
        const double startX = 80;
        const double startY = 80;

        var depths = BuildLayoutDepths(graph);
        var layers = graph.Nodes
            .OrderBy(node => depths.TryGetValue(node.Id, out var depth) ? depth : int.MaxValue)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(node => depths.TryGetValue(node.Id, out var depth) ? depth : int.MaxValue)
            .ToList();

        foreach (var layer in layers)
        {
            var orderedNodes = layer.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < orderedNodes.Count; i++)
            {
                if (horizontal)
                {
                    orderedNodes[i].X = startX + (layer.Key * primarySpacing);
                    orderedNodes[i].Y = startY + (i * secondarySpacing);
                }
                else
                {
                    orderedNodes[i].X = startX + (i * primarySpacing);
                    orderedNodes[i].Y = startY + (layer.Key * secondarySpacing);
                }
            }
        }
    }

    private static Dictionary<string, int> BuildLayoutDepths(ArchitectureGraph graph)
    {
        var nodeLookup = graph.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var incomingCounts = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoingLookup = graph.Edges
            .Where(edge => nodeLookup.ContainsKey(edge.FromNodeId) && nodeLookup.ContainsKey(edge.ToNodeId))
            .GroupBy(edge => edge.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges)
        {
            if (incomingCounts.ContainsKey(edge.ToNodeId))
            {
                incomingCounts[edge.ToNodeId]++;
            }
        }

        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var root in graph.Nodes
                     .Where(node => incomingCounts[node.Id] == 0)
                     .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
        {
            depths[root.Id] = 0;
            queue.Enqueue(root.Id);
        }

        if (queue.Count == 0 && graph.Nodes.Count > 0)
        {
            var fallbackRoot = graph.Nodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).First();
            depths[fallbackRoot.Id] = 0;
            queue.Enqueue(fallbackRoot.Id);
        }

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentDepth = depths[currentId];
            if (!outgoingLookup.TryGetValue(currentId, out var nextIds))
            {
                continue;
            }

            foreach (var nextId in nextIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (depths.ContainsKey(nextId))
                {
                    continue;
                }

                depths[nextId] = currentDepth + 1;
                queue.Enqueue(nextId);
            }
        }

        var nextDepth = depths.Count == 0 ? 0 : depths.Values.Max() + 1;
        foreach (var node in graph.Nodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!depths.ContainsKey(node.Id))
            {
                depths[node.Id] = nextDepth++;
            }
        }

        return depths;
    }

    private static string BuildGraphLayoutKey(GraphType graphType, GraphGroupingMode groupingMode, string layoutMode)
    {
        return $"{graphType}|{groupingMode}|{layoutMode}";
    }
}
