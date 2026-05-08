using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class SymbolSearchService
{
    public Task<IReadOnlyList<SymbolInfoModel>> SearchAsync(
        IReadOnlyList<SymbolInfoModel> index,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SymbolInfoModel>>([]);
        }

        var q = query.Trim();
        var scored = index
            .Select(s => new { Symbol = s, Score = ScoreMatch(s, q) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Symbol.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(x => x.Symbol)
            .ToList();

        return Task.FromResult<IReadOnlyList<SymbolInfoModel>>(scored);
    }

    private static int ScoreMatch(SymbolInfoModel symbol, string query)
    {
        var qLower = query.ToLowerInvariant();
        var name = symbol.Name.ToLowerInvariant();
        var display = symbol.DisplayName.ToLowerInvariant();
        var ns = symbol.Namespace.ToLowerInvariant();
        var fq = symbol.SymbolKey.ToLowerInvariant();

        if (name.Equals(qLower, StringComparison.Ordinal))
        {
            return 1000;
        }

        if (display.Equals(qLower, StringComparison.Ordinal))
        {
            return 950;
        }

        if (name.StartsWith(qLower, StringComparison.Ordinal))
        {
            return 800 - Math.Abs(name.Length - qLower.Length);
        }

        if (display.Contains(qLower, StringComparison.Ordinal))
        {
            return 600;
        }

        if (fq.Contains(qLower, StringComparison.Ordinal))
        {
            return 500;
        }

        if (ns.Contains(qLower, StringComparison.Ordinal))
        {
            return 400;
        }

        return FuzzySubstringScore(name, qLower);
    }

    /// <summary>Very small fuzzy score: all query chars in order in name.</summary>
    private static int FuzzySubstringScore(string source, string query)
    {
        if (query.Length == 0 || source.Length == 0)
        {
            return 0;
        }

        var qi = 0;
        for (var si = 0; si < source.Length && qi < query.Length; si++)
        {
            if (source[si] == query[qi])
            {
                qi++;
            }
        }

        return qi == query.Length ? 90 - query.Length : 0;
    }
}
