using System.Collections.Concurrent;
using CsArchViewer.DotNet.Roslyn;
using CsArchViewer.DotNet.SymbolExplorer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CsArchViewer.DotNet.SymbolExplorer;

public sealed class SymbolIndexBuilder
{
    private readonly RoslynSolutionLoader _loader = new();
    private readonly object _lock = new();

    private Solution? _solution;
    private List<SymbolInfoModel> _symbols = [];

    public Solution? CurrentSolution
    {
        get
        {
            lock (_lock)
            {
                return _solution;
            }
        }
    }

    public IReadOnlyList<SymbolInfoModel> Symbols
    {
        get
        {
            lock (_lock)
            {
                return _symbols.ToList();
            }
        }
    }

    public async Task RebuildAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var solution = await _loader.LoadAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var list = new List<SymbolInfoModel>();
        if (solution is not null)
        {
            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var document in project.Documents)
                {
                    await IndexDocumentAsync(document, list, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        lock (_lock)
        {
            _solution = solution;
            _symbols = list;
        }
    }

    /// <summary>
    /// Removes symbols defined in the given files and re-indexes only those documents (incremental).
    /// </summary>
    public async Task UpdateFilesAsync(IReadOnlyCollection<string> absoluteFilePaths, CancellationToken cancellationToken = default)
    {
        if (absoluteFilePaths.Count == 0)
        {
            return;
        }

        List<SymbolInfoModel> working;
        Solution? solution;
        lock (_lock)
        {
            solution = _solution;
            working = _symbols.Where(s =>
                    !absoluteFilePaths.Any(p =>
                        PathsEqual(s.FilePath, p)))
                .ToList();
        }

        if (solution is null)
        {
            return;
        }

        var pathSet = new HashSet<string>(absoluteFilePaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Documents)
            {
                var fp = document.FilePath;
                if (string.IsNullOrEmpty(fp) || !pathSet.Contains(NormalizePath(fp)))
                {
                    continue;
                }

                await IndexDocumentAsync(document, working, cancellationToken).ConfigureAwait(false);
            }
        }

        lock (_lock)
        {
            if (_solution == solution)
            {
                _symbols = working;
            }
        }
    }

