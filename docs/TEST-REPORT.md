# Skeer autoSTEM — test report

Date of testing: 2026-11-04
Machine: Windows 11-era desktop (AMD Ryzen 9 3900X, 64 GB RAM, RTX 2080 Ti), interactive desktop session.

---

## 1. Ableton Live build tested

Detected automatically by StemForge's own locator:

| | |
| --- | --- |
| **Tested build** | **Ableton Live 12.3.5 Suite** |
| Executable | `C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe` |
| Second install present | Ableton Live 12.3.2 Suite, `C:\ProgramData\Ableton\Live 12 Suite\Program\…` |
| Chosen | 12.3.5 (newest) — the locator ranked it first, as intended |
| Working-project template | `…\.Live 12 Suite_updated\Resources\Core Library\Defaults\Creating Tracks\Audio Track\Default Audio Track.als` |

Stem separation and both processing modes were confirmed present in the installed binary by
scanning it for the feature's own strings (`sMenuSeparateStemsToNewAudioTracks`,
`sDialogTitleSeparateStems`, `VocalsCheckControl`, `DrumsCheckControl`, `BassCheckControl`,
`OthersCheckControl`, `HighQualityCheckControl`, `Samples/Processed/Stems`, and the dialog's
accessibility text). Full detail in [LIVE-AUTOMATION.md](LIVE-AUTOMATION.md).

---

## 2. Launch and completion mechanisms

**Launch:** autostem generates its own Live Set (Live's stock empty-audio-track template +
one injected, unwarped, non-looping, full-length clip referencing the source file in place)
and starts `Ableton Live.exe "<project>"`. It does not use, and cannot be affected by, a
different set.

**Entry point:** `Create > Separate Stems to New Audio Tracks` — the only user-facing entry
point the 12.3.5 binary contains — reached through Windows UI Automation.

**Mode:** the dialog's single **High Quality** toggle. Unchecked = High Speed,
checked = High Quality. There is no "High Speed" control, and no "Merge to Single Track"
option, in 12.3.5.

**Completion:** bounded detection — new/changed files in
`<project>\Samples\Processed\Stems` that have been byte-stable for 2 s. A fixed sleep is
never used. A closing dialog with no new files is classified as cancellation, not success.

**Obtaining the WAVs:** Ableton writes the separated audio into the working project's
`Samples/Processed/Stems` folder, exactly as its documentation describes. autostem copies
those files directly. It renders nothing. If (and only if) an installed build wrote a
supported non-WAV format such as AIFF, autostem decodes and rewrites it as WAV with the
sample values untouched — no normalisation, no effects, no time stretching. The normal path
needs no conversion at all.

---

## 3. What was verified against real Ableton Live

Verified on this machine, against the installed Live 12.3.5:

1. **Automatic location and identification.** The locator found both installs, ranked
   12.3.5 first, reported `Suite`, and resolved the Core Library template. The app displays
   `Ableton Live: Ableton Live 12.3.5 Suite (C:\ProgramData\Ableton\.Live 12 Suite_updated\…)`.
2. **Feature support confirmed** for the installed build (strings above), including both
   processing modes and the documented `Samples/Processed/Stems` output location.
3. **The generated working project opens in Live.** Live 12.3.5 loaded the tool's `.als`
   with the window title `autostem Working Set - Ableton Live 12 Suite`, showing the
   injected audio clip in the first track's first clip slot, with **no** missing-file,
   schema or version warning dialog.
4. **Clip selection works.** autostem selected the clip through UI Automation using Live's
   own element (`SessionView.Track[0].Slot[0]`, named
   `<clip name>, stopped, in track 1-Audio, scene 1`).
5. **The menu path works.** autostem opened the `Create` menu through UI Automation
   (`MenuBar` → `Create`) and found the `Separate Stems to New Audio Tracks` entry in the
   menu popup, confirmed by dumping Live's live automation tree.
6. **Safe-stop behaviour works.** When a step could not be verified, autostem stopped and
   paused the batch with a specific reason (`LiveLostFocus` while stealing the foreground
   from a background process; `LiveErrorDialog` when the menu item could not be confirmed),
   rather than sending input blindly. Each failure produced a diagnostic screenshot and a
   run-log entry.
