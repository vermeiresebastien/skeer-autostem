# Skeer autoSTEM — test report

Date of testing: 2026-11-04, re-run against real Live on 2026-11-14 (runs `run-20261114-*`) and
again on 2026-11-15 (runs `run-20261115-*`, which is where the end-to-end completion was reached).
Machine: Windows 10 Home 22H2 (build 19045) desktop (AMD Ryzen 9 3900X, 64 GB RAM, RTX 2080 Ti),
interactive desktop session. This report records no Windows 11 run: the OS line used to read
"Windows 11-era desktop", which was a description of the hardware generation rather than a
measurement, and `docs/LICENSING-REQUIREMENTS.md` §7 settled it against the registry and CIM.

> The 2026-11-14 re-runs overturned two claims this report made about clip selection and the
> menu path. They are corrected below, in place, with what was actually measured instead.
>
> One more claim went the same way on 2026-11-15: which Live build the tool runs. It used to be
> "the newest, 12.3.5". The locator now treats Ableton's hidden `.Live 12 Suite_updated`
> update-staging folder as a copy, not an install, so the real `Live 12 Suite` install - 12.3.2 -
> wins even though it is the older build. Everything below that says *which build* is now dated
> accordingly, and the 12.3.5 measurements stay as measurements of 12.3.5.

---

## 1. Ableton Live build tested

Detected automatically by StemForge's own locator:

| | |
| --- | --- |
| **Runs this build now starts** | **Ableton Live 12.3.2 Suite** — `run-20261115-191232` logged `Live install: Ableton Live 12.3.2 Suite (C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12 Suite.exe)` |
| **Build the earlier runs were developed against** | **Ableton Live 12.3.5 Suite**, `C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe` — an update-staging copy, so the current locator no longer chooses it while 12.3.2 exists |
| Second install present | Ableton Live 12.3.2 Suite, `C:\ProgramData\Ableton\Live 12 Suite\Program\…` |
| Chosen | 12.3.2 — a staging copy ranks below a real install *whatever* its version, and is only used when it is the only Live on the machine |
| Working-project template | resolved from the chosen install's own `Resources\Core Library\Defaults\Creating Tracks\Audio Track\Default Audio Track.als` |

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

Verified on this machine. The numbered observations below were measured on 2026-11-14 against the
build that install's locator then chose - the **12.3.5** staging copy; the 2026-11-15 run that
finished end to end ran on **12.3.2**, and where a claim is build-specific it says which.

1. **Automatic location and identification.** The locator found both installs, reported `Suite`,
   and resolved the Core Library template from the chosen one. On 2026-11-14 it ranked 12.3.5
   first because it was the newest - which is the claim the 2026-11-15 change overturned: Ableton's
   hidden `.Live 12 Suite_updated` folder is an update-staging **copy**, so the real 12.3.2 install
   now ranks above it and the app displays
   `Ableton Live: Ableton Live 12.3.2 Suite (C:\ProgramData\Ableton\Live 12 Suite\Program\…)`,
   which is what `run-20261115-191232` launched.
2. **Feature support confirmed** for the installed build (strings above), including both
   processing modes and the documented `Samples/Processed/Stems` output location.
3. **The generated working project opens in Live.** Live 12.3.5 loaded the tool's `.als`
   with the window title `autostem Working Set - Ableton Live 12 Suite`, showing the
   injected audio clip in the first track's first clip slot, with **no** missing-file,
   schema or version warning dialog. 12.3.2 loaded it too - no warning interrupted the
   2026-11-15 run, and that run got all the way to four published stems.
4. **The clip slot is found, and selecting it does take effect — but not every time.**
   autostem locates the slot through UI Automation (`SessionView.Track[0].Slot[0]`, named
   `<clip name>, stopped, in track 1-Audio, scene 1`). In the 03:14:49 UTC run the slot was
   selected and four seconds later Live's Separate Stems command was enabled and its dialog
   opened, so selection demonstrably works. In the 04:00:37 UTC run the same line of code left
   Live reporting the command `disabled`. Live's clip slot exposes **no** selection pattern, so
   the old log wording — "selected through UI Automation" — was wrong even when the outcome was
   right: the call that actually did it was the mouse click the pattern path fell through to.
   Selection is now an explicit real click on the slot's own rectangle, and the command's
   `IsEnabled` state — not the call's return value — decides whether it worked.
