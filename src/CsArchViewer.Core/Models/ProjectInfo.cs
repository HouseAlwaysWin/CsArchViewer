namespace CsArchViewer.Core.Models;

public sealed class ProjectInfo
{
    public required string Name { get; init; }
    public required string CsProjPath { get; init; }
    public required string TargetFramework { get; init; }
    public required string OutputType { get; init; }
    public List<string> ProjectReferences { get; init; } = [];
    public List<PackageInfo> PackageReferences { get; init; } = [];
}
