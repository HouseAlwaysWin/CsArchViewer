namespace CsArchViewer.Core.Models;

public sealed class AppSettings
{
    public string Theme { get; init; } = "Dark";
    public string GraphLayout { get; init; } = "Auto";
    public string OverlayMode { get; init; } = "None";
    public string LanguageCode { get; init; } = "zh-TW";
    public bool ShowLineCountOnNodes { get; init; }
    public bool RestoreLastSession { get; init; } = true;
    public bool AutoSaveSession { get; init; } = true;
    public int DiagnosticsDepthThreshold { get; init; } = 4;
}