5. **The `Create` menu, the dialog, and Live's own separation have all been driven.**
   `MenuBar → Create` opens through UI Automation, `Separate Stems to New Audio Tracks`
   (`id="Item 935"`) is in the popup, and when the clip is selected pressing it opens the dialog
   — whose full tree was logged by the 03:14:49 UTC run:

   ```
   Pane name="Separate Stems"
     CheckBox "Vocals, Include or exclude the Vocals stem."  id="VocalsCheckControl"
     CheckBox "Drums, ..."  "Bass, ..."  "Others, ..."       id="…CheckControl"
     Text "High Speed"
     CheckBox "Quality Mode, Choose between High Speed or High Quality." id="HighQualityCheckControl"
     Text "High Quality"
     Text "This action will stop audio."
     Button name="Separate" id="SeparateButton"    Button name="Cancel" id="CancelButton"
   ```

   There is no "Merge to Single Track" option in this build — the run says so rather than
   assuming it, and 12.3.2 said the same on 2026-11-15 (`this build's Separate Stems dialog has
   no 'Merge to Single Track' option`). All four stem toggles and the mode were set and re-read
   (`stem toggle Bass = on (verified)`), `Separate` was clicked, and **218 s later Live had
   written four real 49.7 MB WAVs** (`… [2026-11-14 031511] (Vocals|Drums|Bass|Others).wav`) under
   `Documents\Ableton\Live Recordings\2026-11-14 041458 Temp Project\Samples\Processed\Stems`.
   The press itself is no longer trusted blindly: a greyed-out entry is Live refusing, not a
   retry, so the command path reads `IsEnabled` first, distinguishes *chosen*, *absent* and
   *greyed out*, re-selects the clip before asking twice, and fails as `ClipNotSelected` with a
   sentence about what Live is refusing and why.
6. **Safe-stop behaviour works.** When a step could not be verified, autostem stopped and
   paused the batch with a specific reason (`LiveLostFocus` while stealing the foreground
   from a background process; `LiveErrorDialog` when the menu item could not be confirmed),
   rather than sending input blindly. Each failure produced a diagnostic screenshot and a
   run-log entry.
7. **The toolbox screenshots show the app itself**: the released executable launches with
   the correct window, both first-launch defaults (All four stems, High Speed), the
   counters, the run-log path and the detected Live build.

## 4. Not yet verified end to end (needs a free desktop)

One leg closed on 2026-11-15, and it was the last one this section named: **the runner has now
collected the files Live produced.** `run-20261115-191232` ran end to end and logged
`separation completed in 212.2s` then `published 4 stem(s) to C:\Users\Sebastien\Desktop\test\Akcept - Howl (Original Mix)`,
and on disk there are four 51 740 796-byte WAVs plus the manifest. That retires the §6 output bug
in `docs/SEPARATION-ENGINES.md`: `StemCandidateFolders` was the fix, and completion detection,
verification and publication are now measured behind it rather than assumed.

What is left is narrower than "an uninterrupted run", and all of it still needs a free desktop:
the posted-key answer to a real startup modal (the run above answered on the click rung), a
**second** track in the same batch (that run's second job died on the clip-slot miss that
`LiveUi.FindClipSlot` has since fixed), and a takeover of a session the user actually had open
(it ended with `Live (pid 20280) did not close on its own; terminating it`).

What the other 2026-11-14 runs established, each from Live's own tree and status line rather
than from an assumption:

