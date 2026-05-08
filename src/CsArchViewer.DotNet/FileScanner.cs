using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet;

public sealed class FileScanner
{
    private static readonly string[] ExcludedDirectories =
    [
        "bin",
        "obj",
        ".git",
        ".vs",
        "packages"
    ];

    public IReadOnlyList<string> FindCSharpFiles(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void BuildFolderAndFileNodes(
        ArchitectureGraph graph,
        string solutionNodeId,
        ProjectInfo project,
        bool includeFiles)
    {
        var projectNodeId = project.CsProjPath;
        var projectDirectory = Path.GetDirectoryName(project.CsProjPath) ?? string.Empty;
        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = FindCSharpFiles(project.CsProjPath);

        graph.Edges.Add(new ArchitectureEdge
        {
            FromNodeId = solutionNodeId,
            ToNodeId = projectNodeId,
            Label = "Contains",
            Type = ArchitectureEdgeType.Contains
        });

        foreach (var filePath in files)
        {
            var relative = Path.GetRelativePath(projectDirectory, filePath);
            var folderParts = Path.GetDirectoryName(relative)?
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];

            var parentId = projectNodeId;
            var cumulative = string.Empty;

            foreach (var folder in folderParts)
            {
                cumulative = string.IsNullOrEmpty(cumulative) ? folder : Path.Combine(cumulative, folder);
                var folderNodeId = $"{projectNodeId}|folder|{cumulative}";
                if (!folderIds.Add(folderNodeId))
                {
                    parentId = folderNodeId;
                    continue;
                }

                var fullFolderPath = Path.GetFullPath(Path.Combine(projectDirectory, cumulative));
                graph.Nodes.Add(new ArchitectureNode
                {
                    Id = folderNodeId,
                    Name = folder,
                    Type = ArchitectureNodeType.Folder,
                    FullPath = fullFolderPath,
                    Metadata = new Dictionary<string, string>
                    {
                        ["ChildCount"] = "0"
                    }
                });

                graph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = parentId,
                    ToNodeId = folderNodeId,
                    Label = "Contains",
                    Type = ArchitectureEdgeType.Contains
                });

                parentId = folderNodeId;
            }

            if (!includeFiles)
            {
                continue;
            }

            var info = new FileInfo(filePath);
            var lineCount = GetLineCount(filePath);
            var fileNodeId = $"{projectNodeId}|file|{relative.Replace('\\', '/')}";
            graph.Nodes.Add(new ArchitectureNode
            {
                Id = fileNodeId,
                Name = Path.GetFileName(filePath),
                Type = ArchitectureNodeType.File,
                FullPath = filePath,
                Metadata = new Dictionary<string, string>
                {
                    ["Extension"] = info.Extension,
                    ["Size"] = info.Length.ToString(),
                    ["LastModified"] = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["LineCount"] = lineCount.ToString()
                }
            });

            graph.Edges.Add(new ArchitectureEdge
            {
                FromNodeId = parentId,
                ToNodeId = fileNodeId,
                Label = "Contains",
                Type = ArchitectureEdgeType.Contains
            });
        }

        UpdateFolderChildCount(graph);
    }

    private static void UpdateFolderChildCount(ArchitectureGraph graph)
    {
        var childCounts = graph.Edges
            .GroupBy(edge => edge.FromNodeId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var folderNode in graph.Nodes.Where(node => node.Type == ArchitectureNodeType.Folder))
        {
            folderNode.Metadata["ChildCount"] = childCounts.TryGetValue(folderNode.Id, out var count)
                ? count.ToString()
                : "0";
        }
    }

    private static bool IsExcluded(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var parts = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static int GetLineCount(string path)
    {
        try
        {
            return File.ReadLines(path).Count();
        }
        catch
        {
            return 0;
        }
    }
}
