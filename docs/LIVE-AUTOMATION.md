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
hand the screen back       (the render needs no input; see "What the screen is for")
wait for completion        (bounded; see below)
collect the generated files, mapping them to the requested stems
convert to WAV only if Live wrote a supported non-WAV format
hand the files to the publisher
```

## Completion detection

A fixed sleep is never used. Before the *Separate* click the runner snapshots the folders Live
may write into, and a job is finished when **all** of:

- one or more **committed** files are new or changed relative to that snapshot — committed
  meaning an audio container, which is `Committed` in `StemFileMapper.ClassifyRender`,
- none of Live's own render targets is still advancing (`Writing` is empty), and
- those files' size and timestamp have been unchanged for `FileStabilitySeconds` (default 2 s).

A poll comes back in one of three states — `Committed`, `Writing`, `Stalled`
(`StemFileMapper.RenderOutput`) — and the third is the difference between a job that waits and a
job that reports. A target that has stopped advancing for `RenderStallPatience` (20 s) is no
longer committing, so audio that really landed is finished and is not pinned behind one stuck
file: the run completes and logs the unfinished targets by name. Where nothing landed at all, the
failure names the file that stopped, with its size and how long ago it was last written, instead
of reporting a timeout.

If **no** committed file appears, the closing of the Separate Stems dialog is treated as
cancellation/failure rather than success. Live counts as still working while a render target is
advancing, while its status line says so, or while it holds another visible window; only a Live
that has gone quiet for a minute, past the first 30 seconds of grace, is a Live that refused.
Waiting is bounded by `MaxSeparationMinutes` (default 30). Processing, success, cancellation, a
stalled render, timeout and failure are distinguished, not conflated.

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

The stability rule has a third trap, and it is the one that made a successful render look like
an empty folder. Live does not write `"... (Bass).wav"` directly: it writes
`AbletonTmp-0000` … `-0003` — **no extension at all**, and a RIFF header whose file-size field
is still the empty-file 72 — and renames them to the committed names when the render is
committed. Measured here on 2026-11-15: four of them, each about half its stem's final length,
sat size-stable for longer than `FileStabilitySeconds` while Live was still writing, the runner
logged "4 generated file(s) have been stable for 2.0s", and the collector then reported
`StemsMissing` over 33 MB of audio. Three rules come out of that, all in
`StemFileMapper.ClassifyRender`: only an audio container is output; a render target that is
*advancing* means the render has not committed, so audio that lands beside it is not yet finished
work; and a target that has stopped advancing for `RenderStallPatience` (20 s) is neither of those —
it is work that stopped, which is its own state (`Stalled`) and is reported by name rather than
waited out. The second rule is scoped to files that are new since the baseline, because a killed
render leaves its `AbletonTmp-*` behind permanently — four of them are what the next job measured as
"4 pre-existing file(s)" — and a scratch file counted from the whole folder would hold the batch
until the 30-minute timeout. That scoping is also what stops a leftover from being a stalled one:
unchanged since the baseline, it never enters the classification, so it pins neither the completion
gate nor the silence clock. The rename-at-commit model is not an assumption: the two temp projects
left by the runs that succeeded on 2026-11-14 contain eight committed names and zero `AbletonTmp-*`.

## Stall and failure handling

Automated input is stopped, and the batch paused, whenever:

- the Windows desktop is not the interactive one **and the run is about to click or type** —
  watching Live render is allowed with the screen locked, and a lock now stops the next
  track's clicks instead of killing the track in flight,
- the foreground window is neither Live nor Skeer autoSTEM, the run is about to click, and the
  focus cannot be reclaimed,
- the Live process exits,
- a dialog appears that is not one of the expected ones.

The desktop test reads the name of the input desktop rather than trusting a single call
(`WindowControl.QueryDesktop`). `Default` means interactive; `Screen-saver` and `Winlogon`
mean locked — the screensaver case used to read as *unlocked* while every synthetic click was
going nowhere. Anything else is *undetermined*, retried three times, then refused: the runner
stops rather than injects input it cannot verify, and says which of the two happened.

Input leaves this process through exactly two doors, and they are checked differently.
`InputSimulator.Send` is the only route to `SendInput` in the assembly and its injection methods are
`internal`, so the only way in is `GuardedInput` — the door that re-reads the desktop, the target
window and the foreground at the moment of the call, refuses with a reason code instead of throwing,
and is the reason "input is only sent after it has been verified" can no longer be forgotten by a
caller. The desktop question above is asked inside `Send`, not in each wait loop. Waiting on a lock
screen used to abort a render that had minutes left and needed nothing; the question is now asked by
whatever is about to press something, and `LiveAutomationRunner.CheckAbort` takes `interacting` to
say which kind of loop it is called from.

The other door is `InputSimulator.PostKey`, which deliberately does not go through `Send`: it
addresses one window's own message queue, so it needs neither the foreground nor a desktop, moves no
cursor, and cannot land in the user's window. Five places use it — Live's startup question, an
unexpected dialog mid-run, the Escape that closes a tracking menu, the Enter that confirms a save
dialog, and the takeover's keyed button. Where a dialog may also be clicked the key is tried first;
the save dialog is never clicked at all, because a save must not need the user's screen.

A prompt's button is chosen by a label matched exactly, from one of four ordered lists that are not
the same list: what a **startup** modal may be answered with is `"OK"`, `"Don't Recover"` (straight,
curly and truncated spellings), `"No"`, `"Cancel"` — deliberately without `"Close"`, because the
close box is what the next rule is about. Mid-run, the **dismiss** list adds `"Discard"` and the
three `"Don't Save"` spellings ahead of `"No"` and ends with `"Close"`. On the way out, the takeover
has its own two: **discard** labels (`"Don't Save"`, `"Do Not Save"`, `"Close without saving"`,
`"No"`) that abandon an unsaved set, and **confirm** labels (`"Save"`, `"OK"`, `"Open"`) for a file
dialog — the pair is separated because one of them destroys work and the other does not. The chosen
button is then answered by key, by a raised real click, and finally by Escape, in that order — the
startup prompt being the one place the ladder stops at the click, because an untitled modal that
will not come forward must not be clicked blind. A
prompt that survives all three pauses the batch and saves a diagnostic screenshot. Three rules here
come from runs that failed:

- **Never substring matching.** "No" is inside "Monitor".
- **Never first-match-in-tree-order.** A Live message box exposes its title-bar `Close`
  before the `No` that answers the question, so walking the accessibility tree and taking the
  first hit pressed the close box and left the question asked — which looked exactly like
  "Live never opened a window". Labels drive the search, elements are the inner loop
  (`LiveSessionTakeover.CollectButtons` and `Pick`).
- **Never a letter two buttons share.** `DialogKey.ForLabel` is handed the whole dialog and
  returns nothing when another button starts with the same character: on a box offering both
  "Close" and "Cancel", 'C' is a coin toss on the one question that decides whether an unsaved
  set survives. Ambiguity falls through to the click.

Live also prompts *before* it has a titled window, and its main window has an empty title
while loading, so a startup modal — "Live unexpectedly quit while you were working … Would you
like to recover your work?" — stalls the window wait indefinitely. `WaitForMainWindowAsync`
therefore answers known startup prompts while it waits. The retry budget counts **presses, not
glances**: while Live is still initialising, the modal's buttons are not in the accessibility
tree yet, and a window that offered nothing on the first poll is the same window that offers
"No" on the tenth — counting glances gave up on the recovery prompt after 1.5 s and then let it
block the main window for the full 180 s startup timeout. Six presses that do not dismiss a
window is the point where it is left alone. The recovery set is not the user's: it is the
working set a previous run killed, so "No" is the correct answer and
`Preferences/CrashRecoveryInfo.cfg` is deliberately left alone. A run that had to terminate Live
therefore leaves a prompt for the next one, which is why this path exists at all — and why the
takeover sends `WM_CLOSE` and waits before it reaches for a kill: a Live that quits on its own
clears its own crash flag, and the prompt stops being manufactured.

A foreign foreground window does not abort on first sight; the runner reclaims focus up to
five times before pausing (a Display Dimmer wake used to kill a run outright).

A file-specific failure is only skipped after Live has returned to a known state; an
unknown state pauses the batch. A run owns Live, so every session still open when a run
starts is saved and then closed first, as far as it can be saved (`LiveSessionTakeover`). A set
that was already saved is written where it lives; a set that was never saved goes to
`%LOCALAPPDATA%\SkeerAutoSTEM\recovered\<title>-<timestamp>.als`, and a name that already
exists is bumped rather than replaced. The app asks **once, before the run begins**, whether it
may take over the sessions it found; the engine itself never asks mid-run — the headless
companion simply does what was agreed and logs what it closed. What that agreement commits to:
a run that takes over Live never discards an open set. If a session has unsaved changes that
cannot be named and written to the recovery folder, the run stops before closing anything and
names the session it left open; only a session proven saved, or one with no unsaved changes, is
ever closed or terminated.

Live also has to be *idle*. While it is analysing files it accepts the
Create > Separate Stems command and drops it, which is what stalled the first real batch
("Analysis: Scanning (8,000 found)" sitting in the status line). Automation reads that
status line (`StatusText`) and does two things about it. It presses Live's own analysis
**pause** control when the build has one — a paused scan still starts with "Analysis", so the
pause is what makes the status line trustworthy (`Analysis: Paused` counts as idle), and the
scan simply resumes on the next launch, which is the deal Live offers and costs the batch
nothing — and it waits for the line to go quiet before the menu command and before the Separate
click.
The control is only in the tree while a scan is really running, so the same run can log both:
on 12.3.2 `run-20261115-191232` reported `Live showed no analysis pause control` at 18:13:13 and
then `paused Live's file analysis so the session can take commands` at 18:18:20.
The wait is bounded: after `SettleTimeoutSeconds` the run continues anyway and logs that Live
was still busy, because the `IsEnabled` gate below is the authority on whether the command can
actually be taken, and it has been right on a desk that was still scanning
(`run-20261115-191232` rendered 212 s with `Analysis: Scanning (2,344 found)` in the line).

