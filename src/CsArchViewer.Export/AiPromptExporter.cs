using System.Text;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Export;

public sealed class AiPromptExporter
{
    public string Export(ArchitectureGraph graph, string graphType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architecture Context For AI");
        sb.AppendLine();
        sb.AppendLine($"GraphType: {graphType}");
        sb.AppendLine($"NodeCount: {graph.Nodes.Count}");
        sb.AppendLine($"EdgeCount: {graph.Edges.Count}");
        sb.AppendLine();
        sb.AppendLine("## Nodes");
        foreach (var node in graph.Nodes.OrderBy(n => n.Type).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- [{node.Type}] {node.Name}");
            sb.AppendLine($"  - Id: {node.Id}");
            sb.AppendLine($"  - Path: {node.FullPath}");
            if (node.Metadata.Count > 0)
            {
                var preview = node.Metadata
                    .Take(6)
                    .Select(kv => $"{kv.Key}={kv.Value?.Replace(Environment.NewLine, " | ")}");
                sb.AppendLine($"  - Metadata: {string.Join("; ", preview)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Edges");
        foreach (var edge in graph.Edges.OrderBy(e => e.Type).ThenBy(e => e.FromNodeId, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- [{edge.Type}] {edge.FromNodeId} -> {edge.ToNodeId} ({edge.Label})");
        }

        sb.AppendLine();
        sb.AppendLine("## Prompt Template");
        sb.AppendLine("Please analyze this architecture graph and provide:");
        sb.AppendLine("1. Main architectural risks and hotspots.");
        sb.AppendLine("2. Coupling / dependency issues.");
        sb.AppendLine("3. Suggested refactoring priorities.");
        sb.AppendLine("4. Concrete next actions (short, medium, long term).");

        return sb.ToString();
    }
}
