# P02

Watches the Life and Mana globes in Path of Exile 2 and taps a key when either
one drops below a level you set.

It can read your life and mana three ways.

**Game memory** - exact, instant, and immune to everything that can go wrong
with looking at a screen. It is also by far the most intrusive: reading another
process is what anti-cheat looks for, where watching the screen is passive. Off
unless you turn it on, and it asks before it does.

**It needs both maxima, and it will not guess without them.** Put your maximum
life and mana in the **My max** boxes on each panel - both, even if you only
watch one globe. Nothing is searched for until they are there.

**Tell it your maximum.** A heap holds thousands of number pairs shaped exactly
like a health pool - ward at 90/90 is indistinguishable from life at 90/90, and
that is precisely what it locked onto once. Put your maximum life and mana in the
**My max** boxes and the search has something to aim at; leave them at 0 and it
is guessing. A memory reading whose maximum does not match what you entered is
ignored and triggers a fresh search, so a wrong lock corrects itself instead of
sitting there being confidently wrong.

It assumes no layout at all, because assuming one is what made it read ward as
life and a mana pool of 17,750. Given your two maxima it finds them sitting near
each other in the heap and learns the distance between them from whichever
distance the candidates agree on, then learns which side of a maximum its
current value sits on the same way. Two known values in one structure is a much
stronger signature than one known value at a guessed offset, and the only thing
it needs to stay true across patches is that life and mana live near each other.

Current above maximum is expected - both pools overstack - so only the maxima
are required to match, and a reading whose maxima drift away from what you
entered starts a fresh search rather than being reported. Published ones go stale on the first patch - the set
this was built from resolved to a null pointer within two months of being
written. It searches for the *shape* of the structure instead: maximum and
current life as adjacent integers, mana 0x50 further on, energy shield 0x88
further still, every current value inside its maximum. A struct's layout changes
far less often than where a pointer to it lives, so this survives patches. The
sweep covers writable heap only - about 7 GB of 23 - and measured 3.3 GB/s, so
roughly two seconds on first use. If more than one candidate matches, the
numbers read off the screen pick between them, and failing that it watches which
one moves.

**Numbers on screen** - press **Find numbers** and it locates them itself, then
reads each one back and tells you what it will be watching. That last part
matters: a box a few pixels out lands on the line below, and life reading the
shield value looks perfectly healthy right up until it kills you. Two stats
reading the same numbers is called out as well. The
OCR engine reports where every word it reads was, so there is no need to drag a
box around anything: it looks in the corners of the game for the words Life,
Mana and Shield, and the numbers beside them are the region. Setting a box by
hand still works, and should **include the label word**.

That word matters. A box around the life numbers almost always catches shield
and ward as well, and those are number pairs too - ward at 90/90 read as life
is a misfire waiting to happen, and it happened. The line is picked by its
label first, by matching your **My max** second, and only by position if
neither is available. Ward, incidentally, cannot be helped by a flask at all,
so firing at it is pure waste.

**Numbers** - point it at the `1,465/1,465` beside the globe and that becomes
what decides. It is an exact ratio, needs no calibration and no colour tuning,
and because the maximum is read too, gear and buffs that move your pool change
nothing. Windows does the reading, so there is nothing extra to install. **Nobody has to type a maximum.** Left at 0, it is read from the numbers and
filled in, which is also what lets the memory search find you - the numbers give
the maximum, the maximum finds the character, and no field needs touching.

**A maximum that changes looks after itself.** Levelling and gear move it, and a
stated maximum that has gone stale refuses every reading in silence - which
looks exactly like the app being broken. The numbers on screen already carry the
true maximum, so when they have insisted on a different one for long enough to
rule out a misread, it is adopted: the setting updates, the memory search is
pointed at the new value, and the panel says what changed. Nothing to maintain.

Every
reading is checked before it is believed, and **filling in My max is what makes
that check exact**. A stray leading digit turns 1,465 into 11,465; a current of
1,465 against that reads as 13%, which is under any trigger, so it fires while
you are at full health. Told what your maximum is, a reading that disagrees is
simply dropped. Without one, a maximum that changes by more than a fifth has to
repeat five times before it is believed, since a real maximum barely ever
changes and a misread one changes constantly. The maximum has to look like a
character's pool and has to repeat before a change to it is accepted, since a
misread maximum is the difference between 40% and 4%. Numbers are only read
within a line, never across one - a box that also catches the shield line turned
a life of 1,465 and a shield of 2,005 into a maximum of 14,652,005, which reads
as 0% life at full health. And a text reading that disagrees with the globe
pixels by more than 40 points is discarded: the pixels are crude, but they are
never wildly wrong.

