using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow
{
    private void DiagnosticsSplitter_OnDoubleTapped(object? sender, TappedEventArgs e)
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
        var currentHeight = diagnosticsContentRow.Height;
        var isCollapsed = currentHeight.IsAbsolute && currentHeight.Value <= 1;
        if (isCollapsed)
        {
            diagnosticsContentRow.Height = _lastDiagnosticsHeight;
            return;
        }

        if (currentHeight.IsAbsolute && currentHeight.Value > 1)
        {
            _lastDiagnosticsHeight = currentHeight;
        }

        diagnosticsContentRow.Height = new GridLength(0);
    }

    private void DependencyPathShortest_OnClick(object? sender, RoutedEventArgs e)
    {
        var graph = ViewModel.GetCurrentGraph();
        var source = ViewModel.Graph.SelectedNode;
        if (graph is null || source is null)
        {
            ViewModel.Status = "Select a source node first.";
            return;
        }

        var target = ResolveNodeByQuery(graph, ViewModel.DependencyPathTargetQuery);
        if (target is null)
        {
            ViewModel.Status = $"Target node not found: {ViewModel.DependencyPathTargetQuery}";
            return;
        }

        var result = _dependencyPathExplorerService.FindShortestPath(graph, source.Id, target.Id);
        ViewModel.PresentDependencyPathResult(result);
        ViewModel.Status = result.Summary;
        _appLogService.Info("DependencyPath", $"{source.Name} -> {target.Name}: {result.Summary}");
        SchedulePersistWorkspaceState();
    }

    private void DependencyPathCycle_OnClick(object? sender, RoutedEventArgs e)
    {
        var graph = ViewModel.GetCurrentGraph();
        var source = ViewModel.Graph.SelectedNode;
        if (graph is null || source is null)
        {
            ViewModel.Status = "Select a source node first.";
            return;
        }

        var result = _dependencyPathExplorerService.FindCycle(graph, source.Id);
        ViewModel.PresentDependencyPathResult(result);
        ViewModel.Status = result.Summary;
        _appLogService.Info("DependencyPath", $"{source.Name}: {result.Summary}");
        SchedulePersistWorkspaceState();
    }

    private async void ExportDiagnostics_OnClick(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            ViewModel.Status = "Diagnostics export unavailable: storage provider unavailable.";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagnostics",
            SuggestedFileName = $"csarchviewer-diagnostics-{DateTime.Now:yyyyMMddHHmmss}.csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });

        if (file is null)
        {
            ViewModel.Status = ViewModel.L("ExportCanceled");
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(BuildDiagnosticsCsv()).ConfigureAwait(false);
            ViewModel.Status = $"Exported diagnostics: {file.Name}";
            _appLogService.Info("Diagnostics", $"Diagnostics exported to {file.Name}");
        }
        catch (Exception ex)
        {
            ViewModel.Status = string.Format(ViewModel.L("ExportFailedTemplate"), ex.Message);
            _appLogService.Error("Diagnostics", ex.Message);
        }
    }

    private string BuildDiagnosticsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Severity,Type,Source,Target,Message");
        foreach (var diagnostic in ViewModel.FilteredDiagnostics)
        {
            sb.Append(EscapeCsv(diagnostic.Severity.ToString()));
            sb.Append(',');
            sb.Append(EscapeCsv(diagnostic.Type));
            sb.Append(',');
            sb.Append(EscapeCsv(diagnostic.Source));
            sb.Append(',');
            sb.Append(EscapeCsv(diagnostic.Target));
            sb.Append(',');
            sb.Append(EscapeCsv(diagnostic.Message));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        var normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static ArchitectureNode? ResolveNodeByQuery(ArchitectureGraph graph, string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return graph.Nodes.FirstOrDefault(node =>
                   string.Equals(node.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(node.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(node.FullPath, normalized, StringComparison.OrdinalIgnoreCase))
               ?? graph.Nodes.FirstOrDefault(node =>
                   node.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                   node.FullPath.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                   node.Id.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
