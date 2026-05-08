using CsArchViewer.Core.Models;

namespace CsArchViewer.Core.Services;

public interface IGraphExporter
{
    string FormatName { get; }
    string FileExtension { get; }
    string Export(ArchitectureGraph graph, string graphTypeName);
}
