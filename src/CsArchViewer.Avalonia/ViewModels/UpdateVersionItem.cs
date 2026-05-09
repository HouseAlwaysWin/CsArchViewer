namespace CsArchViewer.Avalonia.ViewModels;

public sealed class UpdateVersionItem
{
    public required string VersionTag { get; init; }
    public required string DownloadUrl { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public string NotesUrl { get; init; } = string.Empty;
    public bool IsPrerelease { get; init; }

    public string DisplayName =>
        $"{VersionTag} ({PublishedAt:yyyy-MM-dd}){(IsPrerelease ? " - prerelease" : string.Empty)}";
}
