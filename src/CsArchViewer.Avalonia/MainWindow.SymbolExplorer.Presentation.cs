using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow
{
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
            await Dispatcher.UIThread.InvokeAsync(() => ViewModel.ExplorerMethodMetadataText = ex.Message);
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

    private static string FormatMethodDetails(MethodInfoModel method)
    {
        var sb = new StringBuilder();
        sb.AppendLine(method.Signature);
        sb.AppendLine($"Return: {method.ReturnType}");
        sb.AppendLine($"Parameters ({method.ParameterCount}): {method.Parameters}");
        sb.AppendLine($"Async={method.IsAsync}, Static={method.IsStatic}, Virtual={method.IsVirtual}, Override={method.IsOverride}, Abstract={method.IsAbstract}");
        sb.AppendLine($"Generics: {method.GenericParameterCount}");
        if (method.UsedTypes.Count > 0)
        {
            sb.AppendLine("Used types (lightweight):");
            foreach (var usedType in method.UsedTypes.Take(40))
            {
                sb.AppendLine($"  · {usedType}");
            }

            if (method.UsedTypes.Count > 40)
            {
                sb.AppendLine($"  … ({method.UsedTypes.Count - 40} more)");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void ApplyTypeAnalysis(TypeInfoModel? typeModel)
    {
        ViewModel.ExplorerTypeMethods.Clear();
        if (typeModel is null)
        {
            ViewModel.ExplorerTypeMembersSummary = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(typeModel.FullName);
        sb.AppendLine($"Kind: {typeModel.Kind}");
        sb.AppendLine($"{ViewModel.BaseTypeText}: {(string.IsNullOrWhiteSpace(typeModel.BaseType) ? "-" : typeModel.BaseType)}");
        if (typeModel.Interfaces.Count > 0)
        {
            sb.AppendLine($"{ViewModel.ImplementedInterfacesText}:");
            foreach (var implemented in typeModel.Interfaces.Take(15))
            {
                sb.AppendLine($"  · {implemented}");
            }

            if (typeModel.Interfaces.Count > 15)
            {
                sb.AppendLine($"  … ({typeModel.Interfaces.Count - 15} more)");
            }
        }

        sb.AppendLine($"{ViewModel.FileText}: {typeModel.FilePath} ({typeModel.LineNumber})");

        if (typeModel.Properties.Count > 0)
        {
            sb.AppendLine($"Properties ({typeModel.Properties.Count}): " +
                          string.Join(", ", typeModel.Properties.Take(12)) +
                          (typeModel.Properties.Count > 12 ? "…" : string.Empty));
        }

        if (typeModel.Fields.Count > 0)
        {
            sb.AppendLine($"Fields ({typeModel.Fields.Count}): " +
                          string.Join(", ", typeModel.Fields.Take(12)) +
                          (typeModel.Fields.Count > 12 ? "…" : string.Empty));
        }

        if (typeModel.Events.Count > 0)
        {
            sb.AppendLine($"Events ({typeModel.Events.Count}): " + string.Join(", ", typeModel.Events.Take(12)));
        }

        ViewModel.ExplorerTypeMembersSummary = sb.ToString().TrimEnd();
        foreach (var method in typeModel.Methods)
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
}
