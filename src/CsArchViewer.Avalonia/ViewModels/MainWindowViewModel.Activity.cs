using System;
using System.Collections.Generic;
using System.Linq;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void AppendLogEntry(AppLogEntry entry)
    {
        LogEntries.Add(entry);
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(0);
        }
    }

    public void ResetLogEntries(IEnumerable<AppLogEntry> entries)
    {
        LogEntries.Clear();
        foreach (var entry in entries.OrderBy(x => x.Timestamp))
        {
            AppendLogEntry(entry);
        }
    }

    public void PresentPerformanceSnapshot(PerformanceSnapshot snapshot)
    {
        _lastPerformanceSnapshot = snapshot;
        PerformanceStatusText =
            $"{L("Performance")}: total {snapshot.TotalMs:N0} ms | analysis {snapshot.AnalysisMs:N0} ms | metrics {snapshot.MetricsMs:N0} ms | symbols {snapshot.SymbolIndexMs:N0} ms | cache {snapshot.CacheHitRate:P0} | mem {snapshot.MemoryUsageMb:N1} MB";
    }

    public void PresentDependencyPathResult(DependencyPathResult result)
    {
        ClearDependencyPathPresentation();
        DependencyPathSummaryText = result.Summary;

        if (!result.Found)
        {
            Graph.Touch();
            return;
        }

        var nodeLookup = Graph.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var edgeLookup = new HashSet<string>(result.EdgeKeys, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < result.NodeIds.Count; i++)
        {
            var nodeId = result.NodeIds[i];
            if (nodeLookup.TryGetValue(nodeId, out var node))
            {
                node.Metadata["IsDependencyPathHit"] = "true";
                DependencyPathSteps.Add($"{i + 1}. {node.Name} ({node.Type})");
            }
            else
            {
                DependencyPathSteps.Add($"{i + 1}. {nodeId}");
            }
        }

        foreach (var edge in Graph.Edges)
        {
            if (edgeLookup.Contains($"{edge.FromNodeId}->{edge.ToNodeId}"))
            {
                edge.Metadata["IsDependencyPathHit"] = "true";
            }
        }

        Graph.Touch();
    }

    public void ClearDependencyPathPresentation()
    {
        foreach (var node in Graph.Nodes)
        {
            node.Metadata.Remove("IsDependencyPathHit");
        }

        foreach (var edge in Graph.Edges)
        {
            edge.Metadata.Remove("IsDependencyPathHit");
        }

        DependencyPathSteps.Clear();
        DependencyPathSummaryText = string.Empty;
    }

    private void UpdateTopStatus()
    {
        TopStatus = $"{Status} | {AnalysisStatus} | {L("StatusTasks")}: {BackgroundTaskCount}";
    }
}
