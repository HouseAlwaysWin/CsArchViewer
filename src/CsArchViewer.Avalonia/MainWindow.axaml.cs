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
using Avalonia.Threading;
using CsArchViewer.Analysis;
using CsArchViewer.Avalonia.ViewModels;
using CsArchViewer.Core.Models;
using CsArchViewer.Diagnostics;
using CsArchViewer.DotNet;
using CsArchViewer.Export;
using CsArchViewer.DotNet.SymbolExplorer;
using CsArchViewer.DotNet.SymbolExplorer.Models;
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
    private readonly AiPromptExporter _aiPromptExporter = new();
    private readonly MetricsJsonExporter _metricsJsonExporter = new();
    private readonly MetricsCsvExporter _metricsCsvExporter = new();
    private readonly MetricsMarkdownExporter _metricsMarkdownExporter = new();
    private readonly CodeMetricsAnalyzer _codeMetricsAnalyzer = new();
    private readonly SymbolIndexBuilder _symbolIndexBuilder = new();
    private readonly SymbolSearchService _symbolSearchService = new();
    private readonly ReferenceFinderService _referenceFinderService = new();
    private readonly TypeMethodAnalyzer _typeMethodAnalyzer = new();
    private readonly MethodMetadataAnalyzer _methodMetadataAnalyzer = new();
    private readonly SymbolNavigationService _symbolNavigationService = new();

    private MetricsSummary? _latestMetricsSummary;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        _incrementalEngine = new IncrementalAnalysisEngine(_analyzer);
        AttachGraphSelectionBridge();
        AttachAnalysisEvents();
        UpdateExportButtonState();
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
            _ = ExplorerAnalyzeGraphTypeAsync(ViewModel.Graph.SelectedNode);
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

                try
                {
                    if (changedFiles is null || changedFiles.Count == 0)
                    {
                        await _symbolIndexBuilder.RebuildAsync(rootPath, token).ConfigureAwait(false);
                    }
                    else
                    {
                        var csharpChanges = changedFiles
                            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (csharpChanges.Count > 0)
                        {
                            await _symbolIndexBuilder.UpdateFilesAsync(csharpChanges, token).ConfigureAwait(false);
                        }
                    }
                }
                catch
                {
                    // Symbol index is best-effort; analysis graphs remain usable.
                }

                Dispatcher.UIThread.Post(() =>
                {
                    ViewModel.SetAnalysisResult(update.Result);
                    ViewModel.SetMetricsSummary(metrics);
                    ViewModel.SetDiagnostics(diagnostics);
                    UpdateExportButtonState();
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

    private async void SymbolExplorerSearch_OnClick(object? sender, RoutedEventArgs e)
    {
        var query = ViewModel.SymbolExplorerSearchQuery?.Trim() ?? string.Empty;
        if (_symbolIndexBuilder.Symbols.Count == 0)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }

        try
        {
            var results = await _symbolSearchService.SearchAsync(_symbolIndexBuilder.Symbols, query, 250).ConfigureAwait(true);
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
        var solution = _symbolIndexBuilder.CurrentSolution;
        if (sym is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoSelection");
            return;
        }

        if (solution is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoIndex");
            return;
        }

        try
        {
            var (refs, _) = await _referenceFinderService.FindReferencesAsync(solution, sym, CancellationToken.None)
                .ConfigureAwait(true);
            ViewModel.ExplorerReferences.Clear();
            foreach (var r in refs)
            {
                ViewModel.ExplorerReferences.Add(r);
            }

            ViewModel.Status = $"References: {refs.Count}";
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

            Process.Start(new ProcessStartInfo
            {
                FileName = target.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("OpenFileFailedTemplate"), ex.Message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _fileChangeTracker.Dispose();
        _analysisScheduler.Dispose();
        base.OnClosed(e);
    }
}