Current above maximum is normal here - skills push life past the pool - so that
is not treated as an error. It is simply clamped: above maximum is above full,
and above full does not fire.

Setting the numbers does one more thing worth having: **the numbers are only
drawn on the gameplay screen**. Open an inventory, the passive tree, a vendor or
the atlas and they vanish - and those screens cover the globe too, so the pixel
fallback ends up reading the panel and firing at it. Losing the numbers for more
than a couple of seconds is taken as "not looking at the game", and P02 holds
fire and stays silent until they come back. Short gaps still fall back to
pixels, because OCR misses the odd frame.

**A better source never falls back quietly.** Once memory or the numbers are
asked for, the globe pixels are not allowed to decide in their place. A source
that has been working and drops out for a moment gets a couple of seconds of
grace, because a heal should not wait on one missed frame - but a source that
has never once produced a reading gets none at all. It is not having a hiccup,
it is not set up, and two seconds is long enough for a globe turning green to
spend every charge you have. Silently dropping to a worse source is how a setup that looks
configured misfires anyway.

**Globe pixels** - the last fallback, used between text reads and when no text
region is set. Worth knowing what they cannot do. **The life globe is not always red**: poison
and other debuffs recolour it, and a green globe has no red in it at all, so a
colour test reads it as empty the moment the colour changes - a jump from full
to nothing that looks exactly like a killing blow. **Tune colours** switches to
judging brightness instead when colour turns out not to separate full from
empty, which survives a recolour: the liquid is bright whatever colour it has
been turned, and the drained part stays dark.

And **energy shield is drawn over the life globe**, so the pixels follow shield loss as well as life loss and will
pot for a shield that is draining while life is untouched. Numbers and memory
both read the life value itself and do not have this problem. Each panel names
which source is deciding, and the pinned readout shows the percentage in amber
whenever it is the pixels. The pixel path produces a
*fraction* of the globe, so gear swaps and buffs change nothing there either -
40% is 40% whether your pool is 1,440 or 3,000.

## What you get

- **Two independent watchers.** Life only, Mana only, both, or neither. Each has
  its own region, trigger point, key and cooldown.
- **Click to arm**, or a global hotkey (F8 by default) so you can toggle it
  without alt-tabbing.
- **Auto-find** for both globes, with a manual drag-a-box fallback.
- **Panic response** — the harder you are dropping, the faster it pots.
- **Check** window that draws what the detector sees, with two live sliders for
  the only setting that ever needs tuning.
- **Self-updating** from GitHub Releases.
- **Pin** in the top right corner puts a small readout over the game: the globes
  you are actually watching, the numbers being read, whether it is armed, and a
  light that blinks when a key goes out, and a count of presses sent in the
  current fight on the far right. A fight is life going down: rises mean nothing
  is hitting you, since every class regenerates. The count clears once life has
  not dropped for eight seconds, so it tells you what a fight cost rather than
  what the session did. It has no background or border - only
  the readouts show - so drag it by any part of it; the position is saved as
  soon as you let go. If it ever goes missing, right-click the pin or use
  **Bring overlay back** on the tray icon. A globe that is switched off is
  left out entirely rather than sitting there saying "off".
- **Energy shield**, off by default. Nothing recovers shield from a flask
  unless something in your build makes it do so, so firing at it is waste for
  most characters and the whole point for a few. It shares the life flask's key
  and *all* of its timing - cooldown, panic gap, presses, hold - because panic
  is a rule about how hard you are being hit, not about which pool is taking it.
  Only the trigger, its own Numbers box and its own maximum are separate. It
  needs that Numbers box: shield is drawn over the life globe, so no colour
  reading can separate them.

  Its **My max** is your maximum *shield*. Putting your life maximum there makes
  it read your life, since the maximum is one of the things used to pick which
  line of the HUD to read - so it says so if you do.
- Runs to the system tray.

## Setting it up

1. Start the game **borderless fullscreen** and get to a safe spot with both
   globes full.
2. Run `P02.exe` and press **Find numbers**. It looks in the corners of the
   game for the words Life, Mana and Shield and points itself at whatever it
   finds. That is the whole setup - no boxes to drag, no calibration.

   If it cannot find them, **Numbers…** on a panel lets you drag the box
   yourself; include the label word.
