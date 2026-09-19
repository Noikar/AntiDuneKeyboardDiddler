# AntiDuneKeyboardDiddler — technical notes

Everything the [README](README.md) deliberately leaves out. For people who want to know how
it works, change it, or reuse the approach against a different game.

## What it actually does

While the game is running it watches the set of loaded input locales. Anything that shows
up which the user did not configure is treated as an intruder, and the tool:

1. restores `HKCU\Keyboard Layout\Preload` if anything wrote to it,
2. unloads the intruding layout so it disappears from the language switcher,
3. puts every affected window back onto the layout the user was on before the hijack,
4. resets the system default input language if that got hijacked too.

When the game exits it does one final cleanup pass and goes back to idling.

That ordering matters and is not the obvious one. Removing a layout makes Windows drop
every thread that was using it onto the *system default input language*, so restoring
before the unload just gets overwritten by that fallback — which leaves the user on
whichever layout happens to be their default rather than the one they were typing in. The
unload therefore comes first, and the restore is repeated for `SettleMilliseconds`
afterwards because the requests that carry it are asynchronous.

### What it does not do

No injection, no memory access, no messages posted to game windows, no driver, nothing that
runs inside the game process. Everything is done with documented `user32` input-locale calls
against the rest of the desktop. Dune Awakening runs BattlEye, and this tool stays entirely
outside its business.

It also does not lock the user to a single layout. It learns which layouts they configured
from the registry, and they can keep switching between them freely while it runs.

## How layouts are identified

This is the part that is easy to get wrong, so it is worth writing down.

Windows identifies a layout by an 8-hex-digit **KLID** in `HKCU\Keyboard Layout\Preload`,
which may be redirected by `HKCU\Keyboard Layout\Substitutes`. What the API actually hands
back, though, is an **HKL**, and the mapping is not the identity:

* the low word is the language id of the entry **as listed in Preload**, surviving substitution
* the high word comes from the **substituted** layout: `0xF000 | "Layout Id"` if it has one
  (every secondary layout of a language does), otherwise its own language id

```
00000409 -> 00020409 (Layout Id 0001)  =>  F0010409   United States-International
00000809 -> 0000041d (no Layout Id)    =>  041D0809   Swedish, listed under English UK
00000409 -> not substituted            =>  04090409   plain US - what the game loads
```

That last line is the whole point: plain US and US-International share a language id, so
anything comparing languages cannot tell them apart. Comparing HKLs can. Getting this wrong
means mistaking one of the user's own layouts for the intruder and unloading it.

The detection itself is then one line — the set of loaded layouts minus the set derived
from the registry:

```csharp
return GetLoadedLayouts().Where(hkl => !expected.ContainsKey(hkl)).ToList();
```

So it is a whitelist of what the user configured, not a blacklist of any particular layout.
If plain US is genuinely installed one day, it lands in `Preload` and becomes allowed
automatically. IME entries (KLIDs starting `E0`) are skipped, since they do not follow the
Layout Id rule. If the expected set comes back empty, the tool refuses to act at all rather
than guess.

One known limitation: the expected set is snapshotted when the guard arms. A layout added in
Windows settings *while the game is running* would be seen as an intruder and evicted.

## Configuration

`AntiDuneKeyboardDiddler.ini` sits next to the executable. Every setting is optional.

| Setting | Default | Meaning |
| --- | --- | --- |
| `WatchProcesses` | `DuneSandbox` | Case-insensitive process-name substrings, comma separated |
| `IdlePollMilliseconds` | `2000` | How often to look for the game |
| `ArmedPollMilliseconds` | `150` | How often to check the layout while the game runs |
| `UnloadRetryMilliseconds` | `3000` | Cooldown on removal attempts and on resetting the default language |
| `SettleMilliseconds` | `2000` | How long to keep restoring the layout after an eviction |
| `EnforceAlways` | `false` | Guard all the time, not just during the game |
| `Notify` | `true` | Show a tray balloon when the layout is put back |
| `Verbose` | `false` | Log every correction |

Dune Awakening runs three processes — `DuneSandbox` (launcher), `DuneSandbox_BE` (BattlEye)
and `DuneSandbox-Win64-Shipping` (the game). The default `DuneSandbox` matches all three, so
the guard is armed before the game window even exists. This matters: in testing, the layout
was loaded 69 seconds after the launcher started.

## Command line

