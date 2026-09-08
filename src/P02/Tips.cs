namespace P02;

/// <summary>
/// Every explanation in one place.
///
/// A setting whose name you have to guess at is a setting that gets left wrong,
/// and several of the worst failures here came from exactly that: a maximum
/// typed into the wrong box, a burst set faster than a key can physically be
/// sent, a colour slider that did nothing. Each of these says what the thing
/// is for, when it is worth changing, and what to set it to.
/// </summary>
internal static class Tips
{
    private static readonly ToolTip Tip = new()
    {
        AutoPopDelay = 30000,
        InitialDelay = 350,
        ReshowDelay = 120,
        ShowAlways = true,
    };

    public static void On(Control c, params string[] lines) =>
        Tip.SetToolTip(c, string.Join(Environment.NewLine, lines));

    // --- per globe ------------------------------------------------------

    public const string Watch =
        "Whether this pool is watched at all.\n\n"
        + "Switched off it is not even read: a screen capture costs the same "
        + "whether the answer is used or not, and skipping one is worth about "
        + "20 ms of every poll.";

    public static readonly string[] FireBelow =
    [
        "The trigger. Below this fraction it presses your flask key.",
        "",
        "Set it above where you are comfortable rather than at the edge - a",
        "flask recovers over a couple of seconds, so firing at the last moment",
        "means the recovery lands after the hit that killed you.",
        "",
        "Around 60-70% for life on a character that takes real hits; lower if",
        "you are mostly chipping through trash and want to keep charges.",
    ];

    public static readonly string[] Cooldown =
    [
        "The gap between presses while you sit below the trigger.",
        "",
        "It cannot go below what a burst takes to send, which the line under",
        "these settings works out for you. Setting it lower does nothing except",
        "make requests get skipped.",
        "",
        "300-900 ms suits most builds.",
    ];

    public static readonly string[] PanicBelow =
    [
        "Under this, it stops waiting: the short gap below is used instead of",
        "the cooldown, and it fires on the first low reading rather than",
        "waiting for a second one to confirm.",
        "",
        "It also triggers on rate - if the pool is falling faster than 30% a",
        "second it panics even while still above this line, so a spike is",
        "caught on the way down instead of after it lands.",
        "",
        "Half your trigger is a reasonable start.",
    ];

    public static readonly string[] PanicGap =
    [
        "The gap between presses while panicking.",
        "",
        "Same floor as the cooldown: a burst takes as long as it takes to send.",
        "60-260 ms.",
    ];

    public static readonly string[] Presses =
    [
        "How many presses one trigger sends.",
        "",
        "Worth knowing before raising this: a flask recovers over a duration and",
        "the recovery is cancelled the moment the pool fills, so a second press",
        "landing while the first is still working spends a charge for almost",
        "nothing. Three presses per trigger is what emptied a full set of",
        "charges in one fight.",
        "",
        "Leave at 1 unless one charge genuinely does not cover a hit. If it does",
        "not, raise the panic settings instead - those fire again only while you",
        "are still low.",
    ];

    public static readonly string[] Hold =
    [
        "How long the key is held down.",
        "",
        "A game reads input once a frame, so a press shorter than one frame can",
        "go down and back up between two of them and never register - 20 ms is",
        "invisible below about 50 fps.",
        "",
        "70 ms spans a frame down to 14 fps and is the default. Raise it if the",
        "ding sounds and nothing happens in game; there is no benefit above",
        "about 120 ms, and every press costs that long to send.",
    ];

    public static readonly string[] KnownMax =
    [
        "Your maximum for this pool, or 0 to work it out.",
        "",
        "Left at 0 it is read from the numbers and filled in, and kept up to",
        "date as you level. That is usually the right choice.",
        "",
        "Setting it by hand makes the misread check exact - a stray digit turns",
        "1,465 into 11,465, which reads as 13% and fires at full health - but a",
        "stale value refuses every reading, so it is only worth typing if the",
        "numbers cannot be read at all.",
    ];

    public static readonly string[] Region =
    [
        "The box on screen holding this globe, for the pixel fallback.",
        "",
        "Only used when nothing better is available. The pixels cannot tell life",
        "from energy shield, because the shield is drawn over the same globe,",
        "and they read a poisoned globe as empty because it turns green.",
        "",
        "Set the numbers up instead and this stops mattering.",
    ];

    public static readonly string[] SetRegion =
    [
        "Drag a box around this globe by hand.",
        "",
        "Use it when Auto-find picks badly. Crop to the liquid only - no frame,",
        "no gargoyle - then press Full = 100% with the globe topped up.",
    ];

    public static readonly string[] AutoFind =
    [
        "Finds this globe by looking for a large round region of its colour in",
        "the corner of the game where it lives.",
        "",
        "The globe has to be full when you press it. It sweeps a range of",
        "colour thresholds and keeps the largest disc any of them finds, then",
        "calibrates against the full globe and checks the result reads 100% -",
        "saying so plainly if it does not, rather than accepting a bad box.",
    ];

