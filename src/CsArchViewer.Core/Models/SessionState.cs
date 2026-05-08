namespace CsArchViewer.Core.Models;

public sealed class SessionState
{
    public string? RootPath { get; init; }
    public GraphType SelectedGraphType { get; init; } = GraphType.ProjectDependencies;
    public GraphGroupingMode SelectedGroupingMode { get; init; } = GraphGroupingMode.None;
    public string SearchText { get; init; } = string.Empty;
    public string SymbolSearchText { get; init; } = string.Empty;
    public string SelectedTypeFilter { get; init; } = "All";
    public string SelectedMetricsFilter { get; init; } = "All";
    public string SelectedOverlayMode { get; init; } = "None";
    public string SelectedDiagnosticsSeverityFilter { get; init; } = "All";
    public List<string> RecentSearches { get; init; } = [];
    public List<string> RecentSymbolSearches { get; init; } = [];
    public Dictionary<string, Dictionary<string, NodeLayoutState>> GraphLayouts { get; init; } = [];
}
