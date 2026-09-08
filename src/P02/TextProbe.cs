namespace P02;

/// <summary>
/// What a globe panel needs from the text reader, and no more: whether OCR
/// works, why not if it does not, and a way to read one region once.
/// </summary>
public sealed class TextProbe
{
    private readonly Func<Rectangle, string> _read;

    public TextProbe(bool available, string reason, Func<Rectangle, string> read)
    {
        Available = available;
        Reason = reason;
        _read = read;
    }

    public bool Available { get; }

    public string Reason { get; }

    public string Probe(Rectangle region) => _read(region);
}
