using System.Collections.Concurrent;

namespace P02;

/// <summary>
/// Sends keys on its own thread, one job at a time.
///
/// A press has to be held a few milliseconds to register and a burst has gaps
/// between presses, so a 3-press burst takes a fifth of a second to send. The
/// poll loop can ask for one of those every 50 ms, which is four times faster
/// than they can physically leave. Queueing those requests just built a backlog
/// seconds deep, so keys arrived long after the moment that asked for them and
/// the configured cooldown stopped meaning anything.
///
/// So there is no queue: while a job is in flight, new requests are refused and
/// counted. Presses then stay in step with what is actually on screen.
/// </summary>
internal sealed class KeyPresser : IDisposable
{
    private readonly record struct Job(string Key, int HoldMs, int Count, int GapMs,
                                      bool Post, nint Window);

    private readonly BlockingCollection<Job> _queue = new(new ConcurrentQueue<Job>(), 1);
    private readonly Thread _thread;
    private int _busy;
    private int _skipped;
    private long _busySinceMs;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>True while a press or burst is still going out.</summary>
    public bool Busy => Volatile.Read(ref _busy) != 0;

    /// <summary>Requests refused because a press was already in flight.</summary>
    public int Skipped => Volatile.Read(ref _skipped);

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

    /// <summary>
    /// Sends a press, unless one is already going out. Returns false when it
    /// was refused. Never blocks the caller.
    /// </summary>
    public bool Send(string key, int holdMs, int count = 1, int gapMs = 40,
                     bool post = false, nint window = 0)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            // A burst has a known duration. If the flag has outlived any
            // plausible one, something went wrong on the sending thread and
            // leaving it set would silently stop every future press - which
            // looks exactly like firing once and then never again.
            long stuckFor = _clock.ElapsedMilliseconds - Volatile.Read(ref _busySinceMs);
            long plausible = Math.Max(2000, (count * (long)holdMs + count * (long)gapMs) * 4);
            if (stuckFor > plausible)
            {
                Log.Write($"key sender was stuck busy for {stuckFor} ms; clearing");
                Volatile.Write(ref _busy, 0);
            }
            else
            {
                Interlocked.Increment(ref _skipped);
                return false;
            }

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                Interlocked.Increment(ref _skipped);
                return false;
            }
        }

        Volatile.Write(ref _busySinceMs, _clock.ElapsedMilliseconds);

        if (_queue.TryAdd(new Job(key, holdMs, count, gapMs, post, window))) return true;

        Volatile.Write(ref _busy, 0);
        return false;
    }

    private void Run()
    {
        try
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                try
                {
                    for (int i = 0; i < job.Count; i++)
                    {
                        if (job.Post) KeySender.PostTo(job.Window, job.Key, job.HoldMs);
                        else KeySender.Tap(job.Key, job.HoldMs);
                        if (i < job.Count - 1) Thread.Sleep(job.GapMs);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"send failed: {ex.Message}");
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
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
