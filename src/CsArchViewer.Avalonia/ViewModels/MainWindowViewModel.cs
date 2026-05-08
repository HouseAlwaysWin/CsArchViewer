using System.Collections.ObjectModel;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> TypeFilterOptions = ["All", "Library", "Exe"];
    public static readonly IReadOnlyList<GraphType> GraphTypeOptions =
    [
        GraphType.ProjectDependencies,
        GraphType.PackageDependencies,
        GraphType.FolderStructure,
        GraphType.FileStructure,
        GraphType.NamespaceDependencies,
        GraphType.ArchitectureViolations,
        GraphType.TypeDependencies,
        GraphType.FileDependencies,
        GraphType.DependencyMatrix
    ];

    private string _searchText = string.Empty;
    private string _selectedTypeFilter = "All";
    private bool _showLineCountOnNodes;
    private GraphType _selectedGraphType = GraphType.ProjectDependencies;
    private string? _drillProjectPath;
    private string? _drillFolderPath;
    private string _status = "Select a folder to analyze.";
    private string _analysisStatus = "Idle";
    private bool _isAnalyzing;
    private int _backgroundTaskCount;
    private string _topStatus = "Select a folder to analyze. | Idle | Tasks: 0";
    private string? _currentRootPath;
    private ProjectInfo? _selectedProject;
    private ArchitectureNode? _selectedListedNode;
    private List<ProjectInfo> _allProjects = [];
    private Dictionary<GraphType, ArchitectureGraph> _graphs = [];

    public ObservableCollection<ProjectInfo> Projects { get; } = [];
    public ObservableCollection<ArchitectureNode> ListedNodes { get; } = [];
    public ObservableCollection<ArchitectureDiagnostic> Diagnostics { get; } = [];
    public GraphViewModel Graph { get; } = new();
    public NodeDetailsViewModel NodeDetails { get; } = new();
    public IReadOnlyList<string> AvailableTypeFilters => TypeFilterOptions;
    public IReadOnlyList<GraphType> AvailableGraphTypes => GraphTypeOptions;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplySearch();
            }
        }
    }

    public bool ShowLineCountOnNodes
    {
        get => _showLineCountOnNodes;
        set
        {
            if (SetProperty(ref _showLineCountOnNodes, value))
            {
                Graph.ShowLineCountOnNodes = value;
                Graph.Touch();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                UpdateTopStatus();
            }
        }
    }

    public string AnalysisStatus
    {
        get => _analysisStatus;
        set
        {
            if (SetProperty(ref _analysisStatus, value))
            {
                UpdateTopStatus();
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set => SetProperty(ref _isAnalyzing, value);
    }

    public int BackgroundTaskCount
    {
        get => _backgroundTaskCount;
        set
        {
            if (SetProperty(ref _backgroundTaskCount, value))
            {
                UpdateTopStatus();
            }
        }
    }

    public string TopStatus
    {
        get => _topStatus;
        private set => SetProperty(ref _topStatus, value);
    }

    public string? CurrentRootPath
    {
        get => _currentRootPath;
        set => SetProperty(ref _currentRootPath, value);
    }

    public ProjectInfo? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            if (value is not null)
            {
                Graph.SelectedNode = Graph.Nodes.FirstOrDefault(n =>
                    string.Equals(n.Id, value.CsProjPath, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public ArchitectureNode? SelectedListedNode
    {
        get => _selectedListedNode;
        set
        {
            if (!SetProperty(ref _selectedListedNode, value))
            {
                return;
            }

            SelectNode(value);
        }
    }

    public string SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public GraphType SelectedGraphType
    {
        get => _selectedGraphType;
        set
        {
            if (SetProperty(ref _selectedGraphType, value))
            {
                BuildActiveGraph();
                ApplyFilters();
                UpdateGraphStatus();
                Graph.RequestAutoFit();
            }
        }
    }

    public void SetAnalysisResult(AnalysisResult result)
    {
        _allProjects = result.Projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _graphs = result.Graphs;

        SelectedTypeFilter = "All";
        SelectedGraphType = GraphType.ProjectDependencies;
        BuildActiveGraph();

        Graph.SelectedNode = null;
        ApplyFilters();
        Graph.Touch();
        Graph.RequestAutoFit();
        NodeDetails.SetProject(null);
        UpdateGraphStatus();
    }

    public void SetDiagnostics(IEnumerable<ArchitectureDiagnostic> diagnostics)
    {
        var uniqueDiagnostics = diagnostics
            .DistinctBy(d => $"{d.Severity}|{d.Type}|{d.Source}|{d.Target}|{d.Message}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        Diagnostics.Clear();
        foreach (var diagnostic in uniqueDiagnostics)
        {
            Diagnostics.Add(diagnostic);
        }
    }

    public void SelectNode(ArchitectureNode? node)
    {
        Graph.SelectedNode = node;
        if (node is null)
        {
            NodeDetails.SetNode(null, null);
            return;
        }

        var project = _allProjects.FirstOrDefault(p =>
            string.Equals(p.CsProjPath, node.Id, StringComparison.OrdinalIgnoreCase));
        NodeDetails.SetNode(node, project);

        if (node.Type == ArchitectureNodeType.Project && project is not null)
        {
            SelectedProject = project;
        }

        Graph.Touch();
    }

    public void DrillInto(ArchitectureNode node)
    {
        switch (node.Type)
        {
            case ArchitectureNodeType.Solution:
                _drillProjectPath = null;
                _drillFolderPath = null;
                SelectedGraphType = GraphType.ProjectDependencies;
                break;
            case ArchitectureNodeType.Project:
                _drillProjectPath = node.FullPath;
                _drillFolderPath = null;
                SelectedGraphType = GraphType.FolderStructure;
                break;
            case ArchitectureNodeType.Folder:
                if (!CanDrillIntoFolder(node))
                {
                    Status = $"Folder '{node.Name}' has no child items to display.";
                    break;
                }

                _drillFolderPath = node.FullPath;
                if (string.IsNullOrWhiteSpace(_drillProjectPath))
                {
                    _drillProjectPath = _allProjects
                        .FirstOrDefault(project => node.FullPath.StartsWith(
                            Path.GetDirectoryName(project.CsProjPath) ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase))
                        ?.CsProjPath;
                }
                SelectedGraphType = GraphType.FileStructure;
                break;
        }

        Graph.RequestAutoFit();
    }

    public ArchitectureGraph? GetCurrentGraph()
    {
        return _graphs.TryGetValue(SelectedGraphType, out var graph) ? graph : null;
    }

    private bool CanDrillIntoFolder(ArchitectureNode node)
    {
        if (!_graphs.TryGetValue(GraphType.FileStructure, out var fileGraph))
        {
            return false;
        }

        var prefix = node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;

        return fileGraph.Nodes.Any(candidate =>
            !string.Equals(candidate.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase) &&
            candidate.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplySearch()
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var keyword = SearchText?.Trim() ?? string.Empty;
        var filtered = _allProjects
            .Where(project => MatchesTypeFilter(project) &&
                              (keyword.Length == 0 ||
                               project.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                               project.CsProjPath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Projects.Clear();
        foreach (var project in filtered)
        {
            Projects.Add(project);
        }

        ListedNodes.Clear();
        foreach (var node in Graph.Nodes.Where(MatchesTypeFilter))
        {
            if (keyword.Length == 0 ||
                node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                node.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                ListedNodes.Add(node);
            }
        }

        MarkSearchMatches();
        Graph.Touch();
    }

    private void MarkSearchMatches()
    {
        var keyword = SearchText?.Trim() ?? string.Empty;

        foreach (var node in Graph.Nodes)
        {
            var typeVisible = MatchesTypeFilter(node);
            var drillVisible = MatchesDrillFilter(node);
            var matched = typeVisible &&
                          drillVisible &&
                          keyword.Length > 0 &&
                          (node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                           node.FullPath.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            node.Metadata["IsSearchHit"] = matched ? "true" : "false";
            node.Metadata["IsTypeVisible"] = (typeVisible && drillVisible) ? "true" : "false";
        }
    }

    private bool MatchesTypeFilter(ProjectInfo project)
    {
        return SelectedTypeFilter switch
        {
            "Library" => IsLibrary(project.OutputType),
            "Exe" => IsExecutable(project.OutputType),
            _ => true
        };
    }

    private static bool IsExecutable(string outputType)
    {
        return outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
               outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLibrary(string outputType)
    {
        return !IsExecutable(outputType);
    }

    private bool MatchesTypeFilter(ArchitectureNode node)
    {
        if (SelectedTypeFilter == "All")
        {
            return true;
        }

        if (node.Type != ArchitectureNodeType.Project)
        {
            return true;
        }

        var outputType = node.Metadata.TryGetValue("OutputType", out var typeValue) ? typeValue : string.Empty;
        return SelectedTypeFilter switch
        {
            "Library" => IsLibrary(outputType),
            "Exe" => IsExecutable(outputType),
            _ => true
        };
    }

    private void BuildActiveGraph()
    {
        Graph.Nodes.Clear();
        Graph.Edges.Clear();

        if (!_graphs.TryGetValue(SelectedGraphType, out var graph))
        {
            return;
        }

        foreach (var node in graph.Nodes)
        {
            Graph.Nodes.Add(node);
        }

        foreach (var edge in graph.Edges)
        {
            Graph.Edges.Add(edge);
        }
    }

    private void UpdateGraphStatus()
    {
        Status = $"Graph: {SelectedGraphType} | Nodes: {Graph.Nodes.Count} | Edges: {Graph.Edges.Count}";
    }

    private void UpdateTopStatus()
    {
        TopStatus = $"{Status} | {AnalysisStatus} | Tasks: {BackgroundTaskCount}";
    }

    private bool MatchesDrillFilter(ArchitectureNode node)
    {
        if (SelectedGraphType == GraphType.ProjectDependencies ||
            SelectedGraphType == GraphType.PackageDependencies ||
            SelectedGraphType == GraphType.NamespaceDependencies ||
            SelectedGraphType == GraphType.ArchitectureViolations ||
            SelectedGraphType == GraphType.TypeDependencies ||
            SelectedGraphType == GraphType.FileDependencies ||
            SelectedGraphType == GraphType.DependencyMatrix)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_drillProjectPath))
        {
            return true;
        }

        var projectDir = Path.GetDirectoryName(_drillProjectPath) ?? string.Empty;
        var inProject = node.Type == ArchitectureNodeType.Solution ||
                        string.Equals(node.FullPath, _drillProjectPath, StringComparison.OrdinalIgnoreCase) ||
                        node.FullPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase);

        if (!inProject)
        {
            return false;
        }

        if (SelectedGraphType != GraphType.FileStructure || string.IsNullOrWhiteSpace(_drillFolderPath))
        {
            return true;
        }

        if (node.Type == ArchitectureNodeType.Solution || node.Type == ArchitectureNodeType.Project)
        {
            return true;
        }

        return node.FullPath.StartsWith(_drillFolderPath, StringComparison.OrdinalIgnoreCase) ||
               _drillFolderPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase);
    }
}
