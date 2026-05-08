using System.Text;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Export;

public sealed class MetricsMarkdownExporter
{
    public string Export(MetricsSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architecture Explorer + Code Health Dashboard");
        sb.AppendLine();
        sb.AppendLine("## Project Summary");
        sb.AppendLine();
        sb.AppendLine($"- Root: `{summary.RootPath}`");
        sb.AppendLine($"- Total Files: {summary.TotalFiles}");
        sb.AppendLine($"- Total LOC: {summary.TotalLoc}");
        sb.AppendLine($"- Circular Dependencies: {summary.CircularDependencyCount}");
        sb.AppendLine($"- Layer Violations: {summary.LayerViolationCount}");
        sb.AppendLine();
        sb.AppendLine("## Metrics Summary");
        sb.AppendLine();
        sb.AppendLine($"- Code LOC: {summary.TotalCodeLoc}");
        sb.AppendLine($"- Comment LOC: {summary.TotalCommentLoc}");
        sb.AppendLine($"- Blank LOC: {summary.TotalBlankLoc}");
        sb.AppendLine();
        sb.AppendLine("## Top Largest Files");
        sb.AppendLine();
        foreach (var file in summary.Files.OrderByDescending(x => x.TotalLines).Take(15))
        {
            sb.AppendLine($"- `{file.FileName}` ({file.TotalLines} lines) - `{file.FilePath}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Top Most Coupled Types");
        sb.AppendLine();
        foreach (var file in summary.Files.OrderByDescending(x => x.DependencyCount + x.ReferencedByCount).Take(15))
        {
            sb.AppendLine($"- `{file.FileName}` (out: {file.DependencyCount}, in: {file.ReferencedByCount})");
        }

        sb.AppendLine();
        sb.AppendLine("## Diagnostics Summary");
        sb.AppendLine();
        foreach (var warning in summary.HealthWarnings)
        {
            sb.AppendLine($"- [{warning.Severity}] {warning.Type} | {warning.Source} | {warning.Message}");
        }

        return sb.ToString();
    }
}
