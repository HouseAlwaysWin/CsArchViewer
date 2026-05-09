using System.Collections.ObjectModel;
using CsArchViewer.Analysis;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet.SymbolExplorer.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = new();
    private readonly GraphGroupingService _groupingService = new();
    public static readonly IReadOnlyList<string> LanguageOptions = ["繁體中文", "English"];
    public static readonly IReadOnlyList<string> TypeFilterOptions = ["All", "Library", "Exe"];
    public static readonly IReadOnlyList<string> OverlayModeOptions = ["None", "Dependency Count", "LOC Heatmap", "Project Size", "Diagnostics Severity"];
    public static readonly IReadOnlyList<string> MetricsFilterOptions = ["All", "Large Files", "Highly Coupled", "Circular Dependencies", "High Dependency Depth"];
    public static readonly IReadOnlyList<string> ThemeOptions = ["Dark", "Light", "Default"];
    public static readonly IReadOnlyList<string> GraphLayoutOptions = ["Auto", "Tree", "Layered"];
    public static readonly IReadOnlyList<string> DiagnosticsSeverityOptions = ["All", "Info", "Warning", "Error"];
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
    public static readonly IReadOnlyList<GraphGroupingMode> GroupingModeOptions =
    [
        GraphGroupingMode.None,
        GraphGroupingMode.Project,
        GraphGroupingMode.Namespace,
        GraphGroupingMode.Folder,
        GraphGroupingMode.Layer
    ];

    private string _searchText = string.Empty;
    private string _selectedTypeFilter = "All";
    private bool _showLineCountOnNodes;
    private GraphType _selectedGraphType = GraphType.ProjectDependencies;
    private GraphGroupingMode _selectedGroupingMode = GraphGroupingMode.None;
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
    private string _selectedTheme = "Dark";
    private string _selectedGraphLayout = "Auto";
    private string _selectedDiagnosticsSeverityFilter = "All";
    private string? _currentRootPath;
    private ProjectInfo? _selectedProject;
    private ArchitectureNode? _selectedListedNode;
    private List<ProjectInfo> _allProjects = [];
    private Dictionary<GraphType, ArchitectureGraph> _graphs = [];
    private List<ArchitectureDiagnostic> _allDiagnostics = [];
    private MetricsSummary? _metricsSummary;
    private string _symbolExplorerSearchQuery = string.Empty;
    private SymbolInfoModel? _selectedExplorerSymbol;
    private MethodInfoModel? _selectedExplorerMethod;
    private ReferenceInfoModel? _selectedExplorerReference;
    private string _explorerSymbolTitle = string.Empty;
    private string _explorerTypeTitle = string.Empty;
    private string _explorerSymbolDetailsText = string.Empty;
    private string _explorerMethodMetadataText = string.Empty;
    private string _explorerTypeMembersSummary = string.Empty;
    private string _performanceStatusText = string.Empty;
    private string _dependencyPathSourceText = "-";
    private string _dependencyPathTargetQuery = string.Empty;
    private string _dependencyPathSummaryText = string.Empty;
    private bool _restoreLastSession = true;
    private bool _autoSaveSession = true;
    private PerformanceSnapshot? _lastPerformanceSnapshot;
    private ArchitectureGraph? _activeGraph;
    private readonly Dictionary<string, Dictionary<string, NodeLayoutState>> _persistedGraphLayouts =
        new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ProjectInfo> Projects { get; } = [];
    public ObservableCollection<ArchitectureNode> ListedNodes { get; } = [];
    public ObservableCollection<ArchitectureDiagnostic> Diagnostics { get; } = [];
    public ObservableCollection<ArchitectureDiagnostic> FilteredDiagnostics { get; } = [];
    public ObservableCollection<FileLineRankItem> TopFilesByLineCount { get; } = [];
    public ObservableCollection<NamespaceMetrics> TopCoupledNamespaces { get; } = [];
    public ObservableCollection<HealthWarning> HealthWarnings { get; } = [];
    public ObservableCollection<SymbolInfoModel> SymbolExplorerResults { get; } = [];
    public ObservableCollection<ReferenceInfoModel> ExplorerReferences { get; } = [];
    public ObservableCollection<MethodInfoModel> ExplorerTypeMethods { get; } = [];
    public ObservableCollection<ExplorerPropertyDisplayItem> ExplorerTypeProperties { get; } = [];
    public ObservableCollection<string> RecentSearches { get; } = [];
    public ObservableCollection<string> RecentSymbolSearches { get; } = [];
    public ObservableCollection<AppLogEntry> LogEntries { get; } = [];
    public ObservableCollection<string> DependencyPathSteps { get; } = [];
    public GraphViewModel Graph { get; } = new();
    public NodeDetailsViewModel NodeDetails { get; } = new();
    public IReadOnlyList<string> AvailableTypeFilters => TypeFilterOptions;
    public IReadOnlyList<GraphType> AvailableGraphTypes => GraphTypeOptions;
    public IReadOnlyList<GraphGroupingMode> AvailableGroupingModes => GroupingModeOptions;
    public IReadOnlyList<string> AvailableLanguages => LanguageOptions;
    public IReadOnlyList<string> AvailableOverlayModes => OverlayModeOptions;
    public IReadOnlyList<string> AvailableMetricsFilters => MetricsFilterOptions;
    public IReadOnlyList<string> AvailableThemeOptions => ThemeOptions;
    public IReadOnlyList<string> AvailableGraphLayoutOptions => GraphLayoutOptions;
    public IReadOnlyList<string> AvailableDiagnosticsSeverityOptions => DiagnosticsSeverityOptions;

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
                UpdateRecentSearchHistory(RecentSearches, value);
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

    public string SelectedTheme
    {
        get => _selectedTheme;
        set => SetProperty(ref _selectedTheme, value);
    }

    public string SelectedGraphLayout
    {
        get => _selectedGraphLayout;
        set
        {
            var previousLayout = _selectedGraphLayout;
            if (SetProperty(ref _selectedGraphLayout, value))
            {
                CaptureGraphLayout(_selectedGraphType, _selectedGroupingMode, previousLayout);
                BuildActiveGraph();
                ApplyFilters();
                UpdateGraphStatus();
                Graph.RequestAutoFit();
            }
        }
    }

    public bool RestoreLastSession
    {
        get => _restoreLastSession;
        set => SetProperty(ref _restoreLastSession, value);
    }

    public bool AutoSaveSession
    {
        get => _autoSaveSession;
        set => SetProperty(ref _autoSaveSession, value);
    }

    public string SelectedDiagnosticsSeverityFilter
    {
        get => _selectedDiagnosticsSeverityFilter;
        set
        {
            if (SetProperty(ref _selectedDiagnosticsSeverityFilter, value))
            {
                ApplyDiagnosticsFilters();
            }
        }
    }

    public string PerformanceStatusText
    {
        get => _performanceStatusText;
        set => SetProperty(ref _performanceStatusText, value);
    }

    public string DependencyPathSourceText
    {
        get => _dependencyPathSourceText;
        set => SetProperty(ref _dependencyPathSourceText, value);
    }

    public string DependencyPathTargetQuery
    {
        get => _dependencyPathTargetQuery;
        set => SetProperty(ref _dependencyPathTargetQuery, value);
    }

    public string DependencyPathSummaryText
    {
        get => _dependencyPathSummaryText;
        set => SetProperty(ref _dependencyPathSummaryText, value);
    }

    public string SymbolExplorerSearchQuery
    {
        get => _symbolExplorerSearchQuery;
        set
        {
            if (SetProperty(ref _symbolExplorerSearchQuery, value))
            {
                UpdateRecentSearchHistory(RecentSymbolSearches, value);
            }
        }
    }

    public SymbolInfoModel? SelectedExplorerSymbol
    {
        get => _selectedExplorerSymbol;
        set => SetProperty(ref _selectedExplorerSymbol, value);
    }

    public MethodInfoModel? SelectedExplorerMethod
    {
        get => _selectedExplorerMethod;
        set => SetProperty(ref _selectedExplorerMethod, value);
    }

    public ReferenceInfoModel? SelectedExplorerReference
    {
        get => _selectedExplorerReference;
        set => SetProperty(ref _selectedExplorerReference, value);
    }

    public string ExplorerSymbolDetailsText
    {
        get => _explorerSymbolDetailsText;
        set => SetProperty(ref _explorerSymbolDetailsText, value);
    }

    public string ExplorerSymbolTitle
    {
        get => _explorerSymbolTitle;
        set => SetProperty(ref _explorerSymbolTitle, value);
    }

    public string ExplorerTypeTitle
    {
        get => _explorerTypeTitle;
        set => SetProperty(ref _explorerTypeTitle, value);
    }

    public string ExplorerMethodMetadataText
    {
        get => _explorerMethodMetadataText;
        set => SetProperty(ref _explorerMethodMetadataText, value);
    }

    public string ExplorerTypeMembersSummary
    {
        get => _explorerTypeMembersSummary;
        set => SetProperty(ref _explorerTypeMembersSummary, value);
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
            var previousGraphType = _selectedGraphType;
            if (SetProperty(ref _selectedGraphType, value))
            {
                CaptureGraphLayout(previousGraphType, _selectedGroupingMode, _selectedGraphLayout);
                BuildActiveGraph();
                ApplyFilters();
                UpdateGraphStatus();
                Graph.RequestAutoFit();
            }
        }
    }

    public GraphGroupingMode SelectedGroupingMode
    {
        get => _selectedGroupingMode;
        set
        {
            var previousGroupingMode = _selectedGroupingMode;
            if (SetProperty(ref _selectedGroupingMode, value))
            {
                CaptureGraphLayout(_selectedGraphType, previousGroupingMode, _selectedGraphLayout);
                BuildActiveGraph();
                ApplyFilters();
                UpdateGraphStatus();
                Graph.RequestAutoFit();
            }
        }
    }

    public void SetAnalysisResult(AnalysisResult result)
    {
        ClearSymbolExplorerUi();
        _allProjects = result.Projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _graphs = result.Graphs;
        PrimeAutoLayoutsFromSourceGraphs();
        _drillProjectPath = null;
        _drillFolderPath = null;

        if (!_graphs.ContainsKey(_selectedGraphType))
        {
            _selectedGraphType = _graphs.ContainsKey(GraphType.ProjectDependencies)
                ? GraphType.ProjectDependencies
                : _graphs.Keys.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGraphType));
        }

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

    public void ClearSymbolExplorerUi()
    {
        SymbolExplorerResults.Clear();
        ExplorerReferences.Clear();
        ExplorerTypeMethods.Clear();
        ExplorerTypeProperties.Clear();
        SelectedExplorerSymbol = null;
        SelectedExplorerMethod = null;
        SelectedExplorerReference = null;
        ExplorerSymbolTitle = string.Empty;
        ExplorerTypeTitle = string.Empty;
        ExplorerSymbolDetailsText = string.Empty;
        ExplorerMethodMetadataText = string.Empty;
        ExplorerTypeMembersSummary = string.Empty;
    }

    public void SelectNode(ArchitectureNode? node)
    {
        Graph.SelectedNode = node;
        DependencyPathSourceText = node?.Name ?? "-";
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
        return _activeGraph;
    }
}
