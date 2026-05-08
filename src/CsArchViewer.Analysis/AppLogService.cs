using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class AppLogService
{
    private readonly LinkedList<AppLogEntry> _entries = [];
    private readonly object _lock = new();
    private readonly int _capacity;

    public AppLogService(int capacity = 500)
    {
        _capacity = Math.Max(50, capacity);
    }

    public event Action<AppLogEntry>? EntryAdded;

    public IReadOnlyList<AppLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public void Trace(string category, string message) => Write(AppLogLevel.Trace, category, message);
    public void Info(string category, string message) => Write(AppLogLevel.Info, category, message);
    public void Warning(string category, string message) => Write(AppLogLevel.Warning, category, message);
    public void Error(string category, string message) => Write(AppLogLevel.Error, category, message);

    public void Write(AppLogLevel level, string category, string message)
    {
        var entry = new AppLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
            Message = message
        };

        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }

        EntryAdded?.Invoke(entry);
    }
}
