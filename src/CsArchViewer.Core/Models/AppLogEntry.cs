namespace CsArchViewer.Core.Models;

public sealed class AppLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public AppLogLevel Level { get; init; } = AppLogLevel.Info;
    public string Category { get; init; } = "General";
    public string Message { get; init; } = string.Empty;
}
