using CsArchViewer.Core.Models;

namespace CsArchViewer.Core.Services;

public interface IAnalyzerModule
{
    string Name { get; }
    Task<IReadOnlyDictionary<GraphType, ArchitectureGraph>> AnalyzeAsync(
        string rootPath,
        CancellationToken cancellationToken = default);
}
