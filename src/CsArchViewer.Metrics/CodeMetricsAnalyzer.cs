using CsArchViewer.Core.Models;
using CsArchViewer.Metrics.Models;

namespace CsArchViewer.Metrics;

public sealed class CodeMetricsAnalyzer
{
    private static readonly string[] SupportedExtensions = [".cs", ".axaml", ".xaml", ".razor", ".cshtml"];
    private readonly FileMetricsAnalyzer _fileAnalyzer = new();
    private readonly ProjectMetricsAnalyzer _projectAnalyzer = new();
    private readonly NamespaceMetricsAnalyzer _namespaceAnalyzer = new();
    private readonly DependencyMetricsAnalyzer _dependencyAnalyzer = new();

    private readonly Dictionary<string, FileMetrics> _cachedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cachedFileFingerprints = new(StringComparer.OrdinalIgnoreCase);

    public MetricsThresholds Thresholds { get; } = new();

    public async Task<MetricsSummary> AnalyzeAsync(
        AnalysisResult analysisResult,
        IReadOnlyCollection<string>? changedFiles = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveChanged = NormalizeChangedFiles(changedFiles);
        var filesToAnalyze = effectiveChanged.Count == 0
            ? null
            : effectiveChanged.Where(HasFileFingerprintChanged).ToList();

        var fileMetrics = _fileAnalyzer.Analyze(
            analysisResult.RootPath,
            filesToAnalyze,
            _cachedFiles,
            analysisResult.Graphs);

        _cachedFiles.Clear();
        foreach (var metric in fileMetrics)
        {
            _cachedFiles[metric.FilePath] = metric;
            _cachedFileFingerprints[metric.FilePath] = BuildFingerprint(metric.FilePath);
        }

        var projectMetrics = _projectAnalyzer.Analyze(analysisResult.Projects, fileMetrics, analysisResult.Graphs);
        var namespaceMetrics = _namespaceAnalyzer.Analyze(fileMetrics, analysisResult.Graphs);
        var dependencyMetrics = _dependencyAnalyzer.Analyze(analysisResult.Graphs);
        var healthWarnings = BuildHealthWarnings(fileMetrics, namespaceMetrics, dependencyMetrics);

        return new MetricsSummary
        {
            RootPath = analysisResult.RootPath,
            Files = fileMetrics,
            Projects = projectMetrics,
            Namespaces = namespaceMetrics,
            Dependencies = dependencyMetrics,
            HealthWarnings = healthWarnings
        };
    }

    private List<HealthWarning> BuildHealthWarnings(
        IReadOnlyList<FileMetrics> files,
        IReadOnlyList<NamespaceMetrics> namespaces,
        IReadOnlyList<DependencyMetrics> dependencies)
    {
        var warnings = new List<HealthWarning>();

        foreach (var file in files.Where(x => x.TotalLines > Thresholds.LargeFileLoc))
        {
            warnings.Add(new HealthWarning
            {
                Type = "LargeFileWarning",
                Severity = "Warning",
                Source = file.FilePath,
                Message = $"File LOC {file.TotalLines} exceeds threshold {Thresholds.LargeFileLoc}."
            });
        }

        foreach (var ns in namespaces.Where(x => x.TotalLines > Thresholds.LargeNamespaceLoc))
        {
            warnings.Add(new HealthWarning
            {
                Type = "LargeNamespaceWarning",
                Severity = "Warning",
                Source = ns.Namespace,
                Message = $"Namespace LOC {ns.TotalLines} exceeds threshold {Thresholds.LargeNamespaceLoc}."
            });
        }

        foreach (var dep in dependencies.Where(x => x.OutgoingDependencyCount > Thresholds.HighDependencyCount))
        {
            warnings.Add(new HealthWarning
            {
                Type = "HighDependencyWarning",
                Severity = "Warning",
                Source = dep.Scope,
                Message = $"Outgoing dependency count {dep.OutgoingDependencyCount} exceeds threshold {Thresholds.HighDependencyCount}."
            });
        }

        foreach (var dep in dependencies.Where(x => x.CircularDependencyCount > 0))
        {
            warnings.Add(new HealthWarning
            {
                Type = "CircularDependencyWarning",
                Severity = "Warning",
                Source = dep.Scope,
                Message = $"Detected {dep.CircularDependencyCount} circular dependencies."
            });
        }

        foreach (var dep in dependencies.Where(x => x.DependencyDepth > Thresholds.DependencyDepth))
        {
            warnings.Add(new HealthWarning
            {
                Type = "DeepDependencyWarning",
                Severity = "Warning",
                Source = dep.Scope,
                Message = $"Dependency depth {dep.DependencyDepth} exceeds threshold {Thresholds.DependencyDepth}."
            });
        }

        return warnings;
    }

    private static IReadOnlyCollection<string> NormalizeChangedFiles(IReadOnlyCollection<string>? changedFiles)
    {
        if (changedFiles is null || changedFiles.Count == 0)
        {
            return [];
        }

        return changedFiles
            .Where(IsSupportedMetricsFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedMetricsFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private bool HasFileFingerprintChanged(string filePath)
    {
        var current = BuildFingerprint(filePath);
        return !_cachedFileFingerprints.TryGetValue(filePath, out var cached) ||
               !string.Equals(current, cached, StringComparison.Ordinal);
    }

    private static string BuildFingerprint(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return "missing";
        }
    }
}

public sealed class MetricsThresholds
{
    public int LargeFileLoc { get; set; } = 2000;
    public int LargeNamespaceLoc { get; set; } = 10000;
    public int DependencyDepth { get; set; } = 10;
    public int HighDependencyCount { get; set; } = 50;
}
