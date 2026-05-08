namespace CsArchViewer.Metrics.Models;

public sealed class HealthWarning
{
    public required string Type { get; init; }
    public required string Severity { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}
