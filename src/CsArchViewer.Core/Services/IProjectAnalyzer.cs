using CsArchViewer.Core.Models;

namespace CsArchViewer.Core.Services;

public interface IProjectAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(string rootPath, CancellationToken cancellationToken = default);
}
