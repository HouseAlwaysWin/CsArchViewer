using CsArchViewer.DotNet.SymbolExplorer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class ReferenceFinderService
{
    public async Task<(IReadOnlyList<ReferenceInfoModel> References, int Count)> FindReferencesAsync(
        Solution solution,
        SymbolInfoModel target,
        CancellationToken cancellationToken = default)
    {
        var symbol = await SymbolResolution.ResolveDeclaredSymbolAsync(solution, target, cancellationToken)
            .ConfigureAwait(false);

        if (symbol is null)
        {
            return ([], 0);
        }

        var results = new List<ReferenceInfoModel>();
        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);

        foreach (var referencedSymbol in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var loc in referencedSymbol.Locations)
            {
                var doc = loc.Document;
                if (string.IsNullOrWhiteSpace(doc.FilePath))
                {
                    continue;
                }

                var tree = await doc.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                if (tree is null)
                {
                    continue;
                }

                var roslynLoc = loc.Location;
                if (!roslynLoc.IsInSource)
                {
                    continue;
                }

                var span = roslynLoc.SourceSpan;
                var lineSpan = roslynLoc.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;
                var col = lineSpan.StartLinePosition.Character + 1;
                var snippet = GetSnippet(tree, span);
                var context = await GetReferenceContextAsync(solution, tree, span, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new ReferenceInfoModel
                {
                    ReferencingSymbolDisplay = context,
                    FilePath = NormalizePath(doc.FilePath ?? string.Empty),
                    LineNumber = line,
                    Column = col,
                    ContextSnippet = snippet
                });
            }
        }

        var ordered = results
            .OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LineNumber)
            .ToList();

        return (ordered, ordered.Count);
    }

    private static async Task<string> GetReferenceContextAsync(
        Solution solution,
        SyntaxTree tree,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        var doc = solution.GetDocument(tree);
        if (doc is null)
        {
            return string.Empty;
        }

        var model = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return string.Empty;
        }

        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var node = root.FindNode(span, findInsideTrivia: true, getInnermostNodeForTie: true);
        var enclosing = model.GetEnclosingSymbol(span.Start);
        return enclosing?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? node?.ToString()?.Trim() ?? string.Empty;
    }

    private static string GetSnippet(SyntaxTree tree, TextSpan span)
    {
        try
        {
            var text = tree.GetText();
            var line = text.Lines.GetLineFromPosition(span.Start);
            return line.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
