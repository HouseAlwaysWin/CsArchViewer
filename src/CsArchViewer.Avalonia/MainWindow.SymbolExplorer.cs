using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CsArchViewer.Core.Models;
using CsArchViewer.DotNet.SymbolExplorer;
using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow
{
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

        foreach (var symbol in symbols)
        {
            if (!IsExplorerTypeSymbolKind(symbol.Kind))
            {
                continue;
            }

            if (!string.Equals(symbol.Name, node.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(symbol.Namespace, ns, StringComparison.OrdinalIgnoreCase))
            {
                return symbol;
            }
        }

        node.Metadata.TryGetValue("FullTypeName", out var fq);
        fq ??= node.Id;
        foreach (var symbol in symbols)
        {
            if (!IsExplorerTypeSymbolKind(symbol.Kind))
            {
                continue;
            }

            if (string.Equals(symbol.SymbolKey, fq, StringComparison.OrdinalIgnoreCase))
            {
                return symbol;
            }

            if (symbol.SymbolKey.EndsWith("." + node.Name, StringComparison.OrdinalIgnoreCase) &&
                fq.Contains(node.Name, StringComparison.OrdinalIgnoreCase))
            {
                return symbol;
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

    private async void SymbolExplorerFindRefs_OnClick(object? sender, RoutedEventArgs e)
    {
        var symbol = ViewModel.SelectedExplorerSymbol;
        if (symbol is null)
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

        if (!IsReferenceQueryableSymbolKind(symbol.Kind))
        {
            ViewModel.ExplorerReferences.Clear();
            ViewModel.SelectedExplorerReference = null;
            ViewModel.Status = $"'{symbol.Kind}' symbols do not support reference lookup. Please select a type/member symbol.";
            return;
        }

        try
        {
            var (references, _) = await _referenceFinderService.FindReferencesAsync(solution, symbol, CancellationToken.None)
                .ConfigureAwait(true);
            ViewModel.ExplorerReferences.Clear();
            ViewModel.SelectedExplorerReference = null;
            foreach (var reference in references)
            {
                ViewModel.ExplorerReferences.Add(reference);
            }

            ViewModel.Status = references.Count == 0
                ? $"No references found for '{symbol.DisplayName}'."
                : $"References: {references.Count}";
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

        var symbol = ViewModel.SelectedExplorerSymbol;
        if (symbol is null)
        {
            ViewModel.Status = ViewModel.L("SymbolExplorerNoSelection");
            return;
        }

        OpenExplorerTarget(_symbolNavigationService.JumpToDefinition(symbol));
    }

    private void ExplorerReferencesOpen_OnClick(object? sender, RoutedEventArgs e)
    {
        var reference = ViewModel.SelectedExplorerReference;
        if (reference is null)
        {
            ViewModel.Status = "No reference selected.";
            return;
        }

        OpenExplorerTarget(_symbolNavigationService.JumpToReference(reference));
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
            }
        }

        return false;
    }

    private static bool PathsEqual(string leftPath, string rightPath)
    {
        try
        {
            return string.Equals(Path.GetFullPath(leftPath), Path.GetFullPath(rightPath), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
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
}