3. Optionally set up the globe itself as a fallback: **Auto-find**, then
   **Full = 100%** while topped up, then spend it to about half and press
   **Tune colours**.
4. Set the trigger percentage and click the key box, then press the key you want
   sent — the flask slot, not a modifier.
5. Tick **Watch this globe** on the ones you want, then click **ARM**.

Auto-find sweeps a range of colour thresholds and keeps the largest disc any of
them finds. One fixed threshold cannot work: strict, and only the bright core of
the liquid passes - and that core is itself disc-shaped, so the box comes out a
fraction of the globe; loose, and the globe merges into its frame. It then
calibrates against the full globe and checks the result actually reads 100%,
saying so plainly when it does not rather than accepting a bad box silently.

Capture reads the screen, so a P02 window sitting over a globe gets read as the
globe. The status line says so when that happens, rather than P02 quietly
reading the wrong thing.

There is a **Hide from screen capture** option for this, and it is off for a
reason: the flag it uses hides the window from *every* capture path on the
system - screenshots, the Snipping Tool, Discord and OBS all see nothing. It was
briefly on by default, which made P02 impossible to screenshot or share. Moving
the window is the better fix.

Auto-find looks in the corners of the **game window**, found by the same title
match, and falls back to the screen your other globe is on. It used to search the
corners of the whole virtual desktop, which on a two-monitor setup meant the
bottom-right search landed on the *other monitor* — so Life worked and Mana
never did. If it fails it now tells you exactly which rectangle it searched.

Auto-find uses the same colour settings as the Check window, so if the globe
does not light up green there, auto-find cannot see it either — fix it there
first. It looks for a large round region of red (or blue) in the bottom corner,
so the globe has to be full when you press it. It works on connected regions,
which is what keeps the blue skill gems and flasks next to the mana globe out of
the result. If it still can't find one it tells you what it saw instead — use
**Set…** and drag the box yourself, which always works.

### A globe reads 100% while it is visibly draining

This is the failure that stops it firing at all, and it looks like nothing is
wrong: armed, focused, dying, charges untouched.

The drained part of the life globe is **the same red as the liquid, only
darker**. A colour test alone cannot tell them apart, so with the Colour margin
low the empty globe counts as full, the reading never falls below the trigger,
and nothing ever fires. No amount of slider fiddling fixes this reliably,
because there is no single colour rule that separates them.

**Tune colours** is the attempt at a fix. Spend the globe down, press it, and it
measures what drained actually looks like in your box, compares it against what
full looked like, and puts the thresholds between the two. Then it reads the
drained globe back: if that still comes out high, the tuning did not work, so it
is **thrown away rather than saved** and it offers to set up the numbers
instead.

It asks for a globe **part way down** rather than an empty one, and reads both
colours out of the same frame: above the liquid is drained, below it is full,
lit identically at the same instant.

That is not fussiness. An empty globe is unreachable - life regenerates, so it
never sits at zero while alive - and the one place it does sit at zero is the
death screen, which paints everything red and makes anything measured there
useless for comparing against a globe seen during play.

Some globes simply cannot be separated by colour at all - full and drained are
the same hue, differing only in brightness, and the two ranges overlap. That is
not a setting to find, and no amount of retrying will find it. The numbers have
neither problem.

Then open **Check**. It is live now: spend the globe and watch the number follow
it down. If it sits at 100% while the globe empties, that is the bug, and it is
visible in one second instead of at the wrong moment in a boss fight.

### A full globe reads 91%, and no slider fixes it

That is the box, not the colours. A box you drew by hand carries some frame
above the liquid, and every one of those rows counts against you — 24 rows of
frame on a 270-row box is exactly 91%. No colour setting can move the top of
your box, which is why the sliders feel useless here.

Press **Full = 100%** with the globe topped up. It records where the liquid
actually starts and ends inside the box, and from then on the reading is
measured against the globe rather than the box edges. Re-drawing the region
clears the calibration, so calibrate after you set a region, not before.

### If the green line is in the wrong place

Tick **Show exactly which pixels count as liquid** in the Check window. Every
pixel the detector accepts turns bright green, so you can see what it is
actually reading instead of guessing.

The green line should track the surface at any fill level. If it sits at the top
when the globe is half empty, something above the liquid is being counted —
raise **Colour margin**. If it sits at the bottom when the globe is full, lower
**Colour margin**, then **Min brightness**.

