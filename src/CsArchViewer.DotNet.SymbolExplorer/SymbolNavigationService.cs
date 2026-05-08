using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class NavigationTarget
{
    public required string FilePath { get; init; }
    public int LineNumber { get; init; }
    public int Column { get; init; } = 1;
}

public sealed class SymbolNavigationService
{
    public NavigationTarget JumpToDefinition(SymbolInfoModel symbol)
    {
        return new NavigationTarget
        {
            FilePath = symbol.FilePath,
            LineNumber = Math.Max(1, symbol.LineNumber),
            Column = 1
        };
    }

    public NavigationTarget JumpToReference(ReferenceInfoModel reference)
    {
        return new NavigationTarget
        {
            FilePath = reference.FilePath,
            LineNumber = Math.Max(1, reference.LineNumber),
            Column = Math.Max(1, reference.Column)
        };
    }

    public NavigationTarget JumpToMethod(MethodInfoModel method)
    {
        return new NavigationTarget
        {
            FilePath = method.FilePath,
            LineNumber = Math.Max(1, method.LineNumber),
            Column = 1
        };
    }

    public NavigationTarget OpenFile(string filePath)
    {
        return new NavigationTarget { FilePath = filePath, LineNumber = 1 };
    }
}
