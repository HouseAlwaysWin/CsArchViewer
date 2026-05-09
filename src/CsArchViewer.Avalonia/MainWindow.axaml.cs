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
            or nameof(MainWindowViewModel.ShowSelectedNodeNeighborhoodOnly)
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
