using System.Text.Json;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Export;

public sealed class MetricsJsonExporter
{
    public string Export(MetricsSummary summary)
    {
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
