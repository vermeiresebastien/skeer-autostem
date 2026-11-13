# Skeer autoSTEM — setup guide

This guide covers launching the app, pointing it at Ableton Live, choosing an output
folder, running the first batch, and leaving a batch running overnight.

---

## 1. Launch the app

The published build is a single self-contained executable:

```
artifacts/SkeerAutoSTEM\SkeerAutoSTEM.exe
```

Double-click it (or make a desktop shortcut to it). No installer and no .NET runtime are
required — everything autostem needs is inside that one file. The first launch takes a
few seconds while Windows unpacks it.

To build it yourself: `./build.ps1`, or

```powershell
dotnet publish src/StemForge.App/StemForge.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The app stores its own state in `%LOCALAPPDATA%\SkeerAutoSTEM`: `settings.json` (your
preferences), `queue.json` (the queue), `work\` (per-job working projects), `logs\` (run
logs and diagnostic screenshots). Deleting that folder resets the app to first-launch
defaults; it never touches your audio or your Live sets.

---

## 2. Locate Ableton Live

autostem finds Live automatically on first launch — it looks at the registry, the
standard Ableton folders (including the hidden `.Live 12 Suite_updated` style folders an
update creates) and any running Live process, and picks the **newest** build it finds.
The **Ableton Live** panel in the app shows what it settled on, for example:

```
Ableton Live: Ableton Live 12.3.5 Suite (C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe)
```

If detection fails, or picks a build you do not want:

- **Locate Live** retries detection.
- **Browse…** lets you point at the executable yourself
  (`…\Program\Ableton Live 12 Suite.exe`).

The verified path is remembered. Stem separation needs **Live 12.1 or newer**; the app
tells you if the build you selected is too old. Live itself is a separate prerequisite —
autostem does not install or modify it.

---

## 3. Choose an output folder

Set **Output folder** once; it is remembered. Everything a batch produces is written there
under one folder per track. Use **Open** to look at it, or **Change…** to move it.

A hidden `.autostem-staging` folder inside the output folder is used while a track is
being verified and published. It is cleaned up automatically, and files only appear in the
final per-track folder once they have been verified.

---

## 4. Run your first batch

1. **Add files**, **Add folder** or drag files/folders onto the window.
   - **Add folder** opens a small menu: *Add folder…* picks the folder, and *Include
     subfolders* controls recursion. Recursion is off by default so a folder never imports
     more than you expect, and the option sits with the action it belongs to.
   - Supported inputs: WAV, AIFF/AIF, MP3, M4A/MP4, FLAC, OGG. Anything else is rejected
     with the reason shown in the row.
2. Choose **Stems**: `All` (all four, the first-launch default), `Drums only`, or `Custom`.
   Custom reveals the individual Vocals / Drums / Bass / Others checkboxes. At least one
   stem is required.
3. Choose **Processing**:
   - **High Speed** (default) — separates the selected stems in a single pass.
   - **High Quality** — separate passes for vocals, drums and bass. Excluding *Others*
     shortens High Quality because the remaining-pass is no longer needed.
   Switching to **Custom** with only some stems ticked shortens High Quality further.
4. Press **Run** — it sits at the right of the command bar and is enabled only once the
   batch can actually start.

If Run is greyed out, the command bar says why, in plain language: *Add a supported file
and choose an output folder.*, *Choose an output folder.*, *No files are ready. Retry the
failed tracks, or add new files.*, *Ableton Live was not found. Open Diagnostics to locate
it.* and so on. The counts beside it come from the queue itself, so two failed rows read
"2 failed · 0 ready" even before any run has happened.

On the first run, Skeer autoSTEM shows the conditions a run needs (leave the desktop
unlocked, leave Live closed, do not use the mouse over Live while a track is processing).
Accept it and the batch starts. You can prepare a whole queue with Live closed; Live is only
started when you press Run.

While a batch runs the command bar shows the current file, the stage, and progress. A
per-track percentage only appears when the engine gives a reliable one; until then the
indicator is indeterminate on purpose. It is hidden entirely when nothing is running.

When the batch finishes you get a summary. Per-track results are in each row's details
panel (`•••`): **Open results** appears only for completed, verified output, and **Open
partial** only when a failed attempt kept stems that individually passed verification.

---

## 5. Leaving a batch running overnight

Skeer autoSTEM is built for unattended batches:

- **The queue is on disk.** Every job and every state transition is written immediately, so
  a crash, a reboot, or Windows Update closing everything does not lose the batch. Start
  the app again and press Run; finished tracks whose verified outputs are still on disk are
  skipped, and the track that was interrupted becomes retryable.
- **Windows is kept awake.** For the duration of a run Skeer autoSTEM asks Windows not to sleep.
  The request is released when the run ends. Windows lock, screen saver and security
  settings are left entirely under your control — the app never changes them.
- **One track at a time.** Live is started on a purpose-built working project containing
  only that track, so a stale file from an earlier track cannot be mistaken for a new
  result.
- **It stops safely.** If Live loses focus, Windows locks, Live crashes, or an unexpected
  dialog appears, automated input stops immediately and the batch pauses with the reason.
  The queue is preserved. Fix the condition and press Run to resume.
- **Failure isolation.** A file-specific failure (unsupported or corrupt file, missing
  source, a stem Live did not produce) is logged and skipped so the rest of the batch
  continues. Anything that leaves Live or the destination in an unknown state pauses the
  whole batch instead, because blind recovery is worse than stopping.

Conditions the automation depends on:

| Condition | Why |
| --- | --- |
| Desktop left **unlocked** | Synthetic input needs an interactive desktop; a locked desktop is detected and the batch pauses. |
| Ableton Live left **closed** before Run | Skeer autoSTEM opens its own working set. If your own project is open it pauses rather than touching it. |
| Do not use mouse/keyboard over **Live** while a track processes | Taking focus away from Live stops automated input safely, but interrupts the track. |
| **Live launched on the monitor it starts on** | The window is maximised and coordinates are window-relative, so it survives moving/resizing, but it must stay on that display. |
| Display scaling | Handled: the app is per-monitor DPI aware. |

The run log (`…\SkeerAutoSTEM\logs\run-<timestamp>.log`, also reachable from **Diagnostics**)
records timestamps, job stages, results and failure reasons. Diagnostic screenshots from a
failure land in `logs\screenshots\`.

---

## 6. Diagnostics and testing without Ableton Live

The **Diagnostics** link in the command bar opens the paths and advanced options: the
detected Live build and its executable path, the run-log path, the per-job working folder,
the application-data folder, the supported input types, and the two advanced switches.

Tick **Use simulated engine (no Ableton Live)** there to exercise the queue, resume,
verification and publishing logic without Live. The header chip changes to
**Simulated engine** and the app never claims otherwise. **Results from the simulated
engine are not Ableton stems** — it exists for testing only.
