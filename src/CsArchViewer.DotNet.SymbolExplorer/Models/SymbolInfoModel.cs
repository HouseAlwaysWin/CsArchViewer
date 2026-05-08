namespace CsArchViewer.DotNet.SymbolExplorer.Models;

public sealed class SymbolInfoModel
{
    public required string SymbolKey { get; init; }
    public ExplorerSymbolKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public required string DisplayName { get; init; }
    public string ContainingTypeName { get; init; } = string.Empty;
    public required string Accessibility { get; init; }
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    /// <summary>Character offset in source file for resolving declared symbol.</summary>
    public int SpanStart { get; init; }
}
