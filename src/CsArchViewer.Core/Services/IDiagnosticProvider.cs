using CsArchViewer.Core.Models;

namespace CsArchViewer.Core.Services;

public interface IDiagnosticProvider
{
    string Name { get; }
    IReadOnlyList<DiagnosticMessage> Analyze(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs);
}
