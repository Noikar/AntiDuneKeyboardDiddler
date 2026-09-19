# AntiDuneKeyboardDiddler

Detects and reverses injection of the ENG-US keyboard layout into the OS by Dune Awakening.

Dune Awakening loads the plain **US** keyboard layout on launch and makes it the active
input locale. If you use English International, English UK, or anything else, your whole
desktop gets dragged onto a layout you did not ask for, and the unwanted layout stays in
your language switcher afterwards.

This is a ~23 KB watchdog that puts it back.

## What it actually does

While the game is running it watches the set of loaded input locales. Anything that shows
up which you did not configure yourself is treated as an intruder, and the tool:

1. restores `HKCU\Keyboard Layout\Preload` if anything wrote to it,
2. unloads the intruding layout so it disappears from the language switcher,
3. puts every affected window back onto the layout **you were on before the hijack**,
4. resets the system default input language if that got hijacked too.

When the game exits it does one final cleanup pass and goes back to idling.

That ordering matters and is not the obvious one. Removing a layout makes Windows drop
every thread that was using it onto the *system default input language*, so restoring
before the unload just gets overwritten by that fallback — which will leave you on
whichever layout happens to be your default rather than the one you were typing in. The
unload therefore comes first, and the restore is repeated for `SettleMilliseconds`
afterwards because the requests that carry it are asynchronous.

### What it does not do

It never touches the game. No injection, no memory access, no messages posted to game
windows, no driver, nothing that runs inside the game process. Everything is done with
documented `user32` input-locale calls against the rest of your desktop. Dune Awakening
runs BattlEye, and this tool stays entirely outside its business.

It also does not lock you to a single layout. It learns which layouts *you* configured
from the registry, and you can keep switching between them freely while it runs — it only
objects to layouts that appear out of nowhere.

## Install

```powershell
pwsh -NoProfile -File .\build.ps1
pwsh -NoProfile -File .\install.ps1
```

