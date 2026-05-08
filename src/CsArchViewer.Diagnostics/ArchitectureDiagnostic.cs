namespace CsArchViewer.Diagnostics;

public sealed class ArchitectureDiagnostic
{
    public required string Type { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public required string Source { get; init; }
    public string Target { get; init; } = "-";
    public required string Message { get; init; }
}
