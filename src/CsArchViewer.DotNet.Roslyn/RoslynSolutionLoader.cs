using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class RoslynSolutionLoader : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MSBuildWorkspace? _workspace;
    private string? _loadedRootPath;

    public async Task<Solution?> LoadAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return await LoadAsync(rootPath, forceReload: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Solution?> LoadAsync(
        string rootPath,
        bool forceReload,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        var normalizedRoot = NormalizePath(rootPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceReload &&
                _workspace is not null &&
                string.Equals(_loadedRootPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                _workspace.CurrentSolution.Projects.Any())
            {
                return _workspace.CurrentSolution;
            }

            _workspace?.Dispose();
            ShutdownDotNetBuildServers();
            var workspace = MSBuildWorkspace.Create();
            _workspace = workspace;
            _loadedRootPath = normalizedRoot;
            workspace.SkipUnrecognizedProjects = true;

            var solutionPath = Directory.EnumerateFiles(rootPath, "*.sln", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        finally
        {
            _gate.Release();
        }
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
        _loadedRootPath = null;
        ShutdownDotNetBuildServers();
        _gate.Dispose();
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static void ShutdownDotNetBuildServers()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build-server shutdown --msbuild --vbcscompiler",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            process?.WaitForExit(3000);
        }
        catch
        {
            // Best-effort cleanup. Some SDK layouts may not expose this command.
        }
    }
}
