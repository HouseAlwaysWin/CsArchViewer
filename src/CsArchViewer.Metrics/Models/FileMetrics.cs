namespace CsArchViewer.Metrics.Models;

public sealed class FileMetrics
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required int TotalLines { get; init; }
    public required int CodeLines { get; init; }
    public required int CommentLines { get; init; }
    public required int BlankLines { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTime LastModified { get; init; }
    public required int DependencyCount { get; init; }
    public required int ReferencedByCount { get; init; }
}
