using CsArchViewer.Core.Models;
using Microsoft.CodeAnalysis;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class SymbolGraphBuilder
{
    private readonly RoslynTypeAnalyzer _typeAnalyzer = new();
    private readonly TypeDependencyAnalyzer _typeDependencyAnalyzer = new();
    private readonly FileDependencyAnalyzer _fileDependencyAnalyzer = new();
    private readonly DependencyMatrixBuilder _matrixBuilder = new();

    public async Task<Dictionary<GraphType, ArchitectureGraph>> AnalyzeAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<GraphType, ArchitectureGraph>();
        var types = await _typeAnalyzer.AnalyzeAsync(solution, cancellationToken);
        output[GraphType.TypeDependencies] = _typeDependencyAnalyzer.BuildGraph(types, includeUsesTypeEdges: true);
        output[GraphType.TypeInheritance] = _typeDependencyAnalyzer.BuildGraph(types, includeUsesTypeEdges: false);
        output[GraphType.FileDependencies] = _fileDependencyAnalyzer.BuildGraph(types);
        output[GraphType.DependencyMatrix] = _matrixBuilder.BuildProjectMatrixGraph(types);
        return output;
    }
}
