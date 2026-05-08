namespace CsArchViewer.DotNet.SymbolExplorer.Models;

public sealed class MethodInfoModel
{
    public required string Name { get; init; }
    public required string ContainingType { get; init; }
    public required string Namespace { get; init; }
    public required string Accessibility { get; init; }
    public int ParameterCount { get; init; }
    public required string Parameters { get; init; }
    public required string ReturnType { get; init; }
    public int GenericParameterCount { get; init; }
    public bool IsAsync { get; init; }
    public bool IsStatic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public bool IsAbstract { get; init; }
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    public required string SymbolKey { get; init; }
    public required string Signature { get; init; }
    public required IReadOnlyList<string> UsedTypes { get; init; }
}