**Save image** writes a before/after pair to `%APPDATA%\P02` if you need to show
someone what it sees.

## How fast it pots

One press every cooldown is right for a slow bleed and far too slow for a big
hit, so each globe has two speeds:

| Setting | What it does |
|---|---|
| Cooldown | Normal gap between presses while below the trigger. Default 900 ms. |
| Panic below | Under this fraction, switch to the short gap and fire on the first low frame instead of waiting for a second. |
| gap | The short gap. Default 260 ms. |
| Presses per trigger | Send the key more than once, for when one charge does not cover the hit. |
| Hold each press | How long the key is held down. A game reads input once a frame, so a press shorter than one frame can go down and up between two of them and never register - 20 ms is invisible below about 50 fps. Default 70 ms, which spans a frame down to 14 fps. Raise it if the ding sounds but nothing happens in game. |

It also panics on **rate**, not just level: if the globe is falling faster than
30% per second it switches to the short gap even while you are still above the
panic line, so a spike is caught on the way down rather than after it lands.

Presses keep coming for as long as you are below the trigger. Whether they do
anything is down to your charges — the app cannot see those.

## Injected or posted keys

**Injected input** is the default: the key goes into the system as a hardware
scancode, which is what most games read.

**Posted to window** sends the keypress straight to the game window instead. It
reaches a window that does not have focus, and some games accept it where
injected input is missed - and some ignore it completely, because it never
touches the keyboard state that raw input reads. Which applies is a matter for
testing, so both are there. If the ding sounds and nothing happens in game,
this is worth trying before anything else.

## Speed, and what is actually achievable

A globe that is switched off is not captured at all. That sounds obvious, but
it used to be read anyway to keep the UI live, and a capture costs the same
whether you use the answer or not - measured at **19.6 ms per poll** given back
by switching one globe off.

Windows screen capture costs about **9 ms per call regardless of size** on an
idle desktop, and closer to **20 ms with a game running** — a
32x32 grab costs the same as a 190x270 one, because the cost is per-call
synchronisation with the desktop compositor, not pixel work. Two globes is two
calls, so the loop tops out near **60 polls per second** on a typical machine.
Capturing both in one call, or on two threads, measured no better.

So the Polls/sec box goes up to 250, but the status line reports what the loop
**actually** achieved, and how many milliseconds of work each poll took. If you ask for 200 and it reports 60, that is the ceiling
and asking for more only burns a core spinning.

Key presses are sent on their own thread. A press has to be held a few
milliseconds to register, and bursts have gaps; doing that on the poll loop used
to stop monitoring for the duration of the press, which is the worst possible
moment to stop looking.

Cooldowns go down to 20 ms and the panic gap to 10 ms. The real floor is the
hold time — at 20 ms hold, presses cannot leave faster than about 50 a second no
matter what the cooldown says.

## Sound

A short ding when a key fires, so you know it acted without looking away from
the fight. Firing can repeat several times a second, so the gap setting
next to it is the minimum time between dings, in milliseconds - 6000 by
default. Set it to 0
for one per press, or untick it entirely.

The ding sounds when a key is actually sent. It can also sound while
**disarmed**, when a globe crosses its trigger, but that is off by default and
lives behind the *when disarmed* box: it is identical to the sound of a real
press, which makes "did it fire?" harder to answer rather than easier. With it
on, the panel still says what would have happened. Nothing is sent - disarmed means no key
ever leaves P02 - so this is the safe way to confirm a trigger point by ear
before arming. It only does this while disarmed, not merely while another window
has focus, so alt-tabbing at low health stays quiet.

Volume is in dB over the original level, so 0 is exactly what it always was. Up
to **+16 dB** that is pure level. Past that the peak is already at full scale -
there is nowhere higher to go in a 16-bit sound - so the extra comes from
filling in the gap between the sharp peak and its quiet tail with a soft curve.
The maximum, **+26**, measures **+22.7 dB** louder than the original with **no
clipped samples at all**; it does sound a little harder-edged, which is the
trade. Everything is normalised to an exact peak after being built, so clipping
is impossible by construction rather than by picking a cautious multiplier.

## Safety rails

