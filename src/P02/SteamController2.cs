using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace P02;

/// <summary>One frame of the real controller's state, as read straight off the wire.</summary>
internal readonly record struct SteamController2State(
    bool A, bool B, bool X, bool Y,
    bool LB, bool RB,
    bool Up, bool Down, bool Left, bool Right,
    bool L3, bool R3,
    bool L4, bool L5, bool R4, bool R5,
    bool Start, bool Back, bool Guide,
    short LeftStickX, short LeftStickY, short RightStickX, short RightStickY,
    short TriggerLeft, short TriggerRight);

/// <summary>
/// Talks to a real Steam Controller 2 directly over HID - the way SteamlessController
/// and SC2Xbox do on this exact hardware - instead of asking Steam Input to translate
/// it first. Steam Input is a black box from the outside: it reads the real pad and
/// hands the game a virtual one of its own, and a second virtual pad from QytOCR pressing
/// alongside it is two sources of truth the game was never asked to reconcile. This
/// reads the controller itself, so QytOCR can be the one and only thing deciding what the
/// game sees - the real presses mirrored through faithfully, and an automatic one laid
/// on top exactly where a real one would go.
///
/// The protocol here - the vendor/product IDs, the "clear digital mappings" feature
/// report that turns off the controller's keyboard-and-mouse fallback, the 54-byte
/// report 0x42 and its button bits - is reverse-engineered and documented by
/// github.com/CouchTurtle/sc2-research (cross-checked against SDL3's own driver) and
/// implemented already by github.com/ddeverill/SteamlessController and
/// github.com/cgallizzi/SC2Xbox. None of it is guessed at, but none of it has been run
/// against the real hardware here either - there isn't one on this machine to test
/// with. Treat a first run's log as the actual proof, not this comment.
///
/// One real, load-bearing catch found in that same research: Steam can hold the
/// device open exclusively while it is actively managing it, which would shut this
/// out entirely. If "steam controller 2: no device found" persists with the
/// controller plainly connected, that is the first thing to rule out - try with
/// Steam fully closed before assuming the protocol itself is wrong.
/// </summary>
internal sealed class SteamController2 : IDisposable
{
    public const ushort VendorId = 0x28DE;

    /// <summary>Ibex (wired), Ibex BLE, Proteus (puck receiver), Nereid (Steam Machine).</summary>
    public static readonly ushort[] ProductIds = [0x1302, 0x1303, 0x1304, 0x1305];

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private SafeFileHandle? _handle;
    private Thread? _thread;
    private SteamController2State _latest;
    private bool _hasLatest;
    private long _lastLizardRefreshMs = long.MinValue / 2;
    private long _lastNoneLoggedMs = long.MinValue / 2;

