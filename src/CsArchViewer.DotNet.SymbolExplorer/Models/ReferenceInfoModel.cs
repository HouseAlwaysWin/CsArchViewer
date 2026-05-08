namespace CsArchViewer.DotNet.SymbolExplorer.Models;

public sealed class ReferenceInfoModel
{
    public required string ReferencingSymbolDisplay { get; init; }
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    public int Column { get; init; }
    public required string ContextSnippet { get; init; }
}