- **`run-09` — the recovery prompt could starve startup.** After a forced termination Live
  reopens with a modal "Live unexpectedly quit while you were working on the Live Set …
  Would you like to recover your work?" whose window has an **empty title** while Live loads.
  The answer loop counted *glances* rather than *presses*, so one pass that saw no buttons yet
  marked the window exhausted and never came back; the prompt then sat in front of the main
  window for the whole startup timeout and the job died as `LiveWindowNotFound`. The budget now
  counts presses, which is the thing that has to succeed. Measured on `run-10`: Live reached
  its main window in 74 s. No prompt appeared that time, so the fix is implemented but the
  modal itself is still unexercised.
- **`run-10` — the same command that opened the dialog at 03:15 was greyed out at 04:00.**
  `Live took the command but opened nothing`, three times, with the status line going
  `Analysis: Scanning (679 found)` → `Analysis: Paused` → `Analysis: Scanning (43 found)`, and
  the tree saying `MenuItem name="Separate Stems to New Audio Tracks" id="Item 935" disabled`.
  Live refuses the command when no audio clip is selected, and Live's selection is not
  something a background process can assume while a person is using the machine. Waiting for
  Live to be idle is necessary and was not sufficient; the `IsEnabled` gate is what turns this
  from a fake success into a named failure (`ClipNotSelected`).
- **The machine is shared with live automation of its own.** A third-party control surface
  (`LK_Bridge`) logs `name '_clip_signature' is not defined` roughly every 15 s (1,184 times
  today) and can move Live's focus and selection underneath a run. That is not autostem's bug,
  but it is a real confound for any verification run: stop it first.

So nothing about the architecture is in question; what is missing is the last few feet of
evidence, and they are the three named above.

### Shortest procedure to close those legs

Do this once, on an idle, unlocked desktop with no other Ableton automation running.
It takes about four minutes for one short track, and the run log names the step if it stops.
Use **two** tracks to cover the second leg as well.

1. Close Ableton Live. If any Live window is open, save your own project first - a run now
   saves and closes whatever it finds, but the save is automatic and the name is not yours.
2. Stop anything else that drives Live (close other DAW tooling / MCP servers, and `LK_Bridge`).
3. Build the tool (or use `launch\SkeerAutoSTEM.exe`).
4. Pick one short track, e.g. a 20–60 s WAV.
5. Run, from a terminal, with Live closed:

   ```powershell
   cd <repo>
   dotnet build StemForge.slnx -c Release
   tools\StemForge.Cli\bin\Release\net10.0-windows\autostem.exe run "C:\path\to\short.wav" `
       --dest "D:\AutoStemOut" --stems all --mode speed --engine live
   ```

   Expected output: stages through `Separating`, then
   `completed 1, skipped 0, failed 0`, and a result folder under `D:\AutoStemOut`.
   The new failure to watch for is `ClipNotSelected` — that means Live still refuses the
   command after a real click on the clip slot, and it is a fact about Live's selection, not a
   timeout to retry.
6. Confirm the result:

   ```powershell
   tools\StemForge.Cli\bin\Release\net10.0-windows\autostem.exe verify "D:\AutoStemOut\short"
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

`dotnet test tests/StemForge.Tests/StemForge.Tests.csproj` → **364 tests, 364 passed, 0 failed**
(2026-11-18. The qualification below this line used to read "most of these tests are still
uncommitted, so the number describes the tree in front of us and not `HEAD`" — that is no longer
true: the tests landed with the code they test, so this describes `HEAD`. It was measured twice,
once on this working tree at 10:46 and again in an isolated detached worktree reconstructed from the
committed bytes, because a green suite only certifies the tree it ran on. It moved 299 → 344 → 360
during 2026-11-16 and reached 364 on 2026-11-18 by way of 350 at the narrow split that turned out to
be uncommittable; it was 263 late on 2026-11-15, 181 earlier the same day, 161 on 2026-11-14 and 88
on 2026-11-04, ~18–25 s.)
These run without Ableton Live — on the simulated engine, on arithmetic, and on windows the tests
build for themselves off-screen — and are kept strictly separate from real Ableton verification.
Nothing in this section is evidence about Live.

