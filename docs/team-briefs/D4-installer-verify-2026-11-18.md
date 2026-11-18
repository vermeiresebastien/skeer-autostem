# D2 + D4 — rebuild, then the 1.1.0 -> 1.1.0 installer upgrade, measured

Run 2026-11-18, against the tree at `411eb32e` + the C# hunks that D1 still owes. Driver:
`run-D4-installer.ps1`. Setup log: `D4-installer-upgrade-2026-11-18.log` (139 lines, exit 0 owned by
`Start-Process -Wait -PassThru`, not inferred).

## 1. The tuple, three copies, one hash each

| file | bytes | sha256 |
|---|---|---|
| `launch/SkeerAutoSTEM.exe` | 76,076,612 | `69ae281fc0200a7acbe1ff0bb24cc3c0050dc7e38787b287b50c718b1e8f92b2` |
| `...\Programs\Skeer autoSTEM\SkeerAutoSTEM.exe` (installed) | 76,076,612 | same |
| `launch/autostem.exe` | 75,516,929 | `4d5068f4f24923ecba7493c569b4223d1a5ff4733ddade413b9d74bb61ae9e65` |
| `...\Programs\Skeer autoSTEM\autostem.exe` (installed) | 75,516,929 | same |
| `launch/Skeer_autoSTEM_Setup_v1.1.0.exe` | 138,923,701 | `2aecaa3ad81976ccc6f1b298f9198140ed7651849e762b2bb913eb2f2b746a95` |

The installed pair moved off the 2026-11-15 build (`1596100012c5f9e5…` / `ed0ab91c61b743cf…`) to the
values above, so this was a real content change, not a no-op re-run. Both notices files and the
licence came through unchanged, as expected — they are byte-stable inputs.

Functional check, not just identity: the **installed** `autostem.exe --version` exits 0 and prints the
command table (`D4-installed-cli-version.txt`, stderr empty). A hash match alone would not have caught
a payload that copied but could not start.

## 2. What the upgrade did to the machine

- `User privileges: None` — `PrivilegesRequired=lowest` held; nothing wrote to HKLM or Program Files.
- `RestartManager found no applications using one of our files.` So `CloseApplications=yes` fired its
  check and force-closed **nothing**. Ableton Live was up on `Untitled*` at the time and was not touched
  by the installer.
- Uninstall log **appended** (`unins000.dat` 42,652 -> 47,581 B), uninstaller renamed, key rewritten with
  `InstallDate=20261118`, `DisplayVersion=1.1.0` unchanged. No orphaned second key.
- Task selection inherited from the 2026-11-15 install: the desktop icon was created again
  (`Desktop\Skeer autoSTEM.lnk`, 10:51:34). No `[Tasks]` entry other than `desktopicon` exists, which is
  what AGENTS.md requires — there is nothing here that can copy a plugin anywhere.
- Associations: `HKCU\Software\Classes\.wav`, `.aif`, `.aiff` default to `SkeerAutoSTEM.Audio`; `.mp3`
  gets an `OpenWithProgids` entry only, no default. That is the documented split, and the same 10 registry
  entries ran on 2026-11-15, so the upgrade **took nothing new** — `.mp3` still opens in whatever it
  opened in before.
- `[Run] ... skipifsilent` honoured: no Skeer process was running after setup (`Get-Process` = none).
- `Need to restart Windows? No`, `Installation process succeeded.`

## 3. Side effect noticed while verifying, not caused by me

Ableton Live pid 11072 is **gone** and 9877 is no longer listening. There is no Application Hang event
and no WER report for Live, so nothing crashed: the Save / Don't Save modal that my earlier `WM_CLOSE`
produced was answered. **Not by me** — I pressed nothing in it. I also will not claim it was you, because
§3's own evidence shows other lanes acting on this machine in the same hours; whoever answered it made the
irreversible call about the unsaved set, and that is not my read to make.

The absence is a measurement, not a null query: the same window returns **88** Application-log events
(45 RestartManager, 18 Security-SPP, 12 chromoting, 8 Bonjour, 3 SecurityCenter, 1 each brave/Chrome) and
zero of them mention Live, Ableton or AutoStem.

I first read those 45 RestartManager events as this install's own probes. That was wrong and the counts
say so: exactly **2** fall at 10:51:30/10:51:34 (the `Starting session 1` / `Ending session 1` pair whose
embedded UTC time `09:51:30.710Z` matches my setup log), and the other **43** cluster at 07:01–07:08 with
singles at 07:30, 07:52, 08:52 and 08:56. Those are someone else's installs — the same window as the
`is-*.tmp` dirs at 06:48/06:52/07:01, the `SKEER DJ LIBRARY.lnk` at 07:08:31 and the step13 keys in §4.
Worth knowing before anyone blames this upgrade for state it did not touch.

I cannot bound the moment Live exited: no `Log.txt` under any Ableton base, and no `*.als` anywhere in
`Documents`, `Desktop`, `D:\out` or this repo modified in the last 14 hours. That last fact is itself the
finding for B1 — **the throwaway set was never saved**, so if Don't Save is what got pressed, B3 has no
set to run against and it has to be rebuilt first.

Consequences, both true:
- The unauthenticated loopback writer seam I was told to keep visible as an exposure is now **closed**.
- B3's deliberate refusal path needs the throwaway set open again, since we do not know which button was
  pressed. Ask before relaunching Live — that is his call.

## 4. Not this repo, left alone

Two `SKEER step13 …` HKCU uninstall entries dated today are another lane's installer probing, not ours
(`SKEER Build Baseline`, not `Skeer autoSTEM`, and a different AppId). One points at
`%TEMP%\is-H07QSN6EN3.tmp\skeer_step13_abort\`, a folder that no longer exists — a broken uninstall entry
whose `unins000.exe` is unrunnable. The other (`…\step13_probe\scratch3\inst-mismatch\`) is intact.
Deleted neither: not my work, and a registry key is the only record that the probe ran.

## 5. Still owed after this

D1's C# half (23 modified tracked files + the untracked `DesktopGuard`/`GuardedInput`/`ScreenYield`/
`CreateMenuChooser`/`LiveRunCopy`/`PostedInput` core and 14 test files) — B3 requires a **committed**
artifact, so the batch has to land first. `launch/` and `artifacts/` stay gitignored; the pre-D2 setup is
backed up at `D:/Projects/tools/autostem-pre-D2-2026-11-18/` because `build.ps1:133` deletes the old
`launch/Skeer_autoSTEM_Setup_*.exe` on every run.
