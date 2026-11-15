# How autostem drives Ableton Live

This is the reference for the Live-specific automation: what it uses, what it verified in
the installed build, and how it fails safely. Everything in this layer is isolated from
queue, file handling and verification (see `src/StemForge.Core/Live`).

---

## Is there a documented interface?

No. Ableton Live 12 exposes no documented API, LOM binding or Extensions entry point for
stem separation. Inspecting the installed `Ableton Live 12 Suite.exe` (12.3.5) shows the
feature is driven internally:

- `sMenuSeparateStemsToNewAudioTracks`, `sDialogTitleSeparateStems`,
  `sDialogSeparatingStems`, `sManeuverSeparateStemsToNewTracks`
- an internal message type `Message_separate_stems_call` under `ConversionsServices`
- `AStemSeparationPrefs` for the last-used selection/mode

So autostem uses **Windows UI Automation**, with window-relative input and image matching
as fallbacks.

## What the installed build exposes

Verified by scanning the installed binary and by dumping Live's live UI Automation tree:

| Thing | Value |
| --- | --- |
| Menu path | `Create > Separate Stems to New Audio Tracks` |
| Menu bar automation id | `MenuBar`, name `Application`; the Create item is `Item 3` |
| Menu popup class | `#32768` (the standard Windows menu window) |
| Dialog controls | `VocalsCheckControl`, `DrumsCheckControl`, `BassCheckControl`, `OthersCheckControl`, `HighQualityCheckControl`, `CancelButton`, `SeparateButton` |
| Accessibility text | `the (separate\|cancel\|vocals\|drums\|bass\|others\|high quality) (?:button\|toggle) in the stem separation dialog` |
| Output location | `<project>\Samples\Processed\Stems` |
| Clip slot automation id | `SessionView.Track[0].Slot[0]`, named `<clip name>, stopped, in track <track>, scene <n>` |
| Progress dialogs | `sDialogSeparatingStems` / `sDialogSeparatingVocals` |

Two consequences worth stating plainly:

- **Processing mode is a single toggle.** Live 12.3.5 has no "High Speed" label; the
  dialog has one **High Quality** switch. Unchecked = High Speed, checked = High Quality.
- **There is no "Merge to Single Track" option in 12.3.5.** The string does not exist in
  the binary. autostem still looks for and disables it if a build does offer it.

## The working project

Rather than dragging files into Live by hand, autostem generates its own Live Set:

1. Take Live's own stock empty-audio-track template
   (`Resources\Core Library\Defaults\Creating Tracks\Audio Track\Default Audio Track.als`)
   so the XML schema is always one the installed build accepts.
2. Inject one audio clip that **cannot shorten or alter the source**:
   `CurrentStart = 0`, `CurrentEnd` = the whole file in beats, `LoopOn = false`,
   `IsWarped = false`, `Fade = false`, pitch neutral, `SampleVolume = 1`, and a
   `FileRef` pointing at the original file **in place** — it is never copied or modified.
