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
/// It assumes no layout at all. Published offsets go stale on the first patch -
/// the base pointer this was built from resolved to null within two months, and
/// assuming the field spacing that came with it made the search match unrelated
/// pairs of numbers, reporting ward as life.
///
/// Instead you tell it your maximum life and mana, and it finds the two of them
/// sitting near each other, learning the distance between them from whichever
/// distance the candidates agree on. Which side of a maximum its current value
/// sits on is learned the same way. Two known values in one structure is a far
/// stronger signature than one known value at a guessed offset, and nothing
/// about it needs to survive a patch except that life and mana live near each
/// other.
/// </summary>
internal sealed class GameMemory : IDisposable
{
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
    private int _manaDelta;         // learned, not assumed
    private int _curOffset = 4;     // learned: which side of maximum current sits
    private int _badReads;

    private Stats _latest;
    private bool _hasLatest;

    /// <summary>Process name to attach to, without ".exe".</summary>
    public string ProcessName { get; set; } = "PathOfExileSteam";

    /// <summary>
    /// The process that owns the game window, when one has been found.
    ///
    /// Matching on a name meant matching on "PathOfExileSteam", so memory
    /// simply never attached on a standalone or Epic install - the checkbox was
    /// on, the search never ran, and the machine fell back to reading the
    /// screen forever. The window is already located by its title for the
    /// firing rules; the process behind it is the right answer and needs no
    /// list of names to guess from.
    /// </summary>
    public volatile int PreferredPid;

    /// <summary>Hint from the screen, used to pick between candidates.</summary>
    public volatile int HintMaxHp;
    public volatile int HintMaxMp;

    /// <summary>
    /// The current values from the screen, when they are readable.
    ///
    /// Without these the search had nothing to test a candidate current
    /// against and took the first integer beside the maximum that was not
    /// absurd - which found the maxima correctly and then read someone else's
    /// number as your life.
    /// </summary>
    public volatile int HintCurHp;
    public volatile int HintCurMp;

