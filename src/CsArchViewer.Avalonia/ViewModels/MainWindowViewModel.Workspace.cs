using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public WorkspaceState ExportWorkspaceState()
    {
        CaptureGraphLayout(SelectedGraphType, SelectedGroupingMode, SelectedGraphLayout);

        return new WorkspaceState
        {
            Settings = new AppSettings
            {
                Theme = SelectedTheme,
                GraphLayout = SelectedGraphLayout,
                OverlayMode = SelectedOverlayMode,
                LanguageCode = SelectedLanguage == "English" ? "en-US" : "zh-TW",
                ShowLineCountOnNodes = ShowLineCountOnNodes,
                RestoreLastSession = RestoreLastSession,
                AutoSaveSession = AutoSaveSession
            },
            Session = new SessionState
            {
                RootPath = CurrentRootPath,
                SelectedGraphType = SelectedGraphType,
                SelectedGroupingMode = SelectedGroupingMode,
                SearchText = SearchText,
                SymbolSearchText = SymbolExplorerSearchQuery,
                SelectedTypeFilter = SelectedTypeFilter,
                SelectedMetricsFilter = SelectedMetricsFilter,
                SelectedOverlayMode = SelectedOverlayMode,
                SelectedDiagnosticsSeverityFilter = SelectedDiagnosticsSeverityFilter,
                RecentSearches = RecentSearches.ToList(),
                RecentSymbolSearches = RecentSymbolSearches.ToList(),
                GraphLayouts = CloneGraphLayouts()
            }
        };
    }

    public void ApplyWorkspaceState(WorkspaceState state)
    {
        var settings = state.Settings;
        var session = state.Session;

        SelectedTheme = ResolveOption(settings.Theme, ThemeOptions, "Dark");
        SelectedGraphLayout = ResolveOption(settings.GraphLayout, GraphLayoutOptions, "Auto");
        SelectedOverlayMode = ResolveOption(settings.OverlayMode, OverlayModeOptions, "None");
        ShowLineCountOnNodes = settings.ShowLineCountOnNodes;
        RestoreLastSession = settings.RestoreLastSession;
        AutoSaveSession = settings.AutoSaveSession;
        SelectedLanguage = string.Equals(settings.LanguageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "English"
            : "繁體中文";

        CopyGraphLayoutsFromSession(session.GraphLayouts);
        ReplaceCollection(RecentSearches, session.RecentSearches);
        ReplaceCollection(RecentSymbolSearches, session.RecentSymbolSearches);

        CurrentRootPath = session.RootPath;
        SelectedTypeFilter = ResolveOption(session.SelectedTypeFilter, TypeFilterOptions, "All");
        SelectedMetricsFilter = ResolveOption(session.SelectedMetricsFilter, MetricsFilterOptions, "All");
        SelectedDiagnosticsSeverityFilter = ResolveOption(session.SelectedDiagnosticsSeverityFilter, DiagnosticsSeverityOptions, "All");
        SearchText = session.SearchText ?? string.Empty;
        SymbolExplorerSearchQuery = session.SymbolSearchText ?? string.Empty;
        SelectedGroupingMode = session.SelectedGroupingMode;
        SelectedGraphType = session.SelectedGraphType;
    }

    private void CopyGraphLayoutsFromSession(Dictionary<string, Dictionary<string, NodeLayoutState>> graphLayouts)
    {
        _persistedGraphLayouts.Clear();
        foreach (var graphLayout in graphLayouts)
        {
            _persistedGraphLayouts[graphLayout.Key] = graphLayout.Value.ToDictionary(
                pair => pair.Key,
                pair => new NodeLayoutState
                {
                    X = pair.Value.X,
                    Y = pair.Value.Y
                },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private Dictionary<string, Dictionary<string, NodeLayoutState>> CloneGraphLayouts()
    {
        return _persistedGraphLayouts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToDictionary(
                inner => inner.Key,
                inner => new NodeLayoutState
                {
                    X = inner.Value.X,
                    Y = inner.Value.Y
                },
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T>? values)
    {
        collection.Clear();
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static string ResolveOption(string? value, IReadOnlyList<string> available, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            available.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return available.First(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        }

        return fallback;
    }
}
