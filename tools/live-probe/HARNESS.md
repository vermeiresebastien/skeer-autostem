# probe-harness.py — the permanent offline proof for the LOM probe

`tools/live-probe/probe-harness.py` is the promoted, hardened form of the throwaway stub harness
that lived in `%TEMP%\probe-sim\sim.py` while the probe was re-aimed. It exists so that the property
making the owner's single Preferences click acceptable — *stage 0 of
`AutoStemProbe/__init__.py` reads Live's object model and never writes to it* — is **gated in the
repo**, not remembered. If someone later edits the probe so that it maps a prompt, executes a menu or
calls the separation command, this harness reddens and says which shape and which call.

It is offline: it never contacts Ableton Live. See *Why this cannot reach a real Live* below.

## The contract, stated as an enforceable rule

Stage 0 (`AutoStemProbe._lom_seam`, plus the corroborating stages it shares `_collect` with) may
resolve and introspect `Live.Conversions`, `Live.Clip` and `Live.MidiMap` with `hasattr`, `dir()`,
`repr()`, `__doc__`, `sys.modules`, `importlib.util.find_spec` and an import. It may call **only**
these four Live API members:

| member | arguments | why it is permitted |
|---|---|---|
| `Live.Application.get_application` | none | the probe cannot obtain the Application handle at all without it; it returns the handle and reads nothing from the set |
| `Live.Application.get_major_version` | none | version read |
| `Live.Application.get_minor_version` | none | version read — see the finding below: the shipped code never actually calls it |
| `Live.Application.get_menu_info` | one menu path | availability read, not execution |

Everything else is a breach, and two classes of breach have their own exit codes so a reader can tell
"*this* was forbidden" from "*this* was simply never documented":

* **forbidden** (exit 3): `map_midi_control_to_prompt`, `remove_midi_map`, `add_auto_mappable_control`,
  `send_midi_cc`, `map_midi_cc`, `execute_menu_item`, `trigger_menu_item`, and the command itself —
  `separate_stems`, `separate_stems_of_clip`, `reassign_stems_source`.
* **undocumented** (exit 4): any other Live API member that gets called. The allowlist *is* the
  contract; a gate that only checked the two names somebody thought of would not be a contract.

`Live.Application.get_application` is a **fourth name beyond the three readers the assignment
enumerated**, and it is not negotiable: the probe's `_collect` starts with
`app = Live.Application.get_application()`, so a three-name allowlist could never go green on the
shipped bytes. The ad-hoc harness this file replaces never instrumented the call, which is how
"only three calls were ever made" was produced and why it was understated. It is permitted here with
the reason written down rather than dropped from the evidence.

## Running it

```
cd tools\live-probe
py -3.11 -B probe-harness.py                             # expect exit 0
py -3.11 -B probe-harness.py --mutation prompt-bind      # expect exit 3
py -3.11 -B probe-harness.py --mutation conversions-flag # expect exit 4
py -3.11 -B probe-harness.py --mutation command-blind    # expect exit 5
py -3.11 -B probe-harness.py --mutations                 # control + all three legs (expect exit 3)
py -3.11 -B probe-harness.py --help
```

