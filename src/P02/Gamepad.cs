using System.Runtime.CompilerServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
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
    private static IXbox360Controller? _xbox;
    private static IDualShock4Controller? _sony;
    private static bool _tried;

    /// <summary>
    /// Which kind of pad to pretend to be.
    ///
    /// It matters more than it sounds. Steam Input does not pass a controller
    /// through to a game - it reads yours and presents one of its own - and
    /// what it does with a third pad that turns up depends on which sort it
    /// thinks that pad is. Steam handles PlayStation controllers by a different
    /// path from Xbox ones, so if one is being swallowed the other is the next
    /// thing to try, and it costs nothing to offer both.
    /// </summary>
    public static string Kind { get; set; } = "xbox";

    /// <summary>Why it is not working, in words worth showing somebody.</summary>
    public static string Why { get; private set; } = "";

    public static bool Available
    {
        get
        {
            Connect();
            return _xbox is not null || _sony is not null;
        }
    }

    /// <summary>Drops the pad so a different kind can be put in its place.</summary>
    public static void Rebuild()
    {
        try { _xbox?.Disconnect(); } catch { }
        try { _sony?.Disconnect(); } catch { }
        _xbox = null;
        _sony = null;
        _client = null;
        _tried = false;
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
            _xbox = null;
            _sony = null;
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

        if (Kind.Equals("sony", StringComparison.OrdinalIgnoreCase))
        {
            _sony = _client.CreateDualShock4Controller();
            _sony.Connect();
        }
        else
        {
            _xbox = _client.CreateXbox360Controller();
            _xbox.Connect();
        }
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
        if (_xbox is null && _sony is null) return false;

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
            _xbox = null;
            _sony = null;
            _tried = false;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Tap(string button, int holdMs)
    {
        Set(button, true);
        Submit();
        Thread.Sleep(Math.Clamp(holdMs, 20, 400));
        Set(button, false);
        Submit();
    }

    private static void Submit()
    {
        _xbox?.SubmitReport();
        _sony?.SubmitReport();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Set(string button, bool down)
    {
        if (_sony is not null) { Sony(button, down); return; }
        if (_xbox is null) return;

        switch (button.ToUpperInvariant())
        {
            case "A": _xbox.SetButtonState(Xbox360Button.A, down); break;
            case "B": _xbox.SetButtonState(Xbox360Button.B, down); break;
            case "X": _xbox.SetButtonState(Xbox360Button.X, down); break;
            case "Y": _xbox.SetButtonState(Xbox360Button.Y, down); break;
            case "LB": _xbox.SetButtonState(Xbox360Button.LeftShoulder, down); break;
            case "RB": _xbox.SetButtonState(Xbox360Button.RightShoulder, down); break;
            case "UP": _xbox.SetButtonState(Xbox360Button.Up, down); break;
            case "DOWN": _xbox.SetButtonState(Xbox360Button.Down, down); break;
            case "LEFT": _xbox.SetButtonState(Xbox360Button.Left, down); break;
            case "RIGHT": _xbox.SetButtonState(Xbox360Button.Right, down); break;
            case "LS": _xbox.SetButtonState(Xbox360Button.LeftThumb, down); break;
            case "RS": _xbox.SetButtonState(Xbox360Button.RightThumb, down); break;
            case "LT": _xbox.SetSliderValue(Xbox360Slider.LeftTrigger, down ? (byte)255 : (byte)0); break;
            case "RT": _xbox.SetSliderValue(Xbox360Slider.RightTrigger, down ? (byte)255 : (byte)0); break;
        }
    }
    /// <summary>
    /// The same buttons on a PlayStation pad.
    ///
    /// Named for Xbox throughout, because that is what the picker offers and
    /// because a face button is a position before it is a letter: A is where
    /// Cross is, and both of them are the bottom one.
    /// </summary>
    private static void Sony(string button, bool down)
    {
        if (_sony is null) return;

        switch (button.ToUpperInvariant())
        {
            case "A": _sony.SetButtonState(DualShock4Button.Cross, down); break;
            case "B": _sony.SetButtonState(DualShock4Button.Circle, down); break;
            case "X": _sony.SetButtonState(DualShock4Button.Square, down); break;
            case "Y": _sony.SetButtonState(DualShock4Button.Triangle, down); break;
            case "LB": _sony.SetButtonState(DualShock4Button.ShoulderLeft, down); break;
            case "RB": _sony.SetButtonState(DualShock4Button.ShoulderRight, down); break;
            case "LS": _sony.SetButtonState(DualShock4Button.ThumbLeft, down); break;
            case "RS": _sony.SetButtonState(DualShock4Button.ThumbRight, down); break;

            case "LT":
                _sony.SetButtonState(DualShock4Button.TriggerLeft, down);
                _sony.SetSliderValue(DualShock4Slider.LeftTrigger, down ? (byte)255 : (byte)0);
                break;
            case "RT":
                _sony.SetButtonState(DualShock4Button.TriggerRight, down);
                _sony.SetSliderValue(DualShock4Slider.RightTrigger, down ? (byte)255 : (byte)0);
                break;

            // A PlayStation pad reports its d-pad as one direction rather than
            // four buttons, so letting go is a direction of its own.
            case "UP":
                _sony.SetDPadDirection(down ? DualShock4DPadDirection.North : DualShock4DPadDirection.None);
                break;
            case "DOWN":
                _sony.SetDPadDirection(down ? DualShock4DPadDirection.South : DualShock4DPadDirection.None);
                break;
            case "LEFT":
                _sony.SetDPadDirection(down ? DualShock4DPadDirection.West : DualShock4DPadDirection.None);
                break;
            case "RIGHT":
                _sony.SetDPadDirection(down ? DualShock4DPadDirection.East : DualShock4DPadDirection.None);
                break;
        }
    }
}
