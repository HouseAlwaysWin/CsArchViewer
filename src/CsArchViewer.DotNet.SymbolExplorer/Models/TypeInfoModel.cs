namespace CsArchViewer.DotNet.SymbolExplorer.Models;

public sealed class TypeInfoModel
{
    public required string FullName { get; init; }
    public required string Namespace { get; init; }
    public ExplorerSymbolKind Kind { get; init; }
    public required string Accessibility { get; init; }
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    public required string SymbolKey { get; init; }
    public string BaseType { get; init; } = string.Empty;
    public required IReadOnlyList<string> Interfaces { get; init; }
    public required IReadOnlyList<MethodInfoModel> Methods { get; init; }
    public required IReadOnlyList<string> Properties { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
}