7. **The toolbox screenshots show the app itself**: the released executable launches with
   the correct window, both first-launch defaults (All four stems, High Speed), the
   counters, the run-log path and the detected Live build.

## 4. Not yet verified end to end (needs an idle machine)

The last leg — **the Separate Stems dialog contents, the completion signal, and real
Live-generated WAVs** — was not driven to completion on this machine. The blocker was
environmental, not architectural:

- the desktop was in active use by another session that was itself driving Ableton
  (an `ableton-mcp` process was running), and
- the desktop was heavily contended, so a background process could not reliably hold the
  foreground long enough to complete a dialog interaction.

Everything up to and including reaching the `Separate Stems to New Audio Tracks` menu item
was verified; the dialog interaction itself was not exercised.

### Shortest procedure to close that gap

Do this once, on an idle, unlocked desktop with no other Ableton automation running.

1. Close Ableton Live. If any Live window is open, save and close your own project (or leave
   it — autostem will pause rather than touch it, but closing is faster).
2. Stop anything else that drives Live (close other DAW tooling / MCP servers).
3. Build the tool (or use `artifacts/SkeerAutoSTEM\SkeerAutoSTEM.exe`).
4. Pick one short track, e.g. a 20–60 s WAV.
5. Run, from a terminal, with Live closed:

   ```powershell
   cd <repo>
   dotnet build tools/StemForge.Cli/StemForge.Cli.csproj
   tools\StemForge.Cli\bin\Debug\net10.0-windows\autostem.exe run "C:\path\to\short.wav" `
       --dest "D:\AutoStemOut" --stems all --mode speed
   ```

   Expected output: stages through `Separating`, then
   `completed 1, skipped 0, failed 0`, and a result folder under `D:\AutoStemOut`.
6. Confirm the result:

   ```powershell
   tools\StemForge.Cli\bin\Debug\net10.0-windows\autostem.exe verify "D:\AutoStemOut\short"
   ```

   Expected: one line per stem, each `OK`, then `verification: PASS`.
7. Listen to the four WAVs and confirm the stem identities (vocals / drums / bass / other).

If step 5 fails, the run log at `%LOCALAPPDATA%\SkeerAutoSTEM\logs\run-*.log` and any
`logs\screenshots\*.png` will name the exact step that stopped, and the app will have paused
rather than guessing. That log plus the screenshot is everything needed to finish the
calibration.

The same check can be driven from the GUI instead: launch `SkeerAutoSTEM.exe`, add the file,
press Run, and accept the run-conditions prompt.

---

## 5. Automated test coverage

`dotnet test tests/StemForge.Tests/StemForge.Tests.csproj` → **88 tests, 88 passed, 0 failed**
(~4–8 s). These run without Ableton Live via the simulated engine and are kept strictly
separate from real Ableton verification — nothing in this section is evidence about Live.

| Area | What is covered |
| --- | --- |
| Job identity | same work → same id; changed source → new id; Drums Only / High Quality never matches All / High Speed; different destination → different job |
| Queue persistence | full round-trip of every field; interrupted `Running` job returns as retryable `Pending`; completed jobs are not reset; missing file → empty queue; corrupt queue preserved not discarded; flags-enum round-trip |
| Queue service | duplicate suppression; folder import is top-level unless recursion is requested (and ignores non-audio); clear completed keeps unfinished work; retry failed; freezing settings re-keys jobs and merges duplicates while leaving finished jobs alone |
| Resume | a completed job whose verified outputs vanished is reprocessed; otherwise it is skipped and not reprocessed on re-run |
| Naming | Windows-safe folder names (invalid chars, reserved names, trailing dots, length); default profile gets a plain name; different stems/mode get their own folders; **two different files with the same basename get distinct folders**; the same job reuses its own folder; a pre-existing foreign folder is never hijacked |
| Audio | WAV round-trip (PCM/float, header parsing); AIFF round-trip incl. the 80-bit extended sample rate; AIFF → WAV conversion leaves already-WAV files alone; tolerance scales with track length |
| Verification | passes for matching stems; fails on wrong duration, missing stem, non-WAV data; cross-stem alignment |
| Publishing | folder + manifest + renamed files; only requested stems are published (a Drums Only job publishes only `drums.wav` even if Live made more); idempotent re-publish; already-satisfied detection; an incomplete manifest is not satisfied and is repaired on retry; unwritable destination → actionable `DestinationUnwritable`; **the source file is never modified** |
| Batch runner | short and longer track both complete; re-run skips verified work; unsupported/corrupt file fails clearly and the next track still runs; missing source does not stop the batch; changed source is detected rather than processed; Stop After Current finishes the active job and preserves the rest; interrupted job resumes; a different mode produces a separate job; two same-named inputs → distinct result folders |
| Safety | Live crash / focus loss / unexpected dialog / startup failure / timeout each **pause** the batch, preserve the queue, and never produce a complete result; a file-specific fault does not stop the batch |
| Partial output | only stems that individually verified are kept; the folder is marked partial; partial output never satisfies the job, is never reused by a later complete run, and nothing is kept when no stem verifies |
| Failure wording | an unsupported extension is reported as "Unsupported file type: .xyz." |
| Working project | injected clip covers the whole file with looping and warping off; source referenced in place with real size/length; XML-escaping of special characters; the generated `.als` is valid gzip/XML; **the real installed Live template is found and usable** |
| UI automation | template matcher finds a reference image at the right place, rejects an absent one, handles a template larger than the scene, and refuses a template with no luminance variation |

### A bug the test suite itself exposed (and fixed)

`AppPaths` resolves its data root from an environment variable so tests stay isolated, but
xunit runs test classes in parallel — so two fixtures could redirect the same process-wide
variable and write into the real `%LOCALAPPDATA%\SkeerAutoSTEM`. This actually happened once and
left simulated stems in the real work folder. The suite now runs sequentially
(`DisableTestParallelization`), the leaked artifacts were removed, and the run was re-checked
to confirm nothing is written outside the test workspace.

---

## 6. Benchmarks

### What the harness does

`autostem bench <short> <longer> --dest DIR` drives the same short and longer tracks through
**All** and **Drums Only**, in **Both** High Speed and High Quality — four runs per track,
each with a **fresh destination** so no run can be satisfied by another's output. It reports
separation time separately from copying/conversion/publishing.

### Simulated run (harness validation only — NOT Ableton numbers)

```
engine   : SIMULATED (numbers are not Ableton)