| Rail | What it does |
|---|---|
| Starts disarmed | Nothing is sent until you arm it, every launch. |
| Window match | Only fires while the focused window title contains your string. "Path of Exile" matches "Path of Exile 2" — it is a substring test, not an exact one. The status line shows the focused window's real title, so you can see what it is comparing against. |
| Cooldown | Minimum gap between presses, per globe. |
| Confirm frames | Two consecutive low reads required, so one flash frame can't fire it. |
| Ignore below | A reading at or under this counts as nothing seen. With the grace period below, that is what stops a loading screen drawing an endless stream of presses. |
| Blind grace | How long a globe may read nothing before it counts as unseen rather than nearly empty. About a second: long enough that a crash to 1% life still fires, short enough that a loading screen goes quiet almost at once. |

The app runs unelevated on purpose. If the game is running as administrator,
Windows blocks our input — run the game normally, or nothing will happen.

## Charges are not being spent even though it is firing

This is a different failure from "not firing", and the log tells them apart:
`Open log folder` and look for lines like `Life: '0' x3 at 12.0% PANIC`. If
those are there, detection and firing are fine and the presses are the problem.

**Check the key is the right key.** Click the key box and press the actual key
you use. Binds are the number row and letters; the numpad is not supported, and
the box says so if you press one.

**Check the game is not elevated.** If the game runs as administrator and P02
does not, Windows silently discards our input. Nothing logs an error; the
presses simply never arrive.

**Presses cannot outrun themselves.** A 3-press burst takes about a fifth of a
second to physically send. Asking for one every 50 ms is four times faster than
that, so requests made while a burst is still going out are refused and counted
rather than queued — the log line shows `skipped=`. A high skipped count means
the cooldown is set far below what can actually be sent, not that anything is
broken. Raise the cooldown or lower Presses per trigger.

## Export diagnostics

The button writes a zip and a matching plain-text report to `%APPDATA%\P02`:

- what P02 sees in each globe right now, as numbers and as before/after images
- every setting, including calibration and the learned full/empty values
- which monitor each globe region is on, and where the game window is
- the scancode each configured key resolves to, so a numpad mix-up is obvious
- the recent log, and the update log
- a short verdict listing anything that looks wrong

Your update token is never included, and anything token-shaped in the log is
redacted. The `.txt` is meant to be pasted straight into a chat.

## Does the press actually do anything?

Flasks in Path of Exile 2 recover **over a duration**, and the recovery is
cancelled the moment the resource fills, so the remainder is wasted. There is no
documented cooldown between uses - the real limit is charges, and a press with
none available does nothing at all. Pressing again mid-recovery costs another
charge for recovery that will be cut short anyway.

So P02 watches the globe for a second after firing - but only as a hint, and it
never gates firing. **Every class regenerates, and life and spell leech refill
the globe as well**, so a rise on its own proves nothing. Only the two ends of
the scale mean anything:

- **A large jump** looks like a flask, because regen could not do that inside a
  second.
- **No movement at all** means nothing healed you: no charges, the wrong key, or
  input not reaching the game. That one is worth acting on, and the panel says
  so loudly after three in a row.
- Anything in between is reported as exactly that - a small rise that could be
  regen or leech.

## It is armed and not firing

Check what the readout says next to the armed state. **held** means it is
deliberately not acting, and the reason is beside it:

| It says | What it means |
|---|---|
| `cannot read the globe` | The globe reads nothing at all. Usually the box is not on it - but also what happens when the globe is recoloured by poison, since a green globe has no red to find. |
| `no exact reading yet` | Memory or the numbers were asked for and have never produced a reading. The pixels are not allowed to stand in for them. |
| `numbers not on screen` | An inventory, the passive tree or a vendor is up. Nothing to do. |

None of those are a timing problem, and none are fixed by making panic faster:
it is not firing late, it is declining to act on a reading it does not trust.
The fix is always to give it a reading it does trust - **Numbers...** is the
quickest.

## A globe reading 0% forever

This is the failure that hides the best, because it looks like nothing at all:
no firing, no error, and a globe reading 0% is indistinguishable from one that
is simply full as far as anything visible goes. It means the box is not on the
globe.

It is called out now: a watched globe that reads nothing for eight seconds gets
a red line in its panel saying so. Long enough that being dead or on a loading
screen does not trip it.

Two things follow from a globe reading 0%, and they pull in opposite directions,
which is what made this so confusing to diagnose:

- **It will not fire**, once the globe has read nothing for eight seconds
  straight. A *single* low reading is never treated that way - doing so refused
  to fire at 1% life, which is the exact moment it is needed most.
