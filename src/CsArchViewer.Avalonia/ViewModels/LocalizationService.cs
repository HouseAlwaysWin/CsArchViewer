namespace CsArchViewer.Avalonia.ViewModels;

public sealed class LocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-TW"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkspaceTab"] = "工作區",
            ["SettingsTab"] = "設定",
            ["OpenFolder"] = "開啟資料夾",
            ["Reload"] = "重新載入",
            ["Export"] = "匯出",
            ["SearchPlaceholder"] = "搜尋專案...",
            ["Nodes"] = "節點",
            ["NodeDetails"] = "節點詳情",
            ["Diagnostics"] = "診斷",
            ["SettingsTitle"] = "設定",
            ["SettingsDescription"] = "外觀與圖表顯示選項。",
            ["GraphSection"] = "圖表",
            ["ShowLineCount"] = "在節點上顯示行數",
            ["Language"] = "語言",
            ["StatusIdle"] = "請選擇要分析的資料夾。",
            ["StatusIdleShort"] = "閒置",
            ["StatusTasks"] = "工作",
            ["StatusGraphTemplate"] = "圖表: {0} | 節點: {1} | 邊: {2}",
            ["SelectFolderTitle"] = "選擇 .NET 方案資料夾",
            ["ExportTitleTemplate"] = "匯出 {0}",
            ["ExportedTemplate"] = "已匯出 {0}: {1}",
            ["RunningFullAnalysis"] = "正在執行完整分析...",
            ["FileChangedTemplate"] = "檔案變更: {0}",
            ["FullAnalysisCompleted"] = "完整分析完成。",
            ["IncrementalUpdatedTemplate"] = "增量分析已更新: {0}",
            ["AnalysisFailedTemplate"] = "分析失敗: {0}",
            ["AnalyzeFailedTemplate"] = "分析失敗: {0}",
            ["FolderNoChildTemplate"] = "資料夾 '{0}' 沒有可顯示的子項。"
        },
        ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkspaceTab"] = "Workspace",
            ["SettingsTab"] = "Settings",
            ["OpenFolder"] = "Open Folder",
            ["Reload"] = "Reload",
            ["Export"] = "Export",
            ["SearchPlaceholder"] = "Search project...",
            ["Nodes"] = "Nodes",
            ["NodeDetails"] = "Node Details",
            ["Diagnostics"] = "Diagnostics",
            ["SettingsTitle"] = "Settings",
            ["SettingsDescription"] = "Appearance and graph display options.",
            ["GraphSection"] = "Graph",
            ["ShowLineCount"] = "Show line count on nodes",
            ["Language"] = "Language",
            ["StatusIdle"] = "Select a folder to analyze.",
            ["StatusIdleShort"] = "Idle",
            ["StatusTasks"] = "Tasks",
            ["StatusGraphTemplate"] = "Graph: {0} | Nodes: {1} | Edges: {2}",
            ["SelectFolderTitle"] = "Select .NET solution folder",
            ["ExportTitleTemplate"] = "Export {0}",
            ["ExportedTemplate"] = "Exported {0}: {1}",
            ["RunningFullAnalysis"] = "Running full analysis...",
            ["FileChangedTemplate"] = "File changed: {0}",
            ["FullAnalysisCompleted"] = "Full analysis completed.",
            ["IncrementalUpdatedTemplate"] = "Incremental analysis updated: {0}",
            ["AnalysisFailedTemplate"] = "Analysis failed: {0}",
            ["AnalyzeFailedTemplate"] = "Analyze failed: {0}",
            ["FolderNoChildTemplate"] = "Folder '{0}' has no child items to display."
        }
    };

    private string _languageCode = "zh-TW";

    public event Action? LanguageChanged;

    public string LanguageCode => _languageCode;

    public string Get(string key)
    {
        if (_resources.TryGetValue(_languageCode, out var langMap) &&
            langMap.TryGetValue(key, out var value))
        {
            return value;
        }

        return _resources["en-US"].TryGetValue(key, out var fallback) ? fallback : key;
    }

    public void SetLanguage(string languageCode)
    {
        if (_resources.ContainsKey(languageCode) && !string.Equals(_languageCode, languageCode, StringComparison.OrdinalIgnoreCase))
        {
            _languageCode = languageCode;
            LanguageChanged?.Invoke();
        }
    }
}