track                                          stems       mode          separation   publish     total
TBG_85_kit_loop_reso_filter_full_Bmin.wav      all stems   High Speed          0.5s      1.5s      1.9s
TBG_85_kit_loop_reso_filter_full_Bmin.wav      drums only  High Speed          0.5s      1.2s      1.7s
TBG_85_kit_loop_reso_filter_full_Bmin.wav      all stems   High Quality        0.5s      1.4s      1.8s
TBG_85_kit_loop_reso_filter_full_Bmin.wav      drums only  High Quality        0.5s      1.3s      1.7s
OSS_TD_95_drum_heardemsay_indie_soul_hiphop_rnb all stems  High Speed          0.4s      4.9s      5.3s
OSS_TD_95_drum_heardemsay_indie_soul_hiphop_rnb drums only  High Speed          0.4s      4.5s      4.9s
OSS_TD_95_drum_heardemsay_indie_soul_hiphop_rnb all stems   High Quality        0.4s      4.9s      5.3s
OSS_TD_95_drum_heardemsay_indie_soul_hiphop_rnb drums only  High Quality        0.4s      4.5s      4.9s
```

These confirm the harness measures the two phases separately and that publishing cost scales
with track length. The simulated engine deliberately does not model Live's work, so the
absence of a stem/mode difference here is expected and meaningless as a performance claim.

### Real Ableton numbers — still to be collected

Real separation timings require the same run against the installed Live build:

```powershell
autostem bench short.wav longer.wav --dest D:\bench
```

**No speedup is promised.** Ableton documents that High Speed separates in a single pass
while High Quality uses separate passes for vocals, drums and bass, and that selecting only
the needed stems can shorten High Quality when *Others* is excluded (selecting *Others*
requires the other passes). So Drums Only is expected to be faster than All in High Quality,
and both modes are expected to be much closer in High Speed. The harness is set up to show
exactly that without hard-coding a figure. Record the machine, Live build and track lengths
with the numbers.

---

## 7. UI verification

The window was exercised against **real application state**, not mock data: queues were
built with the companion (`autostem enqueue`), runs were executed with the simulated engine
so the real Live install was not driven, and where a click was needed the app was driven
through UI Automation (`autostem uia-invoke --button ...`). Each screen below is a capture
of the built binary.

| State | What was checked | Result |
| --- | --- | --- |
| Empty | full drag-and-drop target; combined blocker "Add a supported file and choose an output folder."; Run disabled and neutral | Pass |
| Ready | pending rows with Ready badges; "Ready to run" with the check icon; Run solid orange and enabled; output folder populated with Browse + Open | Pass |
| Running | current file, stage and orange progress bar; "N of M done"; Stop after current enabled; Run disabled; one slate Processing badge, the rest Ready | Pass |
| Mixed | green Completed rows ("4 of 4 stems verified") beside a red Failed row ("Unsupported file type: .xyz."); summary "1 failed · 0 ready" | Pass |
| Failed only | red "2 failed" pill beside Queue; both rows red with the real reasons; "2 failed · 0 ready"; blocker "No files are ready…"; Retry failed shown; Run disabled | Pass |
| Completed | all rows green; "8 completed"; progress row hidden; Run disabled with "Nothing left to do…" | Pass |
| Row details | toggled with `•••`: Saved settings, Output, verified partial output, and Open results / Open partial / Remove as applicable | Pass |

Layout checks:

- **Long filenames** truncate with a real ellipsis in the File column and expose the full
  path on hover; a 98-character name renders as "Skeer Test - This Is An Ex…".
- **Narrow window**: at the enforced minimum (1040x700) nothing overlaps or clips, the three
  columns stay readable, and the command bar keeps Run on one line.
- **Display scaling**: the app is per-monitor DPI aware (PMv2 via `app.manifest`) and the
  layout is star/auto based, but changing the Windows scale factor needs a sign-out, so a
  live scaled render was not exercised here. The scale-sensitive defect that did appear on
  this machine (the window opening mostly off-screen on a mixed-DPI, multi-monitor desk) was
  fixed by centring explicitly on the primary work area.

Defects found and fixed during this pass:

1. **Row details never appeared** — the row style pinned `Height=38`, so the details panel
   had no room. Rows now use `MinHeight`.
2. **The File column could not ellipsize** — the name sat in a `StackPanel`, which gives its
   child infinite width so `TextTrimming` never engages. It is a two-column `Grid` now.
3. **The completion dialog opened before the UI returned to idle**, so the command bar still
   claimed a run was in progress behind it. The state is now reset before the summary.
4. `autostem enqueue`/`run` did not load the existing queue first, silently replacing it.

---

## 8. Packaging verification

- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces
  a single **72 MB** `SkeerAutoSTEM.exe` with no installer and no .NET prerequisite.
- The published executable was launched and its window captured: title
  `Skeer autoSTEM`, the toolbar (Add Files, Add Folder,
  Include subfolders, Output folder, Change…, Open), the Queue grid, the Stems panel with
  **All four stems selected by default**, Processing mode with **High Speed selected by
  default**, the Ableton Live panel showing the detected 12.3.5 Suite build, all the run
  controls, the counters and the run-log path.
- `build.ps1` reproduces the build; the same source tree also produces the headless
  `SkeerAutoSTEM.exe` companion.

---

## 9. Summary

| | |
| --- | --- |
| Standalone app, packaged and launched from its own executable | **Verified** |
| First-launch defaults (All stems, High Speed), persistence, queue UI | **Verified** |
| Queue, resume, verification, naming, publishing, safety logic | **Verified by 88 automated tests** |
| Locating and identifying the installed Live build | **Verified against Live 12.3.5 Suite** |
| Working project generated and opened by Live, clip intact, no warnings | **Verified against Live 12.3.5 Suite** |
| Clip selection and the `Create > Separate Stems to New Audio Tracks` menu path via UI Automation | **Verified against Live 12.3.5 Suite** |
| Safe-stop on focus loss / unverifiable state | **Verified in practice** (it stopped and paused, with diagnostics) |
| Separate Stems dialog configuration, completion signal, real Live WAVs | **Outstanding** — needs one run on an idle machine (§4) |
| Real separation-time benchmark | **Outstanding** — one command (§6) |