    /// <summary>
    /// Bumped every time a new address is adopted, so a caller can tell one
    /// lock from the next and insist on confirming each one for itself.
    /// </summary>
    public volatile int Generation;

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
                    Status = "the game does not seem to be running";
                    _stop.Token.WaitHandle.WaitOne(2000);
                    continue;
                }

                if (_address == 0)
                {
                    Status = "searching memory for the player stats";
                    // Search explains itself; do not paper over it with a
                    // vaguer message.
                    long found = Search();
                    if (found == 0)
                    {
                        _stop.Token.WaitHandle.WaitOne(5000);
                        continue;
                    }
                    _address = found;
                    Generation++;
                    Log.Write($"memory: found player stats at 0x{found:X}");
                }

                if (ReadStats(_address, out var s) && Plausible(s) && Matches(s))
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

    private Process? FromWindow()
    {
        int pid = PreferredPid;
        if (pid <= 0) return null;

        try
        {
            var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p;
        }
        catch { return null; }
    }

    /// <summary>Every name this game ships under, not only the Steam one.</summary>
    private Process? ByName()
    {
        foreach (string name in new[]
                 { ProcessName, "PathOfExileSteam", "PathOfExile", "PathOfExile_x64",
                   "PathOfExileEGS", "PathOfExile2", "PathOfExile2Steam" })
        {
            var p = Process.GetProcessesByName(name).FirstOrDefault();
            if (p is not null) return p;
        }

        return null;
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

        var proc = FromWindow() ?? ByName();
        if (proc is null) return false;

        _handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, proc.Id);
        if (_handle == 0)
        {
            Status = $"cannot open {ProcessName} (error {Marshal.GetLastWin32Error()})";
            return false;
        }
        _pid = proc.Id;
        _address = 0;
        Log.Write($"memory: attached to {proc.ProcessName} pid {proc.Id}");
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
        s = default;
        if (!ReadInt(addr, out int maxHp)) return false;
        if (!ReadInt(addr + _curOffset, out int curHp)) return false;
        if (!ReadInt(addr + _manaDelta, out int maxMp)) return false;
        if (!ReadInt(addr + _manaDelta + _curOffset, out int curMp)) return false;

        s = new Stats(curHp, maxHp, curMp, maxMp, 0, 0, _clock.ElapsedMilliseconds);
        return true;
    }

    // Current may exceed maximum - life and mana both overstack - so the bound
    // on current is generous. The maxima are what must hold steady, and they
    // are checked against what you entered before any reading is believed.
    private static bool Plausible(Stats s) =>
        s.MaxHp is >= 20 and <= 100000 && s.CurHp >= 0 && s.CurHp <= s.MaxHp * 3
        && s.MaxMp is >= 1 and <= 100000 && s.CurMp >= 0 && s.CurMp <= s.MaxMp * 3;

    /// <summary>
    /// Finds the character by looking for your two maxima near each other, and
    /// learns the distance between them rather than assuming it.
    ///
    /// The published layout - mana exactly 0x50 past life - is as perishable as
    /// the base pointer that came with it, and assuming it made this match
    /// unrelated pairs of adjacent numbers: it reported ward as life, and mana
    /// as 2. Two known values in one structure is a far stronger signature than
    /// one known value at a guessed distance, and it needs nothing to stay true
    /// across patches except that life and mana live near each other.
    /// </summary>
    private long Search()
    {
        int wantHp = HintMaxHp, wantMp = HintMaxMp;
        if (wantHp <= 0 || wantMp <= 0)
        {
            // Saying which one is missing matters: one of these is usually
            // already filled in, and "enter both" reads as though neither is.
            Status = wantHp <= 0 && wantMp <= 0
                ? "needs your maximum life and mana - press Find numbers and they fill in"
                : wantMp <= 0
                    ? $"has life ({wantHp}) but needs maximum mana - one number matches "
                      + "thousands of places, two identifies you"
                    : $"has mana ({wantMp}) but needs maximum life - one number matches "
                      + "thousands of places, two identifies you";
            return 0;
        }

        var lifeAt = new List<long>();
        var manaAt = new List<long>();
        var buf = new byte[32 * 1024 * 1024];

        foreach (var (start, size) in Regions())
        {
            if (_stop.IsCancellationRequested) return 0;
            for (long off = 0; off < size; off += buf.Length - 8)
            {
                int chunk = (int)Math.Min(buf.Length, size - off);
                if (chunk < 8) break;
                if (!ReadProcessMemory(_handle, (nint)(start + off), buf, chunk, out var got))
                    continue;

                int usable = (int)got - 4;
                for (int i = 0; i + 4 <= usable; i += 4)
                {
                    int v = BitConverter.ToInt32(buf, i);
                    if (v == wantHp) lifeAt.Add(start + off + i);
                    else if (v == wantMp) manaAt.Add(start + off + i);
                }
            }
        }

        Log.Write($"memory: {lifeAt.Count} places hold {wantHp}, {manaAt.Count} hold {wantMp}");
        if (lifeAt.Count == 0 || manaAt.Count == 0)
        {
            Status = $"no {wantHp} and {wantMp} found together - are those your maxima?";
            return 0;
        }

        manaAt.Sort();
        var manaArr = manaAt.ToArray();

        // Pair them up: every life candidate with a mana value close by. The
        // real distance between the two shows up as the one that repeats.
        var byDelta = new Dictionary<long, List<long>>();
        foreach (long a in lifeAt)
        {
            int idx = Array.BinarySearch(manaArr, a - 0x400);
            if (idx < 0) idx = ~idx;
            for (int k = idx; k < manaArr.Length && manaArr[k] <= a + 0x400; k++)
            {
                long delta = manaArr[k] - a;
                if (delta == 0) continue;
                if (!byDelta.TryGetValue(delta, out var list))
                    byDelta[delta] = list = [];
                list.Add(a);
            }
        }

        if (byDelta.Count == 0)
        {
            Status = $"{wantHp} and {wantMp} never appear near each other";
            return 0;
        }

        // Prefer the distance that the most candidates agree on, and among
        // equals prefer the smaller gap: fields of one structure sit close.
        var best = byDelta.OrderByDescending(kv => kv.Value.Count)
                          .ThenBy(kv => Math.Abs(kv.Key))
                          .First();
        _manaDelta = (int)best.Key;
        Log.Write($"memory: mana sits {_manaDelta:+#;-#;0} bytes from life "
                  + $"in {best.Value.Count} candidate(s)");

        // Current sits beside maximum; which side is not worth assuming either.
        //
        // "Not absurd" was not a test. Any integer in a wide range passed, and
        // the first one to do so won - which is how a perfectly located pair of
        // maxima ended up reporting a life of 240 out of 1,490 while the screen
        // said 1,947. When the screen can be read, the current has to match it;
        // when it cannot, the best that can be said is that a real current
        // never exceeds its own maximum by much, so prefer the candidate that
        // sits closest to being a sensible fraction rather than taking whatever
        // comes first.
        int hintCurHp = HintCurHp, hintCurMp = HintCurMp;
        long bestAddr = 0;
        int bestOff = 4;
        double bestScore = double.MaxValue;

        foreach (long a in best.Value)
        {
            // Directly beside the maximum was an assumption, and with seven
            // candidates all offering 240 it was plainly the wrong one - the
            // real current was never four bytes away. A wider sweep would once
            // have been reckless, but nothing is accepted now without matching
            // what the screen reads, so looking further costs only time.
            foreach (int curOff in CurrentOffsets)
            {
                if (!ReadInt(a + curOff, out int curHp)) continue;
                if (!ReadInt(a + _manaDelta + curOff, out int curMp)) continue;

                // Both pools have to make sense, not just the one being matched.
                // Accepting a candidate on life alone let it lock onto numbers
                // that merely equalled life at that instant - and the log shows
                // exactly what that produced: "life 792/2078, mana 1600/738",
                // a mana current larger than its own maximum, and "life 738",
                // which is the mana maximum wearing life's hat. A coincidence
                // does not move afterwards, which is why the readout sat still.
                //
                // Overhealing is real, so a pool may exceed its maximum - but by
                // a third, not by double.
                if (curHp < 0 || curHp > wantHp * 4 / 3) continue;
                if (wantMp > 0 && (curMp < 0 || curMp > wantMp * 4 / 3)) continue;

                double score;
                if (hintCurHp > 0)
                {
                    // The screen is the witness. Anything more than a few
                    // percent off is a different number that happens to fit.
                    double offHp = Math.Abs(curHp - hintCurHp) / (double)wantHp;
                    double offMp = hintCurMp > 0 && wantMp > 0
                        ? Math.Abs(curMp - hintCurMp) / (double)wantMp
                        : 0;
                    if (offHp > 0.05 || offMp > 0.05) continue;

                    // Prefer the candidate whose mana is furthest from being a
                    // second copy of its own maximum. A structure where both
                    // pools sit exactly at full is indistinguishable from two
                    // stray copies of the maxima; one that is part-full is not.
                    score = offHp + offMp;
                    if (wantMp > 0 && curMp == wantMp) score += 0.02;
                }
                else
                {
                    // No reading from the screen to check against - which is the
                    // usual state on a machine where the numbers will not read,
                    // and precisely the machine that needs memory most. Waiting
                    // for the numbers there means waiting forever.
                    //
                    // A full pool is its own hint, and needs nobody to read
                    // anything: at full, current IS the maximum. So the search
                    // asks for that instead. It costs the person standing at
                    // full life while it looks, which they usually are.
                    if (curHp != wantHp) continue;
                    if (wantMp > 0 && curMp != wantMp) continue;
                    score = 0;
                }

                if (score >= bestScore) continue;
                bestScore = score;
                bestAddr = a;
                bestOff = curOff;
            }
        }

        if (bestAddr != 0)
        {
            _curOffset = bestOff;
            ReadInt(bestAddr + bestOff, out int gotHp);
            ReadInt(bestAddr + _manaDelta + bestOff, out int gotMp);
            Log.Write($"memory: current sits {bestOff:+#;-#} from maximum; "
                      + $"life {gotHp}/{wantHp}, mana {gotMp}/{wantMp}"
                      + (hintCurHp > 0 ? $" (screen said {hintCurHp}/{hintCurMp})" : ""));
            return bestAddr;
        }

        Status = hintCurHp > 0
            ? "found the maxima but no current beside them matching the screen"
            : "found the maxima but nothing beside them at full - stand at full "
              + "life and mana and press Re-scan";
        return 0;
    }

    /// <summary>
    /// Where a current value might sit relative to its maximum: the two places
    /// beside it first, then outwards through the rest of the structure.
    /// </summary>
    private static IEnumerable<int> CurrentOffsets
    {
        get
        {
            yield return 4;
            yield return -4;
            for (int d = 8; d <= 128; d += 4)
            {
                yield return d;
                yield return -d;
            }
        }
    }

    /// <summary>The maxima must still be the ones we searched for.</summary>
    /// <summary>
    /// Whether a locked address is still the one we found.
    ///
    /// Not by an exact maximum. A maximum changes every level and every gear
    /// swap, and the structure does not move when it does - so demanding the
    /// old value threw the lock away at exactly the moment everything else was
    /// changing too, and the search then needed the numbers to rebuild it. That
    /// is the level-up breakage.
    ///
    /// A pool that has become half or double what it was is a different pool;
    /// anything nearer than that is the same one, levelled.
    /// </summary>
    private bool Matches(Stats s) =>
        Near(s.MaxHp, HintMaxHp) && Near(s.MaxMp, HintMaxMp);

    private static bool Near(int now, int hint) =>
        hint <= 0 || (now > 0 && now * 2 >= hint && now <= hint * 2);

    private bool ReadInt(long addr, out int value)
    {
        var b = new byte[4];
        if (ReadProcessMemory(_handle, (nint)addr, b, 4, out var got) && (int)got == 4)
        {
            value = BitConverter.ToInt32(b);
            return true;
        }
        value = 0;
        return false;
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