`install.ps1` copies the executable to `app\`, registers it to start with Windows, and
runs it. A keyboard icon appears in the notification area next to the clock — grey when
idle, green while it is guarding a running game. There is no console window and no main
window; the tray icon is the whole interface.

Autostart points at `app\` rather than `build\` on purpose, so that rebuilding never has
to fight a running copy for the file.

To undo: `pwsh -NoProfile -File .\install.ps1 -Uninstall`, or untick **Start with Windows**
in the tray menu. Everything is per-user — no administrator rights, no service, no
scheduled task, just a value under `HKCU\...\CurrentVersion\Run`.

## Windows will warn you about this

Downloading the release and running it gets you a blue **"Windows protected your PC"**
box. Click **More info → Run anyway**.

That warning does not mean anything was found. It is SmartScreen saying it does not
recognize the file, because the executable is not signed with a code signing certificate —
those cost money and have to be renewed yearly, which is a lot to ask of a free utility
this small. SmartScreen builds trust in an unsigned file from download volume alone, and
since that trust is tied to the exact file, every new release starts from zero again.

The cleanest way to avoid it entirely is to **unblock the zip before extracting**:

> Right-click the downloaded `.zip` → **Properties** → tick **Unblock** → **OK**, and
> *then* extract it.

Windows tags downloaded files with a marker that spreads to anything extracted out of
them, so clearing it on the zip clears it for the executable inside. Doing it afterwards
means unblocking the `.exe` separately.

### If you would rather check before you run it

Entirely reasonable for a utility that touches your keyboard settings. Any of these work,
in increasing order of how much they actually tell you:

**Check the hash.** Every release lists the SHA-256 of what was uploaded. Compare:

```powershell
Get-FileHash .\AntiDuneKeyboardDiddler.exe -Algorithm SHA256
```

Matching means you have the exact bytes from the release page and nothing altered them in
transit. It does not say anything about whether those bytes are trustworthy.

**Scan it.** Upload the `.exe` to [VirusTotal](https://www.virustotal.com), which runs it
past around seventy engines at once. Fair warning: **a detection or two would not be
surprising**, and would not mean much. This tool legitimately does several things that
heuristic scanners treat as suspicious in combination — it enumerates every window on the
desktop, posts messages to windows belonging to other processes, writes a startup entry to
the registry, and changes keyboard layout state. A tiny unsigned executable doing all that
is exactly the shape of thing generic heuristics flag. Look at *which* engines complain and
what they call it; a couple of "Trojan.Generic" style hits from the less selective engines
mean something very different from a specific, named identification agreed on by the major
ones.

**Ask an AI to read it.** The entire program is about 1,800 lines of commented C# across
nine files in [`src/`](src/), with no dependencies beyond Windows itself. Point Claude,
ChatGPT, Copilot or whatever you use at this repository and ask it what the code actually
does, whether anything sends data anywhere, or whether anything touches the game. It is
small enough to be read in full in one go, which is not true of most software you install.
Worth asking specifically: *does this make any network connections?* (It does not — there
is no networking code anywhere in it, and nothing to configure.)

**Build it yourself.** The strongest option, and it takes one command. You end up running
bytes you compiled from source you can read, and Windows does not warn about locally built
files at all, because they were never downloaded. See [Building](#building) — it needs no
SDK, no NuGet, and no project file, just the compiler already sitting in your Windows
install.

## The tray menu

Right-click the icon:

| Item | What it does |
| --- | --- |
| *(top line)* | Whether it is idle or currently guarding, and what it is guarding |
| **Open log** | Opens the plain-text log in Notepad |
| **Show status...** | Configured vs. loaded layouts, and what is currently active |
| **Remove stray layouts now** | Evicts anything uninvited right now, without waiting for the game |
| **Guard even when the game is not running** | For anything else that hijacks your layout |
| **Start with Windows** | Toggles the startup registration |
| **Exit** | Stops it |

Double-clicking the icon shows the status. A balloon appears when your layout gets put
back, so a correction is never silent; turn that off with `Notify = false` in the ini.

## Command line

Mostly useful for checking things or scripting:

```
AntiDuneKeyboardDiddler.exe                   Sit in the tray and guard (the normal case)
AntiDuneKeyboardDiddler.exe --status          Show configured vs. loaded layouts, then exit
AntiDuneKeyboardDiddler.exe --cleanup         Remove stray layouts once, then exit
AntiDuneKeyboardDiddler.exe --enforce-always  Guard continuously, not just during the game
AntiDuneKeyboardDiddler.exe --verbose         Log every individual correction
```

`--cleanup` is the one to run if the game already left a layout behind before you started
using this. `--status` is worth running once to check it has understood your setup: it
should list your own layouts as configured, and flag anything else as `INTRUDER`.

```
Configured layouts (from HKCU\Keyboard Layout):
  041D0809 (Swedish)
  F0010409 (United States-International)

Currently loaded:
  F0010409 (United States-International)  [ok]
  041D0809 (Swedish)                      [ok]
  04090409 (en-US)                        [INTRUDER]
