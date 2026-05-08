namespace CsArchViewer.Avalonia.ViewModels;

public sealed class FileLineRankItem
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required int LineCount { get; init; }
}
