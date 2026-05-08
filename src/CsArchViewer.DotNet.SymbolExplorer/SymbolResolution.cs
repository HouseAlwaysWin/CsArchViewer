using CsArchViewer.DotNet.SymbolExplorer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CsArchViewer.DotNet.SymbolExplorer;

internal static class SymbolResolution
{
    public static async Task<ISymbol?> ResolveDeclaredSymbolAsync(
        Solution solution,
        SymbolInfoModel model,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!PathsEqual(document.FilePath, model.FilePath))
                {
                    continue;
                }

                var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                var semantic = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (tree is null || semantic is null)
                {
                    continue;
                }

                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                var spanLen = Math.Max(1, Math.Min(8, root.Span.Length - model.SpanStart));
                if (model.SpanStart < 0 || model.SpanStart >= root.Span.Length)
                {
                    continue;
                }

                var node = root.FindNode(new TextSpan(model.SpanStart, spanLen), findInsideTrivia: false, getInnermostNodeForTie: true);
                var declared = semantic.GetDeclaredSymbol(node, cancellationToken)
                               ?? WalkUpForDeclared(semantic, node, cancellationToken);

                if (declared is not null &&
                    string.Equals(
                        declared.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        model.SymbolKey,
                        StringComparison.Ordinal))
                {
                    return declared;
                }
            }
        }

        return null;
    }

    private static ISymbol? WalkUpForDeclared(SemanticModel semantic, SyntaxNode? node, CancellationToken cancellationToken)
    {
        while (node is not null)
        {
            var sym = semantic.GetDeclaredSymbol(node, cancellationToken);
            if (sym is not null)
            {
                return sym;
            }

            node = node.Parent;
        }

        return null;
    }

    private static bool PathsEqual(string? a, string b)
    {
        if (string.IsNullOrWhiteSpace(a))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
