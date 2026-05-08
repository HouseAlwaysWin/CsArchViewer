using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CsArchViewer.Analysis;
using CsArchViewer.Avalonia.ViewModels;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet;
using CsArchViewer.DotNet.Roslyn;
using CsArchViewer.Export;
using CsArchViewer.DotNet.SymbolExplorer;
using CsArchViewer.DotNet.SymbolExplorer.Models;
using CsArchViewer.Metrics;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow : Window
{
    private GridLength _lastDiagnosticsHeight = new(180);

    private readonly RoslynSolutionLoader _roslynSolutionLoader = new();
    private readonly DotNetProjectAnalyzer _analyzer;
    private readonly IncrementalAnalysisEngine _incrementalEngine;
    private readonly AnalysisScheduler _analysisScheduler = new();
    private readonly FileChangeTracker _fileChangeTracker = new();
    private readonly DiagnosticsEngine _diagnosticsEngine = new();
    private readonly SearchIndexService _searchIndex = new();
    private readonly DependencyExplorerService _dependencyExplorer = new();
    private readonly MermaidExporter _mermaidExporter = new();
    private readonly JsonExporter _jsonExporter = new();
    private readonly GraphvizExporter _graphvizExporter = new();
    private readonly AiPromptExporter _aiPromptExporter = new();
    private readonly MetricsJsonExporter _metricsJsonExporter = new();
    private readonly MetricsCsvExporter _metricsCsvExporter = new();
    private readonly MetricsMarkdownExporter _metricsMarkdownExporter = new();
    private readonly CodeMetricsAnalyzer _codeMetricsAnalyzer = new();
    private readonly SymbolIndexBuilder _symbolIndexBuilder;
    private readonly SymbolSearchService _symbolSearchService = new();
    private readonly ReferenceFinderService _referenceFinderService = new();
    private readonly TypeMethodAnalyzer _typeMethodAnalyzer = new();
    private readonly MethodMetadataAnalyzer _methodMetadataAnalyzer = new();
    private readonly SymbolNavigationService _symbolNavigationService = new();
    private readonly WorkspaceStatePersistenceService _workspaceStatePersistenceService = new();
    private readonly AppLogService _appLogService = new();
    private readonly PerformanceMonitorService _performanceMonitorService = new();
    private readonly DependencyPathExplorerService _dependencyPathExplorerService = new();

    private MetricsSummary? _latestMetricsSummary;
    private CancellationTokenSource? _stateSaveCts;
    private bool _isRestoringWorkspaceState;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        _analyzer = new DotNetProjectAnalyzer(_roslynSolutionLoader);
        _symbolIndexBuilder = new SymbolIndexBuilder(_roslynSolutionLoader);
        _incrementalEngine = new IncrementalAnalysisEngine(_analyzer);
        AttachV65Services();
        AttachGraphSelectionBridge();
        AttachAnalysisEvents();
        ApplyThemeVariant();
        UpdateExportButtonState();
        Opened += MainWindow_OnOpened;
    }

    private void AttachV65Services()
    {
        _appLogService.EntryAdded += entry =>
            Dispatcher.UIThread.Post(() => ViewModel.AppendLogEntry(entry));
        _performanceMonitorService.SnapshotUpdated += snapshot =>
            Dispatcher.UIThread.Post(() => ViewModel.PresentPerformanceSnapshot(snapshot));
        ViewModel.ResetLogEntries(_appLogService.Entries);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (_isRestoringWorkspaceState || string.IsNullOrWhiteSpace(args.PropertyName))
            {
                return;
            }

            if (args.PropertyName is nameof(MainWindowViewModel.SelectedGraphType) or nameof(MainWindowViewModel.SelectedGroupingMode))
            {
                _appLogService.Info("Graph", $"Graph updated: {ViewModel.SelectedGraphType} | Grouping: {ViewModel.SelectedGroupingMode}");
                UpdateExportButtonState();
            }
            else if (args.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
            {
                ApplyThemeVariant();
            }

            if (ShouldPersistProperty(args.PropertyName))
            {
                SchedulePersistWorkspaceState();
            }
        };
    }

    private void ApplyThemeVariant()
    {
        var themeVariant = ViewModel.SelectedTheme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        RequestedThemeVariant = themeVariant;
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = themeVariant;
        }
    }

    private async void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_OnOpened;
        await RestoreWorkspaceStateAsync().ConfigureAwait(true);
    }

    private static bool ShouldPersistProperty(string propertyName)
    {
        return propertyName is nameof(MainWindowViewModel.CurrentRootPath)
            or nameof(MainWindowViewModel.SearchText)
            or nameof(MainWindowViewModel.SymbolExplorerSearchQuery)
            or nameof(MainWindowViewModel.SelectedGraphType)
            or nameof(MainWindowViewModel.SelectedGroupingMode)
            or nameof(MainWindowViewModel.SelectedTypeFilter)
            or nameof(MainWindowViewModel.SelectedMetricsFilter)
            or nameof(MainWindowViewModel.SelectedOverlayMode)
            or nameof(MainWindowViewModel.SelectedDiagnosticsSeverityFilter)
            or nameof(MainWindowViewModel.SelectedLanguage)
            or nameof(MainWindowViewModel.SelectedTheme)
            or nameof(MainWindowViewModel.SelectedGraphLayout)
            or nameof(MainWindowViewModel.ShowLineCountOnNodes)
            or nameof(MainWindowViewModel.RestoreLastSession)
            or nameof(MainWindowViewModel.AutoSaveSession);
    }

    private void AttachGraphSelectionBridge()
    {
        ViewModel.Graph.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ViewModel.Graph.SelectedNode))
            {
                return;
            }

            InjectDependencyExplorerMetadata(ViewModel.Graph.SelectedNode);
            ViewModel.SelectNode(ViewModel.Graph.SelectedNode);
            _ = SyncSymbolExplorerFromSelectedNodeAsync(ViewModel.Graph.SelectedNode);
        };

        GraphViewControl.NodeDoubleClicked += HandleNodeDoubleClicked;
    }

    private void HandleNodeDoubleClicked(ArchitectureNode node)
    {
        if (node.Type != ArchitectureNodeType.File)
        {
            ViewModel.DrillInto(node);
            return;
        }

        if (!File.Exists(node.FullPath))
        {
            ViewModel.Status = string.Format(ViewModel.L("OpenFileNotFoundTemplate"), node.FullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = node.FullPath,
                UseShellExecute = true
            });
            ViewModel.Status = string.Format(ViewModel.L("OpenedFileTemplate"), Path.GetFileName(node.FullPath));
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("OpenFileFailedTemplate"), ex.Message);
        }
    }

    private void AttachAnalysisEvents()
    {
        _analysisScheduler.QueueLengthChanged += count =>
        {
            Dispatcher.UIThread.Post(() => ViewModel.BackgroundTaskCount = count);
        };

        _fileChangeTracker.FileChanged += filePath =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (string.IsNullOrWhiteSpace(ViewModel.CurrentRootPath))
                {
                    return;
                }

                QueueAnalysis(
                    ViewModel.CurrentRootPath!,
                    [filePath],
                    AnalysisPriority.Normal,
                    string.Format(ViewModel.L("FileChangedTemplate"), Path.GetFileName(filePath)));
            });
        };
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = ViewModel.L("SelectFolderTitle"),
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return;
        }

        var path = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadAsync(path);
    }

    private async void Reload_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentRootPath))
        {
            return;
        }

        await LoadAsync(ViewModel.CurrentRootPath);
    }

    private async Task LoadAsync(string rootPath)
    {
        ViewModel.CurrentRootPath = rootPath;
        _appLogService.Info("Workspace", $"Loading workspace: {rootPath}");
        _fileChangeTracker.Start(rootPath);
        QueueAnalysis(rootPath, null, AnalysisPriority.High, ViewModel.L("RunningFullAnalysis"));
        SchedulePersistWorkspaceState();
        await Task.CompletedTask;
    }

    private async Task RestoreWorkspaceStateAsync()
    {
        _isRestoringWorkspaceState = true;
        try
        {
            var state = await _workspaceStatePersistenceService.LoadAsync().ConfigureAwait(true);
            ViewModel.ApplyWorkspaceState(state);
            _appLogService.Info("Persistence", "Workspace state restored.");

            var rootPath = state.Session.RootPath;
            if (ViewModel.RestoreLastSession &&
                !string.IsNullOrWhiteSpace(rootPath) &&
                Directory.Exists(rootPath))
            {
                await LoadAsync(rootPath).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _appLogService.Warning("Persistence", $"Workspace state restore failed: {ex.Message}");
        }
        finally
        {
            _isRestoringWorkspaceState = false;
        }
    }

    private void SchedulePersistWorkspaceState()
    {
        if (_isRestoringWorkspaceState || !ViewModel.AutoSaveSession)
        {
            return;
        }

        _stateSaveCts?.Cancel();
        _stateSaveCts?.Dispose();
        _stateSaveCts = new CancellationTokenSource();
        _ = PersistWorkspaceStateDebouncedAsync(_stateSaveCts.Token);
    }

    private async Task PersistWorkspaceStateDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(750, cancellationToken).ConfigureAwait(true);
            await PersistWorkspaceStateAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _appLogService.Warning("Persistence", $"Workspace state save failed: {ex.Message}");
        }
    }

    private async Task PersistWorkspaceStateAsync(CancellationToken cancellationToken = default)
    {
        if (_isRestoringWorkspaceState)
        {
            return;
        }

        var state = ViewModel.ExportWorkspaceState();
        await _workspaceStatePersistenceService.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async void ExportMermaid_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportAsync("mermaid", ".mmd", graph => _mermaidExporter.Export(graph, ViewModel.SelectedGraphType.ToString()));
    }

    private async void ExportJson_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportAsync("json", ".json", graph => _jsonExporter.Export(graph));
    }

    private async void ExportDot_OnClick(object? sender, RoutedEventArgs e)
    {
        await ExportAsync("dot", ".dot", graph => _graphvizExporter.Export(graph, ViewModel.SelectedGraphType.ToString()));
    }

    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        var combo = this.FindControl<ComboBox>("ExportFormatCombo");
        var selected = (combo?.SelectedItem as ComboBoxItem)?.Content as string ?? "Mermaid";

        switch (selected)
        {
            case "JSON":
                await ExportAsync("json", ".json", graph => _jsonExporter.Export(graph));
                break;
            case "DOT":
                await ExportAsync("dot", ".dot", graph => _graphvizExporter.Export(graph, ViewModel.SelectedGraphType.ToString()));
                break;
            case "AI Prompt":
                await ExportAsync("ai prompt", ".prompt.md", graph => _aiPromptExporter.Export(graph, ViewModel.SelectedGraphType.ToString()));
                break;
            case "Metrics JSON":
                await ExportMetricsAsync("metrics json", ".metrics.json", summary => _metricsJsonExporter.Export(summary));
                break;
            case "Metrics CSV":
                await ExportMetricsAsync("metrics csv", ".metrics.csv", summary => _metricsCsvExporter.Export(summary));
                break;
            case "Metrics Markdown":
                await ExportMetricsAsync("metrics markdown", ".metrics.md", summary => _metricsMarkdownExporter.Export(summary));
                break;
            default:
                await ExportAsync("mermaid", ".mmd", graph => _mermaidExporter.Export(graph, ViewModel.SelectedGraphType.ToString()));
                break;
        }
    }

    private void ExportFormatCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateExportButtonState();
    }

    private void UpdateExportButtonState()
    {
        ComboBox? combo;
        Button? button;
        try
        {
            combo = this.FindControl<ComboBox>("ExportFormatCombo");
            button = this.FindControl<Button>("ExportActionButton");
        }
        catch (InvalidOperationException)
        {
            // SelectionChanged can fire while XAML name scope is still being constructed.
            return;
        }

        if (button is null)
        {
            return;
        }

        var selected = (combo?.SelectedItem as ComboBoxItem)?.Content as string ?? "Mermaid";
        var requiresMetrics = selected is "Metrics JSON" or "Metrics CSV" or "Metrics Markdown";
        var hasGraph = ViewModel.GetCurrentGraph() is not null;
        var hasMetrics = _latestMetricsSummary is not null;
        button.IsEnabled = requiresMetrics ? hasMetrics : hasGraph;
    }

    private async Task ExportAsync(string formatName, string extension, Func<CsArchViewer.Core.Models.ArchitectureGraph, string> writer)
    {
        try
        {
            var graph = ViewModel.GetCurrentGraph();
            if (graph is null)
            {
                ViewModel.Status = ViewModel.L("ExportUnavailableGraph");
                return;
            }

            if (StorageProvider is null)
            {
                ViewModel.Status = string.Format(ViewModel.L("ExportFailedTemplate"), "storage provider unavailable");
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = string.Format(ViewModel.L("ExportTitleTemplate"), formatName),
                SuggestedFileName = $"csarchviewer-{ViewModel.SelectedGraphType}{extension}",
                FileTypeChoices =
                [
                    new FilePickerFileType(formatName.ToUpperInvariant())
                    {
                        Patterns = [$"*{extension}"]
                    }
                ]
            });

            if (file is null)
            {
                ViewModel.Status = ViewModel.L("ExportCanceled");
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            using var writerStream = new StreamWriter(stream);
            await writerStream.WriteAsync(writer(graph));
            ViewModel.Status = string.Format(ViewModel.L("ExportedTemplate"), formatName, file.Name);
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("ExportFailedTemplate"), ex.Message);
        }
    }

    private async Task ExportMetricsAsync(string formatName, string extension, Func<MetricsSummary, string> writer)
    {
        try
        {
            if (_latestMetricsSummary is null)
            {
                ViewModel.Status = ViewModel.L("ExportUnavailableMetrics");
                return;
            }

            if (StorageProvider is null)
            {
                ViewModel.Status = string.Format(ViewModel.L("ExportFailedTemplate"), "storage provider unavailable");
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = string.Format(ViewModel.L("ExportTitleTemplate"), formatName),
                SuggestedFileName = $"csarchviewer-metrics-{DateTime.Now:yyyyMMddHHmmss}{extension}",
                FileTypeChoices =
                [
                    new FilePickerFileType(formatName.ToUpperInvariant())
                    {
                        Patterns = [$"*{extension}"]
                    }
                ]
            });

            if (file is null)
            {
                ViewModel.Status = ViewModel.L("ExportCanceled");
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            using var writerStream = new StreamWriter(stream);
            await writerStream.WriteAsync(writer(_latestMetricsSummary));
            ViewModel.Status = string.Format(ViewModel.L("ExportedTemplate"), formatName, file.Name);
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("ExportFailedTemplate"), ex.Message);
        }
    }

    private void FitToScreen_OnClick(object? sender, RoutedEventArgs e)
    {
        GraphViewControl.FitToScreen();
        _appLogService.Info("Graph", "Fit-to-screen applied.");
    }

    private void ZoomToSelection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Graph.SelectedNode is not { } selectedNode)
        {
            return;
        }

        GraphViewControl.ZoomToNode(selectedNode);
        _appLogService.Info("Graph", $"Zoomed to selection: {selectedNode.Name}");
    }

    private void QueueAnalysis(
        string rootPath,
        IReadOnlyCollection<string>? changedFiles,
        AnalysisPriority priority,
        string status)
    {
        ViewModel.IsAnalyzing = true;
        ViewModel.Status = status;
        ViewModel.AnalysisStatus = status;
        _appLogService.Info("Analysis", status);
        _analysisScheduler.Enqueue(async token =>
        {
            try
            {
                var totalStopwatch = Stopwatch.StartNew();
                var analysisStopwatch = Stopwatch.StartNew();
                var update = await _incrementalEngine.AnalyzeAsync(rootPath, changedFiles, token);
                analysisStopwatch.Stop();

                var metricsStopwatch = Stopwatch.StartNew();
                var metrics = await _codeMetricsAnalyzer.AnalyzeAsync(update.Result, changedFiles, token);
                metricsStopwatch.Stop();
                _latestMetricsSummary = metrics;

                var diagnostics = _diagnosticsEngine.Analyze(update.Result.Graphs).ToList();
                diagnostics.AddRange(metrics.HealthWarnings.Select(warning => new ArchitectureDiagnostic
                {
                    Type = warning.Type,
                    Severity = warning.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
                        ? DiagnosticSeverity.Error
                        : warning.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                            ? DiagnosticSeverity.Warning
                            : DiagnosticSeverity.Info,
                    Source = warning.Source,
                    Message = warning.Message
                }));
                _searchIndex.BuildIndex(update.Result);
                _appLogService.Trace("Graph", $"Search index rebuilt for {update.Result.Graphs.Count} graphs.");

                var symbolStopwatch = Stopwatch.StartNew();
                try
                {
                    if (changedFiles is null || changedFiles.Count == 0)
                    {
                        _appLogService.Trace("Roslyn", "Rebuilding symbol index from workspace.");
                        await _symbolIndexBuilder.RebuildAsync(rootPath, token).ConfigureAwait(false);
                    }
                    else
                    {
                        var csharpChanges = changedFiles
                            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (csharpChanges.Count > 0)
                        {
                            _appLogService.Trace("Roslyn", $"Updating symbol index for {csharpChanges.Count} changed C# files.");
                            await _symbolIndexBuilder.UpdateFilesAsync(csharpChanges, token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _appLogService.Warning("Roslyn", $"Symbol index refresh failed: {ex.Message}");
                }
                finally
                {
                    symbolStopwatch.Stop();
                }

                totalStopwatch.Stop();
                var snapshot = new PerformanceSnapshot
                {
                    AnalysisMs = analysisStopwatch.Elapsed.TotalMilliseconds,
                    MetricsMs = metricsStopwatch.Elapsed.TotalMilliseconds,
                    SymbolIndexMs = symbolStopwatch.Elapsed.TotalMilliseconds,
                    TotalMs = totalStopwatch.Elapsed.TotalMilliseconds,
                    CacheHitRate = _incrementalEngine.CacheHitRate,
                    SymbolIndexSize = _symbolIndexBuilder.Symbols.Count,
                    MemoryUsageMb = GC.GetTotalMemory(false) / (1024d * 1024d),
                    NodeCount = update.Result.Graphs.Values.Sum(graph => graph.Nodes.Count),
                    EdgeCount = update.Result.Graphs.Values.Sum(graph => graph.Edges.Count)
                };

                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.SetAnalysisResult(update.Result);
                    ViewModel.SetMetricsSummary(metrics);
                    ViewModel.SetDiagnostics(diagnostics);
                    _performanceMonitorService.Update(snapshot);
                    UpdateExportButtonState();
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = update.IsIncremental
                        ? string.Format(ViewModel.L("IncrementalUpdatedTemplate"), string.Join(", ", update.ImpactedGraphs))
                        : ViewModel.L("FullAnalysisCompleted");
                    _appLogService.Info(
                        "Analysis",
                        $"{(update.IsIncremental ? "Incremental" : "Full")} analysis completed in {snapshot.TotalMs:N0} ms. Cache hit rate: {snapshot.CacheHitRate:P0}.");
                    SchedulePersistWorkspaceState();
                });
            }
            catch (Exception ex)
            {
                _appLogService.Error("Analysis", ex.Message);
                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = string.Format(ViewModel.L("AnalysisFailedTemplate"), ex.Message);
                    ViewModel.Status = string.Format(ViewModel.L("AnalyzeFailedTemplate"), ex.Message);
                    UpdateExportButtonState();
                });
            }
        }, priority);
    }

    private void InjectDependencyExplorerMetadata(ArchitectureNode? node)
    {
        if (node is null)
        {
            return;
        }

        var graph = ViewModel.GetCurrentGraph();
        if (graph is null)
        {
            return;
        }

        var explorer = _dependencyExplorer.Explore(graph, node.Id);
        node.Metadata["DependsOn"] = explorer.Outgoing.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, explorer.Outgoing);
        node.Metadata["DependencyIncoming"] = explorer.Incoming.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, explorer.Incoming);
        node.Metadata["DependencyCount"] = (explorer.Outgoing.Count + explorer.Incoming.Count).ToString();
        node.Metadata["CircularDependencyCount"] = explorer.CircularDependencyCount.ToString();
        node.Metadata["ViolationCount"] = explorer.ViolationCount.ToString();
    }

    private static bool IsExplorerGraphType(ArchitectureNodeType nodeType)
    {
        return nodeType is ArchitectureNodeType.Type or ArchitectureNodeType.Interface or ArchitectureNodeType.Struct
            or ArchitectureNodeType.Enum or ArchitectureNodeType.Record;
    }

    private static bool IsExplorerTypeSymbolKind(ExplorerSymbolKind kind)
    {
        return kind is ExplorerSymbolKind.Class or ExplorerSymbolKind.Interface or ExplorerSymbolKind.Struct
            or ExplorerSymbolKind.Enum or ExplorerSymbolKind.Record or ExplorerSymbolKind.Delegate;
    }

    private static bool IsReferenceQueryableSymbolKind(ExplorerSymbolKind kind)
    {
        return kind is ExplorerSymbolKind.Class
            or ExplorerSymbolKind.Interface
            or ExplorerSymbolKind.Struct
            or ExplorerSymbolKind.Enum
            or ExplorerSymbolKind.Record
            or ExplorerSymbolKind.Delegate
            or ExplorerSymbolKind.Method
            or ExplorerSymbolKind.Property
            or ExplorerSymbolKind.Field
            or ExplorerSymbolKind.Event;
    }

    private static SymbolInfoModel? MatchSymbolForGraphNode(ArchitectureNode node, IReadOnlyList<SymbolInfoModel> symbols)
    {
        node.Metadata.TryGetValue("Namespace", out var ns);
        ns ??= string.Empty;

        foreach (var s in symbols)
        {
            if (!IsExplorerTypeSymbolKind(s.Kind))
            {
                continue;
            }

            if (!string.Equals(s.Name, node.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(s.Namespace, ns, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        node.Metadata.TryGetValue("FullTypeName", out var fq);
        fq ??= node.Id;
        foreach (var s in symbols)
        {
            if (!IsExplorerTypeSymbolKind(s.Kind))
            {
                continue;
            }

            if (string.Equals(s.SymbolKey, fq, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }

            if (s.SymbolKey.EndsWith("." + node.Name, StringComparison.OrdinalIgnoreCase) &&
                fq.Contains(node.Name, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return null;
    }

    private async Task ExplorerAnalyzeGraphTypeAsync(ArchitectureNode? node)
    {
        if (node is null || !IsExplorerGraphType(node.Type))
        {
            return;
        }

        var solution = _symbolIndexBuilder.CurrentSolution;
        var symbols = _symbolIndexBuilder.Symbols;
        if (solution is null || symbols.Count == 0)
        {
            return;
        }

        var hit = MatchSymbolForGraphNode(node, symbols);
        if (hit is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ViewModel.SelectedExplorerSymbol = hit);
        await RefreshExplorerSymbolAsync(hit).ConfigureAwait(true);
    }

    private async Task SyncSymbolExplorerFromSelectedNodeAsync(ArchitectureNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.Type != ArchitectureNodeType.File || !node.FullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            await ExplorerAnalyzeGraphTypeAsync(node).ConfigureAwait(true);
            return;
        }

        if (!await EnsureSymbolIndexReadyAsync().ConfigureAwait(true))
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }

        var symbols = _symbolIndexBuilder.Symbols
            .Where(s => PathsEqual(s.FilePath, node.FullPath))
            .OrderBy(s => GetKindOrder(s.Kind))
            .ThenBy(s => s.LineNumber)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ViewModel.SymbolExplorerResults.Clear();
        foreach (var symbol in symbols)
        {
            ViewModel.SymbolExplorerResults.Add(symbol);
        }

        ViewModel.SymbolExplorerSearchQuery = Path.GetFileNameWithoutExtension(node.FullPath);
        ViewModel.SelectedExplorerSymbol = symbols.FirstOrDefault();
        if (ViewModel.SelectedExplorerSymbol is not null)
        {
            await RefreshExplorerSymbolAsync(ViewModel.SelectedExplorerSymbol).ConfigureAwait(true);
        }
        else
        {
            ViewModel.ExplorerSymbolDetailsText = string.Empty;
            ViewModel.ExplorerTypeMembersSummary = string.Empty;
            ViewModel.ExplorerMethodMetadataText = string.Empty;
            ViewModel.ExplorerTypeMethods.Clear();
        }

        ViewModel.Status = symbols.Count == 0
            ? $"No symbols found in {Path.GetFileName(node.FullPath)}."
            : $"Loaded {symbols.Count} symbols from {Path.GetFileName(node.FullPath)}.";
    }

    private async void SymbolExplorerSearch_OnClick(object? sender, RoutedEventArgs e)
    {
        var query = ViewModel.SymbolExplorerSearchQuery?.Trim() ?? string.Empty;
        if (!await EnsureSymbolIndexReadyAsync().ConfigureAwait(true))
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }

        try
        {
            var results = await _symbolSearchService.SearchAsync(_symbolIndexBuilder.Symbols, query, 1000).ConfigureAwait(true);
            ViewModel.SymbolExplorerResults.Clear();
            foreach (var item in results)
            {
                ViewModel.SymbolExplorerResults.Add(item);
            }
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("AnalyzeFailedTemplate"), ex.Message);
        }
    }

    private async void SymbolExplorerResults_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox box)
        {
            return;
        }

        if (box.SelectedItem is not SymbolInfoModel sym)
        {
            ViewModel.ExplorerSymbolDetailsText = string.Empty;
            ViewModel.ExplorerTypeMembersSummary = string.Empty;
            ViewModel.ExplorerMethodMetadataText = string.Empty;
            ViewModel.ExplorerTypeMethods.Clear();
            return;
        }

        await RefreshExplorerSymbolAsync(sym).ConfigureAwait(true);
    }

    private async Task RefreshExplorerSymbolAsync(SymbolInfoModel sym)
    {
        ViewModel.ExplorerSymbolDetailsText = FormatSymbolDetails(sym);
        ViewModel.ExplorerTypeMembersSummary = string.Empty;
        ViewModel.ExplorerMethodMetadataText = string.Empty;
        ViewModel.ExplorerTypeMethods.Clear();

        var solution = _symbolIndexBuilder.CurrentSolution;
        if (solution is null)
        {
            return;
        }

        try
        {
            if (IsExplorerTypeSymbolKind(sym.Kind))
            {
                var typeModel = await _typeMethodAnalyzer.AnalyzeTypeAsync(solution, sym, CancellationToken.None)
                    .ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() => ApplyTypeAnalysis(typeModel));
            }
            else if (sym.Kind == ExplorerSymbolKind.Method)
            {
                var meta = await _methodMetadataAnalyzer.TryFromSymbolInfoAsync(solution, sym, CancellationToken.None)
                    .ConfigureAwait(false);
                var text = meta is null ? string.Empty : FormatMethodDetails(meta);
                await Dispatcher.UIThread.InvokeAsync(() => ViewModel.ExplorerMethodMetadataText = text);
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ViewModel.ExplorerMethodMetadataText = ex.Message);
        }
    }

    private static string FormatSymbolDetails(SymbolInfoModel sym)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{sym.DisplayName}");
        sb.AppendLine($"Kind: {sym.Kind}");
        sb.AppendLine($"Namespace: {sym.Namespace}");
        sb.AppendLine($"Accessibility: {sym.Accessibility}");
        if (!string.IsNullOrWhiteSpace(sym.ContainingTypeName))
        {
            sb.AppendLine($"Containing type: {sym.ContainingTypeName}");
        }

        sb.AppendLine($"File: {sym.FilePath}");
        sb.AppendLine($"Line: {sym.LineNumber}");
        return sb.ToString().TrimEnd();
    }

    private static string FormatMethodDetails(MethodInfoModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine(m.Signature);
        sb.AppendLine($"Return: {m.ReturnType}");
        sb.AppendLine($"Parameters ({m.ParameterCount}): {m.Parameters}");
        sb.AppendLine($"Async={m.IsAsync}, Static={m.IsStatic}, Virtual={m.IsVirtual}, Override={m.IsOverride}, Abstract={m.IsAbstract}");
        sb.AppendLine($"Generics: {m.GenericParameterCount}");
        if (m.UsedTypes.Count > 0)
        {
            sb.AppendLine("Used types (lightweight):");
            foreach (var t in m.UsedTypes.Take(40))
            {
                sb.AppendLine($"  · {t}");
            }

            if (m.UsedTypes.Count > 40)
            {
                sb.AppendLine($"  … ({m.UsedTypes.Count - 40} more)");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void ApplyTypeAnalysis(TypeInfoModel? tm)
    {
        ViewModel.ExplorerTypeMethods.Clear();
        if (tm is null)
        {
            ViewModel.ExplorerTypeMembersSummary = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(tm.FullName);
        sb.AppendLine($"Kind: {tm.Kind}");
        sb.AppendLine($"{ViewModel.BaseTypeText}: {(string.IsNullOrWhiteSpace(tm.BaseType) ? "-" : tm.BaseType)}");
        if (tm.Interfaces.Count > 0)
        {
            sb.AppendLine($"{ViewModel.ImplementedInterfacesText}:");
            foreach (var i in tm.Interfaces.Take(15))
            {
                sb.AppendLine($"  · {i}");
            }

            if (tm.Interfaces.Count > 15)
            {
                sb.AppendLine($"  … ({tm.Interfaces.Count - 15} more)");
            }
        }

        var pf = ViewModel.FileText;
        sb.AppendLine($"{pf}: {tm.FilePath} ({tm.LineNumber})");

        if (tm.Properties.Count > 0)
        {
            sb.AppendLine($"Properties ({tm.Properties.Count}): " +
                          string.Join(", ", tm.Properties.Take(12)) +
                          (tm.Properties.Count > 12 ? "…" : string.Empty));
        }

        if (tm.Fields.Count > 0)
        {
            sb.AppendLine($"Fields ({tm.Fields.Count}): " +
                          string.Join(", ", tm.Fields.Take(12)) +
                          (tm.Fields.Count > 12 ? "…" : string.Empty));
        }

        if (tm.Events.Count > 0)
        {
            sb.AppendLine($"Events ({tm.Events.Count}): " + string.Join(", ", tm.Events.Take(12)));
        }

        ViewModel.ExplorerTypeMembersSummary = sb.ToString().TrimEnd();
        foreach (var method in tm.Methods)
        {
            ViewModel.ExplorerTypeMethods.Add(method);
        }
    }

    private void ExplorerMethods_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: MethodInfoModel method })
        {
            return;
        }

        ViewModel.ExplorerMethodMetadataText = FormatMethodDetails(method);
    }

    private async void SymbolExplorerFindRefs_OnClick(object? sender, RoutedEventArgs e)
    {
        var sym = ViewModel.SelectedExplorerSymbol;
        if (sym is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoSelection");
            return;
        }

        if (!await EnsureSymbolIndexReadyAsync().ConfigureAwait(true))
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }
        var solution = _symbolIndexBuilder.CurrentSolution;
        if (solution is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }

        if (!IsReferenceQueryableSymbolKind(sym.Kind))
        {
            ViewModel.ExplorerReferences.Clear();
            ViewModel.SelectedExplorerReference = null;
            ViewModel.Status = $"'{sym.Kind}' symbols do not support reference lookup. Please select a type/member symbol.";
            return;
        }

        try
        {
            var (refs, _) = await _referenceFinderService.FindReferencesAsync(solution, sym, CancellationToken.None)
                .ConfigureAwait(true);
            ViewModel.ExplorerReferences.Clear();
            ViewModel.SelectedExplorerReference = null;
            foreach (var r in refs)
            {
                ViewModel.ExplorerReferences.Add(r);
            }

            ViewModel.Status = refs.Count == 0
                ? $"No references found for '{sym.DisplayName}'."
                : $"References: {refs.Count}";
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("AnalyzeFailedTemplate"), ex.Message);
        }
    }

    private void SymbolExplorerGoToDef_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedExplorerMethod is { } method)
        {
            OpenExplorerTarget(_symbolNavigationService.JumpToMethod(method));
            return;
        }

        var sym = ViewModel.SelectedExplorerSymbol;
        if (sym is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoSelection");
            return;
        }

        OpenExplorerTarget(_symbolNavigationService.JumpToDefinition(sym));
    }

    private void ExplorerReferencesOpen_OnClick(object? sender, RoutedEventArgs e)
    {
        var r = ViewModel.SelectedExplorerReference;
        if (r is null)
        {
            ViewModel.Status = "No reference selected.";
            return;
        }

        OpenExplorerTarget(_symbolNavigationService.JumpToReference(r));
    }

    private void ExplorerReferences_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel.SelectedExplorerReference is { } reference)
        {
            OpenExplorerTarget(_symbolNavigationService.JumpToReference(reference));
        }
    }

    private void OpenExplorerTarget(NavigationTarget target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target.FilePath) || !File.Exists(target.FilePath))
            {
                ViewModel.Status = string.Format(ViewModel.L("OpenFileNotFoundTemplate"), target.FilePath);
                return;
            }

            if (TryLaunchEditorAtLocation(target))
            {
                ViewModel.Status = $"Opened: {Path.GetFileName(target.FilePath)}:{Math.Max(1, target.LineNumber)}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{target.FilePath}\"",
                UseShellExecute = true
            });
            ViewModel.Status = $"Opened file: {Path.GetFileName(target.FilePath)}";
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("OpenFileFailedTemplate"), ex.Message);
        }
    }

    private static bool TryLaunchEditorAtLocation(NavigationTarget target)
    {
        var line = Math.Max(1, target.LineNumber);
        var col = Math.Max(1, target.Column);
        var escaped = $"\"{target.FilePath}:{line}:{col}\"";
        foreach (var cli in GetEditorLaunchCommands())
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = cli.FileName,
                    Arguments = $"--goto {escaped}",
                    UseShellExecute = cli.UseShellExecute,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (process is not null)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore and try fallback command/file open.
            }
        }

        return false;
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static int GetKindOrder(ExplorerSymbolKind kind)
    {
        return kind switch
        {
            ExplorerSymbolKind.Class => 0,
            ExplorerSymbolKind.Record => 1,
            ExplorerSymbolKind.Struct => 2,
            ExplorerSymbolKind.Interface => 3,
            ExplorerSymbolKind.Enum => 4,
            ExplorerSymbolKind.Delegate => 5,
            ExplorerSymbolKind.Method => 6,
            ExplorerSymbolKind.Property => 7,
            ExplorerSymbolKind.Field => 8,
            ExplorerSymbolKind.Event => 9,
            ExplorerSymbolKind.Namespace => 10,
            _ => 99
        };
    }

    private static IEnumerable<(string FileName, bool UseShellExecute)> GetEditorLaunchCommands()
    {
        // Try direct exe paths first (more reliable than PATH in desktop apps).
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cursorExe = Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe");
        var codeExe = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");

        if (File.Exists(cursorExe))
        {
            yield return (cursorExe, false);
        }

        if (File.Exists(codeExe))
        {
            yield return (codeExe, false);
        }

        // Fallback to PATH-based launch commands.
        yield return ("cursor", false);
        yield return ("code", false);
    }

    private async Task<bool> EnsureSymbolIndexReadyAsync()
    {
        if (_symbolIndexBuilder.Symbols.Count > 0 && _symbolIndexBuilder.CurrentSolution is not null)
        {
            return true;
        }

        var root = ViewModel.CurrentRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return false;
        }

        try
        {
            ViewModel.Status = "Building symbol index...";
            await _symbolIndexBuilder.RebuildAsync(root, CancellationToken.None).ConfigureAwait(true);
            var ready = _symbolIndexBuilder.Symbols.Count > 0 && _symbolIndexBuilder.CurrentSolution is not null;
            ViewModel.Status = ready
                ? $"Symbol index ready: {_symbolIndexBuilder.Symbols.Count} items"
                : ViewModel.L("SymbolExplorerNoIndex");
            return ready;
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"Symbol index build failed: {ex.Message}";
            return false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _stateSaveCts?.Cancel();
        _stateSaveCts?.Dispose();
        try
        {
            PersistWorkspaceStateAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore persistence failures during shutdown.
        }

        _fileChangeTracker.Dispose();
        _analysisScheduler.Dispose();
        _symbolIndexBuilder.Dispose();
        _analyzer.Dispose();
        _roslynSolutionLoader.Dispose();
        base.OnClosed(e);
    }
}
