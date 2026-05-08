using CsArchViewer.DotNet.SymbolExplorer.Models;
using Microsoft.CodeAnalysis;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class TypeMethodAnalyzer
{
    private readonly MethodMetadataAnalyzer _methods = new();

    public async Task<TypeInfoModel?> AnalyzeTypeAsync(
        Solution solution,
        SymbolInfoModel typeSymbolEntry,
        CancellationToken cancellationToken)
    {
        if (typeSymbolEntry.Kind is not (
            ExplorerSymbolKind.Class or
            ExplorerSymbolKind.Interface or
            ExplorerSymbolKind.Struct or
            ExplorerSymbolKind.Enum or
            ExplorerSymbolKind.Record or
            ExplorerSymbolKind.Delegate))
        {
            return null;
        }

        var resolved = await SymbolResolution.ResolveDeclaredSymbolAsync(solution, typeSymbolEntry, cancellationToken)
            .ConfigureAwait(false) as INamedTypeSymbol;

        if (resolved is null)
        {
            return null;
        }

        return await BuildTypeModelAsync(resolved, solution, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TypeInfoModel?> AnalyzeTypeByKeyAsync(
        Solution solution,
        string fullyQualifiedKey,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (var type in compilation.GlobalNamespace.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = FindNamedType(type, fullyQualifiedKey);
                if (match is not null)
                {
                    return await BuildTypeModelAsync(match, solution, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var ns in compilation.GlobalNamespace.GetNamespaceMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var type in CollectTypes(ns))
                {
                    if (string.Equals(
                            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            fullyQualifiedKey,
                            StringComparison.Ordinal))
                    {
                        return await BuildTypeModelAsync(type, solution, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        return null;
    }

    private async Task<TypeInfoModel> BuildTypeModelAsync(
        INamedTypeSymbol resolved,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var syntax = resolved.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var line = 1;
        var path = string.Empty;
        SemanticModel? semantic = null;

        if (syntax is not null)
        {
            var doc = solution.GetDocument(syntax.SyntaxTree);
            var tree = syntax.SyntaxTree;
            path = tree.FilePath ?? string.Empty;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // ignore
            }

            line = tree.GetLineSpan(syntax.Span).StartLinePosition.Line + 1;
            if (doc is not null)
            {
                semantic = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var methods = new List<MethodInfoModel>();
        var properties = new List<string>();
        var fields = new List<string>();
        var events = new List<string>();

        foreach (var member in resolved.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } m:
                    if (semantic is not null)
                    {
                        methods.Add(_methods.Build(m, semantic, cancellationToken));
                    }

                    break;
                case IPropertySymbol p:
                    properties.Add(p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    break;
                case IFieldSymbol f when !f.IsImplicitlyDeclared:
                    fields.Add(f.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    break;
                case IEventSymbol e:
                    events.Add(e.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    break;
            }
        }

        var interfaces = resolved.Interfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseType = resolved.BaseType is { SpecialType: not SpecialType.System_Object }
            ? resolved.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : string.Empty;

        return new TypeInfoModel
        {
            FullName = resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = resolved.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            Kind = MapKind(resolved),
            Accessibility = resolved.DeclaredAccessibility.ToString(),
            FilePath = path,
            LineNumber = line,
            SymbolKey = resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BaseType = baseType,
            Interfaces = interfaces,
            Methods = methods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Properties = properties.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            Fields = fields.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            Events = events.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static INamedTypeSymbol? FindNamedType(INamedTypeSymbol root, string fullyQualifiedKey)
    {
        if (string.Equals(
                root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                fullyQualifiedKey,
                StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var nested in root.GetTypeMembers())
        {
            var found = FindNamedType(nested, fullyQualifiedKey);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static ExplorerSymbolKind MapKind(INamedTypeSymbol t)
    {
        return t.TypeKind switch
        {
            TypeKind.Interface => ExplorerSymbolKind.Interface,
            TypeKind.Struct => ExplorerSymbolKind.Struct,
            TypeKind.Enum => ExplorerSymbolKind.Enum,
            TypeKind.Delegate => ExplorerSymbolKind.Delegate,
            TypeKind.Class when t.IsRecord => ExplorerSymbolKind.Record,
            TypeKind.Class => ExplorerSymbolKind.Class,
            _ => ExplorerSymbolKind.Unknown
        };
    }

    private static IEnumerable<INamedTypeSymbol> CollectTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in CollectNested(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in CollectTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> CollectNested(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deep in CollectNested(nested))
            {
                yield return deep;
            }
        }
    }
}
