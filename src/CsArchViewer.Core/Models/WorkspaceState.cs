namespace CsArchViewer.Core.Models;

public sealed class WorkspaceState
{
    public AppSettings Settings { get; init; } = new();
    public SessionState Session { get; init; } = new();
}
