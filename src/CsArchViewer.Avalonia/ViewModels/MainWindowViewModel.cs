using System.Collections.ObjectModel;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = new();
    public static readonly IReadOnlyList<string> LanguageOptions = ["繁體中文", "English"];
    public static readonly IReadOnlyList<string> TypeFilterOptions = ["All", "Library", "Exe"];
    public static readonly IReadOnlyList<string> OverlayModeOptions = ["None", "Dependency Count", "LOC Heatmap", "Project Size", "Diagnostics Severity"];
    public static readonly IReadOnlyList<string> MetricsFilterOptions = ["All", "Large Files", "Highly Coupled", "Circular Dependencies", "High Dependency Depth"];
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
    private string _status = string.Empty;
    private string _analysisStatus = string.Empty;
    private bool _isAnalyzing;
    private int _backgroundTaskCount;
    private string _topStatus = string.Empty;
    private string _selectedLanguage = "繁體中文";
    private string _selectedOverlayMode = "None";
    private string _selectedMetricsFilter = "All";
    private string? _currentRootPath;
    private ProjectInfo? _selectedProject;
    private ArchitectureNode? _selectedListedNode;
    private List<ProjectInfo> _allProjects = [];
    private Dictionary<GraphType, ArchitectureGraph> _graphs = [];
    private MetricsSummary? _metricsSummary;

    public ObservableCollection<ProjectInfo> Projects { get; } = [];
    public ObservableCollection<ArchitectureNode> ListedNodes { get; } = [];
    public ObservableCollection<ArchitectureDiagnostic> Diagnostics { get; } = [];
    public ObservableCollection<FileLineRankItem> TopFilesByLineCount { get; } = [];
    public ObservableCollection<NamespaceMetrics> TopCoupledNamespaces { get; } = [];
    public ObservableCollection<HealthWarning> HealthWarnings { get; } = [];
    public GraphViewModel Graph { get; } = new();
    public NodeDetailsViewModel NodeDetails { get; } = new();
    public IReadOnlyList<string> AvailableTypeFilters => TypeFilterOptions;
    public IReadOnlyList<GraphType> AvailableGraphTypes => GraphTypeOptions;
    public IReadOnlyList<string> AvailableLanguages => LanguageOptions;
    public IReadOnlyList<string> AvailableOverlayModes => OverlayModeOptions;
    public IReadOnlyList<string> AvailableMetricsFilters => MetricsFilterOptions;

    public MainWindowViewModel()
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        Status = L("StatusIdle");
        AnalysisStatus = L("StatusIdleShort");
        UpdateTopStatus();
    }

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

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            _localization.SetLanguage(value == "English" ? "en-US" : "zh-TW");
        }
    }

    public string SelectedOverlayMode
    {
        get => _selectedOverlayMode;
        set
        {
            if (SetProperty(ref _selectedOverlayMode, value))
            {
                ApplyMetricsOverlay();
                Graph.Touch();
            }
        }
    }

    public string SelectedMetricsFilter
    {
        get => _selectedMetricsFilter;
        set
        {
            if (SetProperty(ref _selectedMetricsFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string WorkspaceTabText => L("WorkspaceTab");
    public string SettingsTabText => L("SettingsTab");
    public string OpenFolderText => L("OpenFolder");
    public string ReloadText => L("Reload");
    public string ExportText => L("Export");
    public string SearchPlaceholderText => L("SearchPlaceholder");
    public string NodesTitleText => L("Nodes");
    public string NodeDetailsTitleText => L("NodeDetails");
    public string ProjectNameText => L("ProjectName");
    public string NodeTypeText => L("NodeType");
    public string FullPathText => L("FullPath");
    public string TargetFrameworkText => L("TargetFramework");
    public string OutputTypeText => L("OutputType");
    public string ProjectReferencesText => L("ProjectReferences");
    public string PackageReferencesText => L("PackageReferences");
    public string ExtensionText => L("Extension");
    public string SizeBytesText => L("SizeBytes");
    public string LastModifiedText => L("LastModified");
    public string ChildCountText => L("ChildCount");
    public string ReferencedNamespacesText => L("ReferencedNamespaces");
    public string ReferencedByText => L("ReferencedBy");
    public string RuleText => L("Rule");
    public string SourceText => L("Source");
    public string TargetText => L("Target");
    public string MessageText => L("Message");
    public string FullTypeNameText => L("FullTypeName");
    public string NamespaceText => L("Namespace");
    public string FileText => L("File");
    public string BaseTypeText => L("BaseType");
    public string ImplementedInterfacesText => L("ImplementedInterfaces");
    public string ReferencedTypesText => L("ReferencedTypes");
    public string DependencyMatrixRowText => L("DependencyMatrixRow");
    public string DependsOnText => L("DependsOn");
    public string IncomingDependenciesText => L("IncomingDependencies");
    public string DependencyCountText => L("DependencyCount");
    public string CircularDependencyCountText => L("CircularDependencyCount");
    public string ViolationCountText => L("ViolationCount");
    public string LineCountText => L("LineCount");
    public string DiagnosticsTitleText => L("Diagnostics");
    public string OverlayModeText => L("OverlayMode");
    public string MetricsFilterText => L("MetricsFilter");
    public string MetricsTabText => L("MetricsTab");
    public string TopCoupledNamespacesText => L("TopCoupledNamespaces");
    public string HealthWarningsText => L("HealthWarnings");
    public string TopFilesByLineCountText => L("TopFilesByLineCount");
    public string LinesUnitText => L("LinesUnit");
    public string SettingsTitleText => L("SettingsTitle");
    public string SettingsDescriptionText => L("SettingsDescription");
    public string GraphSectionText => L("GraphSection");
    public string ShowLineCountText => L("ShowLineCount");
    public string LanguageText => L("Language");
    public string TotalLocText => L("TotalLoc");
    public string TotalFilesText => L("TotalFiles");
    public string LargestFileText => L("LargestFile");
    public string LargestNamespaceText => L("LargestNamespace");
    public string CircularDependenciesText => L("CircularDependencies");
    public string LayerViolationsText => L("LayerViolations");

    public int MetricsTotalLoc => _metricsSummary?.TotalLoc ?? 0;
    public int MetricsTotalFiles => _metricsSummary?.TotalFiles ?? 0;
    public string MetricsLargestFile => _metricsSummary?.LargestFile?.FileName ?? "-";
    public string MetricsLargestNamespace => _metricsSummary?.LargestNamespace?.Namespace ?? "-";
    public int MetricsCircularDependencies => _metricsSummary?.CircularDependencyCount ?? 0;
    public int MetricsLayerViolations => _metricsSummary?.LayerViolationCount ?? 0;

    public string L(string key) => _localization.Get(key);

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
        UpdateTopFileLineRanking();
        UpdateGraphStatus();
        ApplyMetricsOverlay();
    }

    public void SetMetricsSummary(MetricsSummary summary)
    {
        _metricsSummary = summary;

        TopFilesByLineCount.Clear();
        foreach (var file in summary.Files.OrderByDescending(x => x.TotalLines).Take(20))
        {
            TopFilesByLineCount.Add(new FileLineRankItem
            {
                FileName = file.FileName,
                FilePath = file.FilePath,
                LineCount = file.TotalLines
            });
        }

        TopCoupledNamespaces.Clear();
        foreach (var ns in summary.Namespaces
                     .OrderByDescending(x => x.DependencyCount + x.ReferencedByCount)
                     .Take(20))
        {
            TopCoupledNamespaces.Add(ns);
        }

        HealthWarnings.Clear();
        foreach (var warning in summary.HealthWarnings)
        {
            HealthWarnings.Add(warning);
        }

        ApplyMetricsToNodeMetadata(summary);
        ApplyMetricsOverlay();
        ApplyFilters();

        OnPropertyChanged(nameof(MetricsTotalLoc));
        OnPropertyChanged(nameof(MetricsTotalFiles));
        OnPropertyChanged(nameof(MetricsLargestFile));
        OnPropertyChanged(nameof(MetricsLargestNamespace));
        OnPropertyChanged(nameof(MetricsCircularDependencies));
        OnPropertyChanged(nameof(MetricsLayerViolations));
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
                    Status = string.Format(L("FolderNoChildTemplate"), node.Name);
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
        ApplyMetricsOverlay();
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
            var metricsVisible = MatchesMetricsFilter(node);
            node.Metadata["IsSearchHit"] = matched ? "true" : "false";
            node.Metadata["IsTypeVisible"] = (typeVisible && drillVisible && metricsVisible) ? "true" : "false";
        }
    }

    private bool MatchesMetricsFilter(ArchitectureNode node)
    {
        return SelectedMetricsFilter switch
        {
            "Large Files" => node.Type == ArchitectureNodeType.File &&
                             node.Metadata.TryGetValue("TotalLines", out var lines) &&
                             int.TryParse(lines, out var parsedLines) &&
                             parsedLines >= 2000,
            "Highly Coupled" => node.Metadata.TryGetValue("DependencyCount", out var depCount) &&
                                int.TryParse(depCount, out var parsedDepCount) &&
                                parsedDepCount >= 20,
            "Circular Dependencies" => node.Metadata.TryGetValue("CircularDependencyCount", out var circular) &&
                                       int.TryParse(circular, out var parsedCircular) &&
                                       parsedCircular > 0,
            "High Dependency Depth" => node.Metadata.TryGetValue("DependencyDepth", out var depth) &&
                                       int.TryParse(depth, out var parsedDepth) &&
                                       parsedDepth >= 10,
            _ => true
        };
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
        Status = string.Format(L("StatusGraphTemplate"), SelectedGraphType, Graph.Nodes.Count, Graph.Edges.Count);
    }

    private void UpdateTopStatus()
    {
        TopStatus = $"{Status} | {AnalysisStatus} | {L("StatusTasks")}: {BackgroundTaskCount}";
    }

    private void UpdateTopFileLineRanking()
    {
        TopFilesByLineCount.Clear();
        if (!_graphs.TryGetValue(GraphType.FileStructure, out var fileGraph))
        {
            return;
        }

        var ranked = fileGraph.Nodes
            .Where(node => node.Type == ArchitectureNodeType.File &&
                           node.Metadata.TryGetValue("LineCount", out var lineValue) &&
                           int.TryParse(lineValue, out _))
            .Select(node => new
            {
                Node = node,
                LineCount = int.TryParse(node.Metadata["LineCount"], out var parsed) ? parsed : 0
            })
            .OrderByDescending(x => x.LineCount)
            .ThenBy(x => x.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        foreach (var item in ranked)
        {
            TopFilesByLineCount.Add(new FileLineRankItem
            {
                FileName = item.Node.Name,
                FilePath = item.Node.FullPath,
                LineCount = item.LineCount
            });
        }
    }

    private void ApplyMetricsToNodeMetadata(MetricsSummary summary)
    {
        var fileLookup = summary.Files.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        var projectLookup = summary.Projects.ToDictionary(x => x.ProjectName, StringComparer.OrdinalIgnoreCase);
        var namespaceLookup = summary.Namespaces.ToDictionary(x => x.Namespace, StringComparer.OrdinalIgnoreCase);
        var dependencyDepth = summary.Dependencies.ToDictionary(x => x.Scope, x => x.DependencyDepth, StringComparer.OrdinalIgnoreCase);

        foreach (var node in Graph.Nodes)
        {
            switch (node.Type)
            {
                case ArchitectureNodeType.File when fileLookup.TryGetValue(node.FullPath, out var file):
                    node.Metadata["TotalLines"] = file.TotalLines.ToString();
                    node.Metadata["CodeLines"] = file.CodeLines.ToString();
                    node.Metadata["CommentLines"] = file.CommentLines.ToString();
                    node.Metadata["BlankLines"] = file.BlankLines.ToString();
                    node.Metadata["FileSize"] = file.FileSizeBytes.ToString();
                    node.Metadata["DependencyCount"] = file.DependencyCount.ToString();
                    node.Metadata["ReferencedByCount"] = file.ReferencedByCount.ToString();
                    break;
                case ArchitectureNodeType.Project when projectLookup.TryGetValue(node.Name, out var project):
                    node.Metadata["TotalLines"] = project.TotalLines.ToString();
                    node.Metadata["TotalFiles"] = project.TotalFiles.ToString();
                    node.Metadata["LargestFile"] = project.LargestFile;
                    node.Metadata["CircularDependencyCount"] = project.CircularDependencyCount.ToString();
                    node.Metadata["DependencyCount"] = project.DependencyCount.ToString();
                    node.Metadata["DependencyDepth"] = dependencyDepth.TryGetValue("Project", out var projectDepth) ? projectDepth.ToString() : "0";
                    break;
                case ArchitectureNodeType.Namespace when namespaceLookup.TryGetValue(node.Name, out var ns):
                    node.Metadata["TypeCount"] = ns.TypeCount.ToString();
                    node.Metadata["TotalLines"] = ns.TotalLines.ToString();
                    node.Metadata["DependencyCount"] = ns.DependencyCount.ToString();
                    node.Metadata["ReferencedByCount"] = ns.ReferencedByCount.ToString();
                    node.Metadata["DependencyDepth"] = dependencyDepth.TryGetValue("Namespace", out var nsDepth) ? nsDepth.ToString() : "0";
                    break;
            }
        }
    }

    private void ApplyMetricsOverlay()
    {
        var depMax = Graph.Nodes
            .Select(node => node.Metadata.TryGetValue("DependencyCount", out var value) && int.TryParse(value, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();
        var lineMax = Graph.Nodes
            .Select(node => node.Metadata.TryGetValue("TotalLines", out var value) && int.TryParse(value, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var node in Graph.Nodes)
        {
            node.Metadata.Remove("OverlayColor");
            node.Metadata.Remove("OverlayScale");

            switch (SelectedOverlayMode)
            {
                case "Dependency Count":
                    if (node.Metadata.TryGetValue("DependencyCount", out var dep) && int.TryParse(dep, out var depCount))
                    {
                        var ratio = depMax <= 0 ? 0 : depCount / (double)depMax;
                        node.Metadata["OverlayScale"] = (1 + (ratio * 0.8)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    break;
                case "LOC Heatmap":
                    if (node.Metadata.TryGetValue("TotalLines", out var line) && int.TryParse(line, out var lineCount))
                    {
                        var ratio = lineMax <= 0 ? 0 : lineCount / (double)lineMax;
                        node.Metadata["OverlayColor"] = InterpolateColor("#22C55E", "#EF4444", ratio);
                    }
                    break;
                case "Project Size":
                    if (node.Type == ArchitectureNodeType.Project &&
                        node.Metadata.TryGetValue("TotalLines", out var total) &&
                        int.TryParse(total, out var totalLines))
                    {
                        var ratio = lineMax <= 0 ? 0 : totalLines / (double)lineMax;
                        node.Metadata["OverlayScale"] = (1 + (ratio * 0.9)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    break;
                case "Diagnostics Severity":
                    if (node.Metadata.TryGetValue("ViolationCount", out var violations) &&
                        int.TryParse(violations, out var count) &&
                        count > 0)
                    {
                        node.Metadata["OverlayColor"] = "#DC2626";
                    }
                    break;
            }
        }
    }

    private static string InterpolateColor(string startHex, string endHex, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var start = ParseHex(startHex);
        var end = ParseHex(endHex);
        var r = (byte)(start.r + ((end.r - start.r) * ratio));
        var g = (byte)(start.g + ((end.g - start.g) * ratio));
        var b = (byte)(start.b + ((end.b - start.b) * ratio));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (byte r, byte g, byte b) ParseHex(string hex)
    {
        var normalized = hex.TrimStart('#');
        return (
            Convert.ToByte(normalized.Substring(0, 2), 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16));
    }

    private void HandleLanguageChanged()
    {
        OnPropertyChanged(nameof(WorkspaceTabText));
        OnPropertyChanged(nameof(SettingsTabText));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(ReloadText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(NodesTitleText));
        OnPropertyChanged(nameof(NodeDetailsTitleText));
        OnPropertyChanged(nameof(ProjectNameText));
        OnPropertyChanged(nameof(NodeTypeText));
        OnPropertyChanged(nameof(FullPathText));
        OnPropertyChanged(nameof(TargetFrameworkText));
        OnPropertyChanged(nameof(OutputTypeText));
        OnPropertyChanged(nameof(ProjectReferencesText));
        OnPropertyChanged(nameof(PackageReferencesText));
        OnPropertyChanged(nameof(ExtensionText));
        OnPropertyChanged(nameof(SizeBytesText));
        OnPropertyChanged(nameof(LastModifiedText));
        OnPropertyChanged(nameof(ChildCountText));
        OnPropertyChanged(nameof(ReferencedNamespacesText));
        OnPropertyChanged(nameof(ReferencedByText));
        OnPropertyChanged(nameof(RuleText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(MessageText));
        OnPropertyChanged(nameof(FullTypeNameText));
        OnPropertyChanged(nameof(NamespaceText));
        OnPropertyChanged(nameof(FileText));
        OnPropertyChanged(nameof(BaseTypeText));
        OnPropertyChanged(nameof(ImplementedInterfacesText));
        OnPropertyChanged(nameof(ReferencedTypesText));
        OnPropertyChanged(nameof(DependencyMatrixRowText));
        OnPropertyChanged(nameof(DependsOnText));
        OnPropertyChanged(nameof(IncomingDependenciesText));
        OnPropertyChanged(nameof(DependencyCountText));
        OnPropertyChanged(nameof(CircularDependencyCountText));
        OnPropertyChanged(nameof(ViolationCountText));
        OnPropertyChanged(nameof(LineCountText));
        OnPropertyChanged(nameof(DiagnosticsTitleText));
        OnPropertyChanged(nameof(OverlayModeText));
        OnPropertyChanged(nameof(MetricsFilterText));
        OnPropertyChanged(nameof(MetricsTabText));
        OnPropertyChanged(nameof(TopCoupledNamespacesText));
        OnPropertyChanged(nameof(HealthWarningsText));
        OnPropertyChanged(nameof(TopFilesByLineCountText));
        OnPropertyChanged(nameof(LinesUnitText));
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(SettingsDescriptionText));
        OnPropertyChanged(nameof(GraphSectionText));
        OnPropertyChanged(nameof(ShowLineCountText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(TotalLocText));
        OnPropertyChanged(nameof(TotalFilesText));
        OnPropertyChanged(nameof(LargestFileText));
        OnPropertyChanged(nameof(LargestNamespaceText));
        OnPropertyChanged(nameof(CircularDependenciesText));
        OnPropertyChanged(nameof(LayerViolationsText));
        OnPropertyChanged(nameof(MetricsTotalLoc));
        OnPropertyChanged(nameof(MetricsTotalFiles));
        OnPropertyChanged(nameof(MetricsLargestFile));
        OnPropertyChanged(nameof(MetricsLargestNamespace));
        OnPropertyChanged(nameof(MetricsCircularDependencies));
        OnPropertyChanged(nameof(MetricsLayerViolations));
        UpdateGraphStatus();
        UpdateTopStatus();
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
