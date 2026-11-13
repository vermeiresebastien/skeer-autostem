# Skeer autoSTEM

Batch stem separation for **Ableton Live 12** on Windows 11.

*(Internal project name: StemForge. The executable is `SkeerAutoSTEM.exe`; the UI carries the
SKEER brand and module name, matching SKEER DJ Library.)*

Skeer autoSTEM is a standalone desktop app that drives the stem separation engine already
inside your installed Ableton Live. Add audio files or folders, choose which stems and
which processing mode, press **Run**, and it launches Live, separates each track in turn,
verifies the results and publishes finished WAVs into readable per-track folders — so a
batch can run unattended overnight.

---

## Quick start

1. Run `SkeerAutoSTEM.exe` (see [Packaging](#packaging) — the published build is a single
   self-contained executable).
2. Point **Output folder** at where you want the stems to land.
3. **Add Files**, **Add Folder** or drag files/folders onto the window.
4. Leave **Stems** on *All* (the first-launch default) or pick *Drums Only* / *Custom*.
5. Leave **Processing mode** on *High Speed* (the first-launch default) or pick *High Quality*.
6. Press **Run**.

Results look like:

```
<Output folder>/
  Artist - Track/
    vocals.wav
    drums.wav
    bass.wav
    other.wav
  Artist - Track (Drums Only)/
    drums.wav
```

A `autostem.manifest.json` is written into every result folder. It records which job
produced the files, which stems and mode were used, and a hash and duration for each WAV,
which is how Skeer autoSTEM proves a result belongs to the current job and never reuses the
wrong one.

Full instructions, including overnight operation and the constraints the automation
depends on, are in [docs/SETUP.md](docs/SETUP.md).

---

## The window

Three things are meant to be obvious at a glance: **which files are ready**, **what needs
fixing**, and **what happens when you click Run**.

- **Queue** (left): `File`, `Status` and `Details` columns. Long names truncate with an
  ellipsis and reveal the full path on hover. Status is a labelled badge — Ready,
  Processing, Completed, Failed, Skipped — with red reserved for failure. Details carries
  the actual reason a track failed ("Unsupported file type: .xyz."). Settings, output file
  names, partial output and Remove live in the row's details panel, opened with the row's
  `•••` button or the "View details" link. A row saved with settings that differ from the
  current batch says so there.
- **Empty queue** shows a full drag-and-drop target; once files are present it shrinks to a
  one-line hint so a long batch keeps its room.
- **Batch settings** (right): output folder first (with Browse, and Open once it is set),
  then stems as a segmented `All / Drums only / Custom` control with a one-line summary
  (`Vocals · Drums · Bass · Others`), then `High Speed / High Quality`. Custom checkboxes
  appear only when Custom is selected; the longer mode explanation lives in a tooltip.
- **Command bar** (bottom): one row that replaces the old status footer. It shows the queue
  counts *from the same state as the rows* ("2 failed · 0 ready"), says why Run is blocked
  in plain language, and holds `Run` at a fixed position on the right. Run is enabled only
  when the batch can actually start. While running it shows the current file, the stage, an
  indeterminate indicator when the engine reports no reliable percentage, and
  `Stop after current`. The progress row is hidden when idle.
- **Diagnostics** (link in the command bar): detected Live build and executable path,
  run-log path, working folders, supported input types, and the advanced options.
- `•••` next to the queue actions holds housekeeping (clear completed, open output folder,
  open log folder); `Retry failed` appears in the command bar only when something failed.

Under the hood:

- The queue, job transitions and run log are written to disk, so an interrupted batch
  resumes with each job's original settings. A completed job is only skipped while its
  verified outputs still exist; an interrupted job becomes retryable.
- Results are staged on the destination volume, verified, then published, with the
  completion marker written last so a crash mid-publish is repaired rather than duplicated.
- Every published stem is checked as a readable WAV whose duration matches the source
  within a documented tolerance and which aligns with its sibling stems.
- A failed attempt keeps only stems that individually **passed** verification, in a folder
  marked partial. Partial output never counts as a finished result, is never offered as
  "Open results", and is never reused by a later run.
- Source audio is only ever opened for reading.

---

## Screenshots

Captured from the published build (`docs/screenshots/`):

| | |
| --- | --- |
| `01-empty.png` | empty queue: full drag-and-drop target, Run blocked with "Add a supported file and choose an output folder." |
| `02-ready.png` | three tracks ready, "Ready to run", Run enabled |
| `03-ready-row-details.png` | a row's details panel (saved settings, output, remove) |
| `04-failed.png` | two failed rows with their real reasons, "2 failed · 0 ready", Retry failed offered, Run disabled |

`tools/capture-window.ps1` renders the app window with `PrintWindow` so the capture stays
clean even when other windows overlap it.

---

## Repository layout

```
src/StemForge.Core          engine: queue, verification, publishing, naming, Live automation
src/StemForge.App           WPF desktop app (the product)
tools/StemForge.Cli         headless companion + diagnostics (run, verify, bench, dump-ui)
tests/StemForge.Tests       88 automated tests
docs/SETUP.md               setup guide
docs/TEST-REPORT.md         test report, benchmarks and outstanding checks
docs/LIVE-AUTOMATION.md     how the Ableton Live automation works, and what it depends on
artifacts/SkeerAutoSTEM         published single-file Windows build
```

---

## Build, test, package

```powershell
# build everything
dotnet build StemForge.slnx -c Release

# run the automated tests
dotnet test tests/StemForge.Tests/StemForge.Tests.csproj

# produce the single-file Windows build
./build.ps1
# -> artifacts/SkeerAutoSTEM/SkeerAutoSTEM.exe   (self-contained, no .NET install required)
```

The headless companion drives exactly the same engine, which makes it useful for overnight
batches and for reproducing a GUI run:

```powershell
autostem locate                                     # list detected Ableton Live builds
autostem prepare "track.wav" --work D:\work         # build the working Live Set
autostem enqueue "album" --dest D:\out              # fill the saved queue without running
autostem run "track.wav" --dest D:\out --stems all --mode speed
autostem run "loop.wav"  --dest D:\out --simulate   # no Ableton required
autostem verify "D:\out\Track"                      # re-verify a published result
autostem bench short.wav longer.wav --dest D:\bench # All vs Drums Only, both modes
autostem dump-ui --window "Ableton Live"            # inspect Live's UI Automation tree
```

---

## Visual identity

The UI follows the SKEER appearance system — the same tokens as SKEER DJ Library, defined
once in `src/StemForge.App/Theme/Skeer.xaml`:

| Token | Value |
| --- | --- |
| Canvas | `#F5F2EB` ivory |
| Panels | `#FCFAF6` paper on the ivory canvas, 1 px `#CFC8BC` border, 8 px radius |
| Ink | `#292821` charcoal, `#716C63` muted ink, `#938D83` muted soft |
| Signal | `#FF6500` orange — the only accent in the UI |
| Type | IBM Plex Sans / IBM Plex Mono / Barlow Condensed, falling back to Segoe UI / Consolas |
| Micro-labels | condensed, uppercase, 0.09 em tracking; buttons 0.06 em |

Warm ivory paper, pale stone, charcoal ink, one orange signal. Brand mark, wordmark and the
Windows icon come from the SKEER brand assets; the title bar is tinted to the palette on
Windows 11 (Windows 10 keeps the system chrome). Because the condensed fonts may not be
installed, the font stack falls back the same way the DJ Library does, so both apps match on
any given machine.

---

## Requirements

- Windows 11 (tested on Windows 11 / 10 22H2)
- **Ableton Live 12.1 or newer**, installed separately. Developed and verified against
  **Ableton Live 12.3.5 Suite**.
- An unlocked interactive desktop while a batch is running.

See [docs/TEST-REPORT.md](docs/TEST-REPORT.md) for exactly what has been verified against
Ableton Live and what still needs a check on your own machine.