    public static readonly string[] FullHundred =
    [
        "Records where the liquid sits when the globe is full.",
        "",
        "A box drawn by hand always carries some frame above the liquid, and",
        "every one of those rows counts as missing - 24 rows of frame on a",
        "270-row box reads as 91% at full health, and no colour setting can fix",
        "it. This is what does.",
        "",
        "Press it with the globe topped up. Re-drawing the region clears it.",
    ];

    public static readonly string[] Check =
    [
        "A live view of what the detector sees.",
        "",
        "Green line = the liquid surface it found, gold = your trigger. Spend",
        "the globe and watch the number follow it down; if it stays near 100%",
        "while the globe drains, that is the bug.",
        "",
        "Tick the mask box to see exactly which pixels it counts as liquid.",
    ];

    // --- global ---------------------------------------------------------

    public static readonly string[] WindowMatch =
    [
        "Only fires while the focused window's title contains this.",
        "",
        "It is a substring test, so \"Path of Exile\" matches \"Path of Exile 2\".",
        "Clearing it lets it fire into whatever has focus, which is rarely what",
        "anyone wants.",
    ];

    public static readonly string[] PollHz =
    [
        "How many times a second the screen is read.",
        "",
        "Screen capture costs about the same whatever the region size, so this",
        "has a ceiling set by your machine rather than by the setting - the",
        "status line reports what it actually achieved. Asking for more than",
        "that only spins a core.",
        "",
        "60 is a good target. Reading from memory instead is far faster than",
        "any of this.",
    ];

    public static readonly string[] ArmKey =
    [
        "A hotkey that arms and disarms without alt-tabbing.",
        "",
        "It works while the game has focus, which is the point.",
    ];

    public static readonly string[] TestKeys =
    [
        "Sends your keys once, ignoring arm state and the window check.",
        "",
        "Click it, click into the game, and watch. If the flask does not fire,",
        "the problem is the keybind or input reaching the game - not detection.",
        "That is the fastest way to split those two apart.",
    ];

    public static readonly string[] Diagnostics =
    [
        "Writes everything needed to explain a misbehaving setup to one zip:",
        "what each globe reads right now as numbers and pictures, every",
        "setting, which monitor things are on, the scancode each key resolves",
        "to, and the recent log.",
        "",
        "It ends with a verdict listing anything that looks wrong. The plain",
        "text version beside it is meant to be pasted into a chat.",
    ];

    public static readonly string[] Ding =
    [
        "A short sound when a key is actually sent.",
        "",
        "It is the difference between \"it decided to fire\" and \"something went",
        "out\", which is otherwise very hard to tell apart while playing.",
    ];

    public static readonly string[] DingGap =
    [
        "The least time between two dings.",
        "",
        "Firing can repeat several times a second and a ding per press is a",
        "machine gun. A few seconds is enough to know it is working.",
    ];

    public static readonly string[] Volume =
    [
        "Loudness over the original level, in dB. 0 is unchanged.",
        "",
        "Up to +16 is pure level. Past that the sound is already at full scale,",
        "so the extra comes from filling in the gap between its sharp peak and",
        "its quiet tail - louder, and a little harder-edged. Nothing clips at",
        "any setting.",
        "",
        "Start around +12 and go up only if it is getting lost under the game.",
    ];

    public static readonly string[] SendBy =
    [
        "How the key is delivered.",
        "",
        "Injected input goes into the system as a hardware scancode, which is",
        "what most games read.",
        "",
        "Posted to window sends it straight to the game window instead. It",
        "reaches a window without focus, and some games take it where injected",
        "input is missed - and some ignore it entirely, because it never touches",
        "the keyboard state raw input reads.",
        "",
        "If the ding sounds and nothing happens in game, try the other one",
        "before changing anything else.",
    ];

    public static readonly string[] Memory =
    [
        "Read life and mana out of the game's memory instead of the screen.",
        "",
        "Exact, and far more responsive: it refreshes every 15 ms where the",
        "numbers manage 60-160 ms. It is also the most intrusive thing here -",
        "reading another process is what anti-cheat looks for, where watching",
        "the screen is passive.",
        "",
        "It hardcodes no offsets, so it survives patches: given your maxima it",
        "finds them near each other in the heap and learns the layout from",
        "whichever spacing the candidates agree on.",
    ];

    public static readonly string[] Rescan =
    [
        "Searches memory again from scratch.",
        "",
        "Rarely needed - it re-searches on its own when a reading stops making",
        "sense, and when your maximum changes. Use it after switching character.",
    ];

    public static readonly string[] Pin =
    [
        "A small always-on-top readout over the game: the pools you watch, the",
        "numbers being read, whether it is armed, a light when a key goes out,",
        "and a count of presses in the current fight.",
        "",
        "It has no background - only the readouts show - so drag it by any part",
        "of it. Right-click this button to bring it back if it goes missing.",
    ];

    public static readonly string[] HideCapture =
    [
        "Hides this window from screen capture, so it cannot be read as a globe",
        "when it covers one.",
        "",
        "Off for a reason: it hides the window from every capture path on the",
        "system - screenshots, the Snipping Tool, Discord and OBS all see",
        "nothing. Moving the window is usually the better answer.",
    ];
}
