namespace CsArchViewer.Core.Models;

public sealed class DiagnosticMessage
{
    public string Type { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
