# P02

Watches the Life and Mana globes in Path of Exile 2 and taps a key when either
one drops below a level you set.

It reads the globes by **pixel colour**, not by the numbers next to them. The
answer it produces is a *fraction* of the globe, so gear swaps, buffs and
anything else that moves your maximum change nothing — 40% is 40% whether your
pool is 1,440 or 3,000.

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
- Runs to the system tray.

## Setting it up

1. Start the game **borderless fullscreen** and get to a safe spot with both
   globes full.
2. Run `P02.exe`. Per globe: **Auto-find**, then **Full = 100%** while the globe
   is topped up, then spend it down and press **Empty = 0%**.
   Both calibrations matter — see below.
3. Set the trigger percentage and click the key box, then press the key you want
   sent — the flask slot, not a modifier.
4. Tick **Watch this globe** on the ones you want, then click **ARM**.

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

**Empty = 0%** fixes it. Spend the globe down, press it, and the app measures
what drained actually looks like in your box, compares it against what full
looked like, and puts the thresholds between the two. It then tells you what
the drained globe reads with the new settings — if that is not low, it says so
rather than pretending it worked.

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

It also panics on **rate**, not just level: if the globe is falling faster than
30% per second it switches to the short gap even while you are still above the
panic line, so a spike is caught on the way down rather than after it lands.

Presses keep coming for as long as you are below the trigger. Whether they do
anything is down to your charges — the app cannot see those.

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

A short, quiet ding when a key fires, so you know it acted without looking away
from the fight. Firing can repeat several times a second, so the gap setting
next to it is the minimum time between dings - 6 seconds by default. Set it to 0
for one per press, or untick it entirely.

## Safety rails

| Rail | What it does |
|---|---|
| Starts disarmed | Nothing is sent until you arm it, every launch. |
| Window match | Only fires while the focused window title contains your string. "Path of Exile" matches "Path of Exile 2" — it is a substring test, not an exact one. The status line shows the focused window's real title, so you can see what it is comparing against. |
| Cooldown | Minimum gap between presses, per globe. |
| Confirm frames | Two consecutive low reads required, so one flash frame can't fire it. |

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

## If it never fires

Work through it in this order.

1. Open **Check** and drain the globe. If the reading does not move, it is
   detection, not keys — do **Empty = 0%**.
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
