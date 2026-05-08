using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
using CsArchViewer.DotNet.SymbolExplorer;
using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.Avalonia;

public partial class MainWindow
{
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
}