| Area | What is covered |
| --- | --- |
| Engine seam | which failure causes only a Live-driving engine may report (a non-Live engine reporting "Live lost the foreground" becomes `Unknown`, so a batch never pauses for an impossible reason); the catalogue states which engines this build can run; asking for an absent engine says so rather than silently using Live; every `ErrorKind` **name** a queue file on disk can hold still exists in this build, because the store writes causes by name and one unrecognised name quarantines the whole file (the ordinals are never read — the earlier "appended, never inserted, because queue files store them positionally" premise was refuted 2026-11-17 and is superseded by `QueuePersistenceTests`) |
| Session takeover | every classification of a Live process already running (ours / the user's / an orphan), and the recovered-set name that bumps rather than replaces an existing `.als` |
| Local engine arithmetic | the part of Demucs that is not the model, ported from `apply.py`/`api.py` and tested before any inference code exists: 10 s windows 330 750 samples apart, the window the reference centres and pads, the triangle fade, the weighted overlap-add divided by the weight that actually landed, the tail of a track that needs **two** short windows, the mono-reference mean/unbiased-std level applied and undone, and the stem order (`drums, bass, other, vocals` - with an assertion that it is *not* Ableton's dialog order, because swapping vocals and others is the mislabel that would ship silently). The central test gives the accumulator its own input back and demands the original mix, sample for sample |
| Source formats | MP3 container header reading (duration, sample rate, channels) without a decoder, and the same tolerance rules applied to it |
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
| Dialog answers | which button label a posted keystroke may be trusted to press: the first plain A–Z letter, only when no other button in that dialog starts with it — so `No` among Close/Yes/**No** is answered, `Cancel` among **Close/Cancel** is refused as a coin toss, and the same label appearing twice is refused rather than believed. A non-Latin first letter gives no key at all; a handle that is not a window is refused, not thrown |
| Posted input plumbing | a key posted to a hidden off-screen window arrives as `WM_KEYDOWN`/`WM_KEYUP` with the right virtual key, and `InputSimulator.InjectionAttempts` does not change across it. The zero delta is the claim that nothing reached the desktop; an earlier version of this test took it from the cursor and the foreground window instead, which is a measurement of the user's machine rather than of this code, and it could only ever notice input landing where the pointer happened to be |
| Guarded injection | the single door in front of every synthetic click and keystroke (`Live/Automation/GuardedInput.cs`): a window that is not the foreground one is never keyed; a handle whose window has gone is refused rather than aimed at whatever reuses the address; the point a click would land on is tested against the **window that owns it** and not against its process, which is the check that let Live's main window vouch for a popup that had already closed; a keystroke and a click are judged by their own rules and cannot be collapsed into one clause; a refusal comes back as a sentence and an error kind, never as a fault; and the verdict for the window genuinely in front is "send it" — asked of that window with nothing sent, because a helper that refused everything would be indistinguishable from a working one. Measured on windows the tests build for themselves, with no Ableton running |
| Render progress | a render target that stops advancing is named and classified **stalled** rather than left to time out silently: a plateau shorter than the patience is still a working render, a target seen for the first time gets the benefit of the doubt, a target that grew since the last poll is working whatever its write time looks like, a render with no history never calls anything stalled, and a stalled target does not pin the audio that did land |
| Screen yield | the window that held the screen before the run gets it back, and nothing else is touched: a window the user minimised stays minimised, one they have taken stays taken, one that has closed is not activated, and Live holding the screen itself means there is nothing to hand back |
| Create menu | an attempt that claims success but opens nothing is followed by the next one; a menu that opened and closed is opened again rather than pressed in the dark; a greyed-out entry is reported as refused and never pressed; every strategy gets its chance before the command is called absent; the coordinate fallback aims at the measured menu row, in the rectangle's own coordinates, so moving the window moves the click |
| Clip slot | Live's slot labels are accepted in the shapes it really writes (`1A`-style, whitespace, case) and the browser's own naming is rejected — the substring search that made the first miss still matches the browser, so the fix cannot regress into it |
| Live install discovery | the dotless `_updated` staging copy is recognised on any share and excluded while a real install exists, even when the staging copy is **newer**; with only a staging copy, or two normal installs, the right one still wins |
| Live support gate | the floor and the edition that carries the separator, asked before anything touches a Live session: the whole truth table with the reason token each refusal returns, the deliberate allowance for an edition this tool cannot name, the requirement sentence built from the constant so a stale copy of the floor shows up as a failure rather than a second answer, and the engine list quoting the floor instead of restating it |
| Edition detection | the route an edition actually arrives by on this machine (Ableton writes ProductName `"Live"`; the install's own folder and file name is the only signal), the whole-word rule that stops "Satellite Sessions", "Standards Library" and "Introduction to Production" being read as Lite, Standard and Intro - which, now that an edition can be refused, would stop a working install from running - and that an install naming nothing is neither refused nor claimed: the gate accepts it and the label says "edition not detected". Path fixtures only; no Live was started to produce these rows |
| Stem discovery | Live's `.asd` analysis sidecars and other non-audio neighbours are never collected as stems |

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
  default**, the Ableton Live panel showing the detected Live build (12.3.5 Suite while the
  locator preferred the newest path; 12.3.2 Suite now that it prefers the real install), all the run
  controls, the counters and the run-log path.
- `build.ps1` reproduces the build; the same source tree also produces the headless companion,
  which builds to `autostem.exe` and lands in `launch\` beside the app.

---

## 9. Summary

| | |
| --- | --- |
| Standalone app, packaged and launched from its own executable | **Verified** |
| First-launch defaults (All stems, High Speed), persistence, queue UI | **Verified** |
| Queue, resume, verification, naming, publishing, safety logic | **Verified by 364 automated tests (§5), committed with the code** |
| Locating and identifying the installed Live build | **Verified against Live 12.3.5 Suite (2026-11-14) and 12.3.2 Suite (2026-11-15)** |
| Working project generated and opened by Live, clip intact, no warnings | **Verified against Live 12.3.5 Suite (2026-11-14) and 12.3.2 Suite (2026-11-15)** |
| The clip slot being found, the `Create` menu, **the Separate Stems dialog and its toggles** | **Verified against Live 12.3.5 Suite (2026-11-14) and 12.3.2 Suite (2026-11-15)** |
| Live performing the separation and writing four WAVs, started by automation | **Verified** — 218 s, four 49.7 MB WAVs in a temp project |
| Safe-stop on focus loss / unverifiable state | **Verified in practice** (it stopped and paused, with diagnostics) |
| The runner *finding* those WAVs, the completion signal, and publishing them to the destination | **Verified** — run `run-20261115-191232`: `separation completed in 212.2s`, then `published 4 stem(s)` to the chosen folder, where four 51 740 796-byte WAVs and the manifest are on disk |
| The screen is the user's again while Live renders | **Verified** — the same run logged `the render is left to itself; the screen is back with 'Qoder'` and still published four stems 212 s later, so the yield is not costing the job |
| A startup modal answered without moving the mouse | **Outstanding** — that run answered it on the click rung. The posted-key rung is unit-tested (the message arrives at the window, and the process's own injection count says nothing was injected) and is tried first in code, but no real Live startup has been observed answering to it yet |
| More than one track in a batch | **Outstanding** — the second track of that run failed `ClipNotSelected`, after warning three times that `the clip slot for 'clip' was not found through UI Automation`; `LiveUi.FindClipSlot` has since been fixed and named by a test, but not yet re-run against Live |
| Takeover of a session the user had open | **Outstanding** — the same run ended with `Live (pid 20280) did not close on its own; terminating it`, so the save-and-close path still needs to be seen working on a real unsaved set |
| Real separation-time benchmark | **Partly closed** — Live's own 218.2 s / 0.744 RTF is measured **on 12.3.5**; the 12.3.2 run that finished took 212.2 s on the same file but was not instrumented for RSS/VRAM, and the four-run `bench` matrix is not (§6) |