3. Write it to `%LOCALAPPDATA%\SkeerAutoSTEM\work\<job id>\`, so each job owns a separate
   project folder and `Samples\Processed\Stems` can never be contaminated by another job.
4. Launch `Ableton Live.exe "<project>"`.

This was verified against Live 12.3.5: Live opens the generated set with the expected title
(`autostem Working Set - Ableton Live 12 Suite`), shows the clip, and raises no warning
about the file or the schema.

## The per-job sequence

```
check desktop unlocked / Live version / no foreign Live session
prepare working project, snapshot the Stems folder as a baseline
launch Live, wait for the main window, activate + maximise
wait for the set to load (window title), dismissing only safe startup dialogs
select the clip            (UIA: SessionView.Track[0].Slot[0])
open Create menu           (UIA: MenuBar > Create, then the #32768 popup)
invoke Separate Stems to New Audio Tracks
set every stem toggle + native processing mode, verifying each toggle's state
disable Merge to Single Track if present
click Separate
wait for completion        (bounded; see below)
collect the generated files, mapping them to the requested stems
convert to WAV only if Live wrote a supported non-WAV format
hand the files to the publisher
```

## Completion detection

A fixed sleep is never used. Before the *Separate* click the runner snapshots the folders Live
may write into, and a job is finished when **all** of:

- one or more files across those folders are new or changed relative to that snapshot, and
- their size and timestamp have been unchanged for `FileStabilitySeconds` (default 2 s).

If **no** files appear, the closing of the Separate Stems dialog is treated as
cancellation/failure rather than success. Waiting is bounded by `MaxSeparationMinutes`
(default 30). Processing, success, cancellation, timeout and failure are distinguished, not
conflated.

The snapshot is taken over *several* folders (`StemCandidateFolders`) because Live does not
always use the one you gave it. It writes into the working project's
`Samples\Processed\Stems`, but a set Live considers loose gets copied into
`Documents\Ableton\Live Recordings\<stamp> Temp Project` first and the stems are generated
there. The working set is therefore built as a real project folder
(`autoSTEM Working Set\autoSTEM Working Set.als`, folder name equal to set name), and the
three newest temp projects are watched as well — measured with Live 12.3.5, where a
separation reported `result: success`, wrote 4 × 49 MB WAVs into a temp project, and autostem
reported an empty folder. Taking the baseline the same way as the poll is what makes that
safe: a temp project left by an earlier run contains only unchanged files, so it can never be
mistaken for this job's output.

Collecting what appeared is a separate step with its own trap. Live writes an `.asd` analysis
sidecar for every stem it produces, and it writes them *after* the WAVs, because Live analyses
its own output. The candidate list is ordered newest-first so that a folder containing work from
two runs yields the recent one, which means the sidecars arrive at the matcher ahead of the
audio: matching on the stem word alone mapped all four stems to `.asd` files, and the converter
then correctly refused them as "neither WAV nor AIFF". Only a real audio container
(`.wav`/`.wave`/`.aif`/`.aiff`/`.aifc`) is a candidate, and the stem word is read from the
**last** parenthesised group of the name — `"... (Original Mix) [2026-11-14 031511] (Vocals).wav"`
is Vocals — so that a track called *Vox Populi* cannot have its drum stem handed back as its
vocal stem. Both cases are covered by `StemFileMapperTests`, against Live's own file names.

## Stall and failure handling

Automated input is stopped, and the batch paused, whenever:

- the Windows desktop is not the interactive one,
- the foreground window is neither Live nor Skeer autoSTEM and cannot be reclaimed,
- the Live process exits,
- a dialog appears that is not one of the expected ones.

The desktop test reads the name of the input desktop rather than trusting a single call
(`WindowControl.QueryDesktop`). `Default` means interactive; `Screen-saver` and `Winlogon`
mean locked — the screensaver case used to read as *unlocked* while every synthetic click was
going nowhere. Anything else is *undetermined*, retried three times, then refused: the runner
stops rather than injects input it cannot verify, and says which of the two happened.

A prompt is answered by a label matched exactly, tried in a fixed preference order ("OK",
"Don't Recover", "Don't Save", "No", "Cancel", then the window's own "Close"), then by
Escape. A prompt that survives both pauses the batch and saves a diagnostic screenshot. Two
rules here come from runs that failed:

- **Never substring matching.** "No" is inside "Monitor".
- **Never first-match-in-tree-order.** A Live message box exposes its title-bar `Close`
  before the `No` that answers the question, so walking the accessibility tree and taking the
  first hit pressed the close box and left the question asked — which looked exactly like
  "Live never opened a window". Labels drive the search, elements are the inner loop
  (`LiveSessionTakeover.FindButton`).

Live also prompts *before* it has a titled window, and its main window has an empty title
while loading, so a startup modal — "Live unexpectedly quit while you were working … Would you
like to recover your work?" — stalls the window wait indefinitely. `WaitForMainWindowAsync`
therefore answers known startup prompts while it waits. The retry budget counts **presses, not
glances**: while Live is still initialising, the modal's buttons are not in the accessibility
tree yet, and a window that offered nothing on the first poll is the same window that offers
"No" on the tenth — counting glances gave up on the recovery prompt after 1.5 s and then let it
block the main window for the full 180 s startup timeout. Three presses that do not dismiss a
window is the point where it is left alone. The recovery set is not the user's: it is the
working set a previous run killed, so "No" is the correct answer and
`Preferences/CrashRecoveryInfo.cfg` is deliberately left alone. Every aborted run therefore
leaves a prompt for the next one, which is why this path exists at all.

A foreign foreground window does not abort on first sight; the runner reclaims focus up to
five times before pausing (a Display Dimmer wake used to kill a run outright).

A file-specific failure is only skipped after Live has returned to a known state; an
unknown state pauses the batch. A run owns Live, so every session still open when a run
starts is saved and closed first (`LiveSessionTakeover`). A set that was already saved is
written where it lives; a set that was never saved goes to
`%LOCALAPPDATA%\SkeerAutoSTEM\recovered\<title>-<timestamp>.als`, and a name that already
exists is bumped rather than replaced. The app asks before closing anything; the headless
companion does not ask and logs what it closed.

Live also has to be *idle*. While it is analysing files it accepts the
Create > Separate Stems command and drops it, which is what stalled the first real batch
("Analysis: Scanning (8,000 found)" sitting in the status line). Automation reads that
status line (`StatusText`), waits for it to go quiet before the menu command and before the
Separate click, retries up to three times, and reports what Live said if it still will not
open the dialog.

Being idle is not the whole precondition, and the difference was only found by looking at
Live's own accessibility tree during a failed run: `Separate Stems to New Audio Tracks` was
present in the popup and **disabled**, because Live did not have the clip selected. Three
answers have to be told apart, and the run now reports them separately:

| what Live's tree says | what it means | what the run does |
|---|---|---|
| entry pressed, dialog appears | the job can run | continue |
| entry pressed, nothing appears | Live was busy | wait for the status line, retry |
| entry present and greyed out | Live refuses the command | click the clip again, ask once more, then fail as `ClipNotSelected` |

The rule this rests on is **verify the effect, never the call**. Live's UIA provider mirrors
the interface faithfully and answers programmatic calls inconsistently: `Invoke` on a menu
item returns without error and does nothing, and a session clip slot exposes no selection
pattern at all. A run that logged "opened Separate Stems through the UI Automation menu" had
in fact opened nothing, and then spent its whole dialog timeout waiting for a dialog that had
never been asked for. Both the clip and the menu entry are therefore pressed with a real
click on the rectangle Live itself reports, and the *next* observable state — the dialog, the
entry's enabled flag — is what is believed.

Only one runner may control Live at a time (a named mutex plus a queue-level guard), so two
instances cannot fight over the same Live session.

## Tunables

Menu/clip geometry and timings live in `%LOCALAPPDATA%\SkeerAutoSTEM\ui-profile.json`
(`LiveUiProfile`): window-relative fractions, timeouts, the file-stability window and
whether to capture diagnostic screenshots. They can be adjusted without recompiling if a
Live layout differs.

---

## What the real runs measured (2026-11-14)

Ten runs against the installed Live 12.3.5 on this machine. Each one got further than the
last, and each one's failure was a different real cause — no two looked the same in the log
but for the first two, which is why they are recorded here rather than fixed silently.

Proven, with Live, on this machine. The first five bullets are all from one run
(`run-20261114-041449`, started 03:14:49 UTC), whose log is the most complete record of the
sequence that exists:

- the working project opens in Live with the correct clip, and the clip slot is found in the
  accessibility tree by its Live-generated name (`"<clip>, stopped, in track 1-Audio, scene 1"`);
- selecting that slot takes effect: four seconds later `Separate Stems to New Audio Tracks` was
  enabled, and pressing it opened its dialog;
- **the Separate Stems dialog has been opened, read and driven.** Its tree, as logged: four
  `CheckBox`es (`VocalsCheckControl`, `DrumsCheckControl`, `BassCheckControl`,
  `OthersCheckControl`), one `HighQualityCheckControl` between the texts "High Speed" and
  "High Quality", the notice "This action will stop audio.", and `Separate` / `Cancel`
  buttons (`SeparateButton`, `CancelButton`). There is no "Merge to Single Track" option in
  this build, which the log states rather than assumes;
- all four stem toggles and the processing mode were set and *re-read* ("stem toggle Bass = on
  (verified)"), and `Separate` was clicked;
- **Live then did the work and wrote real WAVs.** 218 s after the click it had produced four
  49.7 MB files — `Akcept - Howl (Original Mix) [2026-11-14 031511] (Vocals|Drums|Bass|Others).wav`,
  the internal stamp being the click time — under
  `%USERPROFILE%\Documents\Ableton\Live Recordings\<temp project>\Samples\Processed\Stems`.
  That is where the Live row of the comparison table in `docs/SEPARATION-ENGINES.md` comes from.

Not proven, and the reason the Live engine cannot yet be called end-to-end:

- **the runner has never collected those files.** In the run above it watched its own working
  project folder and logged `separating: 350s elapsed, no stem files yet` while Live was
  writing two directories away. That is the §6 output bug; `StemCandidateFolders` is the fix
  and it is *written*, not yet demonstrated against a live app. Everything downstream of it —
  completion detection, verification, publication into the destination — is unmeasured for the
  same reason.
- **the same command path is not repeatable under desktop contention.** A later run
  (`run-20261114-050037`) reached the identical line of code and Live's tree reported the entry
  as `MenuItem name="Separate Stems to New Audio Tracks" id="Item 935" disabled`. The command
  path therefore now reads `IsEnabled` before pressing and re-selects the clip when Live says
  no, instead of trusting the call.
- the crash-recovery modal: the retry budget that fixes it is in, but no run since has actually
  been shown the modal, so the answer path is still unexercised.
- the desktop was in active use by the owner's own session during the last attempt — another
  app holding Live's focus, Display Dimmer taking foreground. Live automation needs the desktop
  to itself, and the runner's own guards (foreground, desktop state) are what made that visible
  instead of producing a wrong click.

The shortest way to close the gap is one uninterrupted run on an idle desktop:
`autostem run --engine live --stems all --dest <folder> <file>`, then read the stage list in
`%LOCALAPPDATA%\SkeerAutoSTEM\logs`. The dialog is known-good; what has to be watched is the
218 s after the *Separate* click and the folder the runner looks in when it ends.

