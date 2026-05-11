using System;

namespace CsArchViewer.Analysis;

public enum AnalysisPriority
{
    High = 0,
    Normal = 1,
    Low = 2
}

public sealed class AnalysisScheduler : IDisposable
{
    private readonly PriorityQueue<AnalysisWorkItem, int> _queue = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public event Action<int>? QueueLengthChanged;

    public AnalysisScheduler()
    {
        _worker = Task.Run(ProcessAsync);
    }

    public void Enqueue(Func<CancellationToken, Task> work, AnalysisPriority priority = AnalysisPriority.Normal)
    {
        lock (_queue)
        {
            // Keep only the latest pending analysis request. Older queued work is stale
            // once a newer file-system event arrives and can otherwise grow without bound.
            _queue.Clear();
            _queue.Enqueue(new AnalysisWorkItem(work), (int)priority);
            QueueLengthChanged?.Invoke(_queue.Count);
        }

        _signal.Release();
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            AnalysisWorkItem? item = null;
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                }
                QueueLengthChanged?.Invoke(_queue.Count);
            }

            if (item is null)
            {
                continue;
            }

            try
            {
                await item.Work(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore cancelled work
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _signal.Release();
        try
        {
            // Large solutions may need time to observe cancellation in Roslyn work items.
            _worker.Wait(TimeSpan.FromMinutes(2));
        }
        catch
        {
            // ignore during dispose
        }
        _signal.Dispose();
        _cts.Dispose();
    }

    private sealed record AnalysisWorkItem(Func<CancellationToken, Task> Work);
}
