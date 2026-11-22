# CompLock Updater

A small Windows program that installs the [CompLock](https://deadlockconfig.com/) pack for Deadlock and shows when one of
its mods has an update. It does what the Pack Builder on the site does, without the browser: pick the mods, press
Install, and the paks and gameinfo.gi go into the game folder.

By jcStani and HoppCX.

## Download

**[CompLock.zip](https://github.com/jcStani/CompLock-Updater/releases/latest/download/CompLock.zip)** is the newest
release: open the zip and run `CompLock.exe`, one file with nothing to install. Every release is built from its tag by
GitHub Actions (`.github/workflows/release.yml`), and its page under
[Releases](https://github.com/jcStani/CompLock-Updater/releases) gives the SHA-256 of the zip and of the exe.

## What it does

- Finds Deadlock through Steam (or a folder you pick) and reads what is already in `addons`.
- Lists the pack as the site has it: HUD++ and gameinfo.gi, every option with its versions and FPS numbers, and when
  each was last updated.
- Install or Apply puts the selection in place. A mod that is out of date gets its own Update button; changing the
  selection never updates other mods on the side.
- Recommended Video Settings sets the quality options of your `video.txt` to the game's lowest and the render scale
  to the one you pick (75% unless changed).
- It asks the site when the window opens and while it is open. Nothing runs in the background or at startup.

## What it changes on your PC

| Where | What |
|---|---|
| `game\citadel\addons\` | the pack's `pakNN_dir.vpk` files, `mods.txt`, and `complock.json` (its own record) |
| `game\citadel\gameinfo.gi` | the pack's gameinfo.gi; the file from before is kept as `gameinfo.gi.complock-bak` |
| `video.txt` (the one the game reads) | only with Recommended Video Settings on; the file from before is kept beside it |
| `%LOCALAPPDATA%\CompLock\` | the last pack data it loaded, the folder you picked, a log |
| `%APPDATA%\grimoire\mod-metadata.json` | only if Grimoire manages the same folder: the names of the pack's own paks (see below) |

It never touches game binaries or game memory, never asks for administrator rights, and adds nothing that starts by
itself. "Remove CompLock from the game" in the More menu takes the pack out again and puts the kept files back.

## Other mods and mod managers

- A pak of the pack that is already in `addons` is recognized by its content, whatever its number, and moved into
  place instead of downloaded.
- Another mod under a file name the pack needs is moved to a free number after a question. It is never overwritten.
- Another copy of one of the pack's mods (an older build, a fork) is asked about: replace it, or keep it as well.
- A file of the pack that you changed yourself is asked about before it is replaced, and can be kept.
- Search paths that a mod manager adds to gameinfo.gi (Grimoire's `citadel/grimoire`, Deadlock Mod Manager's block)
  are left in and are not treated as an update.
- Grimoire names a pak from its own record for that file name. After an install the app writes those records for
  the pack's paks from the mod info inside each pak, so Grimoire shows the right names.

## Safety of downloads

Every pak is checked against the size and CRC-32 the site gives and the SHA-256 prefix in its address, in a folder
beside `addons`; nothing in the game folder changes until every download checked out. The app does not install into a
game folder Deadlock is running from.

## Test

```powershell
powershell -ExecutionPolicy Bypass -File test.ps1
```

The test runs ten selections through the site's own Pack Builder script (in headless Edge or Chrome) and through the
app, and compares gameinfo.gi, `mods.txt`, the paks, the download size and the estimate. Then it installs from the
live site into a made-up game folder and goes through the cases above. It never touches a real Deadlock folder.

## Source

| File | What is in it |
|---|---|
| `src/Program.cs` | start-up, and the command-line modes the test uses |
| `src/Pack.cs` | the pack and a selection: the same logic as the site's `pack.js` |
| `src/Core.cs` | the site, Steam, the app's record, and the installer |
| `src/Compat.cs` | gameinfo.gi search paths, reading paks, mod managers, video settings |
| `src/Ui.cs` | the window |
| `src/Json.cs` | reading and writing JSON |

The exe is not signed, so Windows SmartScreen warns the first time it runs.

## Privacy

The app has no account, telemetry or analytics. While its window is open it downloads the pack's list and files from
deadlockconfig.com; those requests say which version of the app is asking and nothing else.

## License

[MIT](LICENSE)
