using CsArchViewer.Core.Models;

namespace CsArchViewer.Analysis;

public sealed class PerformanceMonitorService
{
    private PerformanceSnapshot _lastSnapshot = new();

    public event Action<PerformanceSnapshot>? SnapshotUpdated;

    public PerformanceSnapshot LastSnapshot => _lastSnapshot;

    public void Update(PerformanceSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        SnapshotUpdated?.Invoke(snapshot);
    }
}