`py -3.11` because Live 12.3.2 ships CPython 3.11 — the README's census fixes the target interpreter
at 3.11.x. On any other interpreter the harness prints a loud NOTE that the run is a portability check
of the harness, not the shipped-interpreter check. It resolves the probe path from its own
`__file__`, so it runs from a clean checkout in any working directory (the green log below was
produced from `tools\live-probe`; the harness was also run from `D:\` and exited 0).

## Exit codes

| code | name | meaning |
|---|---|---|
| 0 | GREEN | all eight shapes answered as documented; the observed call set never left the four permitted names |
| 2 | PIN_MISMATCH | the probe's bytes are not the bytes this proof is about |
| 3 | FORBIDDEN_CALL | a mapping/menu executor or the separation command was called |
| 4 | UNDOCUMENTED_CALL | some other Live API member was called |
| 5 | VERDICT_MISMATCH | a shape printed an answer other than the documented one |
| 6 | HARNESS_INVALID | stub-safety or self-check failed: recorders blind, an import reached for a disk path, the probe folder or the owner's real report moved, a mutation anchor not unique, the shipped source unparsable |
| 7 | MUTATION_NOT_CAUGHT | falsifier failure — a mutation leg did not produce the verdict predicted for it (the broken copy stayed green, or a gate fired with another code), the control leg was not green, or no gate caught anything. Green from a harness that cannot redden on a broken copy would mean nothing, so this is the worst exit in the table. |

In `--mutation` runs the exit code is the **gate's own verdict on the mutant**: 3, 4 or 5 there is the
expected result and means the falsifier caught the mutation; 0 or 7 means it did not. When several
mutants run in one process (`--mutations`), the exit is the strongest red by the fixed precedence
forbidden (3) > undocumented (4) > answer (5); the per-leg `FALSIFIER: PASS` lines, not the final
number, are what tell you all three gates fired.

**Every code in that table has been seen to fire.** 0, 3, 4 and 5 come from runs launched inside this
folder; 2 comes from the pin-drift leg, which is the same harness pointed at a drifted scratch copy of
the probe. 6 was induced twice — once with the probe source missing from the folder the harness resolves,
once by deleting `map_midi_control_to_prompt` from the harness's *own* `FORBIDDEN_METHODS`, which the
self-check catches before any mutation is executed. 7 was induced by giving one mutation a
deliberately wrong `expect_code`: it is the only way to watch the falsifier-failure branch fire without
weakening a gate to make it reachable. Those three legs are in `probe-harness-selfcheck.log`, SECTION 2 —
re-run 16:41 from copies of the shipped harness with recorded hashes, the first attempt's scratch trees
under `%TEMP%\selfcheck-legs\` having been deleted without being hashed — and nothing in them edited a
file in this folder.

A related honest limit: the harness prints one informational line comparing the probe's declared
`FORBIDDEN_CALLS` / `COMMAND_NAMES` against the sets gated here (`every one gated here: yes`). That
coverage line is *not* a gate — a name added to the probe later would not redden it. The allowlist is
the gate: any Live API member outside the four permitted names reddens with exit 4 whether or not
anyone remembered to list it as forbidden, which is why the contract is the allowlist and not the
forbidden list.

## The bytes this proof is about

`AutoStemProbe/__init__.py` sha256
`b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` (545 lines, 25,992 bytes, LF only).
The pin is in the header of `probe-harness.py`, the hash is recomputed from disk on every run, and a
mismatch is exit 2 — no shape is executed against unrecognised bytes. When the probe is deliberately
changed, the pin moves in the same change; that red is the point.

**That last rule was exercised on 2026-11-18, and this section's headline value is now the superseded
generation.** The probe deliberately changed — the module gained the `create_instance` factory Live
requires and one swallowed-`TypeError` arity call was fixed — so the pin moved with it:

| | superseded | current |
|---|---|---|
| `AutoStemProbe/__init__.py` | `b995063b…` (545 lines, 25,992 B) | `f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29` |
| `probe-harness.py` | `5de18524…` | `a5e991884f508209801581928e033a27184cf9886bcc6ae2c7951ff0514d3d7c` |

The reason the old bytes are worth keeping is that they are the bytes every log below and beside this
file describes; they are recoverable from the G0 snapshot rather than from git, since they were never
committed. The reason the change matters is in the next section.

**The gate has a structural blind spot, now closed by a companion rather than by this file.**
`probe-harness.py:625` builds the surface with `probe = probe_module.AutoStemProbe(CInstance())` — it
constructs the class directly and never calls `create_instance` (zero references in the harness outside
the comment added with the re-pin). So the harness verifies the *call contract* while bypassing the only
entry point Live uses, and it went green for days on bytes Live could not load at all. Loadability is
gated by `instantiation-gate.py` in this folder; the two are not interchangeable and Phase B needs both.

## The eight interpreter shapes

Each shape is a different way Live's script-facing Python could present the seam. All eight run in one
process against freshly rebuilt stubs, and each one is graded on both axes: the answer it produced and
the calls it made.

| shape | what the interpreter looks like | documented answer |
|---|---|---|
| `attribute` | `Live.Conversions` attached with `separate_stems`, `separate_stems_of_clip`, the high-quality flag and `reassign_stems_source`; `Clip.can_stems_be_separated` a class property | yes |
| `class-member` | the command is reachable only as a member of a class inside the module, not on the module itself | yes |
| `import-only` | nothing on the package, nothing in `sys.modules`, `find_spec` says None; only an actual import produces the module — how a builtin Live submodule behaves | yes |
| `flag-only` | a capability flag with no command behind it | partial |
| `no-members` | both modules resolve and neither carries anything worth finding | no, dead end 2 |
| `nothing` | neither module resolves by any of the four routes | no, dead end 1 |
| `stray-only` | an unpredicted separation-shaped name (`separate_audio_to_stems`) that must be reported without being inflated into a yes | no |
| `fatal-dir` | `dir()` on `Live.Conversions` raises, so stage 0 must come back inconclusive rather than guess | inconclusive |

Observed calls are **identical in all eight shapes**: `get_application` ×1, `get_major_version` ×1,
`get_menu_info` ×3, and nothing else — no `Conversions`, `Clip`, `MidiMap` or `Song` member is ever
called, and stage 4's prompt-name attempt is reported as refused (`attempted=False`).

## The deliberate mutations (this is what makes it a falsifier)

`--mutation` runs a **control leg** on the unmodified shipped source first, then one leg per named
mutation. Only the in-memory source string is patched; **no file on disk is modified by a mutation
leg** — the harness re-hashes the shipped file at the end of every run and prints `unchanged: True`,
and the post-run safety block fails with exit 6 if it ever were not.

| mutation | what is spliced into the shipped source, in memory | caught by | expected exit |
|---|---|---|---|
| `prompt-bind` | `Live.MidiMap.map_midi_control_to_prompt(40, 14, 0, "Live.Create.AudioSeparation")` inserted above stage 0's target loop | the forbidden gate | 3 |
| `conversions-flag` | `Live.Song.get_song()` inserted at the same anchor | the allowlist gate | 4 |
| `command-blind` | `COMMAND_NAMES = ["separate_stems", "separate_stems_of_clip"]` replaced with two names no shape defines | the answer gate | 5 |

`prompt-bind` is the mutation that matters: it re-adds exactly the call the deleted helpers used to
make. `conversions-flag` exists because a gate that only catches the one call someone thought of is
not yet a contract. Both call mutations redden **8/8 shapes**, and a mutant that failed to redden every
shape would exit 7 rather than be reported as a pass.

`command-blind` is a different kind of falsifier, so its PASS criterion is different and is stated per
mutation in the harness: it must not touch any call, and it must flip **exactly** `attribute`,
`class-member` and `import-only` from `yes` to `partial` while leaving the other five shapes as
documented. That asymmetry is the point: only those three shapes claim `yes`, so blinding the command
names can degrade only those three, and the other five never depended on finding a command. A mutation
that reddened all eight would be a call breach wearing an answer-gate costume and would prove nothing
about the answer gate. Proving exit 5 in memory also kept it clear of the pin gate: an on-disk edit is
exit 2 before any shape runs, which is precedence working as designed (and is what the first attempt at
this leg hit).

## Recorded runs

This index is the thing people will quote for the safety claim behind the owner's click, so it states
what each log can and cannot prove about *which bytes produced it*.

**The limit that shaped this section.** `probe-harness.py` prints the probe's hash on every run, but it
prints **no hash of itself** — only its own path. So the `harness : … sha256 5de18524…` line at the top
of each log is *my* record of what was on disk when I ran it, not the harness's own attestation. That
was fixable two ways: edit the harness to self-report (not allowed in this lane — `probe-harness.py` was
out of scope), or **replay**. I chose replay, and every log below is tied to the bytes by a measured
number rather than by an mtime argument.

| log | command | exit | harness it embeds | this log's own sha256 | tied to the shipped bytes by |
|---|---|---|---|---|---|
| `probe-harness-green.log` | `py -3.11 -B probe-harness.py` | 0 | `5de18524…` | `fb5aa199b8a84891929ce2cf0165cd6b4708c87689956ab1690f6da984210b0b` | replayed 16:41, output byte-identical |
| `probe-harness-mutation-prompt-bind.log` | `… --mutation prompt-bind` | 3 (expected) | `5de18524…` | `3232ad9b02476b2459970aba586ec80eef35bfe1a4b2b6a144b1a6bb45942ec4` | replayed 16:41, byte-identical |
| `probe-harness-mutation-conversions-flag.log` | `… --mutation conversions-flag` | 4 (expected) | `5de18524…` | `5c6d3f3e7d8840e68907458612c0e573af7788fba4fcd56523a9996c4653e9d2` | replayed 16:41, byte-identical |
| `probe-harness-mutation-command-blind.log` | `… --mutation command-blind` | 5 (expected) | `5de18524…` | `c57773cc473dae6c5b964468e15b4ff0590c5c20228d76681ba51d10ce684d51` | replayed 16:41, byte-identical |
| `probe-harness-mutations-all.log` | `… --mutations` | 3 (expected) | `5de18524…` | `b72244d86f4ff9e5a603ed27ba9e679f9062f2198e62fd57117b9994feacf965` | run from the shipped file after its last edit |
| `probe-harness-pin-drift.log` | `py -3.11 -B %TEMP%\task13-replay\pin-drift-2\probe-harness.py` | 2 (expected) | `5de18524…` | `4424d0ffbb1b8754de6f3e5f197efd7e65fb19846f86bff9ea8f5aabfabb46b4` | SECTION 2 re-run from a hashed copy |
| `probe-harness-selfcheck.log` | three legs under `%TEMP%\task13-replay\`, commands quoted inside | 6, 6, 7 (expected) | `5de18524…` | `3e7d8b2debb39dd05d970f4d6bc2380b26dd78df6eb33536ae924dedcddb6ac2` | SECTION 2 re-run, each copy hashed |
| `probe-harness-selfhash-table-2026-11-19.log` | `py -3.11 -B probe-harness.py --mutations` | 3 (expected) | `1d59b88f…` | `c04f9688bf156ec5a309c1b543de1db39df85f0a76ddc9adb5cff837658e7e5a` | the harness's own new `harness sha256` line; probe `f34292cf…` printed before and after the run |

**Superseded, 2026-11-18, but not invalidated.** Every row in that table is a true record of harness
`5de18524…` exercising probe `b995063b…`, and the G0 snapshot
`D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/` (42 files, manifest
`4292f6a16eb741d4d6011b56cc1a0642aa03c22b25ff88a1b78c28663d08ccf3`) is the witness that those bytes
existed and were what the logs say. What the table cannot support is any inference about *running*
them: `b995063b…` has no `create_instance`, so none of these greens describes a probe Live can load.
The same ten-leg table re-run against the current bytes is in `acceptance-repin-f34292cf/`, and its
own §5 states why it is single-witness.

The shipped bytes throughout: `probe-harness.py`
`5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1` (1,016 lines, 48,884 bytes, LF only)
and `AutoStemProbe/__init__.py`
`b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`. The harness was last edited at
16:24:47; the four mutation logs and the green log were written 16:26, and `--mutations` at 16:31, all
after that. **Replay is the proof, not that ordering**: at 16:41 each of those five was re-run from the
shipped `probe-harness.py` and the fresh output matched the recorded body **byte for byte**, ignoring
only the random `tempfile.mkdtemp()` folder name printed in the run preamble. Anyone can repeat it:
re-run the command in the table's second column from this folder, diff against everything below the log's
`HARNESS OUTPUT VERBATIM BELOW` line, and expect a difference only in that one path.

`probe-harness-mutations-all.log` is the one process that runs the control leg and all three mutants, so
it carries the three `FALSIFIER: PASS` lines side by side and ends with `CONTROL LEG GREEN (exit 0)`.
Its exit is 3 rather than one code per leg, by the precedence noted above.

The green run was also repeated with cwd `D:\` (`py -3.11 -B
D:\Projects\skeer-audio\autostem\tools\live-probe\probe-harness.py`) and exited 0 — the clean-checkout,
any-working-directory check, since the harness resolves the probe from its own `__file__`.

### Two logs that carry a superseded section

`probe-harness-pin-drift.log` and `probe-harness-selfcheck.log` each originally ran from a **scratch copy
of the harness whose bytes were never hashed**. Their verdicts were right and their exit codes matched
this index; what they could not do was name the harness that produced them. Both files now carry a
labelled `SECTION 2` — the same inductions re-run at 16:41 from copies with recorded hashes — and declare
SECTION 1 superseded **for provenance only**. Nothing was deleted from either file, and both SECTION 1
bodies are still byte-identical prefixes of what is on disk now (verified). The re-runs repeat the same
codes: 2 for the pin drift, and 6 / 6 / 7 for the three self-check legs.

Two details worth not tripping over:

* The drifted probe in SECTION 2 is `a927a63e…`, not SECTION 1's `1c571b2d…`. Both are "the shipped probe
  plus one appended comment line"; the comment text differs, so the hashes differ. Either is a valid
  drift, and the pin gate fires on both.
* Self-check legs B and C **cannot** run on the shipped bytes — a neutered allowlist and a wrong
  expectation are the whole point. Each records the shipped hash it was derived from, the one-line
  change, and the derived copy's own hash (`2e8c969c…` and `200aaa45…`). A green never comes from those
  copies; that is the assertion being tested.

Everything in this folder is LF-only, logs included: the harness reconfigures its own stdout to LF,
because Python would otherwise write CRLF into a redirected handle and mix separators inside one file.
One log needed help after the fact — `probe-harness-selfcheck.log` was assembled in three legs and six
CRLF terminators landed in **its own header labels**, not in any harness output; they were normalised,
and that file's header records the correction, what it touched, and the hash before the SECTION 2 append.
A correction like that belongs in the artifact, not only in a chat message.

### If anyone maintains this harness

Two things would remove the class of defect this section exists to work around; both are one-line changes
to `probe-harness.py`, and both were out of scope in the lane that wrote it:

1. **~~Print its own sha256 in the preamble~~ — DONE, 2026-11-19.** Harness `1d59b88f…` prints
   `harness sha256` next to the probe's, and `probe-harness-selfhash-table-2026-11-19.log` is the
   first log that self-attests: control GREEN (exit 0) plus all three mutants RED with their
   expected codes (3 FORBIDDEN_CALL, 4 UNDOCUMENTED_CALL, 5 VERDICT_MISMATCH), 3/3 FALSIFIER PASS,
   against probe `f34292cf…`.
2. **Re-run the five replays after any harness edit**, the same way this section does: the mutation legs
   and the pin leg cannot drift silently (they fail loudly), but a *recorded log* can quietly stop
   describing the current harness, which is exactly what happened here once.

## Provenance of `probe-reaim-2026-11-16.diff`

The re-aim diff is now a tracked-in-spirit file in this folder rather than a temp file:

* `probe-reaim-2026-11-16.diff` (30,616 bytes, 602 lines, LF only, no CR bytes)
  sha256 `e1cbb9e6b0b8d3b053f3f78beadd868e28252e713079828dcefc508d4856665b`. This is the
  **commit-ready** copy.
* It is the same change as `%TEMP%\probe-sim-orig\task12.diff`
  (sha256 `33d3cad688906dde4ca80d24fa76ed49fd83a0cf636a910aa2e74f50199bf03f`), whose header lines
  pointed at the temp path `a/C:/Users/…/probe-sim-orig/__init__.py` and therefore could not be
  reverse-applied. Only the four header lines differ: `tail -n +5` of the two files is **identical**
  (checked with `diff`), and both carry the same blob index line `cd9435a..eab9b99` and 602 lines.
* The copy is self-proving, which is what makes the temp originals disposable. Verified in a scratch
  tree outside the repo:

  ```
  git -c core.autocrlf=false apply -R -p1 probe-reaim-2026-11-16.diff   # exit 0
  ```

  reverse-applied to the shipped `b995063b…` bytes reproduces
  `738083ee5e77ae26ade23ed49a902189da05c7f0fa8a9a4c78614ac449fde50b`, which is exactly the hash of the
  pre-edit `%TEMP%\probe-sim-orig\__init__.py.before`, and `cmp` against those preserved bytes
  reported byte-identical. So `shipped bytes + this diff` pins both sides of the change.
* Consequently `%TEMP%\probe-sim-orig\` (`__init__.py.before`, `__init__.py.installed-before`,
  `task12.diff`) and `%TEMP%\probe-sim\` (`sim.py` and its run outputs) are **disposable**: the
  record they carried is now in this folder. Nothing in `probe-harness.py` reads from them.

## Why this cannot reach a real Live

1. **Stubs only.** `Live`, `Live.Application`, `Live.MidiMap`, `Live.Song`, `Live.Conversions`,
   `Live.Clip`, `_Framework` and `_Framework.ControlSurface` are built by this file and pre-populated
   in `sys.modules`. The probe is loaded with `compile()` + `exec` into a hand-made module, so no
   loader ever resolves the probe's own path and no `__pycache__` is written beside it (the harness
   also lists `AutoStemProbe/` before and after and fails on any change).
2. **`sys.path` scrub.** Every path matching `ableton` or `remote scripts` (case-insensitive) is
   removed before anything is imported and re-checked after the run. The pattern is deliberately
   narrow: scrubbing `Program Files` would break the local interpreter, and no Live-side module can
   be reached through it anyway.
3. **Guard finder at `sys.meta_path[0]`.** Any import whose root is `Live`, `_Framework`, `ableton`,
   `_Generic`, `Push` or `_TaskBar` is recorded and refused with `ImportError` — unless the shape
   explicitly declares that name synthetic-served (only `import-only` does, for `Live.Conversions`).
   So even a probe bug that reaches for an unstubbed Live module cannot touch a disk path. This is
   observable, not assumed: in the `nothing` shape the log shows
   `imports the guard saw: ['Live.Clip', 'Live.Conversions'] | refused: ['Live.Clip',
   'Live.Conversions']` — the probe really does try its fourth route, and the guard really does stop
   it.
4. **Post-run proof.** The harness asserts that no loaded Live-side module carries a `__file__`, that
   the scrubbed `sys.path` stayed clean, that the shipped probe hash is unchanged, and that the
   owner's real `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` did not move (its
   mtime/size/sha256 are recorded before and after; any change is exit 6). The report the probe writes
   goes to a `tempfile.mkdtemp()` scratch folder that is removed again.
5. **Nothing external.** No socket, no `127.0.0.1:9877`, no subprocess, no process enumeration, no
   window or pointer event, no registry, no PowerShell, no deletion of anything outside its own
   scratch folder.

## Findings (reported, not fixed)

* **`policy.read_only` is a claim, not a measurement - which is the reason this harness exists.** In
  the `prompt-bind` mutant every shape still reported `policy.read_only=True`, `attempted=False` and
  the rest of the refusal prose, while the injected code was in fact binding a MIDI prompt. The
  probe's own policy block therefore cannot be trusted as evidence about the calls; only an external
  recorder can. Read the report's policy keys as intent, and this harness's exit code as fact.

* **A real defect in the shipped probe, found while stubbing.**
  `AutoStemProbe/__init__.py:143` reads
  `report["environment"]["live_version"] = self._call(app, "get_major_version", "get_minor_version")`,
  but `_call`'s signature is `(self, target, method, *args)` (line 461), so `get_minor_version`
  arrives as an **argument** to `get_major_version()`, which takes none. On a real Live `_call`
  catches the `TypeError` and the report carries an error string where the version should be;
  `get_minor_version` is never called at all. The harness's per-shape `called as …` line shows it
  directly: `Application.get_major_version('get_minor_version') x1`. Impact is limited to
  `environment.live_version` — stage 0 does not read it, so the fork answer and every verdict line in
  the brief are unaffected. **Not fixed:** this brief forbids editing `AutoStemProbe/__init__.py`, and
  the owner is about to click against these exact bytes.
* **`Live.Application.get_menu_item_info` is uncovered scope.** Stage 2 loops
  `("get_menu_info", "get_menu_item_info")` and breaks on the first that exists (lines 190-196), so
  the fallback read never fires on a Live that has `get_menu_info`. It is present in the stub but
  deliberately **not** in the allowlist: if some build lacks `get_menu_info`, this harness reddens with
  exit 4 by design and the name must be added with the same read-only reason before the gate can be
  trusted there.
* The stub recorders accept any arguments, which is why the wrong-arity call above shows up as a call
  rather than as a `TypeError`. Nothing here proves what Live's builtins accept — only what this
  script asks of them.

## What this harness does *not* cover

* It is not the experiment. It proves the probe makes no forbidden calls and produces the documented
  sentence for each interpreter shape; it cannot say which shape Live's real interpreter is. That is
  still the owner's one click.
* Only the names this file's stubs *have* are instrumented. A member the stubs do not model cannot be
  observed — the guard finder is the backstop for that case, since an unstubbed Live-side name raises
  rather than silently resolving, and the refusal is printed per shape.
* `Live.Song`, `Live.Devices` and the rest of the object model are only present as far as stage 0
  touches them; no song-level or device-level call surface is covered.
* Byte-code and import side effects inside Live, the real `ControlSurface` lifecycle
  (`component_guard`, `schedule_message`) and the transport are stubbed as no-ops, so this harness
  says nothing about them.
* It gates the machine answer `report["answer"]` — one of `yes`, `partial`, `no`, `inconclusive` — per
  shape, and prints the human sentence `report["verdict"]` for a reader. It does not gate that
  sentence's wording: prose drift in the verdict lines is the README's business, not this gate's.
* **Tracking status.** Nothing here is committed. `tools/live-probe/` is still entirely untracked
  (`?? tools/live-probe/`), so the harness and its logs become a durable record only when someone
  authorises a commit. Until then this folder is as volatile as the `%TEMP%` directory it replaces —
  which is worth saying out loud, because the point of the task was to stop storing a proof in a
  volatile place.
