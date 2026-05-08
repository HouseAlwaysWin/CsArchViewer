using System.Text;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Export;

public sealed class MetricsCsvExporter
{
    public string Export(MetricsSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilePath,FileName,TotalLines,CodeLines,CommentLines,BlankLines,FileSizeBytes,DependencyCount,ReferencedByCount");
        foreach (var file in summary.Files)
        {
            sb.AppendLine(string.Join(",",
                Escape(file.FilePath),
                Escape(file.FileName),
                file.TotalLines,
                file.CodeLines,
                file.CommentLines,
                file.BlankLines,
                file.FileSizeBytes,
                file.DependencyCount,
                file.ReferencedByCount));
        }

        sb.AppendLine();
        sb.AppendLine("ProjectName,TotalFiles,TotalLines,TotalCodeLines,TotalCommentLines,TotalBlankLines,AverageFileSize,LargestFile,DependencyCount,CircularDependencyCount");
        foreach (var project in summary.Projects)
        {
            sb.AppendLine(string.Join(",",
                Escape(project.ProjectName),
                project.TotalFiles,
                project.TotalLines,
                project.TotalCodeLines,
                project.TotalCommentLines,
                project.TotalBlankLines,
                project.AverageFileSize.ToString("F2"),
                Escape(project.LargestFile),
                project.DependencyCount,
                project.CircularDependencyCount));
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
