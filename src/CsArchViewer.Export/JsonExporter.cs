using System.Text.Json;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Export;

public sealed class JsonExporter
{
    public string Export(ArchitectureGraph graph)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(graph, options);
    }
}
