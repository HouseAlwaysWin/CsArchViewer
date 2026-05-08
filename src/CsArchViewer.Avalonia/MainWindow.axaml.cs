using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CsArchViewer.Analysis;
using CsArchViewer.Avalonia.ViewModels;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet;
using CsArchViewer.Export;

namespace CsArchViewer.Avalonia;

public partial class MainWindow : Window
{
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

                QueueAnalysis(ViewModel.CurrentRootPath!, [filePath], AnalysisPriority.Normal, $"File changed: {Path.GetFileName(filePath)}");
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
            Title = "Select .NET solution folder",
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
        QueueAnalysis(rootPath, null, AnalysisPriority.High, "Running full analysis...");
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

    private async Task ExportAsync(string formatName, string extension, Func<CsArchViewer.Core.Models.ArchitectureGraph, string> writer)
    {
        var graph = ViewModel.GetCurrentGraph();
        if (graph is null || StorageProvider is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {formatName}",
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
        ViewModel.Status = $"Exported {formatName}: {file.Name}";
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
                var diagnostics = _diagnosticsEngine.Analyze(update.Result.Graphs);
                _searchIndex.BuildIndex(update.Result);

                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.SetAnalysisResult(update.Result);
                    ViewModel.SetDiagnostics(diagnostics);
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = update.IsIncremental
                        ? $"Incremental analysis updated: {string.Join(", ", update.ImpactedGraphs)}"
                        : "Full analysis completed.";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.IsAnalyzing = false;
                    ViewModel.AnalysisStatus = $"Analysis failed: {ex.Message}";
                    ViewModel.Status = $"Analyze failed: {ex.Message}";
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