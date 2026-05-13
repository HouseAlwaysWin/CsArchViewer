using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsArchViewer.DotNet.SymbolAnalysis;

public sealed class RoslynTypeAnalyzer
{
    private static readonly SymbolDisplayFormat TypeReferenceFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public async Task<IReadOnlyList<TypeDescriptor>> AnalyzeAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TypeDescriptor>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.SourceCodeKind != SourceCodeKind.Regular)
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Cast<SyntaxNode>();

                foreach (var declaration in typeDeclarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
                    if (symbol is null || string.IsNullOrWhiteSpace(symbol.ContainingNamespace?.ToDisplayString()))
                    {
                        continue;
                    }

                    var typeId = symbol.ToDisplayString(TypeReferenceFormat);
                    var typeKind = MapKind(symbol);
                    if (!results.TryGetValue(typeId, out var descriptor))
                    {
                        descriptor = new TypeDescriptor
                        {
                            Id = typeId,
                            Name = symbol.Name,
                            FullName = typeId,
                            Namespace = symbol.ContainingNamespace.ToDisplayString(),
                            FilePath = document.FilePath ?? string.Empty,
                            Kind = typeKind
                        };
                        results[typeId] = descriptor;
                    }

                    descriptor.BaseTypes.UnionWith(CollectBaseTypes(symbol));
                    descriptor.Interfaces.UnionWith(symbol.Interfaces.Select(i => i.ToDisplayString(TypeReferenceFormat)));
                    descriptor.ReferencedTypes.UnionWith(CollectReferencedTypes(symbol));
                    descriptor.Attributes.UnionWith(symbol.GetAttributes().Select(a => a.AttributeClass?.ToDisplayString(TypeReferenceFormat)).Where(v => !string.IsNullOrWhiteSpace(v))!);
                }
            }
        }

        return results.Values.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ArchitectureTypeKind MapKind(INamedTypeSymbol symbol)
    {
        return symbol.TypeKind switch
        {
            TypeKind.Interface => ArchitectureTypeKind.Interface,
            TypeKind.Struct => ArchitectureTypeKind.Struct,
            TypeKind.Enum => ArchitectureTypeKind.Enum,
            _ when symbol.IsRecord => ArchitectureTypeKind.Record,
            _ => ArchitectureTypeKind.Type
        };
    }

    private static IEnumerable<string> CollectBaseTypes(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        if (baseType is null || baseType.SpecialType == SpecialType.System_Object)
        {
            yield break;
        }

        yield return baseType.ToDisplayString(TypeReferenceFormat);
    }

    private static IEnumerable<string> CollectReferencedTypes(INamedTypeSymbol symbol)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                    AddType(field.Type, set);
                    break;
                case IPropertySymbol property:
                    AddType(property.Type, set);
                    break;
                case IMethodSymbol method:
                    AddType(method.ReturnType, set);
                    foreach (var parameter in method.Parameters)
                    {
                        AddType(parameter.Type, set);
                    }

                    foreach (var attribute in method.GetAttributes())
                    {
                        AddType(attribute.AttributeClass, set);
                    }
                    break;
            }
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            AddType(attribute.AttributeClass, set);
        }

        return set;
    }

    private static void AddType(ITypeSymbol? symbol, ISet<string> set)
    {
        if (symbol is null)
        {
            return;
        }

        var display = symbol.ToDisplayString(TypeReferenceFormat);
        if (string.IsNullOrWhiteSpace(display))
        {
            return;
        }

        set.Add(display);

        if (symbol is INamedTypeSymbol named && named.IsGenericType)
        {
            foreach (var typeArg in named.TypeArguments)
            {
                AddType(typeArg, set);
            }
        }
    }
}

public sealed class TypeDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Namespace { get; init; }
    public required string FilePath { get; init; }
    public required ArchitectureTypeKind Kind { get; init; }
    public HashSet<string> BaseTypes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Interfaces { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ReferencedTypes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Attributes { get; } = new(StringComparer.Ordinal);
}

public enum ArchitectureTypeKind
{
    Type,
    Interface,
    Struct,
    Enum,
    Record
}
