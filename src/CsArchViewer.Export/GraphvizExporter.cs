using System.Text;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Export;

public sealed class GraphvizExporter
{
    public string Export(ArchitectureGraph graph, string graphName = "ArchitectureGraph")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"digraph {Sanitize(graphName)} {{");
        sb.AppendLine("  rankdir=LR;");

        foreach (var node in graph.Nodes)
        {
            var id = Quote(node.Id);
            var label = Quote(node.Name);
            sb.AppendLine($"  {id} [label={label}];");
        }

        foreach (var edge in graph.Edges)
        {
            var from = Quote(edge.FromNodeId);
            var to = Quote(edge.ToNodeId);
            var label = Quote(edge.Label);
            sb.AppendLine($"  {from} -> {to} [label={label}];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
}
