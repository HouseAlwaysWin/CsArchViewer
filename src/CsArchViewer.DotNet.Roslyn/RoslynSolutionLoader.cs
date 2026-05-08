using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class RoslynSolutionLoader : IDisposable
{
    private MSBuildWorkspace? _workspace;

    public async Task<Solution?> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        _workspace?.Dispose();
        var workspace = MSBuildWorkspace.Create();
        _workspace = workspace;
        workspace.SkipUnrecognizedProjects = true;

        var solutionPath = Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        }

        var projectPaths = Directory.EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projectPaths.Count == 0)
        {
            return null;
        }

        Exception? lastLoadError = null;
        foreach (var projectPath in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsProjectLoaded(workspace.CurrentSolution, projectPath))
            {
                continue;
            }

            try
            {
                await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastLoadError = ex;
            }
        }

        if (workspace.CurrentSolution.Projects.Any())
        {
            return workspace.CurrentSolution;
        }

        if (lastLoadError is not null)
        {
            throw lastLoadError;
        }

        return null;
    }

    private static bool IsProjectLoaded(Solution solution, string projectPath)
    {
        return solution.Projects.Any(project =>
            !string.IsNullOrWhiteSpace(project.FilePath) &&
            string.Equals(Path.GetFullPath(project.FilePath), Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
    }
}
