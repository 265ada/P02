namespace P02;

/// <summary>
/// The last few seconds of readings, kept so that a press can be explained
/// after the fact.
///
/// "It fired and it should not have" is impossible to answer from a log that
/// only records the press: by then the interesting part - what each source was
/// reading on the way down, which one was believed, and what the rest of the
/// window was doing - has already scrolled past. This keeps a short ring of it
/// in memory at all times and writes the whole thing out only when a key
/// actually goes out, so the log stays small and the moment stays complete.
/// </summary>
internal sealed class FireTrail
{
    private const int Keep = 80;

    private readonly string[] _lines = new string[Keep];
    private readonly object _lock = new();
    private int _next;
    private int _count;

    /// <summary>Records one poll. Cheap enough to call every time.</summary>
    public void Note(string line)
    {
        lock (_lock)
        {
            _lines[_next] = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
            _next = (_next + 1) % Keep;
            if (_count < Keep) _count++;
        }
    }

    /// <summary>
    /// Writes the run-up to a press into the log, headed by why it fired.
    /// </summary>
    public void Dump(string why)
    {
        string[] recent;
        lock (_lock)
        {
            recent = new string[_count];
            for (int i = 0; i < _count; i++)
                recent[i] = _lines[(_next - _count + i + Keep) % Keep];
        }

        Log.Write("");
        Log.Write($"=== FIRED: {why}");
        Log.Write($"=== the {recent.Length} polls before it:");
        foreach (string line in recent) Log.Write("    " + line);
        Log.Write("=== end");
        Log.Write("");
    }
}
