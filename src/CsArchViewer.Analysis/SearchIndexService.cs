using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class SearchIndexService
{
    private List<SearchEntry> _entries = [];

    public void BuildIndex(AnalysisResult result)
    {
        var entries = new List<SearchEntry>();

        entries.AddRange(result.Projects.Select(project => new SearchEntry(project.Name, project.CsProjPath, "Project")));

        foreach (var graph in result.Graphs.Values)
        {
            entries.AddRange(graph.Nodes.Select(node => new SearchEntry(node.Name, node.FullPath, node.Type.ToString())));
        }

        _entries = entries
            .DistinctBy(e => $"{e.Kind}:{e.Path}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<SearchEntry> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        query = query.Trim();
        return _entries
            .Select(entry => new { Entry = entry, Score = Score(entry, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int Score(SearchEntry entry, string query)
    {
        if (entry.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (IsFuzzyMatch(entry.Name, query))
        {
            return 40;
        }

        if (entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        return 0;
    }

    private static bool IsFuzzyMatch(string target, string query)
    {
        var ti = 0;
        var qi = 0;
        while (ti < target.Length && qi < query.Length)
        {
            if (char.ToUpperInvariant(target[ti]) == char.ToUpperInvariant(query[qi]))
            {
                qi++;
            }
            ti++;
        }

        return qi == query.Length;
    }
}

public sealed record SearchEntry(string Name, string Path, string Kind);
