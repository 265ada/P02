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
        "300-900 ms suits most builds. This is the setting that controls how",
        "often it fires - not the hold time.",
        "",
        "It also stops on its own when presses are doing nothing: after three",
        "in a row that do not move the pool, it waits a few seconds rather than",
        "spending what is left of your charges on a flask that cannot use them.",
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
        "",
        "It is not a rate limit. A long hold does slow firing down, but only",
        "because each press takes that long to leave - and it slows the",
        "emergency press down with everything else. Use Cooldown to control how",
        "often it fires.",
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

    public static readonly string[] TuneColours =
    [
        "Works out which colours count as liquid, from this globe as it is now.",
        "",
        "It samples the globe drained and full and picks the threshold that",
        "separates them, instead of you guessing at the sliders. Use it if the",
        "reading does not follow the globe down, or after recolouring the",
        "interface.",
        "",
        "It says so plainly when colour alone cannot tell full from empty here -",
        "which happens with a poisoned globe, and is the reason to use the",
        "numbers instead.",
    ];

    public static readonly string[] Numbers =
    [
        "The numbers printed above the globe - 1,490/1,490 and so on.",
        "",
        "This is the reading worth having. It is exact, needs no calibration, it",
        "cannot confuse life with energy shield, and it does not care what colour",
        "the globe has turned. It also tells it there are no numbers on screen at",
        "all, which is how it stays quiet in menus and loading screens.",
        "",
        "It finds the box for you; drag one by hand only if it picks badly.",
    ];

    public static readonly string[] Key =
    [
        "The key pressed for this pool. Click and press it.",
        "",
        "It has to be the key your flask is actually bound to in game - the slot",
        "number, not the flask. Test keys is the way to check without dying to",
        "find out.",
    ];

    public static readonly string[] Uber =
    [
        "A last-resort press, one only, when everything else has missed.",
        "",
        "The cooldown and the hold both mean there are moments where nothing can",
        "be sent, and a hit landing in one of those is how you die at what looks",
        "like a safe fraction. Under this line it sends one press regardless.",
        "",
        "One press, not a stream - it re-arms only after you have recovered, so",
        "it cannot drain your charges. Capped at 30%; set it well under your",
        "trigger, around 15-20%.",
    ];

    public static readonly string[] ShieldOn =
    [
        "Fire this pool's flask for energy shield as well as for life.",
        "",
        "Off by default because it is wrong for most characters: shield already",
        "recharges on its own, and a flask does nothing for it. Tick it only if",
        "you have the passive or the item that makes flasks recover shield.",
        "",
        "Left off, shield is not just ignored - it is kept out of the life",
        "reading, which is otherwise a real source of misfires, because the",
        "shield is drawn over the same globe.",
    ];

    public static readonly string[] ShieldBelow =
    [
        "The fraction of your shield that fires the flask.",
        "",
        "Uses the same panic and emergency rules as life.",
    ];

    public static readonly string[] ShieldMax =
    [
        "Your maximum energy shield, or 0 to read it from its own numbers box.",
        "",
        "It has to be the shield maximum, not the life one - typing life here",
        "makes the shield reading follow your life, which fires at the wrong",
        "time in both directions.",
    ];

    public static readonly string[] AnyWindow =
    [
        "Clears the window filter, so it fires into whatever has focus.",
        "",
        "Only useful for testing against something that is not the game. Put the",
        "title back before playing.",
    ];

    public static readonly string[] OpenLog =
    [
        "Opens the folder holding the log and your settings file.",
        "",
        "The log says what it read and why it did or did not fire, every poll",
        "that mattered. Export diagnostics is the better thing to send, but this",
        "is where to look if you want to read it yourself.",
    ];

    public static readonly string[] CheckUpdates =
    [
        "Asks GitHub whether there is a newer build.",
        "",
        "It lists everything that changed since the version you are on, not just",
        "the newest release, and says how many you are behind. It also checks on",
        "its own at launch unless you turn that off.",
    ];

    public static readonly string[] UpdateAtLaunch =
    [
        "Checks for a new version each time it starts.",
        "",
        "One request, and nothing installs without you saying so.",
        "",
        "It also looks again every five minutes for releases marked critical -",
        "the ones that fix a way for this to sit quiet while you die. Those are",
        "the only ones that interrupt: thirty seconds of warning on the overlay,",
        "then your game is paused, and never while you are in a fight.",
    ];

    public static readonly string[] FindNumbers =
    [
        "Finds the printed numbers for both pools at once.",
        "",
        "This is the single step everything else depends on: it sets both boxes,",
        "fills in both maxima, keeps them up to date as you level, and gives the",
        "memory search the two maxima it needs to find anything. One number",
        "matches thousands of places in a heap; two pin it down.",
        "",
        "Have the game up with the numbers visible, then press it.",
    ];

    public static readonly string[] DingDisarmed =
    [
        "Ding for presses that were only decided on, while disarmed.",
        "",
        "Nothing is sent either way. It is for hearing whether it would have",
        "fired at the right moments before trusting it with your flasks - which",
        "is worth an evening.",
    ];

    public static readonly string[] Status =
    [
        "What it is doing right now: polls a second actually achieved, how long",
        "each poll takes, and where the readings are coming from.",
        "",
        "This is the line that says whether memory has locked on, and what it is",
        "still missing if it has not.",
    ];

    public static readonly string[] Focus =
    [
        "Why it is or is not firing at this moment.",
        "",
        "Disarmed, the focused window not matching, and nothing being readable",
        "all look identical from the outside. This says which one it is.",
    ];

    public static readonly string[] Live =
    [
        "The last thing that happened, as it happens: what was read, what was",
        "decided, and whether a key went out.",
    ];
}
