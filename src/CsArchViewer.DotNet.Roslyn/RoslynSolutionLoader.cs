using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class RoslynSolutionLoader
{
    public async Task<Solution?> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        using var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;

        var solutionPath = Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        }

        var projectPath = Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        return project.Solution;
    }
}