    private static async Task IndexDocumentAsync(
        Document document,
        List<SymbolInfoModel> target,
        CancellationToken cancellationToken)
    {
        var path = document.FilePath;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        SemanticModel? semantic = null;
        try
        {
            semantic = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fallback to syntax-only indexing when semantic model is unavailable.
        }

        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (semantic is null)
            {
                switch (node)
                {
                    case ClassDeclarationSyntax cd:
                        AddTypeFallback(cd.Identifier.Text, cd, ExplorerSymbolKind.Class, path, target);
                        break;
                    case RecordDeclarationSyntax rd:
                        AddTypeFallback(rd.Identifier.Text, rd, ExplorerSymbolKind.Record, path, target);
                        break;
                    case StructDeclarationSyntax sd:
                        AddTypeFallback(sd.Identifier.Text, sd, ExplorerSymbolKind.Struct, path, target);
                        break;
                    case InterfaceDeclarationSyntax id:
                        AddTypeFallback(id.Identifier.Text, id, ExplorerSymbolKind.Interface, path, target);
                        break;
                    case EnumDeclarationSyntax ed:
                        AddTypeFallback(ed.Identifier.Text, ed, ExplorerSymbolKind.Enum, path, target);
                        break;
                    case DelegateDeclarationSyntax dd:
                        AddTypeFallback(dd.Identifier.Text, dd, ExplorerSymbolKind.Delegate, path, target);
                        break;
                }

                continue;
            }

            switch (node)
            {
                case FileScopedNamespaceDeclarationSyntax fsNs:
                    AddNamespace(fsNs.Name.ToString(), fsNs, semantic, path, target);
                    break;
                case NamespaceDeclarationSyntax ns:
                    AddNamespace(ns.Name.ToString(), ns, semantic, path, target);
                    break;
                case ClassDeclarationSyntax cd:
                    AddType(cd, semantic, ExplorerSymbolKind.Class, path, target);
                    break;
                case RecordDeclarationSyntax rd:
                    AddType(rd, semantic, ExplorerSymbolKind.Record, path, target);
                    break;
                case StructDeclarationSyntax sd:
                    AddType(sd, semantic, ExplorerSymbolKind.Struct, path, target);
                    break;
                case InterfaceDeclarationSyntax id:
                    AddType(id, semantic, ExplorerSymbolKind.Interface, path, target);
                    break;
                case EnumDeclarationSyntax ed:
                    AddType(ed, semantic, ExplorerSymbolKind.Enum, path, target);
                    break;
                case DelegateDeclarationSyntax dd:
                    AddType(dd, semantic, ExplorerSymbolKind.Delegate, path, target);
                    break;
                case MethodDeclarationSyntax md:
                    AddMember(md, semantic, ExplorerSymbolKind.Method, md.Identifier.Text, path, target);
                    break;
                case ConstructorDeclarationSyntax ctor:
                    AddMember(ctor, semantic, ExplorerSymbolKind.Method, ctor.Identifier.Text, path, target);
                    break;
                case PropertyDeclarationSyntax pd:
                    AddMember(pd, semantic, ExplorerSymbolKind.Property, pd.Identifier.Text, path, target);
                    break;
                case EventDeclarationSyntax ev:
                    AddMember(ev, semantic, ExplorerSymbolKind.Event, ev.Identifier.Text, path, target);
                    break;
                case VariableDeclaratorSyntax vd when vd.Parent?.Parent is FieldDeclarationSyntax:
                    AddMember(vd, semantic, ExplorerSymbolKind.Field, vd.Identifier.Text, path, target);
                    break;
            }
        }
    }

    private static void AddNamespace(
        string nsText,
        SyntaxNode nsNode,
        SemanticModel semantic,
        string path,
        List<SymbolInfoModel> target)
    {
        var span = nsNode.Span;
        var sym = semantic.GetDeclaredSymbol(nsNode, CancellationToken.None) as INamespaceSymbol;
        var key = sym?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? nsText;
        target.Add(new SymbolInfoModel
        {
            SymbolKey = key,
            Kind = ExplorerSymbolKind.Namespace,
            Name = nsText,
            Namespace = nsText,
            DisplayName = nsText,
            ContainingTypeName = string.Empty,
            Accessibility = "namespace",
            FilePath = NormalizePath(path),
            LineNumber = GetLine(semantic.SyntaxTree, span),
            SpanStart = span.Start
        });
    }

    private static void AddType(
        SyntaxNode decl,
        SemanticModel semantic,
        ExplorerSymbolKind kind,
        string path,
        List<SymbolInfoModel> target)
    {
        var symbol = semantic.GetDeclaredSymbol(decl, CancellationToken.None) as INamedTypeSymbol;
        if (symbol is null)
        {
            return;
        }

        var span = decl.Span;
        target.Add(new SymbolInfoModel
        {
            SymbolKey = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Kind = kind,
            Name = symbol.Name,
            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ContainingTypeName = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            FilePath = NormalizePath(path),
            LineNumber = GetLine(semantic.SyntaxTree, span),
            SpanStart = span.Start
        });
    }

    private static void AddMember(
        SyntaxNode decl,
        SemanticModel semantic,
        ExplorerSymbolKind kind,
        string name,
        string path,
        List<SymbolInfoModel> target)
    {
        var symbol = semantic.GetDeclaredSymbol(decl, CancellationToken.None);
        if (symbol is null)
        {
            return;
        }

        var span = decl.Span;
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var containingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty;

        target.Add(new SymbolInfoModel
        {
            SymbolKey = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Kind = kind,
            Name = name,
            Namespace = ns,
            DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ContainingTypeName = containingType,
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            FilePath = NormalizePath(path),
            LineNumber = GetLine(semantic.SyntaxTree, span),
            SpanStart = span.Start
        });
    }

    private static int GetLine(SyntaxTree tree, TextSpan span)
    {
        var pos = tree.GetLineSpan(span);
        return pos.StartLinePosition.Line + 1;
    }

    private static void AddTypeFallback(
        string name,
        SyntaxNode decl,
        ExplorerSymbolKind kind,
        string path,
        List<SymbolInfoModel> target)
    {
        var span = decl.Span;
        var ns = GetNamespaceText(decl);
        var containingType = GetContainingTypeText(decl);
        var symbolKey = string.IsNullOrWhiteSpace(ns) ? name : $"{ns}.{name}";
        var displayName = string.IsNullOrWhiteSpace(containingType) ? name : $"{containingType}.{name}";
        var accessibility = GetAccessibilityText(decl);

        target.Add(new SymbolInfoModel
        {
            SymbolKey = symbolKey,
            Kind = kind,
            Name = name,
            Namespace = ns,
            DisplayName = displayName,
            ContainingTypeName = containingType,
            Accessibility = accessibility,
            FilePath = NormalizePath(path),
            LineNumber = GetLine(decl.SyntaxTree, span),
            SpanStart = span.Start
        });
    }

    private static string GetNamespaceText(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }
        }

        return string.Empty;
    }

    private static string GetContainingTypeText(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case TypeDeclarationSyntax td:
                    return td.Identifier.Text;
                case EnumDeclarationSyntax ed:
                    return ed.Identifier.Text;
            }
        }

        return string.Empty;
    }

    private static string GetAccessibilityText(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax btd => GetAccessibilityFromTokens(btd.Modifiers),
            DelegateDeclarationSyntax dd => GetAccessibilityFromTokens(dd.Modifiers),
            _ => "NotApplicable"
        };
    }

    private static string GetAccessibilityFromTokens(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
        {
            return "Public";
        }

        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
        {
            return "Private";
        }

        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword)))
        {
            return "Internal";
        }

        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword)))
        {
            return "Protected";
        }

        return "NotApplicable";
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
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
