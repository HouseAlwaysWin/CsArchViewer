using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CsArchViewer.Metrics;

public sealed class FileMetricsAnalyzer
{
    public IReadOnlyList<FileMetrics> Analyze(
        string rootPath,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyDictionary<string, FileMetrics> existingMetrics,
        IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var targetFiles = changedFiles is { Count: > 0 }
            ? changedFiles.Where(IsCSharpFile).ToList()
            : Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories).ToList();

        var merged = new Dictionary<string, FileMetrics>(existingMetrics, StringComparer.OrdinalIgnoreCase);
        var (outgoing, incoming) = BuildDependencyLookups(graphs);

        foreach (var filePath in targetFiles)
        {
            if (!File.Exists(filePath))
            {
                merged.Remove(filePath);
                continue;
            }

            merged[filePath] = AnalyzeSingleFile(filePath, outgoing, incoming);
        }

        if (changedFiles is { Count: > 0 })
        {
            var deleted = changedFiles.Where(path => !File.Exists(path)).ToList();
            foreach (var path in deleted)
            {
                merged.Remove(path);
            }
        }

        return merged.Values
            .OrderByDescending(x => x.TotalLines)
            .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FileMetrics AnalyzeSingleFile(
        string filePath,
        IReadOnlyDictionary<string, int> outgoing,
        IReadOnlyDictionary<string, int> incoming)
    {
        var text = File.ReadAllText(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(text, path: filePath);
        var root = syntaxTree.GetRoot();
        var sourceText = syntaxTree.GetText();

        var totalLines = sourceText.Lines.Count;
        var lineKinds = new LineKind[totalLines];

        // Mark blank lines first.
        for (var i = 0; i < totalLines; i++)
        {
            lineKinds[i] = string.IsNullOrWhiteSpace(sourceText.Lines[i].ToString())
                ? LineKind.Blank
                : LineKind.Comment;
        }

        // Mark comment spans.
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!IsCommentTrivia(trivia.Kind()))
            {
                continue;
            }

            MarkSpan(sourceText, trivia.Span, lineKinds, LineKind.Comment);
        }

        // Mark code spans by tokens (non-whitespace/comment trivia excluded).
        foreach (var token in root.DescendantTokens())
        {
            MarkSpan(sourceText, token.Span, lineKinds, LineKind.Code);
        }

        var codeLines = lineKinds.Count(kind => kind == LineKind.Code);
        var blankLines = lineKinds.Count(kind => kind == LineKind.Blank);
        var commentLines = totalLines - codeLines - blankLines;

        var fileInfo = new FileInfo(filePath);
        return new FileMetrics
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            TotalLines = totalLines,
            CodeLines = codeLines,
            CommentLines = Math.Max(0, commentLines),
            BlankLines = blankLines,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue,
            DependencyCount = outgoing.TryGetValue(filePath, out var outCount) ? outCount : 0,
            ReferencedByCount = incoming.TryGetValue(filePath, out var inCount) ? inCount : 0
        };
    }

    private static (Dictionary<string, int> outgoing, Dictionary<string, int> incoming)
        BuildDependencyLookups(IReadOnlyDictionary<GraphType, ArchitectureGraph> graphs)
    {
        var outgoing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!graphs.TryGetValue(GraphType.FileDependencies, out var graph))
        {
            return (outgoing, incoming);
        }

        foreach (var edge in graph.Edges.Where(x => x.Type == ArchitectureEdgeType.UsesFile))
        {
            outgoing[edge.FromNodeId] = outgoing.TryGetValue(edge.FromNodeId, out var outCount) ? outCount + 1 : 1;
            incoming[edge.ToNodeId] = incoming.TryGetValue(edge.ToNodeId, out var inCount) ? inCount + 1 : 1;
        }

        return (outgoing, incoming);
    }

    private static bool IsCommentTrivia(SyntaxKind kind)
    {
        return kind is SyntaxKind.SingleLineCommentTrivia
            or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia
            or SyntaxKind.MultiLineDocumentationCommentTrivia;
    }

    private static void MarkSpan(SourceText sourceText, TextSpan span, IList<LineKind> lineKinds, LineKind kind)
    {
        var startLine = sourceText.Lines.GetLineFromPosition(span.Start).LineNumber;
        var endLine = sourceText.Lines.GetLineFromPosition(Math.Max(span.Start, span.End - 1)).LineNumber;
        for (var i = startLine; i <= endLine; i++)
        {
            if (i < 0 || i >= lineKinds.Count)
            {
                continue;
            }

            if (kind == LineKind.Code)
            {
                lineKinds[i] = LineKind.Code;
            }
            else if (lineKinds[i] != LineKind.Code)
            {
                lineKinds[i] = kind;
            }
        }
    }

    private static bool IsCSharpFile(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private enum LineKind
    {
        Code,
        Comment,
        Blank
    }
}
