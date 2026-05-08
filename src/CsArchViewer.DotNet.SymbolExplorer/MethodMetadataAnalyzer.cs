using CsArchViewer.DotNet.SymbolExplorer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class MethodMetadataAnalyzer
{
    public async Task<MethodInfoModel?> TryFromSymbolInfoAsync(
        Solution solution,
        SymbolInfoModel model,
        CancellationToken cancellationToken = default)
    {
        var symbol = await SymbolResolution.ResolveDeclaredSymbolAsync(solution, model, cancellationToken)
            .ConfigureAwait(false) as IMethodSymbol;

        if (symbol is null)
        {
            return null;
        }

        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        var tree = syntaxRef?.SyntaxTree;
        if (tree is null)
        {
            return null;
        }

        var doc = solution.GetDocument(tree);
        if (doc is null)
        {
            return null;
        }

        var semanticModel = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        return semanticModel is null ? null : Build(symbol, semanticModel, cancellationToken);
    }

    public MethodInfoModel Build(IMethodSymbol method, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var line = 1;
        var path = string.Empty;
        if (syntax is not null)
        {
            var tree = syntax.SyntaxTree;
            path = tree.FilePath ?? string.Empty;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // keep raw
            }

            line = tree.GetLineSpan(syntax.Span).StartLinePosition.Line + 1;
        }

        var parameters = string.Join(", ",
            method.Parameters.Select(p =>
                $"{FormatType(p.Type)} {p.Name}"));

        var usedTypes = CollectUsedTypes(method, semanticModel, syntax as CSharpSyntaxNode, cancellationToken);

        return new MethodInfoModel
        {
            Name = method.Name,
            ContainingType = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
            Namespace = method.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            Accessibility = method.DeclaredAccessibility.ToString(),
            ParameterCount = method.Parameters.Length,
            Parameters = parameters,
            ReturnType = method.ReturnsVoid ? "void" : FormatType(method.ReturnType),
            GenericParameterCount = method.TypeParameters.Length,
            IsAsync = method.IsAsync,
            IsStatic = method.IsStatic,
            IsVirtual = method.IsVirtual,
            IsOverride = method.IsOverride,
            IsAbstract = method.IsAbstract,
            FilePath = path,
            LineNumber = line,
            SymbolKey = method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Signature = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            UsedTypes = usedTypes
        };
    }

    private static IReadOnlyList<string> CollectUsedTypes(
        IMethodSymbol method,
        SemanticModel semanticModel,
        CSharpSyntaxNode? declarationSyntax,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in method.Parameters)
        {
            AddTypeSymbol(p.Type, set);
        }

        if (!method.ReturnsVoid)
        {
            AddTypeSymbol(method.ReturnType, set);
        }

        if (declarationSyntax is MethodDeclarationSyntax md && md.Body is not null)
        {
            CollectFromSyntax(md.Body, semanticModel, set, cancellationToken);
        }
        else if (declarationSyntax is AccessorDeclarationSyntax accessor && accessor.Body is not null)
        {
            CollectFromSyntax(accessor.Body, semanticModel, set, cancellationToken);
        }
        else if (declarationSyntax is ConstructorDeclarationSyntax ctor && ctor.Body is not null)
        {
            CollectFromSyntax(ctor.Body, semanticModel, set, cancellationToken);
        }

        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectFromSyntax(
        SyntaxNode body,
        SemanticModel semanticModel,
        HashSet<string> set,
        CancellationToken cancellationToken)
    {
        foreach (var node in body.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case IdentifierNameSyntax id:
                    AddFromSymbolInfo(semanticModel.GetSymbolInfo(id, cancellationToken), set);
                    break;
                case GenericNameSyntax gn:
                    AddFromSymbolInfo(semanticModel.GetSymbolInfo(gn, cancellationToken), set);
                    break;
            }
        }
    }

    private static void AddFromSymbolInfo(SymbolInfo info, HashSet<string> set)
    {
        var sym = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (sym is INamedTypeSymbol nt)
        {
            AddTypeSymbol(nt, set);
            return;
        }

        if (sym?.ContainingType is INamedTypeSymbol ct)
        {
            AddTypeSymbol(ct, set);
        }
    }

    private static void AddTypeSymbol(ITypeSymbol type, HashSet<string> set)
    {
        if (type is IArrayTypeSymbol arr)
        {
            AddTypeSymbol(arr.ElementType, set);
            return;
        }

        if (type is IPointerTypeSymbol ptr)
        {
            AddTypeSymbol(ptr.PointedAtType, set);
            return;
        }

        if (type.SpecialType != SpecialType.None ||
            type is ITypeParameterSymbol)
        {
            return;
        }

        if (type is INamedTypeSymbol named &&
            type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate)
        {
            set.Add(named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
    }

    private static string FormatType(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
