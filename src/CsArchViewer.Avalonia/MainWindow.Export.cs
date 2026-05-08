using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow
{
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
}
