using System.Collections.ObjectModel;
using CsArchViewer.Analysis;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet.SymbolExplorer.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
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
        set => SetProperty(ref _selectedGraphLayout, value);
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
    public string SymbolExplorerTabText => L("SymbolExplorerTab");
    public string SymbolExplorerSearchPlaceholderText => L("SymbolExplorerSearchPlaceholder");
    public string SymbolExplorerSearchButtonText => L("SymbolExplorerSearch");
    public string SymbolExplorerDetailsTabText => L("SymbolExplorerDetailsTab");
    public string SymbolExplorerFindRefsText => L("SymbolExplorerFindRefs");
    public string SymbolExplorerGoToDefText => L("SymbolExplorerGoToDef");
    public string SymbolExplorerTypeMembersTitleText => L("SymbolExplorerTypeMembersTitle");
    public string SymbolExplorerMethodMetaTitleText => L("SymbolExplorerMethodMetaTitle");
    public string BottomPanelDiagnosticsTabText => L("BottomPanelDiagnosticsTab");
    public string SymbolExplorerReferencesTabText => L("SymbolExplorerReferencesTab");
    public string SymbolExplorerJumpOpenText => L("SymbolExplorerJumpOpen");
    public string GroupByText => L("GroupBy");
    public string FitToScreenText => L("FitToScreen");
    public string ZoomToSelectionText => L("ZoomToSelection");
    public string DependencyPathTabText => L("DependencyPathTab");
    public string DependencyPathSourceTextLabel => L("DependencyPathSource");
    public string DependencyPathTargetTextLabel => L("DependencyPathTarget");
    public string DependencyPathShortestText => L("DependencyPathShortest");
    public string DependencyPathCycleText => L("DependencyPathCycle");
    public string DiagnosticsSeverityFilterText => L("DiagnosticsSeverityFilter");
    public string ExportDiagnosticsText => L("ExportDiagnostics");
    public string RecentSearchesText => L("RecentSearches");
    public string LogsTabText => L("LogsTab");
    public string PerformanceText => L("Performance");
    public string RestoreLastSessionText => L("RestoreLastSession");
    public string AutoSaveSessionText => L("AutoSaveSession");
    public string ThemeText => L("Theme");
    public string GraphLayoutText => L("GraphLayout");

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
            var previousGraphType = _selectedGraphType;
            if (SetProperty(ref _selectedGraphType, value))
            {
                CaptureGraphLayout(previousGraphType, _selectedGroupingMode);
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
                CaptureGraphLayout(_selectedGraphType, previousGroupingMode);
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
        SelectedExplorerSymbol = null;
        SelectedExplorerMethod = null;
        SelectedExplorerReference = null;
        ExplorerSymbolDetailsText = string.Empty;
        ExplorerMethodMetadataText = string.Empty;
        ExplorerTypeMembersSummary = string.Empty;
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

        _allDiagnostics = uniqueDiagnostics;

        Diagnostics.Clear();
        foreach (var diagnostic in uniqueDiagnostics)
        {
            Diagnostics.Add(diagnostic);
        }

        ApplyDiagnosticsFilters();
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

    public WorkspaceState ExportWorkspaceState()
    {
        CaptureGraphLayout(SelectedGraphType, SelectedGroupingMode);

        return new WorkspaceState
        {
            Settings = new AppSettings
            {
                Theme = SelectedTheme,
                GraphLayout = SelectedGraphLayout,
                OverlayMode = SelectedOverlayMode,
                LanguageCode = SelectedLanguage == "English" ? "en-US" : "zh-TW",
                ShowLineCountOnNodes = ShowLineCountOnNodes,
                RestoreLastSession = RestoreLastSession,
                AutoSaveSession = AutoSaveSession
            },
            Session = new SessionState
            {
                RootPath = CurrentRootPath,
                SelectedGraphType = SelectedGraphType,
                SelectedGroupingMode = SelectedGroupingMode,
                SearchText = SearchText,
                SymbolSearchText = SymbolExplorerSearchQuery,
                SelectedTypeFilter = SelectedTypeFilter,
                SelectedMetricsFilter = SelectedMetricsFilter,
                SelectedOverlayMode = SelectedOverlayMode,
                SelectedDiagnosticsSeverityFilter = SelectedDiagnosticsSeverityFilter,
                RecentSearches = RecentSearches.ToList(),
                RecentSymbolSearches = RecentSymbolSearches.ToList(),
                GraphLayouts = CloneGraphLayouts()
            }
        };
    }

    public void ApplyWorkspaceState(WorkspaceState state)
    {
        var settings = state.Settings;
        var session = state.Session;

        SelectedTheme = ResolveOption(settings.Theme, ThemeOptions, "Dark");
        SelectedGraphLayout = ResolveOption(settings.GraphLayout, GraphLayoutOptions, "Auto");
        SelectedOverlayMode = ResolveOption(settings.OverlayMode, OverlayModeOptions, "None");
        ShowLineCountOnNodes = settings.ShowLineCountOnNodes;
        RestoreLastSession = settings.RestoreLastSession;
        AutoSaveSession = settings.AutoSaveSession;
        SelectedLanguage = string.Equals(settings.LanguageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "English"
            : "繁體中文";

        CopyGraphLayoutsFromSession(session.GraphLayouts);
        ReplaceCollection(RecentSearches, session.RecentSearches);
        ReplaceCollection(RecentSymbolSearches, session.RecentSymbolSearches);

        CurrentRootPath = session.RootPath;
        SelectedTypeFilter = ResolveOption(session.SelectedTypeFilter, TypeFilterOptions, "All");
        SelectedMetricsFilter = ResolveOption(session.SelectedMetricsFilter, MetricsFilterOptions, "All");
        SelectedDiagnosticsSeverityFilter = ResolveOption(session.SelectedDiagnosticsSeverityFilter, DiagnosticsSeverityOptions, "All");
        SearchText = session.SearchText ?? string.Empty;
        SymbolExplorerSearchQuery = session.SymbolSearchText ?? string.Empty;
        SelectedGroupingMode = session.SelectedGroupingMode;
        SelectedGraphType = session.SelectedGraphType;
    }

    public void AppendLogEntry(AppLogEntry entry)
    {
        LogEntries.Add(entry);
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(0);
        }
    }

    public void ResetLogEntries(IEnumerable<AppLogEntry> entries)
    {
        LogEntries.Clear();
        foreach (var entry in entries.OrderBy(x => x.Timestamp))
        {
            AppendLogEntry(entry);
        }
    }

    public void PresentPerformanceSnapshot(PerformanceSnapshot snapshot)
    {
        _lastPerformanceSnapshot = snapshot;
        PerformanceStatusText =
            $"{L("Performance")}: total {snapshot.TotalMs:N0} ms | analysis {snapshot.AnalysisMs:N0} ms | metrics {snapshot.MetricsMs:N0} ms | symbols {snapshot.SymbolIndexMs:N0} ms | cache {snapshot.CacheHitRate:P0} | mem {snapshot.MemoryUsageMb:N1} MB";
    }

    public void PresentDependencyPathResult(DependencyPathResult result)
    {
        ClearDependencyPathPresentation();
        DependencyPathSummaryText = result.Summary;

        if (!result.Found)
        {
            Graph.Touch();
            return;
        }

        var nodeLookup = Graph.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var edgeLookup = new HashSet<string>(result.EdgeKeys, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < result.NodeIds.Count; i++)
        {
            var nodeId = result.NodeIds[i];
            if (nodeLookup.TryGetValue(nodeId, out var node))
            {
                node.Metadata["IsDependencyPathHit"] = "true";
                DependencyPathSteps.Add($"{i + 1}. {node.Name} ({node.Type})");
            }
            else
            {
                DependencyPathSteps.Add($"{i + 1}. {nodeId}");
            }
        }

        foreach (var edge in Graph.Edges)
        {
            if (edgeLookup.Contains($"{edge.FromNodeId}->{edge.ToNodeId}"))
            {
                edge.Metadata["IsDependencyPathHit"] = "true";
            }
        }

        Graph.Touch();
    }

    public void ClearDependencyPathPresentation()
    {
        foreach (var node in Graph.Nodes)
        {
            node.Metadata.Remove("IsDependencyPathHit");
        }

        foreach (var edge in Graph.Edges)
        {
            edge.Metadata.Remove("IsDependencyPathHit");
        }

        DependencyPathSteps.Clear();
        DependencyPathSummaryText = string.Empty;
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

    private void ApplyDiagnosticsFilters()
    {
        var filtered = SelectedDiagnosticsSeverityFilter == "All"
            ? _allDiagnostics
            : _allDiagnostics.Where(diagnostic =>
                string.Equals(diagnostic.Severity.ToString(), SelectedDiagnosticsSeverityFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FilteredDiagnostics.Clear();
        foreach (var diagnostic in filtered)
        {
            FilteredDiagnostics.Add(diagnostic);
        }
    }

    private static void UpdateRecentSearchHistory(ObservableCollection<string> collection, string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (collection.Count > 0 &&
            normalized.StartsWith(collection[0], StringComparison.OrdinalIgnoreCase))
        {
            collection[0] = normalized;
        }
        else
        {
            var existingIndex = collection
                .Select((item, index) => new { item, index })
                .FirstOrDefault(x => string.Equals(x.item, normalized, StringComparison.OrdinalIgnoreCase))
                ?.index;

            if (existingIndex.HasValue)
            {
                collection.RemoveAt(existingIndex.Value);
            }

            collection.Insert(0, normalized);
        }

        while (collection.Count > 10)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T>? values)
    {
        collection.Clear();
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static string ResolveOption(string? value, IReadOnlyList<string> available, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            available.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return available.First(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        }

        return fallback;
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
        ClearDependencyPathPresentation();
        Graph.Nodes.Clear();
        Graph.Edges.Clear();
        _activeGraph = null;

        if (!_graphs.TryGetValue(SelectedGraphType, out var graph))
        {
            return;
        }

        var sourceGraph = SelectedGroupingMode == GraphGroupingMode.None
            ? graph
            : _groupingService.Group(graph, SelectedGroupingMode, _allProjects);
        ApplyStoredGraphLayout(sourceGraph, SelectedGraphType, SelectedGroupingMode);
        _activeGraph = sourceGraph;

        foreach (var node in sourceGraph.Nodes)
        {
            Graph.Nodes.Add(node);
        }

        foreach (var edge in sourceGraph.Edges)
        {
            Graph.Edges.Add(edge);
        }

        Graph.SelectedNode = null;
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

    private void CaptureGraphLayout(GraphType graphType, GraphGroupingMode groupingMode)
    {
        if (Graph.Nodes.Count == 0)
        {
            return;
        }

        _persistedGraphLayouts[BuildGraphLayoutKey(graphType, groupingMode)] = Graph.Nodes
            .ToDictionary(
                node => node.Id,
                node => new NodeLayoutState
                {
                    X = node.X,
                    Y = node.Y
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyStoredGraphLayout(ArchitectureGraph graph, GraphType graphType, GraphGroupingMode groupingMode)
    {
        if (!_persistedGraphLayouts.TryGetValue(BuildGraphLayoutKey(graphType, groupingMode), out var layouts))
        {
            return;
        }

        foreach (var node in graph.Nodes)
        {
            if (layouts.TryGetValue(node.Id, out var layout))
            {
                node.X = layout.X;
                node.Y = layout.Y;
            }
        }
    }

    private void CopyGraphLayoutsFromSession(Dictionary<string, Dictionary<string, NodeLayoutState>> graphLayouts)
    {
        _persistedGraphLayouts.Clear();
        foreach (var graphLayout in graphLayouts)
        {
            _persistedGraphLayouts[graphLayout.Key] = graphLayout.Value.ToDictionary(
                pair => pair.Key,
                pair => new NodeLayoutState
                {
                    X = pair.Value.X,
                    Y = pair.Value.Y
                },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private Dictionary<string, Dictionary<string, NodeLayoutState>> CloneGraphLayouts()
    {
        return _persistedGraphLayouts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToDictionary(
                inner => inner.Key,
                inner => new NodeLayoutState
                {
                    X = inner.Value.X,
                    Y = inner.Value.Y
                },
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildGraphLayoutKey(GraphType graphType, GraphGroupingMode groupingMode)
    {
        return $"{graphType}|{groupingMode}";
    }

    private void ApplyMetricsToNodeMetadata(MetricsSummary summary)
    {
        var fileLookup = summary.Files.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
        var projectLookup = summary.Projects.ToDictionary(x => x.ProjectName, StringComparer.OrdinalIgnoreCase);
        var namespaceLookup = summary.Namespaces.ToDictionary(x => x.Namespace, StringComparer.OrdinalIgnoreCase);
        var dependencyDepth = summary.Dependencies.ToDictionary(x => x.Scope, x => x.DependencyDepth, StringComparer.OrdinalIgnoreCase);

        var allNodes = _graphs.Values
            .SelectMany(graph => graph.Nodes)
            .DistinctBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var node in allNodes)
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
        OnPropertyChanged(nameof(SymbolExplorerTabText));
        OnPropertyChanged(nameof(SymbolExplorerSearchPlaceholderText));
        OnPropertyChanged(nameof(SymbolExplorerSearchButtonText));
        OnPropertyChanged(nameof(SymbolExplorerDetailsTabText));
        OnPropertyChanged(nameof(SymbolExplorerFindRefsText));
        OnPropertyChanged(nameof(SymbolExplorerGoToDefText));
        OnPropertyChanged(nameof(SymbolExplorerTypeMembersTitleText));
        OnPropertyChanged(nameof(SymbolExplorerMethodMetaTitleText));
        OnPropertyChanged(nameof(BottomPanelDiagnosticsTabText));
        OnPropertyChanged(nameof(SymbolExplorerReferencesTabText));
        OnPropertyChanged(nameof(SymbolExplorerJumpOpenText));
        OnPropertyChanged(nameof(GroupByText));
        OnPropertyChanged(nameof(FitToScreenText));
        OnPropertyChanged(nameof(ZoomToSelectionText));
        OnPropertyChanged(nameof(DependencyPathTabText));
        OnPropertyChanged(nameof(DependencyPathSourceTextLabel));
        OnPropertyChanged(nameof(DependencyPathTargetTextLabel));
        OnPropertyChanged(nameof(DependencyPathShortestText));
        OnPropertyChanged(nameof(DependencyPathCycleText));
        OnPropertyChanged(nameof(DiagnosticsSeverityFilterText));
        OnPropertyChanged(nameof(ExportDiagnosticsText));
        OnPropertyChanged(nameof(RecentSearchesText));
        OnPropertyChanged(nameof(LogsTabText));
        OnPropertyChanged(nameof(PerformanceText));
        OnPropertyChanged(nameof(RestoreLastSessionText));
        OnPropertyChanged(nameof(AutoSaveSessionText));
        OnPropertyChanged(nameof(ThemeText));
        OnPropertyChanged(nameof(GraphLayoutText));
        OnPropertyChanged(nameof(MetricsTotalLoc));
        OnPropertyChanged(nameof(MetricsTotalFiles));
        OnPropertyChanged(nameof(MetricsLargestFile));
        OnPropertyChanged(nameof(MetricsLargestNamespace));
        OnPropertyChanged(nameof(MetricsCircularDependencies));
        OnPropertyChanged(nameof(MetricsLayerViolations));
        UpdateGraphStatus();
        UpdateTopStatus();
        if (_lastPerformanceSnapshot is not null)
        {
            PresentPerformanceSnapshot(_lastPerformanceSnapshot);
        }
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
