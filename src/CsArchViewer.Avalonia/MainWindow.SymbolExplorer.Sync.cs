using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CsArchViewer.Core.Models;
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
            ViewModel.ExplorerSymbolTitle = string.Empty;
            ViewModel.ExplorerTypeTitle = string.Empty;
            ViewModel.ExplorerSymbolDetailsText = string.Empty;
            ViewModel.ExplorerTypeMembersSummary = string.Empty;
            ViewModel.ExplorerMethodMetadataText = string.Empty;
            ViewModel.ExplorerTypeMethods.Clear();
            ViewModel.ExplorerTypeProperties.Clear();
        }

        ViewModel.Status = symbols.Count == 0
            ? $"No symbols found in {Path.GetFileName(node.FullPath)}."
            : $"Loaded {symbols.Count} symbols from {Path.GetFileName(node.FullPath)}.";
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
