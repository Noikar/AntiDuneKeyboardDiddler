# AntiDuneKeyboardDiddler

Detects and reverses injection of the ENG-US keyboard layout into the OS by Dune Awakening.

Dune Awakening adds the plain **US** keyboard layout when it launches and switches you onto
it. If you use English International, English UK, Swedish or anything else, your whole
desktop ends up on a layout you never asked for, and the unwanted one sticks around in your
language switcher afterwards.

This is a 35 KB tray utility that puts it back. It watches for the game, removes whatever
layout gets added, and returns you to the one you were actually typing in.

It does not touch the game in any way — no injection, no memory access, nothing sent to the
game's windows. Dune Awakening runs BattlEye, and this stays well clear of it.

## Install

Download it from the [latest release](../../releases/latest). No installer, no setup.

1. Grab `AntiDuneKeyboardDiddler-<version>-win.zip`.
2. **Before extracting**, right-click the zip → **Properties** → tick **Unblock** → **OK**.
   This saves you a security warning — see below.
3. Extract it anywhere you like and run `AntiDuneKeyboardDiddler.exe`.
4. Right-click the tray icon and tick **Start with Windows**.

That is it. Windows 10/11 only, and nothing else needs installing.

## Using it

A keyboard icon sits in the notification area next to the clock: **grey** when idle,
**green** while the game is running and it is actively guarding. There is no window — the
tray icon is the whole thing.

Right-click it for:

| | |
| --- | --- |
| **Open log** | What it has done, in plain text |
| **Show status...** | Your layouts vs. what is currently loaded |
| **Remove stray layouts now** | Clean up after a session that ran without it |
| **Guard even when the game is not running** | If something other than Dune does this to you |
| **Start with Windows** | Launch automatically at sign-in |
| **Exit** | Stop it |

When it puts your layout back you get a brief notification, so it never happens silently.

You can keep switching between your own layouts freely while it runs. It only removes
layouts that turn up on their own, and it learns which ones are yours from your Windows
settings — it never assumes a particular layout is the bad one.

**Playing something else that does this?** Open `AntiDuneKeyboardDiddler.ini` next to the
exe and put that game's process name in `WatchProcesses`.

## Windows will warn you about this

If you skipped the unblock step, running it gives you a blue **"Windows protected your PC"**
box. Click **More info → Run anyway**.

Nothing was found. That warning means Windows does not recognize the file, because it is
not signed with a code signing certificate — those cost money and need renewing every year,
which is a lot for a free tool this small. It will not go away on its own, either: unsigned
files earn trust purely through download numbers, and that trust is tied to one exact file,
so every new release starts over.

### If you would rather check first

Fair enough, for something that touches your keyboard settings. In order of how much each
one actually tells you:

- **Check the hash.** Every release lists the SHA-256 of what was uploaded. Run
  `Get-FileHash .\AntiDuneKeyboardDiddler.exe -Algorithm SHA256` and compare. Proves you
  got the exact file from the release page, nothing more.
- **Scan it.** Upload the exe to [VirusTotal](https://www.virustotal.com). *Expect a
  detection or two* — this tool legitimately looks at every window on your desktop, writes a
  startup entry, and changes keyboard settings, which is the shape of thing generic scanners
  flag. Check *which* engines complain: a couple of "Trojan.Generic" hits from minor engines
  is very different from a specific, named result the major ones agree on.
- **Have an AI read it.** The whole program is about 1,800 lines of commented C# in
  [`src/`](src/). Point Claude, ChatGPT or Copilot at this repository and ask what it does,
  whether it sends anything anywhere, or whether it touches the game. Small enough to read
  end to end in one go, which is not true of most things you install. It makes no network
  connections at all — there is no networking code in it.
- **Build it yourself.** The strongest option and one command, needing no developer tools.
  You run bytes you compiled from source you can read, and Windows raises no warning about
  locally built files. See [TECHNICAL.md](TECHNICAL.md#building).

## More detail

[TECHNICAL.md](TECHNICAL.md) covers how it decides what is a stray layout, the full
settings file, the command line, building from source, and the diagnostic tool used to work
out what the game was doing in the first place.

## Licence

MIT — see [LICENSE](LICENSE).