There is a version of "busy" that no wait can fix, and this machine had been living with it:
`C:\ProgramData\Ableton` holds both `Live 12 Suite` and a hidden `.Live 12 Suite_updated`,
the copy a patch leaves behind. The staging copy is a complete Live and reports the newer
version, so version-ordered detection launched it — and it has never imported its Core
Library, so it comes up with the main window disabled and its accessibility tree reporting
two elements for the whole application. For those minutes every click, every menu read and
every status line is meaningless, and the log says only that the Create menu would not open.
`LiveLocator.Rank` now puts a staging directory last (`LiveLocator.IsUpdateStagingPath`), so
the install a person actually runs is the one that gets launched.

Edition is resolved the same way the folder is resolved - by name. Ableton's own version info says
`ProductName = "Live"` on both installs on this desk (`Ableton Live 12 Suite.exe` at 12.3.2 and the
12.3.5 staging copy, read from file metadata on 2026-11-16 with Live not started), so `Suite` reaches
`LiveInstall.Edition` from the folder and file names and from nowhere else. Two rules follow from
that and both are gated by tests: an edition word counts only as a whole word, so a folder named
"Satellite Sessions" is not a Live 12 Lite install; and an install that names no edition at all is
**allowed**, because `LiveInstall.CheckSupport` would rather let one mislabelled Standard build meet
a greyed-out menu than refuse a Suite batch over a moved folder. The app therefore refuses only what
it can identify, and the run-conditions dialog says so in those words.

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

