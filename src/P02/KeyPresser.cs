using System.Collections.Concurrent;

namespace P02;

/// <summary>
/// Sends keys on its own thread. A key press has to be held for a few
/// milliseconds to register, and a burst has gaps between presses; doing that
/// on the poll loop stalled monitoring for as long as the press took, which is
/// exactly when we least want to stop looking at the globes.
/// </summary>
internal sealed class KeyPresser : IDisposable
{
    private readonly record struct Job(string Key, int HoldMs, int Count, int GapMs);

    private readonly BlockingCollection<Job> _queue = new(new ConcurrentQueue<Job>(), 32);
    private readonly Thread _thread;

    public KeyPresser()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "P02 keys",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>Queues a press. Returns immediately; never blocks the caller.</summary>
    public void Send(string key, int holdMs, int count = 1, int gapMs = 40)
    {
        // If the queue is full we are already pressing faster than the game can
        // matter, so dropping is better than backing up the loop.
        _queue.TryAdd(new Job(key, holdMs, count, gapMs));
    }

    private void Run()
    {
        try
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                for (int i = 0; i < job.Count; i++)
                {
                    try
                    {
                        KeySender.Tap(job.Key, job.HoldMs);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"send failed: {ex.Message}");
                        break;
                    }
                    if (i < job.Count - 1) Thread.Sleep(job.GapMs);
                }
            }
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* queue completed */ }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(500);
        _queue.Dispose();
    }
}
