using System.Text;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Export;

public sealed class MermaidExporter
{
    public string Export(ArchitectureGraph graph, string title = "Architecture Graph")
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: {title}");
        sb.AppendLine("---");
        sb.AppendLine("graph TD");

        foreach (var node in graph.Nodes)
        {
            var id = EscapeId(node.Id);
            var label = EscapeLabel(node.Name);
            sb.AppendLine($"    {id}[\"{label}\"]");
        }

        foreach (var edge in graph.Edges)
        {
            var from = EscapeId(edge.FromNodeId);
            var to = EscapeId(edge.ToNodeId);
            var label = EscapeLabel(edge.Label);
            sb.AppendLine($"    {from} -->|{label}| {to}");
        }

        return sb.ToString();
    }

    private static string EscapeId(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    private static string EscapeLabel(string value)
    {
        return value.Replace("\"", "\\\"");
    }
}
