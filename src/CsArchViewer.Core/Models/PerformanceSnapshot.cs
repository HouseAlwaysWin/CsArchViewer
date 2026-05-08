namespace CsArchViewer.Core.Models;

public sealed class PerformanceSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double AnalysisMs { get; init; }
    public double MetricsMs { get; init; }
    public double SymbolIndexMs { get; init; }
    public double TotalMs { get; init; }
    public double CacheHitRate { get; init; }
    public int SymbolIndexSize { get; init; }
    public double MemoryUsageMb { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
}
