namespace P02;

/// <summary>
/// What a globe panel needs from the text reader, and no more: whether OCR
/// works, why not if it does not, and a way to read one region once.
/// </summary>
public sealed class TextProbe
{
    public delegate string ReadRegion(Rectangle region, out Bitmap? shot);

    private readonly ReadRegion _read;

    public TextProbe(bool available, string reason, ReadRegion read)
    {
        Available = available;
        Reason = reason;
        _read = read;
    }

    public bool Available { get; }

    public string Reason { get; }

    public string Probe(Rectangle region) => _read(region, out _);

    /// <summary>Reads a region and hands back the picture, for saving when it
    /// comes back empty.</summary>
    public string Probe(Rectangle region, out Bitmap? shot) => _read(region, out shot);
}