    public bool Connected { get; private set; }
    public string Status { get; private set; } = "not started";

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "QytOCR steam controller 2" };
        _thread.Start();
    }

    /// <summary>The most recent frame, or false if nothing has ever been read.</summary>
    public bool TryGet(out SteamController2State state)
    {
        lock (_gate) { state = _latest; return _hasLatest; }
    }

    private void Run()
    {
        byte[] buf = new byte[64];

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_handle is null)
                {
                    _handle = Open();
                    if (_handle is null)
                    {
                        Connected = false;
                        Status = "no Steam Controller 2 found";
                        lock (_gate) _hasLatest = false;

                        // Once, not every two seconds forever - the wait below
                        // already paces the retry.
                        if (_clock.ElapsedMilliseconds - _lastNoneLoggedMs > 20000)
                        {
                            _lastNoneLoggedMs = _clock.ElapsedMilliseconds;
                            Log.Write("steam controller 2: no device found "
                                      + $"(looking for VID_{VendorId:X4}, PID one of "
                                      + string.Join('/', ProductIds.Select(p => $"{p:X4}"))
                                      + ") - if it is plugged in, try with Steam fully "
                                      + "closed, since Steam can hold it open exclusively");
                        }
                        _stop.Token.WaitHandle.WaitOne(2000);
                        continue;
                    }
                    Connected = true;
                    Status = "connected";
                }

                DisableLizardMode(_handle);

                if (!Native.ReadFile(_handle, buf, (uint)buf.Length, out uint got, 0) || got == 0)
                {
                    Log.Write("steam controller 2: stopped answering - looking for it again");
                    _handle.Dispose();
                    _handle = null;
                    Connected = false;
                    lock (_gate) _hasLatest = false;
                    continue;
                }

                if (buf[0] == 0x42)
                {
                    var state = Parse(buf);
                    lock (_gate) { _latest = state; _hasLatest = true; }
                    Status = "reading";
                }
                // Anything else - 0x43 battery, 0x7b status - is not what QytOCR needs
                // and is left alone; the last real 0x42 frame stays current.
            }
            catch (Exception ex)
            {
                Log.Write($"steam controller 2: {ex.Message}");
                _handle?.Dispose();
                _handle = null;
                Connected = false;
                lock (_gate) _hasLatest = false;
                _stop.Token.WaitHandle.WaitOne(2000);
            }
        }
    }

    /// <summary>
    /// Turns off the controller's own keyboard-and-mouse fallback ("lizard mode")
    /// so real button and stick data comes through instead. Resent every 800ms,
    /// the same interval SteamlessController uses, because the controller (or
    /// Steam noticing it) can turn it back on.
    /// </summary>
    private void DisableLizardMode(SafeFileHandle handle)
    {
        if (_clock.ElapsedMilliseconds - _lastLizardRefreshMs < 800) return;
        _lastLizardRefreshMs = _clock.ElapsedMilliseconds;

        // [report id | command | payload size | payload...]. 0x81 is
        // CMD_CLEAR_DIGITAL_MAPPINGS - documented by SteamlessController as the
        // command that turns lizard mode off.
        byte[] report = new byte[64];
        report[0] = 0x01;
        report[1] = 0x81;
        report[2] = 0x00;
        try { Native.HidD_SetFeature(handle, report, report.Length); }
        catch { /* best effort - tried again in 800ms regardless */ }
    }

    /// <summary>
    /// Bit positions from the report-0x42 layout documented at
    /// github.com/CouchTurtle/sc2-research (docs/HID_REPORT_FORMAT.md), counting
    /// from bit 0 of the little-endian uint32 at bytes 0x02-0x05.
    /// </summary>
    private static SteamController2State Parse(byte[] b)
    {
        uint buttons = BitConverter.ToUInt32(b, 2);
        bool Bit(int n) => (buttons & (1u << n)) != 0;
        short S(int offset) => BitConverter.ToInt16(b, offset);

        return new SteamController2State(
            A: Bit(0), B: Bit(1), X: Bit(2), Y: Bit(3),
            Back: Bit(6), R4: Bit(7),
            R5: Bit(8), RB: Bit(9),
            Down: Bit(10), Right: Bit(11), Left: Bit(12), Up: Bit(13),
            Start: Bit(14), L3: Bit(15),
            Guide: Bit(16), L4: Bit(17), L5: Bit(18), LB: Bit(19),
            R3: Bit(5),
            // Sticks and triggers: raw hall-effect/TMR values, passed through
            // rather than re-centred or re-scaled here. Not verified against real
            // hardware - if the left stick or triggers feel inverted or dead in
            // one direction once someone actually plays with this, that is a
            // sign bit or an offset here, not the report layout itself.
            LeftStickX: S(10), LeftStickY: S(12),
            RightStickX: S(14), RightStickY: S(16),
            TriggerLeft: S(6), TriggerRight: S(8));
    }

    /// <summary>Finds the device by VID/PID among every HID interface on the
    /// system, and opens it for reading and writing feature reports.</summary>
    private static SafeFileHandle? Open()
    {
        Native.HidD_GetHidGuid(out Guid hidGuid);
        nint infoSet = Native.SetupDiGetClassDevs(ref hidGuid, 0, 0,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (infoSet == 0 || infoSet == -1) return null;

        try
        {
            for (uint i = 0; ; i++)
            {
                var ifData = new Native.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!Native.SetupDiEnumDeviceInterfaces(infoSet, 0, ref hidGuid, i, ref ifData))
                    return null; // enumerated every HID device there is - not found

                Native.SetupDiGetDeviceInterfaceDetail(infoSet, ref ifData, 0, 0, out uint needed, 0);
                if (needed == 0) continue;

                nint detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    // The famous SetupDi quirk: cbSize describes only the fixed
                    // header (a DWORD plus alignment padding to the string that
                    // follows), never the buffer actually allocated for it.
                    Marshal.WriteInt32(detail, Environment.Is64BitProcess ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(infoSet, ref ifData, detail,
                            needed, out _, 0))
                        continue;

                    string path = Marshal.PtrToStringAuto(detail + 4) ?? "";
                    if (path.Length == 0) continue;

                    var handle = Native.CreateFile(path,
                        Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                        0, Native.OPEN_EXISTING, 0, 0);
                    if (handle.IsInvalid) { handle.Dispose(); continue; }

                    var attrs = new Native.HIDD_ATTRIBUTES
                    {
                        Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>(),
                    };
                    if (Native.HidD_GetAttributes(handle, ref attrs) && attrs.VendorID == VendorId
                        && Array.IndexOf(ProductIds, attrs.ProductID) >= 0)
                    {
                        Log.Write("steam controller 2: found it - "
                                  + $"VID_{attrs.VendorID:X4}&PID_{attrs.ProductID:X4}");
                        return handle;
                    }
                    handle.Dispose();
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(infoSet); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _handle?.Dispose();
    }
}
