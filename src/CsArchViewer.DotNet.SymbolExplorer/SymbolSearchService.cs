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
        var tokens = Tokenize(q);
        var scored = index
            .Select(s => new { Symbol = s, Score = ScoreMatch(s, q, tokens) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Symbol.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(x => x.Symbol)
            .ToList();

        return Task.FromResult<IReadOnlyList<SymbolInfoModel>>(scored);
    }

    private static int ScoreMatch(SymbolInfoModel symbol, string query, IReadOnlyList<string> queryTokens)
    {
        var qLower = query.ToLowerInvariant();
        var name = symbol.Name.ToLowerInvariant();
        var display = symbol.DisplayName.ToLowerInvariant();
        var ns = symbol.Namespace.ToLowerInvariant();
        var fq = symbol.SymbolKey.ToLowerInvariant();
        var containingType = symbol.ContainingTypeName.ToLowerInvariant();
        var searchable = string.Join(" ", new[] { name, display, ns, fq, containingType });

        if (!ContainsAllTokens(searchable, queryTokens))
        {
            return 0;
        }

        var kindBoost = GetKindBoost(symbol.Kind);

        if (name.Equals(qLower, StringComparison.Ordinal))
        {
            return 1000 + kindBoost;
        }

        if (display.Equals(qLower, StringComparison.Ordinal))
        {
            return 950 + kindBoost;
        }

        if (name.StartsWith(qLower, StringComparison.Ordinal))
        {
            return 800 - Math.Abs(name.Length - qLower.Length) + kindBoost;
        }

        if (display.Contains(qLower, StringComparison.Ordinal))
        {
            return 600 + kindBoost;
        }

        if (fq.Contains(qLower, StringComparison.Ordinal))
        {
            return 500 + kindBoost;
        }

        if (ns.Contains(qLower, StringComparison.Ordinal))
        {
            return 400 + kindBoost;
        }

        return FuzzySubstringScore(name, qLower) + kindBoost;
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

    private static IReadOnlyList<string> Tokenize(string query)
    {
        return query
            .Split(new[] { ' ', '.', ':', '/', '\\', '-', '_', '+', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsAllTokens(string searchable, IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (!searchable.Contains(token, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetKindBoost(ExplorerSymbolKind kind)
    {
        return kind switch
        {
            ExplorerSymbolKind.Class => 120,
            ExplorerSymbolKind.Record => 110,
            ExplorerSymbolKind.Struct => 100,
            ExplorerSymbolKind.Interface => 95,
            ExplorerSymbolKind.Enum => 90,
            ExplorerSymbolKind.Delegate => 85,
            ExplorerSymbolKind.Namespace => 70,
            ExplorerSymbolKind.Method => 40,
            ExplorerSymbolKind.Property => 30,
            ExplorerSymbolKind.Field => 20,
            ExplorerSymbolKind.Event => 15,
            _ => 0
        };
    }
}
