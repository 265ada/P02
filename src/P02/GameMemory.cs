using System.Diagnostics;
using System.Runtime.InteropServices;

namespace P02;

/// <summary>
/// Reads life, mana and energy shield straight out of the game's memory.
///
/// This is exact and instant, with none of the failure modes of looking at the
/// screen. It is also the most intrusive thing here by a distance, and it is
/// off unless you turn it on.
///
/// It does not use hardcoded offsets. Published ones go stale on the first
/// patch - the set this was built from resolved to a null pointer within two
/// months. Instead it searches for the shape of the structure itself: maximum
/// and current life as adjacent integers, mana the same 0x50 further on, energy
/// shield 0x88 further still, each current value within a plausible distance of
/// its maximum. That survives patches, because the layout of a struct changes
/// far less often than where a pointer to it happens to live.
/// </summary>
internal sealed class GameMemory : IDisposable
{
    // Layout taken from sjh001111/poe2-auto-potion: MaxHP +0x1DC, CurHP +0x1E0,
    // MaxMP +0x22C, MaxES +0x264 - so relative to MaxHP, mana sits 0x50 along
    // and energy shield 0x88.
    private const int ManaDelta = 0x50;
    private const int EsDelta = 0x88;

    private const uint PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000, MEM_PRIVATE = 0x20000;
    private const uint PAGE_GUARD = 0x100, PAGE_READWRITE = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    private struct MBI
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect; public int _pad;
        public nint RegionSize;
        public uint State, Protect, Type; public int _pad2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint h, nint addr, byte[] buf,
                                                 nint size, out nint read);

    [DllImport("kernel32.dll")]
    private static extern nint VirtualQueryEx(nint h, nint addr, out MBI mbi, nint len);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint h);

    public readonly record struct Stats(int CurHp, int MaxHp, int CurMp, int MaxMp,
                                        int CurEs, int MaxEs, long AtMs)
    {
        // Clamped: skills can push a pool past its maximum, and above full is
        // above full however far past it goes.
        public double LifeFraction =>
            MaxHp > 0 ? Math.Clamp(CurHp / (double)MaxHp, 0, 1) : 0;

        public double ManaFraction =>
            MaxMp > 0 ? Math.Clamp(CurMp / (double)MaxMp, 0, 1) : 0;
    }

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    private nint _handle;
    private int _pid;
    private long _address;          // address of MaxHP
    private int _badReads;

    private Stats _latest;
    private bool _hasLatest;

    /// <summary>Process name to attach to, without ".exe".</summary>
    public string ProcessName { get; set; } = "PathOfExileSteam";

    /// <summary>Hint from the screen, used to pick between candidates.</summary>
    public volatile int HintMaxHp;
    public volatile int HintMaxMp;

    public bool Attached => _handle != 0;

    public bool Found => _address != 0;

    public string Status { get; private set; } = "off";

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "P02 memory" };
        _thread.Start();
    }

    public bool TryGet(out Stats stats)
    {
        lock (_gate)
        {
            stats = _latest;
            return _hasLatest;
        }
    }

    public long NowMs => _clock.ElapsedMilliseconds;

    /// <summary>Drops the cached address so the next pass searches again.</summary>
    public void Rescan()
    {
        lock (_gate) { _address = 0; _hasLatest = false; }
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (!EnsureAttached())
                {
                    Status = $"{ProcessName} is not running";
                    _stop.Token.WaitHandle.WaitOne(2000);
                    continue;
                }

                if (_address == 0)
                {
                    Status = "searching memory for the player stats";
                    long found = Search();
                    if (found == 0)
                    {
                        Status = "could not find the stats - the layout may have changed";
                        _stop.Token.WaitHandle.WaitOne(5000);
                        continue;
                    }
                    _address = found;
                    Log.Write($"memory: found player stats at 0x{found:X}");
                }

                if (ReadStats(_address, out var s) && Plausible(s))
                {
                    lock (_gate) { _latest = s; _hasLatest = true; }
                    _badReads = 0;
                    Status = $"reading: HP {s.CurHp}/{s.MaxHp}  MP {s.CurMp}/{s.MaxMp}";
                }
                else if (++_badReads > 20)
                {
                    // The structure moved: a new zone, a new character, a
                    // restart. Search again rather than reading rubbish.
                    Log.Write("memory: address went bad, searching again");
                    lock (_gate) { _address = 0; _hasLatest = false; }
                    _badReads = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"memory loop: {ex.Message}");
                _stop.Token.WaitHandle.WaitOne(1000);
            }

            // Far faster than anything on screen and essentially free.
            _stop.Token.WaitHandle.WaitOne(15);
        }
    }

    private bool EnsureAttached()
    {
        if (_handle != 0)
        {
            try
            {
                using var live = Process.GetProcessById(_pid);
                if (!live.HasExited) return true;
            }
            catch { /* gone */ }
            Detach();
        }

        var proc = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (proc is null) return false;

        _handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, proc.Id);
        if (_handle == 0)
        {
            Status = $"cannot open {ProcessName} (error {Marshal.GetLastWin32Error()})";
            return false;
        }
        _pid = proc.Id;
        _address = 0;
        Log.Write($"memory: attached to {ProcessName} pid {proc.Id}");
        return true;
    }

    private void Detach()
    {
        if (_handle != 0) CloseHandle(_handle);
        _handle = 0;
        _pid = 0;
        _address = 0;
        lock (_gate) _hasLatest = false;
    }

    private bool ReadStats(long addr, out Stats s)
    {
        var buf = new byte[EsDelta + 8];
        if (!ReadProcessMemory(_handle, (nint)addr, buf, buf.Length, out var got)
            || (int)got != buf.Length)
        {
            s = default;
            return false;
        }
        s = FromBuffer(buf, 0);
        return true;
    }

    private Stats FromBuffer(byte[] b, int i) => new(
        BitConverter.ToInt32(b, i + 4), BitConverter.ToInt32(b, i),
        BitConverter.ToInt32(b, i + ManaDelta + 4), BitConverter.ToInt32(b, i + ManaDelta),
        BitConverter.ToInt32(b, i + EsDelta + 4), BitConverter.ToInt32(b, i + EsDelta),
        _clock.ElapsedMilliseconds);

    // Current may exceed maximum - skills allow it - so the bound is generous
    // rather than exact. It still has to look like a character sheet.
    private static bool Plausible(Stats s) =>
        s.MaxHp is >= 20 and <= 100000 && s.CurHp >= 0 && s.CurHp <= s.MaxHp * 3
        && s.MaxMp is >= 1 and <= 100000 && s.CurMp >= 0 && s.CurMp <= s.MaxMp * 3
        && s.MaxEs is >= 0 and <= 200000 && s.CurEs >= 0 && s.CurEs <= Math.Max(1, s.MaxEs) * 3;

    /// <summary>
    /// Sweeps the writable heap for the stat structure. Measured at about
    /// 3 GB/s over roughly 7 GB, so a couple of seconds.
    /// </summary>
    private long Search()
    {
        var regions = Regions();
        var buf = new byte[32 * 1024 * 1024];
        var hits = new List<(long Addr, Stats S)>();
        int wantHp = HintMaxHp, wantMp = HintMaxMp;

        foreach (var (start, size) in regions)
        {
            if (_stop.IsCancellationRequested) return 0;

            // Overlap by the struct span so a candidate on a chunk boundary is
            // not missed.
            for (long off = 0; off < size; off += buf.Length - (EsDelta + 8))
            {
                int chunk = (int)Math.Min(buf.Length, size - off);
                if (chunk < EsDelta + 8) break;
                if (!ReadProcessMemory(_handle, (nint)(start + off), buf, chunk, out var got))
                    continue;

                int usable = (int)got - (EsDelta + 8);
                for (int i = 0; i + 4 <= usable; i += 4)
                {
                    int maxHp = BitConverter.ToInt32(buf, i);
                    if (maxHp < 20 || maxHp > 100000) continue;
                    if (wantHp > 0 && maxHp != wantHp) continue;

                    var s = FromBuffer(buf, i);
                    if (!Plausible(s)) continue;
                    if (wantMp > 0 && s.MaxMp != wantMp) continue;

                    hits.Add((start + off + i, s));
                    if (hits.Count > 4000) break;
                }
            }
        }

        if (hits.Count == 0) return 0;
        Log.Write($"memory: {hits.Count} candidate(s) matched the layout"
                  + (wantHp > 0 ? $" with max life {wantHp}" : ""));

        if (hits.Count == 1) return hits[0].Addr;

        // Several matched. Watch them change: the real one moves with the
        // fight, while stale copies and unrelated integers sit still.
        return Narrow(hits);
    }

    private long Narrow(List<(long Addr, Stats S)> hits)
    {
        var live = hits.ToList();
        for (int round = 0; round < 6 && live.Count > 1; round++)
        {
            _stop.Token.WaitHandle.WaitOne(400);
            if (_stop.IsCancellationRequested) return 0;

            var next = new List<(long Addr, Stats S)>();
            foreach (var (addr, before) in live)
            {
                if (!ReadStats(addr, out var now) || !Plausible(now)) continue;
                // The maximum should hold steady even as the current value moves.
                if (now.MaxHp != before.MaxHp || now.MaxMp != before.MaxMp) continue;
                next.Add((addr, now));
            }

            if (next.Count == 0) break;
            live = next;

            var moved = live.Where(c => c.S.CurHp != hits.First(h => h.Addr == c.Addr).S.CurHp)
                            .ToList();
            if (moved.Count > 0) return moved[0].Addr;
        }

        return live.Count > 0 ? live[0].Addr : 0;
    }

    private List<(long Base, long Size)> Regions()
    {
        var list = new List<(long, long)>();
        nint addr = 0;
        while (VirtualQueryEx(_handle, addr, out var m, Marshal.SizeOf<MBI>()) != 0)
        {
            long size = (long)m.RegionSize;
            if (size <= 0) break;

            // Game state lives in writable private heap; skipping everything
            // else cuts 23 GB down to about 7.
            if (m.State == MEM_COMMIT && m.Type == MEM_PRIVATE
                && (m.Protect & PAGE_GUARD) == 0 && m.Protect == PAGE_READWRITE
                && size >= 4096 && size <= 512L * 1024 * 1024)
                list.Add(((long)m.BaseAddress, size));

            addr = m.BaseAddress + (nint)size;
            if ((ulong)addr > 0x7FFFFFFFFFFF) break;
        }
        return list;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(1000);
        Detach();
        _stop.Dispose();
    }
}
