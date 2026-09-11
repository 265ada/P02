using System.Runtime.CompilerServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace P02;

/// <summary>
/// Presses on a controller, for people who play on one.
///
/// A game in controller mode ignores the keyboard outright, so every key this
/// sent went nowhere and the flasks simply never fired. There is no way to
/// press a real pad from software - the buttons are on the pad - so the only
/// honest route is to add a second one that is not physical, and press that.
///
/// That is what ViGEmBus is: a driver by Nefarius, the same one DS4Windows and
/// most of the controller-remapping world is built on, which lets a program
/// present a virtual Xbox pad to Windows. It has to be installed separately,
/// because it is a kernel driver and nothing here is going to put one of those
/// on your machine without being asked.
///
/// If it is not there, this says so plainly and the keyboard carries on as
/// before, rather than failing silently - which is what the last few evenings
/// were.
/// </summary>
internal static class Gamepad
{
    private static ViGEmClient? _client;
    private static IXbox360Controller? _pad;
    private static bool _tried;

    /// <summary>Why it is not working, in words worth showing somebody.</summary>
    public static string Why { get; private set; } = "";

    public static bool Available
    {
        get
        {
            Connect();
            return _pad is not null;
        }
    }

    /// <summary>
    /// The buttons worth binding a flask to, in the order they sit on a pad.
    /// Names are Xbox first with the PlayStation equivalent beside it, because
    /// the pad in your hands is one or the other and neither should have to be
    /// translated in your head.
    /// </summary>
    public static readonly (string Name, string Says)[] Buttons =
    [
        ("A", "A  /  Cross"),
        ("B", "B  /  Circle"),
        ("X", "X  /  Square"),
        ("Y", "Y  /  Triangle"),
        ("LB", "LB  /  L1"),
        ("RB", "RB  /  R1"),
        ("LT", "LT  /  L2"),
        ("RT", "RT  /  R2"),
        ("Up", "D-pad up"),
        ("Down", "D-pad down"),
        ("Left", "D-pad left"),
        ("Right", "D-pad right"),
        ("LS", "Left stick click  /  L3"),
        ("RS", "Right stick click  /  R3"),
    ];

    public static bool IsButton(string name) =>
        Array.Exists(Buttons, b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void Connect()
    {
        if (_tried) return;
        _tried = true;

        // The work is in a method of its own, called from inside the try.
        //
        // A missing or unloadable assembly does not fail where it is used - it
        // fails when the method that mentions it is compiled, and that happens
        // on entry to the method, before a single line of it runs. So a try
        // block wrapped around the code itself is never entered, the catch
        // never catches, nothing is logged, and from the outside the button
        // simply does nothing at all. Which is precisely what it did.
        //
        // Called across a method boundary, the failure lands at the call - and
        // the call is inside the try.
        try
        {
            string before = Native.ControllerSlots();
            Open();

            // A game looks for controllers when it starts and when one arrives.
            // A pad that appears in the same instant as the button press it is
            // meant to carry has not been noticed yet by anything - so it is
            // worth a moment, and worth saying where it landed.
            Thread.Sleep(400);
            Log.Write($"controller: a virtual pad is connected. Windows had slots "
                      + $"[{before}] before and [{Native.ControllerSlots()}] now");
            Why = "";
        }
        catch (Exception ex)
        {
            _pad = null;
            _client = null;
            Why = "The ViGEmBus driver is not installed, so there is no controller to "
                  + "press. Get it from github.com/nefarius/ViGEmBus/releases, install "
                  + "it, and restart P02. "
                  + $"({ex.GetType().Name})";
            Log.Write($"controller: no virtual pad - {ex.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Open()
    {
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
        _pad.Connect();
    }

    /// <summary>Forgets a failed attempt, so installing the driver needs no restart.</summary>
    public static void TryAgain()
    {
        _tried = false;
        Why = "";
    }

    /// <summary>
    /// Holds one button for a moment, the way a thumb would.
    ///
    /// The triggers are axes rather than buttons, so they are pushed to the top
    /// of their travel instead - a game watching for "trigger pressed" wants a
    /// value, not a flag.
    /// </summary>
    public static bool Press(string button, int holdMs)
    {
        Connect();
        if (_pad is null) return false;

        try
        {
            // Longer than a keyboard tap. A key is read as an event and a
            // single frame of it is enough; a controller button is read as a
            // state, polled once a frame, and a game running at sixty frames
            // can miss anything held for less than a couple of them.
            Tap(button, Math.Max(holdMs, 120));
            Log.Write($"controller: pressed {button} for {Math.Max(holdMs, 120)} ms");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"controller: press failed - {ex.Message}");
            Why = $"The virtual pad stopped answering: {ex.Message}";
            _pad = null;
            _tried = false;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Tap(string button, int holdMs)
    {
        Set(button, true);
        _pad!.SubmitReport();
        Thread.Sleep(Math.Clamp(holdMs, 20, 400));
        Set(button, false);
        _pad.SubmitReport();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Set(string button, bool down)
    {
        if (_pad is null) return;

        switch (button.ToUpperInvariant())
        {
            case "A": _pad.SetButtonState(Xbox360Button.A, down); break;
            case "B": _pad.SetButtonState(Xbox360Button.B, down); break;
            case "X": _pad.SetButtonState(Xbox360Button.X, down); break;
            case "Y": _pad.SetButtonState(Xbox360Button.Y, down); break;
            case "LB": _pad.SetButtonState(Xbox360Button.LeftShoulder, down); break;
            case "RB": _pad.SetButtonState(Xbox360Button.RightShoulder, down); break;
            case "UP": _pad.SetButtonState(Xbox360Button.Up, down); break;
            case "DOWN": _pad.SetButtonState(Xbox360Button.Down, down); break;
            case "LEFT": _pad.SetButtonState(Xbox360Button.Left, down); break;
            case "RIGHT": _pad.SetButtonState(Xbox360Button.Right, down); break;
            case "LS": _pad.SetButtonState(Xbox360Button.LeftThumb, down); break;
            case "RS": _pad.SetButtonState(Xbox360Button.RightThumb, down); break;
            case "LT": _pad.SetSliderValue(Xbox360Slider.LeftTrigger, down ? (byte)255 : (byte)0); break;
            case "RT": _pad.SetSliderValue(Xbox360Slider.RightTrigger, down ? (byte)255 : (byte)0); break;
        }
    }
}
