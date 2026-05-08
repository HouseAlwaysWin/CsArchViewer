using System.Text.RegularExpressions;

namespace CsArchViewer.DotNet;

public sealed class SolutionScanner
{
    private static readonly Regex ProjectLineRegex = new(
        "Project\\(\"\\{.*\\}\"\\)\\s*=\\s*\".*\",\\s*\"(?<path>.*?\\.csproj)\"",
        RegexOptions.Compiled);

    public IReadOnlyList<string> FindSolutionFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        return Directory
            .GetFiles(rootPath, "*.sln", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> FindProjectsFromSolution(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var results = new List<string>();

        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var relativePath = match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));

            if (File.Exists(fullPath))
            {
                results.Add(fullPath);
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