```

Although it is a windowed application with no console of its own, `--status` and
`--cleanup` borrow the console of whatever terminal launched them, so they still print
normally.

## The log

Plain text, next to the executable, or in
`%LOCALAPPDATA%\AntiDuneKeyboardDiddler\` if that folder is not writable. It rotates at
512 KB. Normal running is quiet — a couple of lines at startup, then only arming,
disarming, and corrections:

```
2026-09-19 09:24:14.953  Guarding. Your layouts: 041D0809 (Swedish), F0010409 (United States-International)
2026-09-19 09:24:14.956  Watching for processes matching: DuneSandbox
2026-09-19 09:26:31.000  ARMED - DuneSandbox (pid 31872) is running. Holding F0010409 (United States-International)
2026-09-19 09:27:40.196  intruder layout appeared: 04090409 (en-US) - restoring F0010409 (United States-International)
2026-09-19 09:27:40.230  unload 04090409 (en-US) -> removed
```

## Configuration

`AntiDuneKeyboardDiddler.ini` sits next to the executable. Every setting is optional.

| Setting | Default | Meaning |
| --- | --- | --- |
| `WatchProcesses` | `DuneSandbox` | Case-insensitive process-name substrings, comma separated |
| `IdlePollMilliseconds` | `2000` | How often to look for the game |
| `ArmedPollMilliseconds` | `150` | How often to check the layout while the game runs |
| `UnloadRetryMilliseconds` | `3000` | Cooldown on removal attempts and on resetting the default language |
| `SettleMilliseconds` | `2000` | How long to keep restoring your layout after an eviction |
| `EnforceAlways` | `false` | Guard all the time, not just during the game |
| `Notify` | `true` | Show a tray balloon when your layout is put back |
| `Verbose` | `false` | Log every correction |

Dune Awakening runs three processes — `DuneSandbox` (launcher), `DuneSandbox_BE`
(BattlEye) and `DuneSandbox-Win64-Shipping` (the game). The default `DuneSandbox` matches
all three, so the guard is armed before the game window even exists.

To use this against a different game, put its process name in `WatchProcesses`.

## How layouts are identified

This is the part that is easy to get wrong, so it is worth writing down.

Windows identifies a layout by an 8-hex-digit **KLID** in `HKCU\Keyboard Layout\Preload`,
which may be redirected by `HKCU\Keyboard Layout\Substitutes`. What the API actually hands
you, though, is an **HKL**, and the mapping is not the identity:

* the low word is the language id of the entry **as listed in Preload**, surviving substitution
* the high word comes from the **substituted** layout: `0xF000 | "Layout Id"` if it has one
  (every secondary layout of a language does), otherwise its own language id

```
00000409 -> 00020409 (Layout Id 0001)  =>  F0010409   United States-International
00000809 -> 0000041d (no Layout Id)    =>  041D0809   Swedish, listed under English UK
00000409 -> not substituted            =>  04090409   plain US - what the game loads
```

That last line is the whole point: plain US and US-International share a language id, so
anything comparing languages cannot tell them apart. Comparing HKLs can. Getting this
wrong means mistaking one of your own layouts for the intruder and unloading it.

## Building

No SDK, no NuGet, no project file. The compiler that ships with Windows is enough:

```powershell
pwsh -NoProfile -File .\build.ps1
```

This compiles everything in `src\` into `build\AntiDuneKeyboardDiddler.exe`, a ~35 KB
single file that runs on any Windows 10/11 machine with no prerequisites — handy for
passing to whoever else you play with. The tray icon is drawn at run time rather than
shipped as an `.ico`, so there is genuinely nothing else to hand over but the one file.

| File | |
| --- | --- |
| `Program.cs` | Entry point, argument handling, single-instance guard |
| `TrayApplication.cs` | Tray icon, menu, and the timer that steps the guard |
| `Guard.cs` | The watchdog: detection, eviction, restoration |
| `LayoutRegistry.cs` | Reading the user's configured layouts, and the KLID to HKL rules |
| `Options.cs` | The ini file |
| `Log.cs` | The log file, with its fallback location |
| `Startup.cs` | The start-with-Windows registry entry |
| `TrayIcons.cs` | Drawing the icon |
| `NativeMethods.cs` | The Win32 declarations |

If it is running, exit it from the tray menu before rebuilding, or the compiler cannot
write over the executable.

## Diagnostics

`tools\Watch-KeyboardLayout.ps1` logs every layout change on the system, along with which
process was in the foreground. Run it, launch the game, and it records what happened:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Watch-KeyboardLayout.ps1
```

Window titles are **not** logged by default, so the log is safe to share; pass
`-IncludeWindowTitles` if you need them for your own debugging.

This is what identified the behavior in the first place: the game loads the layout into
the session only, and does not write to `Preload`, which is why the fix is an unload rather
than a registry fight.

## Related Windows setting

Independent of this tool, **Settings → Time & language → Typing → Advanced keyboard
settings → "Let me use a different input method for each app window"** limits the blast
radius of any app that does this, by giving each window its own input locale. It does not
stop the layout being added, but it stops a game changing what your other windows are
using. Worth turning on either way.
