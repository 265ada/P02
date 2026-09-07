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
   is topped up, then **Check** to confirm it reads 100%.
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

Windows screen capture costs about **9 ms per call regardless of size** — a
32x32 grab costs the same as a 190x270 one, because the cost is per-call
synchronisation with the desktop compositor, not pixel work. Two globes is two
calls, so the loop tops out near **60 polls per second** on a typical machine.
Capturing both in one call, or on two threads, measured no better.

So the Polls/sec box goes up to 250, but the status line reports what the loop
**actually** achieved. If you ask for 200 and it reports 60, that is the ceiling
and asking for more only burns a core spinning.

Key presses are sent on their own thread. A press has to be held a few
milliseconds to register, and bursts have gaps; doing that on the poll loop used
to stop monitoring for the duration of the press, which is the worst possible
moment to stop looking.

Cooldowns go down to 20 ms and the panic gap to 10 ms. The real floor is the
hold time — at 20 ms hold, presses cannot leave faster than about 50 a second no
matter what the cooldown says.

## Safety rails

| Rail | What it does |
|---|---|
| Starts disarmed | Nothing is sent until you arm it, every launch. |
| Window match | Only fires while the focused window title contains your string. "Path of Exile" matches "Path of Exile 2" — it is a substring test, not an exact one. The status line shows the focused window's real title, so you can see what it is comparing against. |
| Cooldown | Minimum gap between presses, per globe. |
| Confirm frames | Two consecutive low reads required, so one flash frame can't fire it. |

The app runs unelevated on purpose. If the game is running as administrator,
Windows blocks our input — run the game normally, or nothing will happen.

## If it never fires

Work through it in this order.

1. **Test keys (3s)** sends the enabled globes' keys once, ignoring arm state and
   the window match. Click it, click into the game, and watch. Nothing happening
   means the problem is the keybind or permissions, not detection.
2. Check the status line's focused-window title against your match string.
3. If the game runs **as administrator** and P02 does not, Windows silently
   blocks our input. Run the game unelevated.
4. Check the reading moves in the UI as the globe drains. If it sits at 0% or
   100%, it is a detection problem — go back to Check.

## Settings

Everything is saved to `%APPDATA%\P02\config.json` as you change it, and again
on exit, so the app comes back exactly as you left it — including where the
window was. Writes are debounced and atomic, so dragging a slider does not
hammer the disk or risk a half-written file.

## Updates

**Check for updates** hits the GitHub Releases feed, downloads the new `P02.exe`
and restarts into it. It also checks once quietly at startup.

While the repo is **private**, that check needs a token — put a GitHub PAT with
`repo` scope in `%APPDATA%\P02\token.txt`, or set `P02_GITHUB_TOKEN`. Without
one the check just says no release was found and carries on.

## Sharing it later

The clean way is a second, **public** repo that holds only the releases. Then
anyone can download `P02.exe` and auto-update with no token, while the source
stays private. Point `Updater.Owner` / `Updater.Repo` at that repo when you're
ready. Handing out a PAT so other people can reach a private repo is the wrong
move — it's your account credential, and it can't be scoped to one repo's
downloads.

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
