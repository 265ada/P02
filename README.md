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
- **Check** window that draws what the detector sees, with two live sliders for
  the only setting that ever needs tuning.
- **Self-updating** from GitHub Releases.
- Runs to the system tray.

## Setting it up

1. Start the game **borderless fullscreen** and get to a safe spot with both
   globes full.
2. Run `P02.exe`. Per globe: **Auto-find**, then **Check** to confirm the green
   line sits on the liquid surface.
3. Set the trigger percentage and click the key box, then press the key you want
   sent — the flask slot, not a modifier.
4. Tick **Watch this globe** on the ones you want, then click **ARM**.

Auto-find works by looking for the mass of red (or blue) pixels in the bottom
corner, so the globe has to be full when you press it. If it can't find one, use
**Set…** and drag the box yourself; that always works.

### If the Check window looks wrong

The green line should track the liquid surface at any fill level. If it sits at
the top when the globe is half empty, the detector is counting the frame or the
background as liquid — drag **Colour strictness** up. If it sits at the bottom
when the globe is full, drag **Min brightness** down.

## Safety rails

| Rail | What it does |
|---|---|
| Starts disarmed | Nothing is sent until you arm it, every launch. |
| Window match | Only fires while the focused window title contains your string. |
| Cooldown | Minimum gap between presses, per globe. |
| Confirm frames | Two consecutive low reads required, so one flash frame can't fire it. |

The app runs unelevated on purpose. If the game is running as administrator,
Windows blocks our input — run the game normally, or nothing will happen.

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