`CreateMenuChooser` is that rule written as code: the ways of opening the Create menu are tried
in order (Live's own rectangle for the entry, the menu pattern, a window-relative point), and
after **each one** the popup is read back out of Live's tree. A strategy is never skipped
because an earlier one claimed success, and an attempt that produced no popup is a line in the
log rather than a verdict. Reading back also catches a menu that opened and closed again —
which is what an earlier attempt's click does to a later one.

The coordinate fallback was also the one that could never have worked, and for a reason that has
nothing to do with fractions: **`MOUSEEVENTF_ABSOLUTE` does not take pixels.** It takes a position
normalised over the whole virtual desktop, and `InputSimulator.MoveTo` passed the pixel pair
straight through. On this machine — two stacked 1920x1080 monitors, so a 1920x2160 virtual
desktop — a click aimed at `(91,34)` moved the cursor to `(2,0)`, measured with `GetCursorPos`
after the injection and confirmed on screen by Live's *system menu* (Restore / Move / Size /
Minimise / Maximise / Close) opening where the Create menu should have been. Every coordinate
click in every log before that fix landed within a few pixels of the top-left corner of the
primary monitor, which is what the month of "Separate Stems is greyed out / absent" was actually
made of: the read-back protocol did not fix the symptom, it named it correctly — "did not bring
up the Create menu" — and that sentence is what led to the corner.

The fraction itself was the second error, and it was tuned against the first one. Live's own UIA
tree reports the menu bar entries at `y=23..46` — `"Create"` as `(67,23,48,23)`, centre `(91,34)`
— against a maximised window rect of `(-8,-8,1936,1066)`, so the row is `0.039` of the window
height. The `y=39..62` and the clickable point `(92,50)` those numbers were read from are the same
row in a screenshot rendered 1.333x larger than the display it measured, and a fraction derived
from them aims at the transport row underneath. Screenshot pixels are not screen pixels.

