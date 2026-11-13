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

A fixed sleep is never used. A job is considered finished when **all** of:

- Live has produced files in `Samples\Processed\Stems` that are new or changed relative to
  the pre-job baseline, and
- those files' size and timestamp have been unchanged for `FileStabilitySeconds` (default
  2 s).

and if **no** files appear, the closing of the Separate Stems dialog is treated as
cancellation/failure rather than success. Waiting is bounded by
`MaxSeparationMinutes` (default 30). Processing, success, cancellation, timeout and failure
are distinguished, not conflated.

## Stall and failure handling

Automated input is stopped, and the batch paused, whenever:

- the Windows desktop is locked / on the secure desktop,
- the foreground window is neither Live nor Skeer autoSTEM,
- the Live process exits,
- a dialog appears that is not one of the expected ones.

Only *safe* answers are ever chosen for a dialog ("don't recover", "discard", "cancel",
"no"). Anything else pauses the batch and saves a diagnostic screenshot instead of guessing.

A file-specific failure is only skipped after Live has returned to a known state; an
unknown state pauses the batch. A leftover autostem working set from an earlier run is
closed automatically, but a Live session showing anything other than a autostem working
set is treated as the user's own project and is never closed, replaced or modified.

Only one runner may control Live at a time (a named mutex plus a queue-level guard), so two
instances cannot fight over the same Live session.

## Tunables

Menu/clip geometry and timings live in `%LOCALAPPDATA%\SkeerAutoSTEM\ui-profile.json`
(`LiveUiProfile`): window-relative fractions, timeouts, the file-stability window and
whether to capture diagnostic screenshots. They can be adjusted without recompiling if a
Live layout differs.

---

## Outstanding verification

The sequence above up to and including *"invoke Separate Stems to New Audio Tracks"* has
been exercised against the installed Live 12.3.5:

- the working project opens in Live with the correct clip,
- the clip slot is found and selected through UI Automation,
- the Create menu opens through UI Automation and the
  `Separate Stems to New Audio Tracks` item is present and reachable in the popup.

Not yet exercised end to end on this machine: the Separate Stems dialog contents, the
completion signal, and real Live-generated WAVs. The blocker was environmental, not
architectural — the desktop was in active use by another session that was itself driving
Ableton (an `ableton-mcp` process was running), so Live could not be driven exclusively and
uninterrupted. See `docs/TEST-REPORT.md` for the shortest procedure to close that gap.
