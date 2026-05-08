using CsArchViewer.Core.Models;
using CsArchViewer.DotNet.Roslyn;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class SymbolGraphBuilder
{
    private readonly RoslynSolutionLoader _solutionLoader = new();
    private readonly RoslynTypeAnalyzer _typeAnalyzer = new();
    private readonly TypeDependencyAnalyzer _typeDependencyAnalyzer = new();
    private readonly FileDependencyAnalyzer _fileDependencyAnalyzer = new();
    private readonly DependencyMatrixBuilder _matrixBuilder = new();

    public async Task<Dictionary<GraphType, ArchitectureGraph>> AnalyzeAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<GraphType, ArchitectureGraph>();
        var solution = await _solutionLoader.LoadAsync(rootPath, cancellationToken);
        if (solution is null)
        {
            output[GraphType.TypeDependencies] = new ArchitectureGraph();
            output[GraphType.FileDependencies] = new ArchitectureGraph();
            output[GraphType.DependencyMatrix] = new ArchitectureGraph();
            return output;
        }

        var types = await _typeAnalyzer.AnalyzeAsync(solution, cancellationToken);
        output[GraphType.TypeDependencies] = _typeDependencyAnalyzer.BuildGraph(types);
        output[GraphType.FileDependencies] = _fileDependencyAnalyzer.BuildGraph(types);
        output[GraphType.DependencyMatrix] = _matrixBuilder.BuildProjectMatrixGraph(types);
        return output;
    }
}
