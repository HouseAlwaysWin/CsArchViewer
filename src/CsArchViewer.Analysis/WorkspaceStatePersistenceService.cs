using System.Text.Json;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class WorkspaceStatePersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;

    public WorkspaceStatePersistenceService(string? stateFilePath = null)
    {
        _stateFilePath = stateFilePath ?? BuildDefaultPath();
    }

    public async Task<WorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return new WorkspaceState();
        }

        await using var stream = File.OpenRead(_stateFilePath);
        var state = await JsonSerializer.DeserializeAsync<WorkspaceState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return state ?? new WorkspaceState();
    }

    public async Task SaveAsync(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDefaultPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CsArchViewer");
        return Path.Combine(baseDir, "workspace-state.json");
    }
}
