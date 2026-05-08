using CsArchViewer.Core.Models;
using CsArchViewer.Core.Services;

namespace CsArchViewer.Analysis;

public sealed class IncrementalAnalysisEngine
{
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly AnalysisCache _cache = new();

    public IncrementalAnalysisEngine(IProjectAnalyzer projectAnalyzer)
    {
        _projectAnalyzer = projectAnalyzer;
    }

    public async Task<AnalysisUpdate> AnalyzeAsync(
        string rootPath,
        IReadOnlyCollection<string>? changedFiles = null,
        CancellationToken cancellationToken = default)
    {
        var impacted = new HashSet<GraphType>();
        var forceFull = changedFiles is null || changedFiles.Count == 0;

        if (!forceFull)
        {
            foreach (var file in changedFiles ?? Array.Empty<string>())
            {
                if (_cache.IsFileChanged(file))
                {
                    impacted.UnionWith(EstimateImpactedGraphs(file));
                }
            }

            if (impacted.Count == 0 && _cache.GetLastResult() is not null)
            {
                return new AnalysisUpdate
                {
                    Result = _cache.GetLastResult()!,
                    IsIncremental = true,
                    ImpactedGraphs = []
                };
            }
        }

        var result = await _projectAnalyzer.AnalyzeAsync(rootPath, cancellationToken);
        _cache.PrimeFromResult(result);

        if (forceFull || impacted.Count == 0)
        {
            impacted.UnionWith(result.Graphs.Keys);
        }

        return new AnalysisUpdate
        {
            Result = result,
            IsIncremental = !forceFull,
            ImpactedGraphs = impacted.OrderBy(x => x.ToString(), StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static IEnumerable<GraphType> EstimateImpactedGraphs(string filePath)
    {
        if (filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return Enum.GetValues<GraphType>();
        }

        if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                GraphType.NamespaceDependencies,
                GraphType.ArchitectureViolations,
                GraphType.TypeDependencies,
                GraphType.FileDependencies,
                GraphType.DependencyMatrix
            ];
        }

        return [];
    }
}

public sealed class AnalysisUpdate
{
    public required AnalysisResult Result { get; init; }
    public required bool IsIncremental { get; init; }
    public IReadOnlyList<GraphType> ImpactedGraphs { get; init; } = [];
}