### What the screen is for

A batch cannot be driven without the screen **for its mouse**, and that was measured twice on this
machine: `PostMessage` and `SendMessage` of `WM_LBUTTONDOWN`/`WM_LBUTTONUP` to Live's window — at
its UIA-reported clickable point, with the window activated first — leave Live's selection and
dialog state exactly as they were. Live does not read a synthetic *click* that arrives through its
message queue; a click has to be moved by `SendInput`, landing on whatever window is topmost. So
Live must be raised, unoccluded and foreground for as long as anything is clicked.

The keyboard is not the same story, and the sentence above used to cover both. A `WM_KEYDOWN` /
`WM_KEYUP` pair posted to one of Live's dialogs **is** acted on: `tools/live-probe/modal-no-mouse.ps1`
posted 'N' (VK 78) at the recovery question and the modal closed, with the cursor where it had been,
the foreground held by another application, and the same window's `Invoke` having just returned
without error having done nothing. `InputSimulator.PostKey` is that call, `DialogKey` chooses the
letter (a button's first letter, refused when two buttons in that dialog start with the same one),
and every dialog the run meets is now answered by key first and clicked only if Live ignores it.
What that removes from the screen is the prompts; what it cannot remove is the clip click, the
Create menu and the dialog toggles, because those are clicks on Live's session, not answers to its
message boxes.

What follows is the shape the run has:

- the screen is owned for a few seconds per track — the clip click, the Create menu, the dialog
  toggles, the Separate button;
- Live's modals — the startup question, the save question on the way out, an unexpected dialog
  mid-run — are answered through their own message queue, needing neither foreground nor screen;
- as soon as Separate has been pressed, `ScreenYield` decides whether the window that had the
  screen when the batch started goes back to the foreground, and the minutes of rendering are
  watched through UIA's status line and the stem folder, neither of which needs Live visible;
- if the user has taken the foreground for themselves in the meantime, `Plan` answers
  `UserHasTheScreen` and nothing is raised — the run does not fight a person for the display;
- a window that has closed, or that the user minimised, is left alone rather than restored.

`docs/SEPARATION-ENGINES.md` exists because the only way to drop those few seconds as well is
to stop driving Live at all, which is the local engine.

### Who owns the foreground

Two things about the foreground were measured on 2026-11-15, and both were silently fatal.

A Live that had to be terminated comes back asking *"Live unexpectedly quit while you were
working on the Live Set … Would you like to recover your work?"*. That question is an untitled
window, so it is invisible to a loop waiting for a main window, and `Invoke` on its **No** button
returns without error and does nothing — the same answer Live gives for its menu items. Three
presses **was** the retry budget, so three silent successes blacklisted the window and the run died
as `LiveWindowNotFound` after 180 s of waiting for a modal to move. `AnswerStartupPromptAsync`
then learned to press, read the window back and escalate to a real click on the button's own
clickable point. The log line from the run that proved *that* step:

> `answered Live's startup prompt with 'No' by clicking it; Invoke had done nothing`

That path has moved on since, in two ways the wording above does not show. The budget is six
presses now (`StartupPromptAttempts`), and a press is spent only on an attempt that actually reached
the window — a round that found no button because Live was still initialising costs nothing. And
`Invoke` is no longer attempted here at all: the key is posted first, and the click is reached only
after the modal is asked, strictly, to be the frontmost window (`TryActivateAndReport`, read as
`TargetIsForeground`, two attempts). A modal that will not come forward is not clicked and is not
charged a press; the next poll looks again.

Elsewhere the ladder is key → click → Escape, in that order, because the first step costs the user
nothing and the second costs him the screen — and the second and third rungs are now
`GuardedInput.Click` and `GuardedInput.PressKey`, which refuse rather than press blind.
`DialogKey.ForLabel` reads the whole dialog before it commits to a letter, so "No" among Close|Yes|No
is answered by a posted 'N' while a "Cancel" that shares its first letter with a "Close" is left to
the click. The key step is what the probe measured against Live's own modal, and the production call
is covered by `PostedKeyTests`: it asserts the two messages and the `wParam` a recorder window is
sent, and that the posted path never reached `InputSimulator.Send` — read from the process's own
injection counter, not from where the cursor happened to be, because a pointer that moves between
two reads is not evidence of anything. A startup modal answered by the shipped runner *has* been
observed (below); what is still outstanding is one answered by the **key** rung in a real run.

Second: `SetForegroundWindow` is refused — quietly, with no reason to ask for — while the Start
menu is open, while a toast is up, and while the user is mid-click. Live's own activation routine
tries every documented route (direct call, synthetic ALT tap, `AttachThreadInput` to the current
foreground thread, `SwitchToThisWindow`) and still returns false, because none of them can take
the foreground from the shell. One glance at that false was enough to abort a job with
`LiveLostFocus`, and the takeover that then closed Live is what produced the next recovery
question. `WindowControl.TryActivateAndReport` treats the read-back as the only verdict and retries
it for up to eight attempts, but it returns two answers rather than one — `TargetIsForeground` (the
exact window asked for is in front) and `ProcessIsForeground` (a window of that process is, because
Live's own modals take the foreground legitimately). `TryActivate` is the lenient OR of them and only
decides whether raising a window is worth trying; every caller that then sends input goes through
`GuardedInput`, which re-asks the strict question at the moment of the send. Collapsing the two was
not academic: a caller that read the lenient answer as the strict one clicked a button on a dialog
the user could not see and spent one of its six answers on it.

Only one runner may control Live at a time (a named mutex plus a queue-level guard), so two
instances cannot fight over the same Live session.

## Tunables

Menu/clip geometry and timings live in `%LOCALAPPDATA%\SkeerAutoSTEM\ui-profile.json`
(`LiveUiProfile`): window-relative fractions — measured against `GetWindowRect`, caption and
borders included, not the client area — timeouts, the file-stability window and whether to
capture diagnostic screenshots. They can be adjusted without recompiling if a
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

What has been proven since, end to end and by the shipped runner
(`run-20261115-191232`, job `a77b0870…`) — and on **Live 12.3.2**, the install the locator now
chooses on this machine, so this is the build the shipped default path is measured against:

> `separation completed in 212.2s`
> `published 4 stem(s) to C:\Users\Sebastien\Desktop\test\Akcept - Howl (Original Mix)`

with four 51 740 796-byte files — `bass.wav`, `drums.wav`, `other.wav`, `vocals.wav` — on disk
after it, from an MP3 dragged in. That closes the §6 output-collection claim above: the runner
watched the folder Live really wrote into, detected completion, verified and published. The same
run also answered a real recovery modal in product:

> `answered Live's startup prompt with 'No' before the main window appeared`

which was the crash-recovery gap above — on the click rung of the ladder.

Not proven, and what each one is waiting on:

- **the key rung has not yet met a Live modal in a run.** `tools/live-probe/modal-no-mouse.ps1`
  measured the mechanism against Live's own question, and `PostedKeyTests` exercises the production
  call against a window the test creates and destroys for itself — it is a real window with a real
  message queue, but it is not Live's, and the test reads back the messages rather than watching
  Live answer. What it also settles, from inside the process, is that the posted path injected
  nothing. No Live startup modal has been handed to the shipped runner by a real run since the key
  rung came first.
- **a second track in the same run.** The next job of `run-20261115-191232` failed
  `ClipNotSelected`, and the cause was this code's own bug: a substring search for the clip named
  "clip" matched the browser's "Clips" category and clicked that. `LiveUi.FindClipSlot` plus
  thirteen tests is the fix; it has not been re-run against Live.
- **the same command path is not repeatable under desktop contention.** The following run
  (`run-20261115-192254`) ended `LiveLostFocus` with another application holding the foreground.
  Live automation needs the desktop for its clicks, and the runner's own guards (foreground,
  desktop state) are what made that visible instead of producing a wrong click.
- **a takeover of a session that was open, unsaved and named.** `WM_CLOSE` goes out before anything
  is killed, the save prompt is answered by key, and the kill itself is now gated: `UnsavedWorkPolicy`
  allows a bounded terminate only for a set proven saved or carrying no unsaved marker, and a session
  whose work could not be named and written is left open while the run stops. That gate is what the
  takeover tests assert — refused outcomes are checked for zero closes, zero terminations and the
  window still visible — but no test here is a Live, and the last real run that came near it logged
  `did not close on its own; terminating it` before the gate existed.

Closing those Live-side gaps costs the owner's screen for a few minutes per track — which he has
asked not to happen unasked — or the Live Object Model seam, which needs one control-surface slot
given to `AutoStemProbe` and has therefore never run. The command, when a run is wanted, is
`autostem run --engine live --stems all --dest <folder> <file>`, and the stage list lands in
`%LOCALAPPDATA%\SkeerAutoSTEM\logs`.