```
AntiDuneKeyboardDiddler.exe                   Sit in the tray and guard (the normal case)
AntiDuneKeyboardDiddler.exe --status          Show configured vs. loaded layouts, then exit
AntiDuneKeyboardDiddler.exe --cleanup         Remove stray layouts once, then exit
AntiDuneKeyboardDiddler.exe --enforce-always  Guard continuously, not just during the game
AntiDuneKeyboardDiddler.exe --verbose         Log every individual correction
```

`--status` is worth running once to check it has understood the setup. It should list the
user's own layouts as configured and flag anything else as `INTRUDER`:

```
Configured layouts (from HKCU\Keyboard Layout):
  041D0809 (Swedish)
  F0010409 (United States-International)

Currently loaded:
  F0010409 (United States-International)  [ok]
  041D0809 (Swedish)                      [ok]
  04090409 (en-US)                        [INTRUDER]
```

Although it is a windowed application with no console of its own, `--status` and `--cleanup`
borrow the console of whatever terminal launched them via `AttachConsole`, so they still
print normally.

## The log

Plain text, next to the executable, or in `%LOCALAPPDATA%\AntiDuneKeyboardDiddler\` if that
folder is not writable. It rotates at 512 KB. Normal running is quiet — a couple of lines at
startup, then only arming, disarming and corrections:

```
2026-09-19 09:24:14.953  Guarding. Your layouts: 041D0809 (Swedish), F0010409 (United States-International)
2026-09-19 09:24:14.956  Watching for processes matching: DuneSandbox
2026-09-19 09:26:31.000  ARMED - DuneSandbox (pid 31872) is running. Holding F0010409 (United States-International)
2026-09-19 09:27:40.196  intruder layout appeared: 04090409 (en-US) - restoring F0010409 (United States-International)
2026-09-19 09:27:40.230  unload 04090409 (en-US) -> removed
```

## Building

No SDK, no NuGet, no project file. The compiler that ships with Windows is enough:

```powershell
pwsh -NoProfile -File .\build.ps1
```

This compiles everything in `src\` into `build\AntiDuneKeyboardDiddler.exe`, a ~35 KB single
file that runs on any Windows 10/11 machine with no prerequisites. The tray icon is drawn at
run time rather than shipped as an `.ico`, so there is genuinely nothing to hand over but
the one file.

`install.ps1` then copies it to `app\`, registers it to start with Windows, and runs it.
Autostart points at `app\` rather than `build\` on purpose, so that rebuilding never has to
fight a running copy for the file. Re-run it after a rebuild to update the installed copy;
`install.ps1 -Uninstall` reverses it.

If it is running, exit it from the tray menu before rebuilding, or the compiler cannot write
over the executable.

| File | |
| --- | --- |
| `Program.cs` | Entry point, argument handling, single-instance guard |
| `TrayApplication.cs` | Tray icon, menu, and the timer that steps the guard |
| `Guard.cs` | The watchdog: detection, eviction, restoration |
| `LayoutRegistry.cs` | Reading the configured layouts, and the KLID to HKL rules |
| `Options.cs` | The ini file |
| `Log.cs` | The log file, with its fallback location |
| `Startup.cs` | The start-with-Windows registry entry |
| `TrayIcons.cs` | Drawing the icon |
| `NativeMethods.cs` | The Win32 declarations |

The guard is stepped from a WinForms timer rather than a background thread, so everything
touching the icon and menu stays on the thread that owns them with no marshalling. Each step
is a handful of cheap Win32 calls, and the interval switches between 2 s idle and 150 ms
armed on its own. A named mutex prevents a second copy fighting the first over the same
layouts.

## Diagnostics

`tools\Watch-KeyboardLayout.ps1` logs every layout change on the system, along with which
process was in the foreground:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Watch-KeyboardLayout.ps1
```

Window titles are **not** logged by default, so the log is safe to share; pass
`-IncludeWindowTitles` if they are needed for local debugging.

This is what identified the behavior in the first place. It showed that the game loads the
layout into the session only and never writes to `Preload`, which is why the fix is an
unload rather than a registry fight. It also showed the game skips the load entirely when
the layout is already present, so reproducing the bug requires starting from a clean layout
list.

## Related Windows setting

Independent of this tool, **Settings → Time & language → Typing → Advanced keyboard settings
→ "Let me use a different input method for each app window"** limits the blast radius of any
app that does this, by giving each window its own input locale. It does not stop the layout
being added, but it stops a game changing what other windows are using.

Also worth knowing: regional formatting (dates, times, decimal separators) comes from
`HKCU\Control Panel\International`, which is entirely separate from the keyboard layout
list. A layout kept purely to get a particular date format is not actually required for it.