- **It used to ding anyway.** The disarmed would-fire ding did not apply that
  same rule, so it chirped continuously about a globe it could not read. The
  sound looked like proof that keys were being sent when the firing path was
  deliberately doing nothing. Both paths follow the same rule now.

## The ding sounds but the flask is not used

That narrows it a long way. The ding plays on the same path as the key press,
so detection, the trigger, the window check and the send all worked - the press
left P02 and the game did not act on it. In order of likelihood:

1. **The press is too short.** Raise **Hold each press**. This was 20 ms by
   default and is the single most likely cause below 50 fps. Set Presses per
   trigger to 1 while testing, so each ding is exactly one press and the result
   is unambiguous.
2. **Wrong key.** Click the key box and press the actual flask key.
3. **No charges.** P02 cannot see them; it will happily press into an empty
   flask.
4. **The game is elevated and P02 is not.** Windows discards the input with no
   error anywhere. Export diagnostics reports this.

## If it never fires

Work through it in this order.

1. Open **Check** and drain the globe. If the reading does not move, it is
   detection, not keys — do **Tune colours**.
2. **Test keys (3s)** sends the enabled globes' keys once, ignoring arm state and
   the window match. Click it, click into the game, and watch. Nothing happening
   means the problem is the keybind or permissions, not detection.
3. Check the status line's focused-window title against your match string.
4. If the game runs **as administrator** and P02 does not, Windows silently
   blocks our input. Run the game unelevated.

While armed, the log records both readings every two seconds along with whether
the window matched, so a session that failed to fire can be explained after the
fact instead of guessed at. **Open log folder** gets you there.

## Settings

Everything is saved to `%APPDATA%\P02\config.json` as you change it, and again
on exit, so the app comes back exactly as you left it — including where the
window was. Writes are debounced and atomic, so dragging a slider does not
hammer the disk or risk a half-written file.

## Updates

**Check for updates** hits the GitHub Releases feed, downloads the new `P02.exe`
and restarts into it. It also checks once quietly at startup, and only speaks
up when there is something to install.

The swap is done by a small batch file that waits for P02 to exit, copies the
new exe over the old one and starts it again, logging to
`%APPDATA%\P02\update.log`. Every command in it is called by full path:
Git for Windows puts Unix tools on PATH, and a bare `find` in a batch file
resolving to Unix `find` was enough to break the wait and let the copy race a
process that still held the exe.

If P02.exe sits somewhere unwritable, the update says so before downloading
50 MB rather than after.

If the check reports a failure, the message names what it found rather than
guessing: which HTTP status came back, and what state the optional token file is
in.

No token is needed. The repository is public, so the releases feed and its
assets are readable by anyone. A `token.txt` in `%APPDATA%\P02` is still honoured
if present, which keeps a private fork working, but you can delete it.

## Sharing it

Send someone the [releases page](https://github.com/265ada/P02/releases). They
download `P02.exe`, run it, and get updates automatically from then on - no
account, no token, no installer. The exe is self-contained, so there is no
runtime to install either.

Windows SmartScreen will warn on first run, because the exe is unsigned. That is
expected for any unsigned binary; "More info" then "Run anyway" gets past it.

## Licence

MIT - see [LICENSE](LICENSE). Do what you like with it, no warranty.

## Releasing

Tag and push; the workflow builds and publishes the exe.

```
git tag v0.2.0
git push origin v0.2.0
```

The tag drives the version the app reports, so the updater compares like for
like. Bump the `<Version>` in `P02.csproj` to match when you tag.

## Building

Needs the .NET 10 SDK.

```
dotnet publish src/P02/P02.csproj -c Release -o publish
```

One self-contained `P02.exe`, about 50 MB, no runtime to install.

## Fair warning

This is third-party input automation. It violates GGG's terms of service and
puts the account at risk. Your call.

## Layout

| File | What's in it |
|---|---|
| `OrbDetector.cs` | the fill-fraction and auto-find algorithms |
| `ScreenCapture.cs` | reused-bitmap screen grabs |
| `KeySender.cs` | scancode `SendInput` |
| `MonitorEngine.cs` | the poll loop and firing rules |
| `MainForm.cs` / `GlobePanel.cs` | the UI |
| `Updater.cs` | GitHub Releases check and self-replace |
| `tests/DetectorTests` | synthetic HUD corners the detector must get right |

Run the detector tests with `dotnet run --project tests/DetectorTests`.
