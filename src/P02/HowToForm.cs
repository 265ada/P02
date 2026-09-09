namespace P02;

/// <summary>
/// What to do, in order, in words anybody can follow.
///
/// Everything here was true before and written down nowhere. It lived in
/// tooltips, in a dialog somebody had already dismissed, and in the heads of
/// the two people who had watched it fail. Somebody handed this application by
/// a friend had no way in at all.
///
/// Numbered because it genuinely is a sequence - each step needs the one above
/// it to have worked - which is the only honest reason to number anything.
/// </summary>
internal sealed class HowToForm : Form
{
    private const string Steps = """
        SETTING IT UP  -  about a minute, once

        1.  Start Path of Exile 2 and get into the game properly.

            Stand in a town or your hideout. Not the login screen, not the
            character select, not paused. Your life and mana numbers have to
            be on screen, because that is what it reads.

        2.  Press "Set it up for me".

            The window hides for a second while it looks at the game, then
            tells you what it found. That is the whole setup. If it worked it
            says so, and says which key it will press.

        3.  Set your flask key, if it is not already right.

            On the Life panel, click the box marked "Key" and press the key
            you use for your life flask in game. Not the flask - the key.

        4.  Click "Armed".

            It is doing nothing until you do. It starts disarmed every time it
            opens, on purpose. F8 arms and disarms it while you play, so you
            do not have to come back to this window.

        THAT IS IT. Everything below is optional.


        IF SOMETHING SEEMS WRONG

        Press "What is wrong?".

        It checks everything and tells you what is broken, worst first, and
        the one thing that fixes each. It is not a log; it is a list of
        answers. Nine times out of ten the answer is "press Set it up for me
        while standing somewhere safe".

        The usual cause: the game only draws your numbers while you are
        actually playing. Not on the death screen, not in a menu, not while
        paused. If it was set up on one of those screens it read nothing.


        WHAT THE MAIN NUMBERS MEAN

        On the Life panel, the line at the bottom says where the reading is
        coming from:

            "memory, life 1,900/2,065"   - the best case. Exact, and refreshed
                                           about seventy times a second.
            "numbers, 1,900/2,065"       - read from the text beside your
                                           globe. Also exact, a little slower.
            "Holding fire ..."           - it cannot see anything it trusts,
                                           so it is deliberately doing nothing.

        "Holding fire" is not a crash. It is the app refusing to guess.


        THE SETTINGS THAT ACTUALLY MATTER

        Fire below      The trigger. 65% suits most builds. Set it higher than
                        feels necessary - a flask heals over a couple of
                        seconds, so firing at the last moment lands the
                        recovery after the hit that killed you.

        Cooldown        How often it may fire while you stay low. 850 ms is
                        sensible. This is your rate limit, not the hold time.

        Presses per     Leave at 1. Three presses per trigger is what empties
        trigger         a full set of charges in one fight.

        My max life     Leave at 0. It fills itself in and keeps up as you
                        level. Typing a number here is how people end up with
                        a stale one that breaks everything.


        THE READOUT OVER THE GAME

        The "P" button at the top right pins a small readout over the game:
        your life, the exact numbers, what would fire, and how many presses
        this fight.

        Ctrl and right-click on it opens its own options - lock it in place,
        or let clicks pass straight through into the game. Hold Ctrl to make
        it solid again while click-through is on. Right-clicking the "P"
        button brings it back if it ever gets away from you.


        UPDATES

        It looks after itself. Small updates install on their own after ten
        seconds of warning, and it comes back armed if it was armed. Updates
        that fix something that can get you killed pause your game first and
        say so. It never restarts you mid-fight.
        """;

    public HowToForm()
    {
        Text = "How to use P02";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 620);
        MinimumSize = new Size(520, 400);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,

            // A monospaced face because this is laid out in columns, and a
            // proportional one would tear the alignment apart.
            Font = new Font("Consolas", 9.75f),
            Text = Steps.Replace("\n", Environment.NewLine),
        };
        body.Select(0, 0);

        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Theme.Bg };
        pad.Controls.Add(body);

        var close = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 34,
            DialogResult = DialogResult.OK,
        };
        Theme.Primary(close, Theme.Accent);

        Controls.Add(pad);
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
    }
}
