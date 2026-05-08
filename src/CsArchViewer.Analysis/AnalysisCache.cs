using System.Security.Cryptography;
using System.Text;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class AnalysisCache
{
    private readonly Dictionary<string, CachedFileEntry> _fileEntries = new(StringComparer.OrdinalIgnoreCase);
    private AnalysisResult? _lastResult;

    public AnalysisResult? GetLastResult() => _lastResult;
    public void SetLastResult(AnalysisResult result) => _lastResult = result;

    public bool IsFileChanged(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return true;
        }

        var hash = ComputeHash(filePath);
        if (!_fileEntries.TryGetValue(filePath, out var entry))
        {
            _fileEntries[filePath] = new CachedFileEntry(hash, DateTime.UtcNow);
            return true;
        }

        if (string.Equals(entry.Hash, hash, StringComparison.Ordinal))
        {
            return false;
        }

        _fileEntries[filePath] = new CachedFileEntry(hash, DateTime.UtcNow);
        return true;
    }

    public void PrimeFromResult(AnalysisResult result)
    {
        var paths = result.Projects.Select(p => p.CsProjPath).ToList();
        foreach (var path in paths.Where(File.Exists))
        {
            _fileEntries[path] = new CachedFileEntry(ComputeHash(path), DateTime.UtcNow);
        }
        _lastResult = result;
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hashBytes = SHA256.HashData(stream);
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private sealed record CachedFileEntry(string Hash, DateTime LastAnalyzedUtc);
}
