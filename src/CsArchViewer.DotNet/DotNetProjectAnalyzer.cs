using CsArchViewer.Core.Models;
using CsArchViewer.Core.Services;
using CsArchViewer.DotNet.Roslyn;
using CsArchViewer.DotNet.SymbolAnalysis;

namespace CsArchViewer.DotNet;

public sealed class DotNetProjectAnalyzer : IProjectAnalyzer
{
    private readonly SolutionScanner _solutionScanner = new();
    private readonly CsProjParser _csProjParser = new();
    private readonly FileScanner _fileScanner = new();
    private readonly RoslynProjectAnalyzer _roslynAnalyzer = new();
    private readonly SymbolGraphBuilder _symbolGraphBuilder = new();

    public async Task<AnalysisResult> AnalyzeAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var solutions = _solutionScanner.FindSolutionFiles(rootPath);
        var allProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var solutionPath in solutions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var projectPath in _solutionScanner.FindProjectsFromSolution(solutionPath))
            {
                allProjectPaths.Add(projectPath);
            }
        }

        if (allProjectPaths.Count == 0)
        {
            foreach (var csProj in Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories))
            {
                allProjectPaths.Add(Path.GetFullPath(csProj));
            }
        }

        var projects = allProjectPaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => _csProjParser.Parse(path))
            .ToList();

        var graphs = BuildGraphs(rootPath, projects);
        var rulesPath = ResolveRulesFilePath();
        var roslynGraphs = await _roslynAnalyzer.AnalyzeAsync(rootPath, rulesPath, cancellationToken);
        foreach (var graph in roslynGraphs)
        {
            graphs[graph.Key] = graph.Value;
        }

        var symbolGraphs = await _symbolGraphBuilder.AnalyzeAsync(rootPath, cancellationToken);
        foreach (var graph in symbolGraphs)
        {
            graphs[graph.Key] = graph.Value;
        }

        var result = new AnalysisResult
        {
            RootPath = rootPath,
            Projects = projects,
            Graphs = graphs
        };

        return result;
    }

    private Dictionary<GraphType, ArchitectureGraph> BuildGraphs(string rootPath, IReadOnlyList<ProjectInfo> projects)
    {
        var projectGraph = BuildProjectGraph(projects);
        ApplyLayeredLayout(projectGraph);

        var packageGraph = BuildPackageGraph(projects);
        ApplyLayeredLayout(packageGraph);

        var folderGraph = BuildStructureGraph(rootPath, projects, includeFiles: false);
        ApplyTreeLayout(folderGraph);

        var fileGraph = BuildStructureGraph(rootPath, projects, includeFiles: true);
        ApplyTreeLayout(fileGraph);

        return new Dictionary<GraphType, ArchitectureGraph>
        {
            [GraphType.ProjectDependencies] = projectGraph,
            [GraphType.PackageDependencies] = packageGraph,
            [GraphType.FolderStructure] = folderGraph,
            [GraphType.FileStructure] = fileGraph
        };
    }

    private static string ResolveRulesFilePath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDirectory, "architecture-rules.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "architecture-rules.json"));
    }

    private static ArchitectureGraph BuildProjectGraph(IReadOnlyList<ProjectInfo> projects)
    {
        var graph = new ArchitectureGraph();
        var byPath = projects.ToDictionary(project => project.CsProjPath, StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            graph.Nodes.Add(new ArchitectureNode
            {
                Id = project.CsProjPath,
                Name = project.Name,
                Type = ArchitectureNodeType.Project,
                FullPath = project.CsProjPath,
                Metadata = new Dictionary<string, string>
                {
                    ["TargetFramework"] = project.TargetFramework,
                    ["OutputType"] = project.OutputType,
                    ["PackageCount"] = project.PackageReferences.Count.ToString(),
                    ["ProjectReferences"] = project.ProjectReferences.Count == 0 ? "(none)" : string.Join(Environment.NewLine, project.ProjectReferences),
                    ["PackageReferences"] = project.PackageReferences.Count == 0
                        ? "(none)"
                        : string.Join(Environment.NewLine, project.PackageReferences.Select(pkg => $"{pkg.Name} ({pkg.Version})"))
                }
            });
        }

        foreach (var project in projects)
        {
            foreach (var referencePath in project.ProjectReferences)
            {
                if (!byPath.TryGetValue(referencePath, out var dependency))
                {
                    continue;
                }

                graph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = project.CsProjPath,
                    ToNodeId = dependency.CsProjPath,
                    Label = "ProjectReference",
                    Type = ArchitectureEdgeType.ProjectReference
                });
            }
        }

        return graph;
    }

    private static ArchitectureGraph BuildPackageGraph(IReadOnlyList<ProjectInfo> projects)
    {
        var graph = new ArchitectureGraph();
        var packageNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            graph.Nodes.Add(new ArchitectureNode
            {
                Id = project.CsProjPath,
                Name = project.Name,
                Type = ArchitectureNodeType.Project,
                FullPath = project.CsProjPath,
                Metadata = new Dictionary<string, string>
                {
                    ["TargetFramework"] = project.TargetFramework,
                    ["OutputType"] = project.OutputType
                }
            });

            foreach (var package in project.PackageReferences)
            {
                var packageNodeId = $"pkg:{package.Name}";
                if (packageNodes.Add(packageNodeId))
                {
                    graph.Nodes.Add(new ArchitectureNode
                    {
                        Id = packageNodeId,
                        Name = package.Name,
                        Type = ArchitectureNodeType.Package,
                        FullPath = packageNodeId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["Version"] = package.Version
                        }
                    });
                }

                graph.Edges.Add(new ArchitectureEdge
                {
                    FromNodeId = project.CsProjPath,
                    ToNodeId = packageNodeId,
                    Label = $"PackageReference ({package.Version})",
                    Type = ArchitectureEdgeType.PackageReference
                });
            }
        }

        return graph;
    }

    private ArchitectureGraph BuildStructureGraph(string rootPath, IReadOnlyList<ProjectInfo> projects, bool includeFiles)
    {
        var graph = new ArchitectureGraph();
        var solutionId = $"solution:{rootPath}";

        graph.Nodes.Add(new ArchitectureNode
        {
            Id = solutionId,
            Name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Type = ArchitectureNodeType.Solution,
            FullPath = rootPath,
            Metadata = new Dictionary<string, string>()
        });

        foreach (var project in projects)
        {
            graph.Nodes.Add(new ArchitectureNode
            {
                Id = project.CsProjPath,
                Name = project.Name,
                Type = ArchitectureNodeType.Project,
                FullPath = project.CsProjPath,
                Metadata = new Dictionary<string, string>
                {
                    ["TargetFramework"] = project.TargetFramework,
                    ["OutputType"] = project.OutputType
                }
            });

            _fileScanner.BuildFolderAndFileNodes(graph, solutionId, project, includeFiles);
        }

        return graph;
    }

    private static void ApplyLayeredLayout(ArchitectureGraph graph)
    {
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0);
        var outgoing = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>());

        foreach (var edge in graph.Edges)
        {
            if (incoming.ContainsKey(edge.ToNodeId))
            {
                incoming[edge.ToNodeId]++;
            }

            if (outgoing.TryGetValue(edge.FromNodeId, out var targets))
            {
                targets.Add(edge.ToNodeId);
            }
        }

        var levelByNode = graph.Nodes.ToDictionary(node => node.Id, _ => 0);
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            var currentLevel = levelByNode[nodeId];

            foreach (var target in outgoing[nodeId])
            {
                levelByNode[target] = Math.Max(levelByNode[target], currentLevel + 1);
                incoming[target]--;
                if (incoming[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        var groups = graph.Nodes
            .GroupBy(node => levelByNode[node.Id])
            .OrderBy(group => group.Key)
            .ToList();

        const double xSpacing = 280;
        const double ySpacing = 120;

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].X = 80 + (group.Key * xSpacing);
                ordered[i].Y = 80 + (i * ySpacing);
            }
        }
    }

    private static void ApplyTreeLayout(ArchitectureGraph graph)
    {
        var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var children = graph.Nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges.Where(edge => edge.Type == ArchitectureEdgeType.Contains))
        {
            if (children.TryGetValue(edge.FromNodeId, out var list))
            {
                list.Add(edge.ToNodeId);
            }

            if (incoming.ContainsKey(edge.ToNodeId))
            {
                incoming[edge.ToNodeId]++;
            }
        }

        foreach (var key in children.Keys.ToList())
        {
            children[key] = children[key]
                .OrderBy(id => nodeById.TryGetValue(id, out var node) ? node.Name : id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var roots = graph.Nodes
            .Where(node => incoming[node.Id] == 0)
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const double xSpacing = 200;
        const double ySpacing = 120;
        var nextX = 0d;

        foreach (var root in roots)
        {
            LayoutSubtree(root.Id, 0);
        }

        void LayoutSubtree(string nodeId, int depth)
        {
            var childIds = children[nodeId];
            if (childIds.Count == 0)
            {
                var node = nodeById[nodeId];
                node.X = 80 + nextX * xSpacing;
                node.Y = 80 + depth * ySpacing;
                nextX++;
                return;
            }

            foreach (var childId in childIds)
            {
                LayoutSubtree(childId, depth + 1);
            }

            var parentNode = nodeById[nodeId];
            var first = nodeById[childIds[0]];
            var last = nodeById[childIds[^1]];
            parentNode.X = (first.X + last.X) / 2d;
            parentNode.Y = 80 + depth * ySpacing;
        }
    }
}
