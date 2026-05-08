using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CsArchViewer.Analysis;
using CsArchViewer.Avalonia.ViewModels;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet;
using CsArchViewer.Export;
using CsArchViewer.Metrics;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow : Window
{
    private GridLength _lastDiagnosticsHeight = new(180);

    private readonly DotNetProjectAnalyzer _analyzer = new();
    private readonly IncrementalAnalysisEngine _incrementalEngine;
    private readonly AnalysisScheduler _analysisScheduler = new();
    private readonly FileChangeTracker _fileChangeTracker = new();
    private readonly DiagnosticsEngine _diagnosticsEngine = new();
    private readonly SearchIndexService _searchIndex = new();
    private readonly DependencyExplorerService _dependencyExplorer = new();
    private readonly MermaidExporter _mermaidExporter = new();
    private readonly JsonExporter _jsonExporter = new();
    private readonly GraphvizExporter _graphvizExporter = new();
    private readonly MetricsJsonExporter _metricsJsonExporter = new();
    private readonly MetricsCsvExporter _metricsCsvExporter = new();
    private readonly MetricsMarkdownExporter _metricsMarkdownExporter = new();
    private readonly CodeMetricsAnalyzer _codeMetricsAnalyzer = new();

    private MetricsSummary? _latestMetricsSummary;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        _incrementalEngine = new IncrementalAnalysisEngine(_analyzer);
        AttachGraphSelectionBridge();
        AttachAnalysisEvents();
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
        };

        GraphViewControl.NodeDoubleClicked += node => ViewModel.DrillInto(node);
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
        _fileChangeTracker.Start(rootPath);
        QueueAnalysis(rootPath, null, AnalysisPriority.High, ViewModel.L("RunningFullAnalysis"));
        await Task.CompletedTask;
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

    private async Task ExportAsync(string formatName, string extension, Func<CsArchViewer.Core.Models.ArchitectureGraph, string> writer)
    {
        var graph = ViewModel.GetCurrentGraph();
        if (graph is null || StorageProvider is null)
        {
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
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        using var writerStream = new StreamWriter(stream);
        await writerStream.WriteAsync(writer(graph));
        ViewModel.Status = string.Format(ViewModel.L("ExportedTemplate"), formatName, file.Name);
    }

    private async Task ExportMetricsAsync(string formatName, string extension, Func<MetricsSummary, string> writer)
    {
        if (_latestMetricsSummary is null || StorageProvider is null)
        {
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
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        using var writerStream = new StreamWriter(stream);
        await writerStream.WriteAsync(writer(_latestMetricsSummary));
        ViewModel.Status = string.Format(ViewModel.L("ExportedTemplate"), formatName, file.Name);
    }

    private void DiagnosticsSplitter_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleDiagnosticsPanel();
    }

    private void DiagnosticsToggleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleDiagnosticsPanel();
    }

    private void ToggleDiagnosticsPanel()
    {
        var layoutGrid = this.FindControl<Grid>("DiagnosticsLayoutGrid");
        if (layoutGrid is null || layoutGrid.RowDefinitions.Count < 3)
        {
            return;
        }

        var diagnosticsContentRow = layoutGrid.RowDefinitions[2];
        var toggleButton = this.FindControl<Button>("DiagnosticsToggleButton");
        var currentHeight = diagnosticsContentRow.Height;
        var isCollapsed = currentHeight.IsAbsolute && currentHeight.Value <= 1;
        if (isCollapsed)
        {
            diagnosticsContentRow.Height = _lastDiagnosticsHeight;
            if (toggleButton is not null)
            {
                toggleButton.Content = "▾";
            }
            return;
        }

        if (currentHeight.IsAbsolute && currentHeight.Value > 1)
        {
            _lastDiagnosticsHeight = currentHeight;
        }

        diagnosticsContentRow.Height = new GridLength(0);
        if (toggleButton is not null)
        {
            toggleButton.Content = "▸";
        }
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
        _analysisScheduler.Enqueue(async token =>
        {
            try
            {
                var update = await _incrementalEngine.AnalyzeAsync(rootPath, changedFiles, token);
                var metrics = await _codeMetricsAnalyzer.AnalyzeAsync(update.Result, changedFiles, token);
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

                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.SetAnalysisResult(update.Result);
                    ViewModel.SetMetricsSummary(metrics);
                    ViewModel.SetDiagnostics(diagnostics);
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = update.IsIncremental
                        ? string.Format(ViewModel.L("IncrementalUpdatedTemplate"), string.Join(", ", update.ImpactedGraphs))
                        : ViewModel.L("FullAnalysisCompleted");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = string.Format(ViewModel.L("AnalysisFailedTemplate"), ex.Message);
                    ViewModel.Status = string.Format(ViewModel.L("AnalyzeFailedTemplate"), ex.Message);
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

    protected override void OnClosed(EventArgs e)
    {
        _fileChangeTracker.Dispose();
        _analysisScheduler.Dispose();
        base.OnClosed(e);
    }
}