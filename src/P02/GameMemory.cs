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

        public double ShieldFraction =>
            MaxEs > 0 ? Math.Clamp(CurEs / (double)MaxEs, 0, 1) : 0;
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

    // The game's own layout, from a published memory model of this client
    // rather than from guessing at adjacent numbers.
    //
    // A Life component holds three of these vitals at fixed offsets, and each
    // vital carries a pointer back to the component that owns it. That back
    // pointer is what makes this certain: an integer pair can look like a pool
    // by luck, but a pair whose neighbour points exactly back at the structure
    // it belongs to cannot.
    private const int HealthInLife = 0x1A8;
    private const int ManaInLife = 0x200;
    private const int ShieldInLife = 0x240;
    private const int VitalOwner = 0x10;    // pointer back to the Life component
    private const int VitalTotal = 0x34;
    private const int VitalCurrent = 0x38;

    /// <summary>Set once the structure has been found properly, rather than guessed.</summary>
    /// <summary>
    /// Components that matched equally well and have not yet given themselves
    /// away by changing. Watched between polls rather than re-found.
    /// </summary>
    private List<(long owner, int health, int mana, int shield)> _pending = [];
    private int[] _pendingFirst = [];

    /// <summary>Waiting for one of several equal matches to give itself away.</summary>
    public bool Pending => _pending.Count > 1;

    private bool _structured;

    // Where each vital sits inside the Life component, taken from the game
    // rather than from a constant. The published numbers are the starting
    // guess; the owner pointer tells us the truth, so a patch that moves a
    // vital does not need anybody to publish new offsets.
    private int _healthOff = HealthInLife;
    private int _manaOff = ManaInLife;
    private int _shieldOff = ShieldInLife;

    /// <summary>
    /// Whether the address came from matching the game's own structure.
    ///
    /// A structural match is not a guess that has to earn trust by moving: the
    /// vitals point back at the component that owns them, which no stray copy
    /// of a maximum does.
    /// </summary>
    public bool Structured => _structured;
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
    /// Your maximum energy shield, when it is known.
    ///
    /// It earns its place by being the number least likely to be stale at the
    /// same moment as the others - which is the whole job here, since the
    /// search needs your component to agree about more than one thing before
    /// it will believe it is yours.
    /// </summary>
    public volatile int HintMaxEs;

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
        // A tie held from before is not worth keeping across an explicit
        // rescan: the reason for asking is usually that something changed.
        _pending = [];
        _pendingFirst = [];
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

                // A tie already found is cheaper to settle than to find again.
                if (_address == 0 && _pending.Count > 1)
                {
                    long settled = SettlePending();
                    if (settled == 0)
                    {
                        _stop.Token.WaitHandle.WaitOne(200);
                        continue;
                    }
                    _address = settled;
                    Generation++;
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

        // Found properly: addr is the Life component, and everything else is at
        // a known offset from it. Nothing is learned, so nothing can drift.
        if (_structured)
        {
            if (!ReadVital(addr + _healthOff, out int curHp, out int maxHp)) return false;
            if (!ReadVital(addr + _manaOff, out int curMp, out int maxMp)) return false;
            ReadVital(addr + _shieldOff, out int curEs, out int maxEs);

            s = new Stats(curHp, maxHp, curMp, maxMp, curEs, maxEs,
                          _clock.ElapsedMilliseconds);
            return true;
        }

        if (!ReadInt(addr, out int oldMaxHp)) return false;
        if (!ReadInt(addr + _curOffset, out int oldCurHp)) return false;
        if (!ReadInt(addr + _manaDelta, out int oldMaxMp)) return false;
        if (!ReadInt(addr + _manaDelta + _curOffset, out int oldCurMp)) return false;

        s = new Stats(oldCurHp, oldMaxHp, oldCurMp, oldMaxMp, 0, 0,
                      _clock.ElapsedMilliseconds);
        return true;
    }

    /// <summary>One vital: its total and its current, at the game's own offsets.</summary>
    private bool ReadVital(long vital, out int current, out int total)
    {
        current = total = 0;
        if (!ReadInt(vital + VitalTotal, out total)) return false;
        if (!ReadInt(vital + VitalCurrent, out current)) return false;
        return true;
    }

    /// <summary>
    /// Whether a vital really is one, by its own back pointer.
    ///
    /// Every vital carries the address of the Life component that owns it. A
    /// pair of integers can look like a pool by coincidence - thousands do -
    /// but a pair sitting at exactly the right offset inside a structure that
    /// points back at itself cannot.
    /// </summary>
    private bool OwnsItself(long life, int vitalOffset)
    {
        if (!ReadPtr(life + vitalOffset + VitalOwner, out long owner)) return false;
        return owner == life;
    }

    private bool ReadPtr(long addr, out long value)
    {
        value = 0;
        var got = new byte[8];
        if (!ReadProcessMemory(_handle, (nint)addr, got, 8, out var read) || read.ToInt64() != 8)
            return false;

        value = BitConverter.ToInt64(got, 0);
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
    /// <summary>
    /// Waits for one of the tied components to prove it is the live one.
    ///
    /// Called between polls, so it costs three reads rather than a sweep of the
    /// whole address space, and it can afford to wait as long as it takes -
    /// which is until something happens to your character. If they all start
    /// moving at once they are not telling us apart from each other and the
    /// whole set is dropped rather than guessed between.
    /// </summary>
    private long SettlePending()
    {
        var moved = new List<int>();
        for (int k = 0; k < _pending.Count; k++)
        {
            if (!ReadVital(_pending[k].owner + _pending[k].health, out int now, out _))
            {
                // It stopped being readable at all, which is answer enough.
                moved.Clear();
                _pending = [];
                _pendingFirst = [];
                Log.Write("memory: one of the tied components vanished - searching again");
                return 0;
            }
            if (now != _pendingFirst[k]) moved.Add(k);
        }

        if (moved.Count == 0) return 0;

        if (moved.Count > 1)
        {
            Log.Write($"memory: {moved.Count} of the tied components moved together - "
                      + "they are copies of each other, searching again");
            _pending = [];
            _pendingFirst = [];
            return 0;
        }

        var win = _pending[moved[0]];
        _pending = [];
        _pendingFirst = [];

        _healthOff = win.health;
        _manaOff = win.mana;
        _shieldOff = win.shield;
        _structured = true;
        _manaDelta = win.mana - win.health;
        _curOffset = VitalCurrent - VitalTotal;

        ReadVital(win.owner + win.health, out int curHp, out int maxHp);
        ReadVital(win.owner + win.mana, out int curMp, out int maxMp);
        Log.Write($"memory: {win.owner:X} changed while the others sat still - that is "
                  + $"your character. life {curHp}/{maxHp}, mana {curMp}/{maxMp}");
        return win.owner;
    }

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

        // Every place holding your maximum life or mana, indexed by the
        // component that claims to own it.
        //
        // What was here before required your maximum mana to sit exactly one
        // vital's width from your maximum life - 0x58 bytes, the distance
        // between the two on one machine on one patch. That distance is
        // precisely the thing this was supposed to stop depending on, and when
        // it no longer held the search found twenty-five thousand candidates
        // and zero pairs, every time, forever.
        //
        // Nothing is assumed about the distance now. A vital names its owner,
        // so each candidate is asked who owns it; a health vital and a mana
        // vital that name the SAME owner are the two halves of one character,
        // whatever the gap between them turns out to be. The owner pointer sits
        // inside the buffer already read, so asking costs nothing.
        var hpBy = new Dictionary<long, long>();
        var mpBy = new Dictionary<long, long>();
        int hpSeen = 0, mpSeen = 0;
        var buf = new byte[32 * 1024 * 1024];

        // How far back the owner pointer sits from the maximum we matched.
        int ownerBack = VitalTotal - VitalOwner;

        foreach (var (start, size) in Regions())
        {
            if (_stop.IsCancellationRequested) return 0;
            for (long off = 0; off < size; off += buf.Length - 0x40)
            {
                int chunk = (int)Math.Min(buf.Length, size - off);
                if (chunk < 8) break;
                if (!ReadProcessMemory(_handle, (nint)(start + off), buf, chunk, out var got))
                    continue;

                // Chunks overlap by more than a vital, so a candidate skipped
                // here for want of the bytes behind it was already covered with
                // room to spare by the chunk before.
                for (int i = ownerBack; i + 4 <= (int)got; i += 4)
                {
                    int v = BitConverter.ToInt32(buf, i);
                    if (v != wantHp && v != wantMp) continue;

                    long owner = BitConverter.ToInt64(buf, i - ownerBack);
                    if (owner <= 0x10000 || owner > 0x7FFFFFFFFFFF) continue;

                    long total = start + off + i;
                    long inside = total - VitalTotal - owner;
                    if (inside <= 0 || inside > 0x2000) continue;

                    if (v == wantHp) { hpSeen++; hpBy.TryAdd(owner, total); }
                    else { mpSeen++; mpBy.TryAdd(owner, total); }
                }
            }
        }

        Log.Write($"memory: {hpSeen} vitals hold {wantHp}, {mpSeen} hold {wantMp}");

        // Which of those owners is a Life component.
        //
        // Requiring your maximum mana to be found as well was still a
        // dependency on a number that goes stale - your gear changes it, and
        // the log shows exactly what that costs: eighty-five plausible life
        // vitals, one mana vital, nothing in common, no lock, forever.
        //
        // A Life component does not need to be told. It owns SEVERAL vitals -
        // life, mana, energy shield - and every one of them points back at it.
        // A stray copy of a number sits in a structure that owns one thing, or
        // none. So the test is the shape of the component itself, and it holds
        // whatever your maxima happen to be today.
        long bestOwner = 0;
        var finalists = new List<(long owner, int health, int mana, int shield)>();
        int bestHealth = 0, bestMana = 0, bestShield = 0, bestVitals = 0;
        var window = new byte[0x800];

        foreach (var (owner, hpTotal) in hpBy)
        {
            if (_stop.IsCancellationRequested) return 0;

            int healthOff = (int)(hpTotal - VitalTotal - owner);
            if (healthOff + VitalCurrent + 4 > window.Length) continue;

            // One read of the whole component, then everything is local.
            if (!ReadProcessMemory(_handle, (nint)owner, window, window.Length, out var got)
                || (int)got < window.Length)
                continue;

            var owned = new List<int>();
            for (int at = 0; at + VitalCurrent + 4 <= window.Length; at += 4)
            {
                if (BitConverter.ToInt64(window, at + VitalOwner) != owner) continue;
                int total = BitConverter.ToInt32(window, at + VitalTotal);
                int cur = BitConverter.ToInt32(window, at + VitalCurrent);
                if (total <= 0 || total > 1_000_000) continue;
                if (cur < 0 || cur > total + total / 3) continue;
                owned.Add(at);
            }

            // Life, mana and energy shield. Fewer than three vitals pointing
            // home is not a character sheet, it is a coincidence with company.
            if (owned.Count < 3) continue;
            int idx = owned.IndexOf(healthOff);
            if (idx < 0 || idx + 2 >= owned.Count) continue;

            // They sit in order - life, then mana, then shield - so the two
            // that follow the one holding your life are the other two pools.
            int manaOff = owned[idx + 1];
            int shieldOff = owned[idx + 2];

            int manaTotal = BitConverter.ToInt32(window, manaOff + VitalTotal);
            int shieldTotal = BitConverter.ToInt32(window, shieldOff + VitalTotal);
            int hpCur = BitConverter.ToInt32(window, healthOff + VitalCurrent);
            int manaCur = BitConverter.ToInt32(window, manaOff + VitalCurrent);
            int shieldCur = BitConverter.ToInt32(window, shieldOff + VitalCurrent);

            // Three pools reading exactly the same thing is not a character.
            //
            // One of these did lock, and the reading it gave was "life
            // 651/1496, mana 651/1496, shield 651/1496" - the same two numbers
            // three times, at a suspiciously even spacing. Your life, mana and
            // shield are independent; a run of identical copies is a template
            // or an array, and it will never move when you take damage.
            int hpTotal32 = BitConverter.ToInt32(window, healthOff + VitalTotal);
            if (manaTotal == hpTotal32 && shieldTotal == hpTotal32
                && manaCur == hpCur && shieldCur == hpCur)
                continue;

            // Which one is YOU.
            //
            // Shape alone was never going to answer this, and the log shows why
            // in the plainest possible way: it locked onto "life 651/1496, mana
            // 1080/1232, shield 651/748" - a perfectly real, perfectly valid
            // Life component. It just belonged to something else. Every monster
            // in the zone has one, and one of them happens to share your
            // maximum life.
            //
            // So the component has to agree with things only your character
            // agrees with, and with more than one of them - any single number
            // has a twin somewhere in a zone full of creatures. Nothing here is
            // required, because any one of them can be stale or unreadable;
            // what is required is that the winner agrees about something and
            // that nothing else agrees about as much.
            // Not all agreements are worth the same. A saved maximum is only
            // as good as the last time it was saved; a value read off the
            // screen a moment ago cannot be stale at all. Counting them equally
            // produced a straight tie between the component that matched a
            // year-old mana number and the one that was actually the player -
            // and a tie means no lock, which is the same as being broken.
            int agree = 0;
            if (HintCurHp > 0 && Math.Abs(hpCur - HintCurHp) <= HintMaxHp / 20) agree += 5;
            if (HintCurMp > 0 && manaTotal > 0
                && Math.Abs(manaCur - HintCurMp) <= manaTotal / 20) agree += 4;
            if (HintMaxEs > 0 && shieldTotal == HintMaxEs) agree += 3;
            if (HintMaxMp > 0 && manaTotal == HintMaxMp) agree += 2;

            if (agree == 0) continue;

            Log.Write($"memory: candidate {owner:X} scores {agree} - {owned.Count} vitals, "
                      + $"life {hpCur}/{hpTotal32}, mana {manaCur}/{manaTotal}, "
                      + $"shield {shieldCur}/{shieldTotal}, at +{healthOff:X}");

            if (agree > bestVitals)
            {
                bestVitals = agree;
                finalists.Clear();
            }
            if (agree == bestVitals) finalists.Add((owner, healthOff, manaOff, shieldOff));
        }

        // When several agree equally well, watch which one is alive.
        //
        // This is not hypothetical. The game holds two components with your
        // exact maxima and an identical layout - one is you, the other reads
        // "life 1263/1496" and has read that ever since, a copy that nothing
        // writes to any more. Every number about them agrees; the only thing
        // that separates them is that yours changes.
        //
        // So they get watched. The one that moves is the one you are playing,
        // and if none of them moves, nothing is picked - a frozen readout is
        // the single worst thing this can do, and it is exactly what taking
        // either of these on a coin-flip produces.
        if (finalists.Count > 1)
        {
            Log.Write($"memory: {finalists.Count} components match equally - watching to "
                      + "see which one is alive");

            var first = new int[finalists.Count];
            for (int k = 0; k < finalists.Count; k++)
                ReadVital(finalists[k].owner + finalists[k].health, out first[k], out _);

            var moved = new List<int>();
            for (int pass = 0; pass < 24 && moved.Count != 1; pass++)
            {
                if (_stop.IsCancellationRequested) return 0;
                Thread.Sleep(100);
                moved.Clear();
                for (int k = 0; k < finalists.Count; k++)
                {
                    if (!ReadVital(finalists[k].owner + finalists[k].health,
                                   out int now, out _)) continue;
                    if (now != first[k]) moved.Add(k);
                }
            }

            if (moved.Count != 1)
            {
                // Kept, not thrown away.
                //
                // Standing at full life in town, none of these will move for as
                // long as you stand there - and the search used to give up,
                // wait five seconds, and rebuild the identical tie from a fresh
                // seven-gigabyte sweep, forever. The answer was there the whole
                // time; what was missing was the patience to wait for it.
                //
                // Watching three addresses costs nothing, so they are now kept
                // and checked between polls. The moment anything happens to
                // you - a hit, a flask, a regen tick - it settles itself,
                // without another sweep and without you being told to go and
                // make something happen.
                _pending = finalists;
                _pendingFirst = first;
                Log.Write($"memory: {finalists.Count} components still match equally - "
                          + "holding them and watching for the first one to change");
                Status = "more than one thing in the game matches your numbers - watching "
                         + "for the one that moves";
                _structured = false;
                return 0;
            }

            finalists = [finalists[moved[0]]];
        }

        if (finalists.Count == 1)
        {
            (bestOwner, bestHealth, bestMana, bestShield) = finalists[0];
        }

        if (bestOwner != 0)
        {
            _healthOff = bestHealth;
            _manaOff = bestMana;
            _shieldOff = bestShield;
            _structured = true;
            _manaDelta = bestMana - bestHealth;
            _curOffset = VitalCurrent - VitalTotal;

            ReadVital(bestOwner + bestHealth, out int curHp, out int maxHp);
            ReadVital(bestOwner + bestMana, out int curMp, out int maxMp);
            ReadVital(bestOwner + bestShield, out int curEs, out int maxEs);
            Log.Write($"memory: found the Life component at {bestOwner:X} - its vitals point "
                      + $"back to it. life {curHp}/{maxHp}, mana {curMp}/{maxMp}, "
                      + $"shield {curEs}/{maxEs}, at +{bestHealth:X} +{bestMana:X} "
                      + $"+{bestShield:X}");
            return bestOwner;
        }

        Log.Write($"memory: {hpBy.Count} owners hold {HintMaxHp} in a vital, none of them "
                  + "is a character with three pools that agrees with your other numbers");

        // No guessing when the structure does not match.
        //
        // The old fallback matched loose numbers by distance, and every frozen
        // reading, every wrong pool and every "reading 100% while you die" came
        // out of it - because a pair of integers that merely equals your maxima
        // is not a character, and there are thousands of them. Saying "still
        // looking" is worth far more than an answer that might be a
        // coincidence, since the numbers on screen carry you meanwhile.
        Status = hpBy.Count == 0
            ? $"no vital holds {HintMaxHp} - is that your maximum life? Press Find "
              + "numbers with your life full"
            : "the game's layout does not match what this knows - the numbers on "
              + "screen are being used instead";
        _structured = false;
        return 0;
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
