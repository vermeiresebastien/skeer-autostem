# Live probe (`AutoStemProbe`) — the read-only LOM seam test: what is already established, what the one click now decides, and how to read its report

**Status, 2026-11-16, measured against the owner's running Live 12.3.2 Suite. The script was
re-aimed and re-installed at 15:44 today; every statement below describes the build whose bytes are
in §10, not the earlier prompt-mapping draft.**

> **Superseding status, 2026-11-18.** Everything below dated 2026-11-16 is a true record of the bytes
> `b995063b…`. Those bytes had a defect no offline gate could see: the module exported **no
> `create_instance` factory**, which is the only name Live uses to instantiate a Remote Script. The
> surface therefore could not appear in any dropdown, the row-7 pick was impossible, and every green
> harness run on record was green about a probe that cannot run. The fix landed 2026-11-18 —
> factory plus one swallowed-`TypeError` arity bug on `get_minor_version` — the harness was re-pinned
> to `f34292cf…`, and the ten-leg exit table was re-run: all ten match. Evidence and the blind-spot
> analysis: `acceptance-repin-f34292cf/INDEX.md`. **The installed copy has not been touched and is
> still the unloadable one.** Read that file's §5 before treating any of this as second-witnessed:
> one pair of hands both wrote the fix and ran the gate.

- **Already established, with no dropdown pick, no click and no cursor movement:** a Python remote
  script runs *inside* his Live process and serves a command channel on loopback — a
  `LISTENING 127.0.0.1:9877` socket held by the Live pid itself (`35676`), answered
  `get_script_info` (`script_version 1.7.1`, `protocol_version 1`, 19 capabilities) and
  `get_session_info` (tempo 129.0, one track, not playing). That is the in-process LOM seam,
  demonstrated live. **Amended 2026-11-18:** that 19 is the handshake's advertised list only —
  **layer 1** — while the same script *dispatches* **37** names; the Qoder wrapper's **37** MCP
  tools are **layer 2**, a different 37, and our own harness permits **4** readers. Keep the three
  apart: `SEAM-CAPABILITIES-2026-11-18.md`. §11 documents it and the read-only script that proves it.
- **What the probe now decides, and nothing else:** whether Live's *script-facing* Python can name the
  separation command at all. Stage 0 (`stages.0_lom_seam`) asks Live's own interpreter, with
  `hasattr`/`dir`/`find_spec`/`import` only, for `Conversions.separate_stems`,
  `Conversions.separate_stems_of_clip`, `Conversions.can_separate_stems_in_high_quality` and
  `Clip.can_stems_be_separated`, and writes its conclusion into the top-level `answer` and `verdict`.
  It does not test whether a script loads (§11), whether a prompt API exists (demoted to corroboration,
  stages 1 and 3), or whether the command can actually be *invoked* — see the next bullet.
- **What even a `yes` will not decide, stated so nobody reads it wrong:** `answer: "yes"` means the
  **name is visible to a remote script**, not that the product can call it. In Ableton's own shipped
  model `separate_stems_of_clip` takes six arguments (`audio_clip, vocals, drums, bass, others,
  use_high_quality`, §7) and `separate_stems` is a `Function` descriptor, so a `yes` licenses the next
  experiment; it is not proof of a working call path. **verified** (shipped `.pyc` name tables) for the
  shape, **unknown** for the script-facing equivalent until the report exists.
- **Build under test:** `sha256 b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`,
  25,992 B, 545 lines, LF-only, repo copy and installed copy byte-identical (§10). The report proves
  which build produced it: a `stages.0_lom_seam` key can only come from this one (§5).
  **Amended 2026-11-18 09:45:11:** the *repo* copy has since moved to **26,112 B / 550 lines /
  `sha256 f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29`** (the factory change,
  dispatched to another lane), while the
  *installed* copy is **still `b995063b…` at 25,992 B** with its 19:32:18 cache unchanged — so
  "repo and installed byte-identical" was true of the build described above and is **not** true of the
  tree now.
  **Re-pin done, 2026-11-18 09:52.** The repo bytes above are now the ones the proof is about: the
  harness pin was moved (`probe-harness.py` `5de18524…` → `a5e99188…`) and the full exit table —
  ten legs, `0/3/4/5/3`, help `0`, pin-drift `2`, self-check `6/6/7` — was re-run green-to-expectation
  against `f34292cf…`. See `acceptance-repin-f34292cf/INDEX.md`, which also characterises the new
  bytes and states why every pre-2026-11-18 green was green on a probe Live could not load. That set
  is **single-witness**: the same hands wrote the fix and ran the table. The installed copy is
  **still `b995063b…`**, unchanged and still unloadable-by-Live; re-copying it is now a prerequisite
  to any dropdown pick (§3).
- **Settled negative, so nobody looks for a shortcut:** the running seam cannot be made to execute
  arbitrary Python — no `importlib`, no `__import__`, no `imp`, no `sys.path` handling, no `exec(`/
  `eval(`/`compile(` in its source (§11). There is no way to answer the narrowed question from the
  socket; it needs code inside Live, which is what the click is for.
- **Consequence that is not optional reading:** a socket that drives a DAW is an exposure, and a
  product that ships its own remote script inherits it. §12.


Labels used below: **verified** = read from Ableton's own text (a page fetched, or a file Ableton
shipped on this machine) or from this machine's own bytes; **inferred** = a conclusion drawn from
that evidence, not stated by it; **blocked** = could not be established, and not to be treated as
safe or as true.

Related but separate: `tools/live-probe/modal-no-mouse.ps1` in this folder answers a *different*
question (can Live's startup "recover?" modal be answered without moving the mouse). It is not part
of this probe and nothing here applies to it. **verified** (read the script).

---

## 1. The question A1 answers

autoSTEM separates stems by driving Ableton Live's own `Create > Separate Stems to New Audio
Tracks` through UI Automation plus Win32 input. That costs the user an idle screen, a yielded
foreground and his mouse. A1 asks the fork question: **is there an in-process (Live Object Model /
Python remote-script) seam that can trigger the same command without touching the screen?**

| `answer` in the report | What it commits the product to |
| --- | --- |
| `yes` — a separation **command** name is reachable from a script (`stages.0_lom_seam.found` holds `separate_stems` or `separate_stems_of_clip`) | Live is a candidate *separation engine*. A2 opens: build the call against a real clip and prove it works head-to-head with the UIA path. Do **not** write "Live can be driven from a script" until A2 exists — this answer is visibility, not invocability. |
| `partial` — only a capability **flag** is reachable (`can_separate_stems_in_high_quality`, `can_stems_be_separated`) | A script can be told whether something is separable, but not told to separate it. Enough for pre-flight validation in the UIA path; not enough to retire it. A1 stays open, one notch narrower. |
| `no` — the objects resolve but carry no such member, **or** `Live.Conversions`/`Live.Clip` is unreachable at all | Desktop automation stays the only route. A1 closes, and the remaining work goes to making the automation safe, not to the LOM. This is a decision, not a failure. |
| `inconclusive`, or no report at all | The click bought nothing — stage 0 raised, or Live never loaded the script. Read the log line first (§7); re-run only after the cause is named. |

**Where A1 stands before any click, as of the re-aim.** The two facts that used to pull against each
other now point the same way. (1) The seam exists and is not in question: a remote script is executing
inside his Live process right now, answering commands over loopback (§11). (2) The command has a home
in Ableton's own shipped model — `live_model\Live\Conversions.pyc` carries **`separate_stems`** (a
`Function` member) and **`separate_stems_of_clip`** (a real method), next to the
`can_separate_stems_in_high_quality` property, and `live_model\Live\Clip.pyc` puts
`can_stems_be_separated` on **`AudioClip`** (§7). What no static read of this machine can settle is the
one remaining gap: `live_model` is Push's proxy layer over Live's internal model RPC, and *that*
package is not the same thing as what `import Live` hands to a control-surface script. Only Live's own
interpreter can answer that, and stage 0 of this build now asks it directly. **The click is therefore
the decisive experiment, not a preliminary one — subject to the visibility-versus-invocation line
above, and to §7's finding that a `yes` naming a `live_model` path would be corroboration, not the
answer.**

## 2. Which script folders Live reads, and which slot this rides in

- Live has **seven** control-surface slots, and Live itself prints them 1-based in its own log — the
  authoritative table, read from
  `%APPDATA%\Ableton\Live 12.3.2\Preferences\Log.txt` (2026-11-16T14:53:43):

  ```
  AMidiIO: Midi Remote Scripts:
    MidiRemoteScript 1 [Control Surface="Push"            Input="Ableton Push" Output="Ableton Push"]
    MidiRemoteScript 2 [Control Surface="LK_Bridge"       Input="None" Output="None"]
    MidiRemoteScript 3 [Control Surface="MIDI_Mix"        Input="None" Output="None"]
    MidiRemoteScript 4 [Control Surface="Keystation_Pro_88" Input="None" Output="None"]
    MidiRemoteScript 5 [Control Surface="Roland_A_PRO"    Input="None" Output="None"]
    MidiRemoteScript 6 [Control Surface="AbletonMCP"      Input=… Output=…]
    MidiRemoteScript 7 [Control Surface="None"            Input="None" Output="None"]
  ```

  **verified** (log read). Two corrections to the hand-around: the free surface is the **seventh
  row**, and the usage-log field `midi_remote_script_slot_N` is **0-based** while
  `midi_io_status_controlsurface {"slot":N}` and this `MidiRemoteScript N` table are **1-based** — so
  "slot 6" in the notes means row 7 here. Quoting a bare number across those two sources is a
  one-row error waiting to happen.
- **A portless script loads and runs, and this is now runtime-proved, not inferred:** rows 2-5 are all
  slotted with `Input="None" Output="None"`, and the same log shows `LK_Bridge` emitting
  `snapshot sent (requested)` every ~15 s and `AbletonMCP` accepting socket connections.
  **verified** (log read). The probe needs no ports, so picking it in row 7 will actually run it.
- **A slot change takes effect immediately, in the session that is open.** The log carries
  `Python: INFO:Push.push:… - Push script loaded` / `Push script unloaded` at 2026-11-15 18:21 and
  2026-11-16 02:15, 11:39 and 14:47 — loads and unloads without a Live restart. **verified** (log
  read). Consequence for safety: the pick injects a new control surface into whatever set is open at
  that moment, which is why §4 starts with "save or close first".
- Folders Live lists scripts from: the built-in
  `<Live install>\Resources\MIDI Remote Scripts` (146 script folders here), the user library
  `%USERPROFILE%\Documents\Ableton\User Library\Remote Scripts`, and a per-version
  `%APPDATA%\Ableton\Live 12.3.2\Preferences\User Remote Scripts`. **verified** (all three listed on
  this machine).
- Ableton's own shipped instruction for that last folder
  (`…\Preferences\User Remote Scripts\InstantMappings-HowTo.txt`, written by Live on this machine):
  *"Create a directory in 'User Remote Scripts'. The name of the directory will show up in the list
  of Control Surfaces in Live."* and *"The name of the directory should start with a letter (not a
  number or special character such as '\_')."* **verified** (file read). `AutoStemProbe` satisfies the
  naming rule. **verified** (same file).

## 3. Does merely being in the folder load it? No — and that is documented

- **verified, from Ableton's own text:** listing is not loading. The shipped how-to above promises
  only that the *name* "will show up in the list of Control Surfaces". Ableton's
  *Installing third-party remote scripts* article (help.ableton.com, 209072009) gives the activation
  step separately: *"Launch Live. Open Live's Preferences → MIDI tab. Select the script using the
  dropdown menu in the Control Surface column."* **verified** (article fetched).
- Corroboration from this machine, in both directions: across the whole retained Live log (2026-11-02
  → 2026-11-16 15:01, 1,645 slot-table rows), the only Control Surface values ever recorded are
  `Push`, `LK_Bridge`, `MIDI_Mix`, `Keystation_Pro_88`, `Roland_A_PRO`, `AbletonMCP`, `Plugindex` and
  `None`. `Plugindex` sits in the user Remote Scripts folder right now, is slotted by nothing right
  now, and produces nothing — it only ever ran when it was explicitly named in row 6 (34 rows).
  **`AutoStemProbe` appears in zero of the 1,162 slot-table rows emitted since its own folder was
  written at 2026-11-15 20:25**, across 13 Live session starts on Nov 15 and 3 on Nov 16. **verified**
  (Log.txt parsed by timestamp). `Tap`'s case-insensitive "matches" are a false positive: `grep -i tap`
  hits `InitApplication` — 55 of them at the 2026-11-18 read, none of them the script name, which
  occurs 0 times case-sensitively. It has never been slotted.
- Consequence: copying the folder in is inert. The load happens **only** when he picks
  `AutoStemProbe` in row 7, and it stops the moment he puts the row back.
- **blocked:** whether a *user* folder beats the *built-in* folder when both hold the same script
  name. Nothing in Ableton's reachable text states an order, Live's log does not print the search
  path, and this machine offers no test — the five user script names (`AbletonMCP`, `AutoStemProbe`,
  `LK_Bridge`, `Plugindex`, `Tap`) collide with none of the 146 built-in folders. The probe does not
  depend on shadowing anything: its name is unique, so whichever order Live uses, it resolves to this
  folder.

## 4. The exact click sequence (Live 12.3.2, this machine)

**Live is running on this machine right now** — the log's last line is
2026-11-16T14:55:34 `(LK_Bridge) LK Bridge: snapshot sent`. Treat any pick as a change to his live
session. **verified** (log read).

1. **Save or close the open set first.** A slot pick does not touch the set, but our own runner has
   discarded an unsaved `Untitled*` set before, and a portless script loads into the session
   immediately (§2). Do this in a throwaway or saved set. **inferred** from repo history plus the
   live-load evidence above.
2. In Live: **Options → Preferences…** and go to the tab Ableton calls *Link / MIDI*
   (`help.ableton.com` 209774285: *"Open Live's Preferences to the Link/MIDI tab. Select your Control
   Surface (e.g. Push 2)."*). **verified** for the tab name (article quoted).
3. In the **Control Surface** column, use the **seventh row** — the one the log reports as
   `MidiRemoteScript 7 [Control Surface="None"]`. Not "slot 6" counting from one; see §2.
4. Pick **`AutoStemProbe`**. Leave its Input/Output ports on `None` — the probe needs none, and rows
   2-5 prove a portless surface still runs.
5. Wait ~5 s. Live's log gets three lines from the script itself, in this order:
   `autoSTEM probe loaded (will run in ~40 ticks)`, then `autoSTEM probe verdict [<answer>]: <verdict>`
   and only then `autoSTEM probe report: {first 2000 chars of JSON}`. The verdict is logged *before* the
   dump on purpose — Live truncates long lines, so the answer must not be what falls off the end. **verified**
   (`_write`, lines 526-540 of the build in §10).
6. Read it: `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` — the reading order is in §5, "How to read
   the report".
7. Put row 7 back to `None` (§6 step 1) and confirm the log shows
   `MidiRemoteScript 7 [Control Surface="None"]` again. Give it ~10 s from the first pick before you
   touch anything else, and do not quit Live inside that window (§7, component guard).

Nothing in steps 2-7 needs an idle screen or a still mouse: it is one dropdown each way. **verified**
(the probe's code has no UI interaction; read `AutoStemProbe/__init__.py` top to bottom).

## 5. What the probe writes, and what it touches

- **Writes one file:** `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json`, creating the
  `%LOCALAPPDATA%\SkeerAutoSTEM` directory if it is not there. It also calls `log_message`, which
  writes into Live's own log. **verified** (`REPORT_PATH`, `_write()`).
- **Changes no preference, no set, no document, no device.** The module level only defines names; the
  first thing that could mutate Live is inside `_run_probe`, scheduled 40 ticks after load and wrapped
  in a try/except that records a fatal traceback instead of propagating. **verified** (code read).
- **Everything it calls inside Live, exhaustively.** An audit of every call site in the 545-line build
  leaves exactly five things that reach into Live: `Live.Application.get_application()` (`:137`),
  `app.get_major_version(…)` (`:143`), `app.get_menu_info(…)` or `get_menu_item_info(…)` once per
  `MENU_PATHS` entry (`:195`), the stage-0 import described below, and the `_Framework` plumbing
  (`component_guard`, `schedule_message`, `log_message`). `_call` (`:461-470`) is the *only* place in
  the file where a fetched attribute is invoked, and it has exactly two callers. Everything else is
  `hasattr`, `getattr`, `dir`, `repr`, `isinstance`, `sorted` and `json`. **verified** (`grep` of every
  `self._call(` site and every `)(` in the build, read against the file in §10).
- **The mapping calls are gone from the file, not disabled.** `_try_prompt`, `_try_prompt_names`,
  `_unmap` and `_midi_devices` do not exist any more (0 occurrences each), and the five names this probe
  used to be afraid of appear only as strings — in `CANDIDATE_NAMES`, in `FORBIDDEN_CALLS` (reported in
  the JSON as `policy.not_attempted`) and inside `hasattr(...)` name lists. Zero matches for
  `map_midi_control_to_prompt(`, `remove_midi_map(`, `add_auto_mappable_control(`,
  `execute_menu_item(`, `trigger_menu_item(`. **verified** (token counts against the shipped bytes). So
  the risk that used to headline this section — `map_midi_control_to_prompt` overwriting a real
  CC 40 / channel 14 binding on one of his five surfaces, and `remove_midi_map` then *deleting* rather
  than restoring it, with no undo — is not mitigated in this build. It is unreachable, and the refusal
  is readable in the code rather than only in a report field.
- **The one thing that is not a read: stage 0 can import.** If `Live.Conversions`, `Live.Clip` or
  `Live.MidiMap` is neither an attribute of the loaded `Live` package nor already in `sys.modules`,
  route 3 tries `importlib.util.find_spec` (metadata only, nothing executed) and route 4 tries
  `__import__("Live.<name>")`, which runs that module's top-level code inside Live's interpreter. The
  script declares this itself in `policy.interpreter_state_it_can_change` (`:151`). Bound of that
  action, from the call-site audit above: it can add modules to `sys.modules`. It cannot touch the set,
  the transport, a device, a mapping or a preference. **verified** (code read) / **inferred** (that the
  bound holds — it rests on the audit, not on a Live-side observation).
- **Writes: a file and a log line, not a document.** Keep the distinction the Team needs: the report and
  `log_message` are filesystem and log writes; the song is never opened, edited, marked dirty or saved.
  A run cannot cost him work in the set he has open, which is a different claim from "a run cannot write
  to disk" — it can, and does, to `%LOCALAPPDATA%\SkeerAutoSTEM\`.
- **What persists after he quits Live:** the report file, the log lines, and the **slot pick** — and the
  pick is the only one of those three inside Live's own state. `%APPDATA%\Ableton\Live 12.3.2\Preferences\Preferences.cfg`
  (131,142 B, last written 14:51:58) holds a `MidiRemoteScriptPreferences` group with **seven** row
  sub-keys, `ScriptPrefs[0]` … `ScriptPrefs[6]` (offsets 19721, 19782 … 20087, 61 bytes apart) — so the
  file numbers the rows **0-based** while Live's log table and the dropdown number them 1-based: row 7 is
  `ScriptPrefs[6]`. Which key stores the row's *Control Surface name* is **blocked**: `AbletonMCP` occurs
  exactly 3 times in the file and all three sit in the MIDI **device** tables (125559 and 128730, beside
  `MidiInDevicePreferences` / `MidiOutDevicePreferences`, plus 131056), not inside the `ScriptPrefs`
  block. This document therefore stops claiming an offset for the name. `MidiMap` occurs **0** times in
  either encoding and there is no mapping payload anywhere in the file. **verified** (byte-level search in
  both encodings, with `AbletonMCP`/`Push` as positive controls). Where *user* MIDI Map Mode mappings are
  stored is separately **blocked** — chapter 34 as fetched does not say and the two articles that might
  returned 403 (§9) — and no longer matters here, since this probe makes no mapping. Script MIDI maps are
  runtime state in any case: Live's own scripts rebuild theirs through `build_midi_map` /
  `request_rebuild_midi_map` (43 and 25 files) each time a surface is enabled, so nothing a script maps
  is read back out of preferences at startup — which is why this build could leave no mapping state even
  if it made a mapping, which it cannot. **inferred** from the shipped-script name census plus the token
  scan above.
- **Read this file's own method warning before trusting any `Preferences.cfg` zero:** keys are Latin-1 but
  **values are stored UTF-16LE with length prefixes**, so a plain ASCII `grep` reports absence for every
  value in it. `AbletonMCP` is 3 hits in UTF-16LE and 0 in ASCII; `Push` is 22 and 1. Every
  `Preferences.cfg` claim above was re-measured in both encodings. **verified** (byte search).

### How to read the report

`live-probe.json` carries the answer at the **top level**, so nobody has to work through the
prompt-and-menu record to find it. Read it in this order.

1. **First, prove which build wrote it.** A `stages.0_lom_seam` key, a top-level `decided_by` and a
   `policy` block can only come from the build in §10; the deleted prompt-mapping draft had none of
   them. `environment.python` must read `3.11.x` — Live's own interpreter — and `environment.file`
   should point at the `AutoStemProbe\__init__.py` in the User Library, not at the repo copy. If
   either is wrong, Live did not run *this* script and nothing below means anything. **verified**
   (Live 12.3.2 ships 3.11; every `.pyc` in the user Remote Scripts folder is `cpython-311`).
2. **`answer`, `verdict`, `decided_by`, `corroborating_only` — this is the result.** `decided_by` is the
   constant `"stages.0_lom_seam"`, `answer` is one of `yes | partial | no | inconclusive`, `verdict` is
   the same conclusion as one sentence with each dead end named, and `corroborating_only` lists the four
   stage keys (`1_api_surface`, `2_menu_info`, `3_prompt`, `4_prompt_names`) that must **not** be read as
   the answer. §1's table says what each value commits us to.
3. **`environment.live_dir`** — the namespace Live's script-facing `Live` package actually exposes. If
   `Conversions` or `Clip` is in this list, stage 0's first route resolved it and the `found`/`missing`
   records below are about a module that was already loaded; if it is absent here but the import route
   succeeded, you have learned that it exists but is lazy-loaded, which is a different (and still useful)
   answer.
4. **`stages.0_lom_seam`** — the evidence behind the answer: `targets[]` per module with
   `looked_for`, `resolves`, `routes_tried` (each of the four routes with what it said), `resolved_by`,
   `holders` and `dir`; `found[]` records with `scope`, `name`, `decides`, `is_flag`, `kind`, `doc`,
   `repr`; `missing[]` with the reason and the routes that failed; `strays[]`;
   `live_submodules_in_sys_modules`; and `fatal` / `strays_error` if a half died.
5. **Then the corroboration**, in this order: `stages.1_api_surface` (each of the eight historical
   prompt/menu names with `present` and, when present, `kind`/`doc`/`repr` and **`called: false`** — this
   build invokes none of them, unlike the draft), `dir_Live_MidiMap` and `dir_c_instance` (raw attribute
   lists, so a wrong guess can be read off the report instead of re-probed), `stages.3_prompt` (presence
   of the three prompt methods plus a named refusal), `stages.2_menu_info`, `stages.4_prompt_names`.

**Four checks to run before you believe a `yes`.** Each one is answerable from the report alone:

- **Which holder?** Read `found[].scope`. `Live.Conversions.separate_stems` (module level) and
  `Live.Conversions.Conversions.separate_stems` (a name on the class) are not the same claim: the class
  one needs an *instance*, and stage 0 never makes one.
- **What kind?** `found[].kind` distinguishes a real function from a descriptor. In Ableton's own shipped
  model, `separate_stems` is built by `Function(...)` and `can_separate_stems_in_high_quality` by
  `ValueProperty(...)` (§7), so expect `kind` to name a descriptor class rather than `function` or
  `method_descriptor`. A descriptor on a class is a strong hint of visibility-only.
- **Which package on disk?** `targets[].routes_tried` records `find_spec` with `origin=`, and that path
  is the discriminator. An origin under `…\Program\Push\python\live_model\` is **Push's proxy layer**,
  not the API a control-surface script is handed — and note that `found[].script_facing` is set to
  `true` unconditionally by `_resolve_target` (`:285`), so *that field cannot make this judgement for
  you; the origin path can.* **verified** (code read).
- **Is it only a stray?** `strays[]` entries carry their own `script_facing`, and the verdict code counts
  only `script_facing: true` strays toward the headline. A hit inside `live_model` or `Push2` is
  corroboration by construction: the name is in the interpreter, which is not the same as a script
  reaching it.

**And four ways to misread a negative or a blank:**

- `no` has two dead ends, and they mean different things. Dead end 1 says the module is unreachable by
  all four routes (nothing to inspect); dead end 2 says `Live.Conversions`/`Live.Clip` **is** reachable
  and the name is simply not on it or on its classes. The second is the stronger result — the objects
  are there and the command is not. Both are worded in `verdict`.
- `targets[].holders` is capped: `_holders` stops at `MAX_HOLDERS_PER_MODULE = 8` entries, i.e. the
  module plus the first seven classes in `dir()` order. In the shipped `live_model`, `Clip` exposes
  exactly seven classes and `AudioClip` — where `can_stems_be_separated` lives — sorts first, so the cap
  does not hide the name we want *there*; for the script-facing package, read `holders` and check the
  list is not truncated before treating a `missing` as a negative. **verified** (code read, shipped
  name tables) / **inferred** (that the cap matters for Live's own `Live.Clip`).
- `stages.2_menu_info` will most likely hold `absent:` flags or one-line `error:` strings. That is a
  **call-shape artefact, not a Live refusal**: the script passes the whole `("Create", "Separate Stems
  to New Audio Tracks")` tuple to `get_menu_info` (`:195`), and Ableton's own shipped Live-API mock
  (`Resources\Python\abl.live\_LiveApiMock\Live\Application.pyc`) does not declare `get_menu_info`,
  `execute_menu_item` or `trigger_menu_item` at all — so the real signature is **blocked** here and no
  verdict should rest on stage 2. Same class of artefact for
  `environment.live_version`: `:143` passes `"get_minor_version"` as an *argument* to
  `get_major_version`, and the same mock declares that method with no parameters, so expect an `error:`
  string there. Live's version is already known from the log and the exe name (§2); do not read this
  field as a finding. **verified** (mock `.pyc` signature table read with `py -3.11` + `marshal`),
  **inferred** (that the native binding matches its own mock).
- `stages.4_prompt_names` is `{"attempted": false, "accepted": [], "candidates": […]}` **by design**. It
  lists the nine prompt names the earlier draft would have mapped and unmapped, and its `verdict` says
  a blank on that stage is a refusal, not a negative. Read it as history.

**Read the report knowing its limit, even when it says yes.** Stage 0 resolves modules and inspects
classes; `_holders` accepts only the module and objects where `isinstance(value, type)` (`:347`), so it
never asks a live track, clip or device anything. It therefore cannot answer whether *a real audio clip
in his set* exposes separation, what arguments the call needs, or whether a separation started this way
behaves like the menu item. Those are A2's questions, and they need a write.

## 6. Removal — the one-command route

Do the dropdown first, the delete second, with Live closed in between; the order is what makes it
clean.

1. In Live, set row 7 back to `None` — one click, and it takes effect immediately (§2). That is the only
   piece of his configuration this probe can leave behind, and it lives in
   `%APPDATA%\Ableton\Live 12.3.2\Preferences\Preferences.cfg`: the `MidiRemoteScriptPreferences` group
   carries one `ScriptPrefs[N]` sub-key per row, `N` = 0…6, so row 7 is `ScriptPrefs[6]` (§5). The exact
   key holding the row's *name* is **blocked** — the three `AbletonMCP` hits in that file are all in the
   MIDI device tables, not in the `ScriptPrefs` block — but the dropdown is Live's own control for that
   state, so using it is both the correct and the sufficient action. Nothing else needs undoing: the
   probe writes no config, and its report is a file he can delete with the folder. **verified**
   (byte-level reads of that file, both encodings, per §5).
2. Quit Live, then:

```powershell
Remove-Item -LiteralPath "$env:USERPROFILE\Documents\Ableton\User Library\Remote Scripts\AutoStemProbe" -Recurse -Force
```

That is the whole removal: the folder and its `__pycache__` go, row 7 already reads `None`, and
nothing else was touched. Leaving the folder in place with the row reset is also fine — it stays
dormant (§3) — so if he wants to keep the probe installed, step 1 alone is enough. **inferred** from
§3.

## 7. What could go wrong mid-run, and what the census could and could not settle

Mid-run risks as the code actually is:

- **The mapping risks this section used to lead with are gone, and not because a guard stops them.**
  Overwriting a real CC 40 / ch 14 binding on Push, LK_Bridge, Keystation Pro 88, Roland A-PRO or
  AbletonMCP with no restore (because `remove_midi_map` deletes rather than puts back), `_unmap()`
  returning early and leaving a binding in place, a quit between map and unmap, a rebuild touching
  another surface's map — all four needed a mapping call, and this build contains none: the helpers are
  deleted and the names appear only in reported lists (§5). What remains of that family is ordinary Live
  behaviour, not a probe defect: enabling **any** new control surface makes Live rebuild the MIDI maps
  for its own controls, which is the same event as plugging a keyboard in and taking it back out.
  **verified** (token counts against the shipped bytes) / **inferred** (that the surface-rebuild is
  harmless here).
- **The one thing that can still execute code Live-side is stage 0's import route** —
  `__import__("Live.Conversions")` and friends, run only when the attribute and `sys.modules` routes
  both miss (`:323-329`). Each route is individually wrapped, so a module that refuses to import comes
  back as a recorded `error:` line in `routes_tried` rather than as a crash. What a *successful* import
  of a Live module does beyond populating `sys.modules` is not documented on this machine and cannot be
  settled statically. **verified** (code read) / **blocked** (what Live's own `Live.*` module bodies do
  at import time — they are compiled into the executable).
- **No call in the probe runs under a component guard.** `__init__` holds one, but `_run_probe` fires
  from `schedule_message` (`:126`) and the whole read sequence happens outside it. What it does is reads —
  `dir`, `hasattr`, `repr`, and two `_call` reads that cannot raise because `_call` converts every
  exception into an `error:` string — and Live's own framework scripts do far more from scheduled
  callbacks, so this is a low-probability concern rather than an expected failure. But the failure mode
  if it bites is not a Python traceback (`_run_probe` catches those) and not a bad report: it is the
  host. A control-surface callback that faults while Live is tearing down can leave Live hung on quit,
  a hung Live has to be killed on this machine, and a killed Live restarts into the recover-or-discard
  dialog over whatever set was open — which this project has already done to the owner once, and his
  answer to that dialog is not ours to make. The mitigation is behavioural and costs nothing: **make the
  pick, leave the Preferences window alone for ~10 s, then put row 7 back; do not quit Live inside that
  window.** **inferred**
  (mechanism, from the code plus this machine's own force-kill history) / **blocked** (Ableton's rule for
  what must run inside a component guard: the control-surface authoring article, help.ableton.com
  206240184, returns 403 — §9).
- **The report may never appear** if Live refuses to load the script (wrong row, stale dropdown, a name
  Live will not list). No file means "did not run", not "found nothing" — check for the three log lines
  in §4 step 5 first. **verified** (code read; the write path is independent of the findings).
- **Do not let a stale bytecode cache worry you, but do confirm the build.** The installed
  `__pycache__\__init__.cpython-311.pyc` is from the *original* file — its header names a 10,593-byte
  source and a 2026-11-15 20:25 mtime, while the `.py` beside it is 25,992 B at 15:44 today (§10) — so
  timestamp invalidation forces a recompile at load rather than a silent old run. The cheap proof is in
  the report itself: a `stages.0_lom_seam` key. **verified** (pyc header fields read with `struct`),
  **inferred** (that Live's importer honours them, i.e. is CPython).

### The static census

Read-only over Ableton's own shipped files. Live was not launched, not touched over any socket, and no
shipped module was imported or run — bytecode was `unmarshal`-loaded and its name tables read, which is a
file parse, not an execution.

What the census **does** settle:

- **The MIDI-prompt and menu-execution APIs are not used by Ableton's own Live 12.3.2 scripts, at all.**
  1,379 `.pyc` files under `Resources\MIDI Remote Scripts` were unmarshalled and their code objects'
  name tables read: 115 files name the bare attribute `MidiMap` on the `Live` module, and every
  attribute they name is a device-mapping call — `map_midi_cc_with_feedback_map` (11 files),
  `map_midi_cc` (5), `map_midi_pitchbend_with_feedback_map` (5), `map_midi_note_with_feedback_map`
  (3). **No** code object names `map_midi_control_to_prompt`, `remove_midi_map`,
  `add_auto_mappable_control`, `send_midi_cc`, `get_menu_info`, `execute_menu_item` or
  `trigger_menu_item`. A byte scan over the same 2,410 files (`Resources\Python` included) finds those
  strings in zero files; the 16 files containing the substring `prompt` are Python stdlib (`cmd`,
  `code`, `doctest`, `getpass`). **verified** (marshal + name tables + byte grep).
- No shipped remote script and no third-party script installed on this machine mentions stem
  separation. `Plugindex\abletonosc` uses the framework class `MidiMapHandler`; `AbletonMCP`'s only
  match for "separate" is the comment *"Start the socket server in a separate thread"*. **verified**.
- The command is a first-class call inside Live's own model layer, under exactly that name:
  `Program\Push\python\live_model\Live\Conversions.pyc` defines a class `Conversions` whose body names
  **`separate_stems`** and **`can_separate_stems_in_high_quality`** as members built by the
  `Function(...)` / `ValueProperty(...)` constructors, alongside the real `def`s
  `separate_stems_of_clip`, `sliced_simpler_to_drum_rack`, `create_midi_track_with_simpler`,
  `audio_to_midi_clip` and three more; `live_model\Live\Clip.pyc` puts `can_stems_be_separated` on
  **`AudioClip`**, not on `Clip`; `live_model\model.pyc` carries `LiveFlip` plus 26 `flip` strings, i.e.
  this package is a proxy over Live's internal "LiveFlip" model RPC. The executable agrees:
  `can_separate_stems_in_high_quality`, `separate_stems_call`, `separate_stems_response`,
  `duplicate_clip_slot_call`, `remove_groove` sit together in one name table at offset 115,217,528, and
  `Message_separate_stems_call` appears next to `push_live_model`, `Unpacker`, `VValue` — right beside
  `Message_press_current_dialog_button` under `DialogServices`. **verified** (files and offsets read;
  class-body `co_names` and every `def`'s `co_argcount`/`co_varnames` unmarshalled with `py -3.11`),
  **inferred** (the `Function(...)`/`ValueProperty(...)` binding form, from the order of names in the
  class body — what is verified is that `separate_stems` is *not* a `def` there, while
  `separate_stems_of_clip` is).
- **What separation actually costs, from Ableton's own declaration.** `Conversions.separate_stems_of_clip`
  unmarshals as `args=7`, `co_varnames=('self', 'audio_clip', 'vocals', 'drums', 'bass', 'others',
  'use_high_quality')` — a clip object plus four stem-destination flags plus the high-quality switch.
  Its body calls `self._flip_object.separate_stems(...)` and wraps the result in `ref(...)`, so even
  Push's own layer treats the underlying call as returning a **reference** that needs resolving. This is
  the strongest available reason to keep "we can name it" and "we can call it" as two separate findings:
  a `yes` from stage 0 still leaves the argument supply (a real `AudioClip`, and what the four booleans
  mean) entirely unwritten. **verified** (bytecode read).
- Live's *own* in-Live Push script touches that class: `Resources\MIDI Remote Scripts\Push2\convert.pyc`
  names `Conversions`, `_conversions_provider`, `available_conversions`, `_invoke_conversion`,
  `_do_conversion_deferred`. Read it for exactly what it says: a script running in the remote-script tree
  holds a **Conversions object handed to it by a provider**, and enumerates the available conversions from
  Live rather than hard-coding them. That is the best static evidence there is for the seam being open to
  scripts — it is *not* the same claim as `Live.Conversions` being importable as a module, and it says
  nothing about stem separation: that file's strings list only the MIDI conversions
  (`ConvertAudioClipToDrumsMidi`, `…ToHarmonyMidi`, `…ToMelodyMidi`), no stem entry. **verified** (name
  and string tables read), **inferred** (how far the analogy reaches).

What the census **cannot** settle — and this is the correction that matters:

- **The absence of `map_midi_control_to_prompt` from the executable proves nothing, and here is the
  control that shows why.** The native `Live` module's member names are not plaintext in
  `Program\Ableton Live 12 Suite.exe` (143,528,920 B). Take `is_group_track`: it occurs in **5** files
  of Ableton's own shipped Live 12.3.2 Python — 2 under
  `Resources\MIDI Remote Scripts\Blackstar_Live_Logic\` (`looper`, `track_util`) and 3 under
  `Program\Push\python\Push2\` (`context_menu_component`, `mixable_utilities`, `track_list`) — and in
  the code-object name tables of 2 of them, so it is a live property the company's own scripts read —
  and **0** times in the executable. Same zero for
  `can_be_sourced`, `monitor_state`, `device_slot`, `num_of_tracks`, `crossfade_state`,
  `insert_track` and `get_project_details_xml`. Only some names surface (`get_major_version` 1,
  `stop_all_clips` 3, `song_length` 2, `signature_numerator` 2), and those coincide with C++ and
  Push-model strings rather than with a Python binding table. **verified** (grep counts over the exe,
  `grep -aril` + marshal name-table scan over the shipped scripts).
  **Therefore: whether `Live.MidiMap.map_midi_control_to_prompt` exists in Live 12.3.2 is unknown, and a
  `present: false` from stage 1 or 3 would be the *first* evidence for it, not the confirmation of a
  proven fact.** The script-side result above ("Ableton's own scripts never name it") stays **verified**
  and still means something — it is evidence of disuse, not of absence.
- **Ableton's own Live-API mock cannot settle any of this, and it is worth knowing that file exists.**
  `Resources\Python\abl.live\_LiveApiMock\Live\` ships three stubs — `Application.pyc`, `Base.pyc`,
  `Licensing.pyc` — and nothing else: no `Conversions`, no `Clip`, no `MidiMap`. So it declares no
  separation member either way, and its silence is not a negative. What it *is* good for is signatures
  where it does declare them, which is how §5 can say with evidence that
  `environment.live_version` will hold an `error:` string (`get_major_version` is declared with no
  parameters). Treat the mock as Ableton's own mirror of the API rather than the API: **inferred**.
- **What stage 0 now decides that no static read could:** whether `Live.Conversions` / `Live.Clip` is
  reachable *from the script-facing interpreter* and whether the four names sit on it. The static record
  stops one step short of that — `live_model` is Push's client-side proxy over LiveFlip, `Push2\convert.pyc`
  proves a provider hands a Conversions object to a script but not that the module is importable, and the
  executable's strings prove only that the native code speaks those names. **unknown until the report
  exists**, and by construction, not for want of searching.
- **Conclusion: no static method on this machine can answer A1's question.** Only Live's own Python can,
  via `dir(Live.Conversions)`, `hasattr(..., "separate_stems")`, `find_spec`, `import`. **That is now
  literally what the shipped build does first** — stage 0, before any of the prompt or menu machinery —
  which is the whole difference between this click and the one the previous draft was asking for.

## 8. Go / no-go for the click, and what each answer commits us to

- **Worth his click: YES.** The re-aim moved stage 0 to the front and aimed it at the names that decide
  the architecture (§7), so every outcome is now a finding rather than a clean negative about a route
  nobody needs. `no` closes A1 and sends the remaining work to hardening the desktop automation;
  `partial` keeps Live as a validator and drops it as an engine; `yes` opens A2 with the holder, kind and
  origin needed to plan it; `inconclusive` names its own dead end. There is no other instrument on this
  machine that can produce any of those (§7's conclusion), and the exposure to get them is one dropdown
  each way.
- **Safety: GO**, re-derived against these bytes rather than inherited from the previous assessment. The
  call-site audit in §5 leaves five ways into Live — one no-argument application getter, two
  wrong-arity-tolerant reads that `_call` cannot let raise, an import of a `Live.*` submodule, and the
  framework plumbing. The prompt-mapping helpers are deleted, not guarded, so the overwrite-without-restore
  hazard this document used to headline cannot occur. He has five real surfaces and none of them can lose a
  binding to this script. It needs no MIDI ports (§2 shows four portless surfaces running), its only file
  write is under `%LOCALAPPDATA%\SkeerAutoSTEM\`, and recovery is one dropdown plus one `Remove-Item` (§6).
  What has changed since this line was first written is the *standing* of the offline half: the "stage 0
  never writes to Live's model" property is no longer only my reading of the code plus the author's stub
  harness — it is now gated in a harness that **three people have run** (the author, the PM, and — as of
  the set copied into `acceptance-2p-1656/` — the Test Engineer), with twenty-four logs whose hashes this
  lane recomputed (§13). That is still not a live run: synthetic `Live` modules cannot tell us what the
  native `Live.Conversions` does, and nothing here has ever contacted Live (§10).
  **inferred** from §5-§7 for the code, **verified** for the offline gating as recorded and re-hashed,
  on the build identified in §10.
- **The two residual risks, named rather than smoothed.** (1) Nothing in the probe runs under a component
  guard, and a control-surface callback that faults while Live is tearing down is a host fault, not a
  Python exception: the tail of that is a hung Live, a kill, and the recover-or-discard dialog over his
  unsaved set — which this project has already caused once. Leave ~10 s after the pick, un-pick, and do
  not quit Live inside that window (§7). (2) The pick acts on the session that is open *now*, so §4 step 1
  stands: save or close the set first. Both are behavioural mitigations; neither is in the code, and neither
  should be read as covered by a guard that is not there.
- **What a `yes` commits us to, and what it must not be allowed to commit us to.** It licenses A2: a
  second experiment that supplies a **real `AudioClip`** and decides `vocals, drums, bass, others,
  use_high_quality`, then checks the result against what the menu item produces (four stems, correct
  names, no ceiling breach, level-matched). It also raises the shipping question in §12 the moment it
  looks real, because the honest form of "Live is a separation engine we call" is "we install a remote
  script in the user's DAW", and that is an exposure decision, not just an engineering one. What a `yes`
  does **not** license: retiring the UIA path, or any doc or plan that says "the LOM can separate stems".
- **Said plainly, because this is the sentence most likely to be misquoted: a `yes` is *visibility of the
  command*, not proof we can invoke it.** The build agrees in its own words — *"A remote script can name
  the separation command, which is the seam the product's fork asked for; invoking it is the next
  question, not this one."* (`_verdict`, `:427-431`.) Five parameters' worth of argument supply, a
  `Function` descriptor rather than a callable, and a returned `ref` that Push's own layer resolves
  (§7) are all still unwritten work between "named" and "works". Run §5's four checks on any `yes` before
  it reaches a plan.
- If he would rather not click at all: `AbletonMCP` is already running in row 6 as an in-process
  command server (`Received command: get_script_info`, `get_session_info` in the log), so a zero-click
  channel into Live's Python does exist on this machine. But it cannot answer this question: its
  dispatcher is a **fixed list** of roughly thirty commands — tracks, clips, notes, tempo, playback,
  browser loading, `inspect_rack`, locators — with **no** separation command, no generic
  "call any LOM member" entry and no introspection command. Its own source does use `dir(app.browser)`
  internally, which is the mechanism a corrected probe needs, but it is not reachable from the socket.
  **verified** (read `AbletonMCP/__init__.py`, 2,589 lines, `_process_command`). Answering
  `Live.Conversions` therefore still requires code running inside Live, and the only two ways to get code
  in there are the §4 pick — whose build already carries the four names, so no further edit is wanted —
  or an edit to a script that is already slotted. The second is a different risk profile on someone else's
  tool, it changes a component the owner installed on purpose, and it is not this lane's call; it is also
  unnecessary now. Note the asymmetry, because it is the reason the click is still the cheap route: that
  server proves a *channel* into Live's Python exists, while this probe works because it can run
  read-only code **in** that interpreter without a channel at all.
- **This folder also contains `tools/live-probe/seam-read.ps1`, written on 2026-11-16 14:54 by another
  member and now owned by this lane** — it is described and kept read-only in §11. It opens
  `127.0.0.1:9877` and sends only `get_script_info` and `get_session_info`, both reads in the table
  above — so it is consistent with the inventory just described, and it cannot reach the separation
  question either. **verified** (read the script).

## 9. Sources read, and the ones that could not be

Read from Ableton directly:
- Ableton Reference Manual, chapter **34 "MIDI and Key Remote Control"**
  (`ableton.com/en/live-manual/12/midi-and-key-remote-control/`) — fetched. Quoted here: *"Enter MIDI
  Map Mode by pressing the MIDI switch in Live's upper right-hand corner."*, *"Click on the Live
  parameter that you'd like to control via MIDI."*, *"Send a MIDI message by pressing a keyboard key,
  turning a knob, etc., on your MIDI controller."*, *"Exit MIDI Map Mode by pressing the MIDI switch
  once again."*, *"Delete mappings using your computer's Backspace (Win) or Delete (Mac) key."*,
  *"Instant mappings are context based and are not displayed in the Mapping Browser."*, *"manually
  override any instant mappings with your own assignments."* Learning is driven by an arriving
  message, which is as close as Ableton comes to saying a binding is inert until the control is sent
  again — and it says nothing about a binding made programmatically to a control no one owns.
  **blocked** for that case.
- Ableton help article *"Installing third-party remote scripts"* (help.ableton.com, 209072009) —
  fetched: *"Launch Live. Open Live's Preferences → MIDI tab. Select the script using the dropdown
  menu in the Control Surface column."*
- Ableton help article *"Using Control Surfaces"* (209774285), via the search index: *"Open Live's
  Preferences to the Link/MIDI tab. Select your Control Surface (e.g. Push 2)."*
- Files **Ableton shipped onto this machine**, read here:
  `%APPDATA%\Ableton\Live 12.3.2\Preferences\User Remote Scripts\InstantMappings-HowTo.txt` and
  `UserConfiguration.txt` (same pair in `Resources\MIDI Remote Scripts\_Tools\`); `Log.txt`;
  `Preferences.cfg`; `Live Reports\Usage\20261116T075632_57360.log`;
  `Resources\Python\abl.live\_LiveApiMock\Live\{__init__,Application,Base,Licensing}.pyc` (signature
  table read with `py -3.11` + `marshal`: `get_major_version` takes no argument, and the mock declares no
  `get_menu_info`/`execute_menu_item`/`trigger_menu_item` and ships no `Conversions`/`Clip`/`MidiMap`
  stub at all); 1,379 `.pyc` under `Resources\MIDI Remote Scripts`;
  `Program\Push\python\live_model\**` (`Conversions` and `AudioClip` class bodies, `co_argcount` and
  `co_varnames` for every `def`); `Program\Ableton Live 12 Suite.exe`. Install root on this machine is
  `C:\ProgramData\Ableton\Live 12 Suite\`.
- Files **this project wrote onto this machine**, read here rather than remembered: `HARNESS.md` (the
  harness's own contract and per-log index), `ACCEPTANCE-pm-2026-11-16.md` (a second reader's measured
  exit codes, log hashes and replay equivalence), `acceptance-2p-1656/` (the Test Engineer's independent
  run of the same bytes, ten logs and a report), `probe-harness.py`, the twenty-four logs in §13, and
  `probe-reaim-2026-11-16.diff` — plus, for the header comparison in §13, the older
  `%TEMP%\probe-sim-orig\task12.diff` copy of that same change, read but not moved or edited. None of
  these can answer anything about Ableton's *behaviour*; they are
  evidence about what our own script does against synthetic modules, and they shorten nothing in the list
  below. **verified** (file reads; every hash quoted in §13 recomputed by this lane).

Could **not** be read, and are therefore **blocked** rather than assumed:
- help.ableton.com *"Making custom MIDI Mappings"* (360000038859) — **403**, twice.
- help.ableton.com *"Creating your own Control Surface script"* (206240184) — **403**.
- `ableton.com/en/live-manual/12/midi-remote-settings/` — **404**; chapter 34 above is its successor.
- Any Ableton statement of the search order between the user Remote Scripts folders and the built-in
  one — no such text found (§3).
- Any Ableton statement of where user MIDI mappings are stored (§5).
- The real signature of `Live.Application.get_menu_info`, `execute_menu_item` or
  `trigger_menu_item`: Ableton's own mock declares none of them (§7) and the two articles that would are
  the 403s above. Stage 2 of the report cannot be interpreted past this line (§5).
- What the native `Live.*` module bodies do at import time — compiled into the executable, so the
  side-effect bound claimed in §5 rests on the probe's own call-site audit, not on a Live-side
  observation.

## 10. Copies, hashes, and what was found about the install

- **The build under test, as I read it at 15:54 today.** The repo copy
  `tools/live-probe/AutoStemProbe/__init__.py` and the installed copy in the User Library's
  `Remote Scripts\AutoStemProbe\` are **byte-identical**: `cmp` silent, both **25,992 B**, **545 lines**,
  **0 CR bytes**, both
  **`sha256 b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`**. The mtimes say the repo
  file moved at 15:42:09 and the installed one at 15:44:15 — write, then copy, which is what "re-aimed and
  installed" is supposed to look like. That number is a SHA-256 of the file bytes; `git hash-object` on the
  same file returns `eab9b99458779377fe2d65c2bd93a105685ddeca`, a SHA-1 over `blob 25992\0` + bytes, which
  is a **different kind of number** and must not be quoted as one. **verified** (`cmp`, `sha256sum`,
  line count, CR-byte count, `git hash-object`).
- **No hash above is reproducible from a commit yet:** the whole `tools/live-probe/` folder is untracked
  (`git ls-files` returns nothing for it). This machine runs `core.autocrlf=true` and the repo has no
  `.gitattributes`, so when this lands, git stores LF and checks out CRLF for `.py` — a fresh clone's
  worktree SHA-256 will **not** equal the number above. Whoever commits it should pin
  `git show <sha>:<path> | sha256sum` and label which hash each number is. **verified** (config and
  missing-`.gitattributes` reads). This is a provenance rule for the commit, not a defect in the probe.
- **Superseded 2026-11-18: the *installed* bytecode cache now names the current build — it was
  overwritten, and the text below used to describe the previous one.** The installed
  `__init__.cpython-311.pyc` is now **32,031 B**, `sha256 5d8af3d8…a14df4`, written **2026-11-16
  19:32:18**, header magic `a70d0d0a` (CPython 3.11), `flags=0`, `src_size 25,992`, `src_mtime
  2026-11-16 15:44:15` — an exact header match for the `.py` beside it. That write time falls inside
  a Live startup (`Log.txt` 19:32:14-31: recovery prompt, `LoadPrefs` done 19:32:18.948,
  remote-script enumeration 19:32:29.556, surfaces up 19:32:30), the window in which Live enumerates
  the Remote Scripts folder. A bytecode cache proves *some* CPython 3.11 compiled the file against
  that source; it does **not** name the writer, and a standalone `pythoncore-3.11` (`py -0p`) would
  write an identical header — **inferred** that it was Live, **not proven**. The repo's
  `__init__.cpython-312.pyc` (12,757 B, 2026-11-15 20:25:07) is unchanged and still the unrelated
  stray. **verified** (header words read with `struct`, sizes, mtimes, log window); see
  `SEAM-CAPABILITIES-2026-11-18.md` §2b.
- **What this bullet used to say, kept as history:** the previous caches — repo `__init__.cpython-312.pyc`
  (12,757 B, 2026-11-15 20:25:07) and installed `__init__.cpython-311.pyc` at 15,266 B, 20:25:39 —
  carried header fields naming a **10,593-byte** source with a 2026-11-15 20:25 mtime (`flags=0`,
  timestamp invalidation), so at the time neither described the 25,992 B current file, and "who
  compiled the 311 file" was blocked against the standalone interpreter above. Only the repo 312
  artifact is still in that state.
- **The click has not been spent — re-checked by file reads, not by touching Live.** *Amended
  2026-11-18: the log-silence half of this bullet is refuted as an argument; the `Preferences.cfg`
  zero and the missing `live-probe.json` are what carry it. See the closing paragraph.* `AutoStemProbe`
  occurs **0** times in `%APPDATA%\Ableton\Live 12.3.2\Preferences\Log.txt` (7,249,438 B, last line
  15:54:04) and **0** times in `Preferences.cfg` **in either encoding** — an ASCII search alone is not
  evidence in that file, because its values are UTF-16LE (§5), so `AbletonMCP` (3 UTF-16 hits, 0 ASCII)
  and `Push` (22 and 1) were run through the same instrument as positive controls. The last slot table in
  that log (starting at byte 7,208,926 of 7,249,665, from the 15:14:06 session) still ends
  `MidiRemoteScript 6 [Control Surface="AbletonMCP" …]` / `MidiRemoteScript 7 [Control Surface="None" …]`;
  and `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` does not exist. So this build has never run.
  Re-read by this lane at 17:26, same instruments: that log is now **7,542,134 B** and still climbing
  while Live writes it, `AutoStemProbe` still occurs **0** times in it, and the positive controls in the
  same read answer (`AbletonMCP` 798, `Push` 1,793). The **last** slot table has moved with the file —
  it now starts at byte 7,470,239 and belongs to a **16:13:19** MIDI re-init inside the *same* Live
  session — and it still ends `MidiRemoteScript 6 […"AbletonMCP"…]` / `MidiRemoteScript 7 […"None"…]`.
  The 15:54 offsets above are kept because that is the read §2's quoted table came from; the numbers
  differ because the file grew, not because anything was picked.
  Re-confirmed by this lane at 17:05 and again at 17:25 — the folder holds `logs/`, `recovered/`,
  `work/`, `settings.json`, `queue.json`, two `queue.json.bak-*`, `startup.log` and `ui-profile.json`,
  and **no** `live-probe.json`; the newest mtime in it is `queue.json` at **2026-11-15 19:23:53**, so
  nothing there has been written today — **inferred** from those mtimes, not observed, that the product
  has not run at all today. The acceptance record performed the same check after its own runs (§13), so
  the negative now has three
  independent reads. One correction on the record's behalf, in passing: the enumeration it prints is
  **shorter than the folder** — it names `logs/`, `queue.json` and two `queue.json.bak-*`, and omits
  `recovered/`, `work/`, `settings.json`, `startup.log` and `ui-profile.json`. Its load-bearing claim
  (no `live-probe.json`) is confirmed by this lane's own listing; the list beside it is not a directory
  census and should not be quoted as one.
  Note what that folder *is*: the product's own state directory, not a probe dump, so
  "the folder exists" is not evidence of a run and a reader should not treat it as one.
  **verified** (byte search in two encodings, offsets, `ls`). The zero is instrumented, not an
  empty-file artefact:
  `Push`, `LK_Bridge` and `AbletonMCP` all appear in the same log. The file grew about 10 KB between the
  engineer's read (7,239,406 B) and mine — the same live log, not a disagreement.
  **Correction of the argument, 2026-11-18 — the log zero is not evidence and must not be quoted as
  such.** The differential: `Tap\__pycache__\Tap.cpython-311.pyc` exists (117,404 B, source 114,763 B,
  written 2026-02-13 02:06:31), so Live compiled `Tap`, and `Tap` occurs **0** times in `Log.txt`
  (case-sensitive; the 55 case-insensitive hits are all `InitApplication`, as §3 says) —
  exactly like `AutoStemProbe`. A script Live compiled can leave no name in the log at all; mentions
  track selection and runtime chatter, not enumeration. (In `Preferences.cfg`, `Tap`'s only hit is a
  MIDI *port* named `Tap Output (DESKTOP-MLIF3MV)`; it has **no** surface-list entry, so "not
  selected" is the other reason it is unlogged — a distinction this bullet used to blur.) What
  *does* still carry the claim, re-read this session (`Log.txt` **9,119,644 B** at
  09:31:37, and **9,271,388 B** at a later read the same morning — still growing, and that is log
  growth, not a different session; `Preferences.cfg` **131,382 B** at 09:27:29): `AutoStemProbe` **0**
  in `Preferences.cfg` in
  both encodings, while `AbletonMCP` shows **3** (twice in the surface-name list, once as its port)
  and `LK_Bridge` **1** (surface list) — the instrument that can answer "is it selected?" answers for
  the controls and returns 0 for the probe; the last slot table still ends `MidiRemoteScript 6 […"AbletonMCP"…]` /
  `MidiRemoteScript 7 […"None"…]`; the installed probe has no module-level `create_instance`, so Live
  cannot construct or offer the surface; and there is no `live-probe.json` in the product's state
  folder. And the installed cache written 19:32:18 (§2b) proves module-level *import* did happen — so
  what has never happened is a stage, a seam query or a socket from our probe, not "no code at all".
  **verified** for the file facts; the cache's writer stays **inferred**.
- **The install delta this section used to record is closed, and so is the header defect it described.**
  The claim flagged here before — that Live "resolves that name from the user's Remote Scripts folder
  before its own, which is how this runs without anyone touching Preferences" — is gone from the file
  (0 matches for `before its own`); the header now says the folder only puts the name in Live's list and
  that a slot pick activates it, which is what §3 proves. Nothing is therefore owed to the installed copy:
  the earlier "re-copy before any click" instruction is spent, because the two files are the same bytes.
  **That last clause is spent, and the instruction it retired is live again.** As of 2026-11-18 the repo
  copy is `f34292cf…` and the installed copy is `b995063b…`, so "the two files are the same bytes" no
  longer holds and **re-copying before any click is a prerequisite again**, not a spent concern. The
  bytes that were installed could not be instantiated by Live at all (no `create_instance`), so no
  amount of re-copying the *old* file would ever have produced a dropdown entry. Nothing has been
  installed on this machine as part of fixing that — the install is the owner's call.
  The **precedence** question in §3 stays **blocked** — the header no longer asserts an order and nothing
  on this machine establishes one. The 10,593 B / `f06770d3…` and 10,802 B / 253-line figures that earlier
  revisions of this file quoted describe two superseded builds; treat any Team message citing them as
  history, not as the current identity.
## 11. The seam that is already established — `tools/live-probe/seam-read.ps1`

This is the part of A1 that no longer needs him. **A remote script is executing inside his Live
process right now, and it answers commands over loopback.**

Measured 2026-11-16, against the running session, no click and no cursor movement:

- `netstat -ano` → `TCP 127.0.0.1:9877 0.0.0.0:0 LISTENING 35676`, and `35676` is the Live process.
  **verified** (command output read). Re-read by this lane at 17:26: the identical line, and `tasklist`
  still names pid `35676` as `Ableton Live 12 Suite.exe` — so the 14:35 socket, the 16:13:19 MIDI re-init
  in §10 and this minute are **one** continuous Live session, not three sightings of three sessions.
  **verified** (both commands re-run); **inferred** (that same pid means same session).
  **Superseded 2026-11-18: that continuity has ended.** `Log.txt` records a fresh launch today —
  `Begin LoadPrefs` at **08:45:13.062**, crash-recovery prompt answered **"Restore Document: Rejected
  by the user"** at **08:46:08.388**, then `DEFAULT.als` loaded — and an earlier launch at 2026-11-17
  16:33:36. The "one continuous session" reading above describes the 2026-11-16/17 window only; the
  current session began 08:45 today. Whether pid `35676` still names a Live process is a question for
  a fresh `netstat`/`tasklist` read, not for this text. **verified** (log timestamps).
- The listener belongs to the `AbletonMCP` remote script, slotted in the **sixth** row of the Control
  Surface column (Live's 1-based `MidiRemoteScript 6`; `slot_5` in the 0-based usage-log field — see
  the base warning in §2). **verified** (log table read).
- Two read commands answered: `get_script_info` → `name AbletonMCP`, `script_version 1.7.1`,
  `protocol_version 1`, `port 9877`, `snapshot_schema ableton_mcp_snapshot_v2`, and
  **19** entries in `SCRIPT_CAPABILITIES`; `get_session_info` → tempo 129.0, one track, not playing,
  `current_song_time` advancing. **verified** (its own source at lines 19-40 and 517-527 read; the
  numbers are the ones the socket returned). **19 is the advertised list only:** the dispatcher
  accepts **37** distinct names (a leading `if command_type == "get_script_info"` plus 36 `elif`
  lines) — 18 more than it advertises — so quote
  the handshake as "19 advertised", never as "the surface". *Re-derived 2026-11-18 from the file:
  `SEAM-CAPABILITIES-2026-11-18.md` §2.*

**`tools/live-probe/seam-read.ps1` (1,131 B) is this lane's now, and it stays exactly what it is:** a
raw `TcpClient` to `127.0.0.1:9877` that writes two JSON lines —
`{"type":"get_script_info","params":{}}` and `{"type":"get_session_info","params":{}}` — reads one
buffer back per command with a 4 s read timeout, closes the socket and prints `seam closed`. Both are
pure reads in the script's dispatcher (`_get_script_info`, `_get_session_info`), it passes no `params`
anyway, and it never touches the cursor, a window, the set or a file. Run it while Live is up:
`powershell -NoProfile -File tools/live-probe/seam-read.ps1`. Keep it read-only: the first
non-read command added to this file turns a passive probe into an act on his session, and that is the
line this whole folder exists to avoid crossing. **verified** (read the script in full, line by line).

**Verified negative — there is no shortcut around the probe through this socket.** The script's source
contains **no** `importlib`, no `__import__`, no `imp`, no `sys.path` handling and no `exec(`,
`eval(` or `compile(` (0 each; positive controls in the same file: `import` 11 lines, `def` 86), so
its path-taking commands only feed audio files and browser items into Live's own importers. You cannot
make the seam evaluate Python you choose, which means you cannot ask it what
`Live.Conversions.separate_stems` is. The question still needs read-only code loaded *through a slot*,
which is exactly what stage 0 of the probe in §10 now is.
**verified.**

## 12. The risk that comes with a seam like this — and that autoSTEM inherits if it ships a script

Not hypothetical: it is the author's own comment, verbatim from lines 20-28 of the running script —

> *Loopback, not 0.0.0.0. This socket takes unauthenticated commands that import arbitrary absolute
> paths, load browser items and drive the transport, so anything that can reach the port controls the
> DAW. Binding every interface exposed that to the whole local network — and to any network the
> machine is routable from.*
>
> *Running Live and the MCP server on different machines is the one legitimate reason to widen this;
> set ABLETON_MCP_HOST to do so deliberately, and put it behind a trusted network or an explicit
> relay.*

- **Unauthenticated by construction.** The handler accepts a connection, reads JSON, dispatches.
  There is no token, no handshake, no per-command authorisation, and no check that the peer is the
  MCP server. `HOST = os.environ.get("ABLETON_MCP_HOST", "127.0.0.1")` — the hardening is a *default*,
  one environment variable away from `0.0.0.0`, and nothing verifies the change. **verified** (source
  read).
- **Loopback is a smaller door, not a safe one.** Any process running as him — a browser tab's
  download helper, a plugin, anything in his user session — can reach `127.0.0.1:9877` and drive the
  transport, create and delete clips, set tempo, import an arbitrary absolute path. **inferred** from
  the command list plus the absence of any authentication; per-connection state is not encrypted, so
  nothing distinguishes his MCP server from another local process.
- **The advertised capability list under-reads the surface.** `SCRIPT_CAPABILITIES` has 19 names; the
  dispatcher accepts **37** distinct names. The 18 it never advertises are six reads
  (`get_browser_tree`, `get_browser_categories`, `get_browser_item`, `get_browser_items`,
  `get_browser_items_at_path`, `inspect_rack`) and twelve mutators (`start_playback`, `stop_playback`,
  `fire_clip`, `stop_clip`, `set_tempo`, `set_current_song_time`, `set_track_name`, `set_clip_name`,
  `set_arrangement_clip_name`, `load_browser_item`, `switch_to_arrangement_view`,
  `map_rack_magnitude`). So **23** of the 37 change the set, the transport or the view, while the
  handshake advertises 11. A consumer that trusts the handshake to decide what is safe will be wrong
  about what is reachable. **verified** (both lists read from the same file; count re-derived
  2026-11-18, `SEAM-CAPABILITIES-2026-11-18.md` §2).
- **autoSTEM inherits this the moment it ships a remote script.** A script we install is Python that
  Live loads into the user's DAW process with full LOM write access, and anything it opens is reachable
  by every local process unless we build the authentication the author of this one did not. If the
  narrowed A1 answer ever becomes "yes, ship a script", the non-negotiables are: bind loopback with no
  env-var widening, gate every mutating command behind a per-session secret the product passes and the
  user cannot set by editing a file, make the destructive commands absent by default rather than
  merely unadvertised, and document the whole surface in the product docs — because
  "unauthenticated local control of the user's DAW" is a support-and-liability sentence we would be
  publishing ourselves. **inferred**, as a standing design constraint, not as a finding about his
  machine.
- Nothing in this section is a defect report against `AbletonMCP`: it is a third-party tool he
  installed deliberately, and its author documented the exact exposure and narrowed the bind. It is a
  preview of what we would be signing up to. **inferred** from the same comment block.

## 13. What already proves the read-only contract offline — the harness, `HARNESS.md`, seven author logs, seven second-party logs, ten third-witness logs, two acceptance records, and the re-aim diff

This section covers **thirty-eight** files: the **eighteen** it enumerated when this section was first
written — `probe-harness.py` (last rewritten at 16:24:47 — 1,016 lines,
48,884 B, `sha256 5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1`); `HARNESS.md`
(336 lines, 25,102 B, `sha256 c32dd51083c76cd0f631076c59d3aac13fae19dd4ed942014e64b0e7c8f51a2e`, revised
twice after my first pass); **seven** logs the harness's author recorded (green, the three mutations,
`--mutations`, pin-drift, self-check); **seven** raw logs a second reader produced
(`pm-acceptance-*.log`, 17:00:23-46); `ACCEPTANCE-pm-2026-11-16.md`, which this section pinned at
134 lines / 8,948 B / `sha256 ba13dc30…7610e6f2` and which is **208 lines / 14,844 B / `sha256
2c173dcbf28c1627a39323b197da64e95dc9c4039b6aed9d1b5bdf95ec514b3e`** as I measured it at 16:53:38 on
2026-11-17 (it was 187 / 12,947 / `d5c50d73…` earlier in this same lane — that file belongs to the PM and
is still being written by another hand, so recompute it before quoting it, and cite
`acceptance-2p-1656/report.md` rather than this record for the third witness); and
`probe-reaim-2026-11-16.diff` (602 lines, 30,616 B,
`sha256 e1cbb9e6b0b8d3b053f3f78beadd868e28252e713079828dcefc508d4856665b`) — the re-aim's own audit
trail, which `HARNESS.md` pins in a *Provenance* section and which this lane tied back to the pinned
build two ways; see the bullet *The re-aim diff, and the cross-check that costs nothing* below.
The other **twenty** are `acceptance-2p-1656/`, and the count is a recursive file walk taken twice in
this lane, at 16:52 and again at 16:53: **ten** logs (`green`, `help`, the three single mutations,
`mutations-all`, `pin-drift-run`, and `selfcheck-A` / `-B` / `-C` as three separate files), one acceptance
record (`report.md`, 429 lines, 30,034 B), the replay tool (`replay-diff.py`), the seam capture
(`seam-read.out`), and **seven** derived copies of the harness and probe that the scratch inductions ran
from. The folder itself has **seventeen** entries at depth 1 — thirteen files and **four** directories
(`pin-drift`, `selfcheck-A`, `selfcheck-B`, `selfcheck-C`) — which is worth spelling out because the
addendum that copied it into the repo enumerated fifteen and named three induction folders.
I executed none of them, so **no exit code below is a result this lane produced**; what this lane did
produce is re-measurement of evidence already on disk — every hash recomputed with `sha256sum`, the
replay `diff` re-run, the verdict lines read out of each log, the argv surface and the `say()` block
grepped out of the harness. Those are recorded as **verified** below and the exit codes are not.
The counts here were a snapshot taken between 17:03 and 17:26 on 2026-11-16 of a folder that had moved
four times that day; this lane re-took it on 2026-11-17 between 16:52 and 16:54, and that is where the
thirty-eight and the seventeen come from. Re-list before quoting a number, and re-derive any count from
the items beside it rather than from this sentence.

**A citation rule for this section, because it matters:** `probe-harness.py` was edited while I read it —
928 lines / 43,698 B at 16:13:55, 949 / 44,749 at 16:20:48, 984 / 46,749 at 16:23:42, 1,016 / 48,884 at
16:24:47, and `StubGuardFinder` and `recorder` each moved 34 lines between two reads. So the harness is
cited below **by symbol and by quoted text, not by line number**. Logs are cited by their own sha256,
which is the only stable handle: `probe-harness-pin-drift.log` and `probe-harness-selfcheck.log` were
both rewritten after I first described them, so any mtime-ordered claim I make about a log is a claim
about the bytes named in §13's hash list below, not about the filename.

- **What it is:** an offline harness that loads the probe source with `compile()` + `exec` into a
  hand-made module against synthetic `Live`, `Live.Application`, `Live.MidiMap`, `Live.Song`,
  `Live.Conversions`, `Live.Clip` and `_Framework.ControlSurface` stubs, runs **eight interpreter shapes**
  (the `SHAPES` table — `attribute`/`class-member`/`import-only` → `yes`, `flag-only` → `partial`,
  `no-members`/`nothing`/`stray-only` → `no`, `fatal-dir` → `inconclusive`), and fails nonzero if the set
  of Live API members the probe actually called leaves a four-name allowlist (the
  `PERMITTED_READERS` set: `Application.get_application`, `get_major_version`, `get_minor_version`,
  `get_menu_info`). Exit codes are owned by the
  file: 0 green, 2 pin mismatch, 3 forbidden call, 4 undocumented call, 5 verdict mismatch, 6 harness
  invalid, 7 mutation not caught. It pins the same
  `sha256 b995063b…20f5` as §10 in a `PINNED_SHA256` constant, recomputes the digest of the source it
  is about to load on every run, and exits 2 on a mismatch — so a green
  over different bytes cannot be quoted as a green over *these* bytes. **verified** (file read; pin
  compared to §10's digest).
- **Three falsifier mutations, which is why its green would mean something.** Two are *call* mutations:
  `prompt-bind` re-inserts a
  `Live.MidiMap.map_midi_control_to_prompt(40, 14, 0, "Live.Create.AudioSeparation")` line at the top of
  `_lom_seam` and must exit 3; `conversions-flag` injects an innocuous `Live.Song.get_song()` and must
  exit 4 — the second one exists precisely because "the allowlist is the contract, not the forbidden
  list". The third is an *answer* mutation, `command-blind`: it replaces the shipped
  `COMMAND_NAMES = ["separate_stems", "separate_stems_of_clip"]` line with two names that cannot exist
  and requires exit 5 with the three command-bearing shapes dropping from `yes` to `partial` — the
  reasoning being that if the probe ever stops looking for the names Ableton's own `Conversions.pyc`
  gave us, it must not keep claiming it tested the seam. Two guards make those reds trustworthy rather
  than decorative: `apply_mutation` raises `HARNESS_INVALID` unless the anchor or the replaced line
  occurs **exactly once** in the shipped source ("refuses to report a red that may not have run"), and
  every mutation is applied to an in-memory string, never to the file.
  **verified** (the `MUTATIONS` table, its `ANCHOR`, and `apply_mutation`).
- **Its logs are LF**, deliberately: `use_lf_stdout()` exists because Python translates `\n` to `\r\n` on
  a redirected Windows stdout and this folder's other files are LF, so a recorded log can be diffed
  without a separator argument. That is the same discipline §10 reports for the probe and this README.
- **Why it cannot reach Live, in its own words:** `sys.path` is scrubbed of Ableton-shaped entries, a guard
  finder installed first on `sys.meta_path` raises `ImportError` for any Live-side root except the name a
  shape
  declares synthetic (`StubGuardFinder.find_spec`, which also records every attempt in
  `IMPORT_ATTEMPTS` / `REFUSED_IMPORTS`), the report is redirected to a scratch folder, and
  the owner's real
  `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` has its size/mtime/hash checked before and after with
  exit 6 if it moved. No socket, subprocess, window, registry or write outside the scratch folder. **inferred**
  from that design as written; **not re-executed here.**
- **Two things the harness says *about* the shipped probe, which the Team should carry with any green.**
  (1) It independently reaches §5's arity finding: the probe calls
  `_call(app, "get_major_version", "get_minor_version")` against `_call(self, target, method, *args)`
  (`__init__.py:461`), so `"get_minor_version"` arrives as an *argument* to a method Ableton's own mock
  declares with none, and `environment.live_version` is expected to hold an `error:` line rather than a
  version. The harness prints that in its closing `say()` block as
  *"REPORTED, NOT FIXED: this brief forbids editing the probe"* — so it is a known defect in the
  engineer's file, not a mystery for whoever reads the report. (2)
  `Application.get_menu_item_info` is stage 2's fallback (`__init__.py:190`) and is
  **deliberately absent from the allowlist**: on a Live that has `get_menu_info` the loop breaks and it
  never fires, but on a build that lacks it this harness reddens with exit 4 by design — the same
  `say()` block says so in as many words.
  Neither is a problem for the fork answer — stage 0 reads neither field — but both are why a blank in
  `environment.live_version` or `stages.2_menu_info` must not be read as a Live refusal.
- **Three things a green run from it does *not* prove, so the Team does not over-quote it.**
  (1) It cannot answer A1: the guard refuses every real `Live.*` import, so the shapes that end in `no`
  are the harness declining to be a Live install, not evidence about Live. (2) It cannot see a wrong-arity
  call *as a failure*, because `recorder()` returns a `_fn(*args, **kwargs)` that accepts anything,
  records the argument shape via `signature()` and hands back a plausible value, while real Live is
  expected to
  raise. The file says this itself: *"stub signatures are not Live's … nothing here proves what Live's
  builtins accept, only what this script asks for"*. So green says nothing about whether
  `environment.live_version` or `stages.2_menu_info` will contain data rather than `error:` text — the
  arity defect above is visible there only because the author went and read the recorded arguments.
  (3) Its own `class-member → yes`
  shape confirms §5's warning rather than resolving it: a name found on a **class** produces the same
  `yes` as one found on the module, so only `found[].scope` in the real report separates "visible" from
  "callable on an instance".
- **The call-count in the hand-around is wrong in membership, not just in size.** The re-aim report said
  the probe's only Live API calls are `get_major_version`, `get_minor_version`, `get_menu_info`. The
  harness's `PERMITTED_READERS` set is **four** names — the same three plus `Application.get_application`,
  which its header comment calls out as "the one handle getter it cannot do without … permitted-with-reason"
  and blames the ad-hoc predecessor for leaving it un-instrumented. But the *observed* set is a different
  three: because of the arity defect above, `get_minor_version` is never reached, so a green run reports
  `get_application`, `get_major_version`, `get_menu_info`. **Four permitted, three observed, and neither
  three-name list agrees with the other.** Quote the allowlist as four and the observation as
  whichever set the log states; do not repeat the re-aim report's sentence. §5 lists the four permitted
  plus the import.
- **`HARNESS.md` is the owned index, and its *Recorded runs* section is now the strongest artifact in
  the folder — which retires the risk note this README carried against it.** It states the contract as an
  enforceable rule (a table of the four permitted members, each with its arguments and a reason), the two
  breach classes and why they get separate exit codes, the run commands, the eight shapes, the bytes the
  proof is about, then a *Recorded runs* table that gives **each log's own sha256 and a "tied to the
  shipped bytes by" column**, a replay recipe anyone can repeat (re-run the command, diff against
  everything below the log's `HARNESS OUTPUT VERBATIM BELOW` marker, expect a difference only in the
  random `tempfile.mkdtemp()` path), a subsection for the two logs that carry a superseded section, a
  disclosed CRLF correction, and a closing *"If anyone maintains this harness"* section with the two
  one-line changes that would remove this whole class of provenance defect.
  I recomputed all seven author-log hashes rather than trusting the table, and **all seven match it
  exactly** (listed in §13's hash list below). **verified** (file read + `sha256sum` of each log).
  The third witness recomputed the same seven on 2026-11-16 and recorded **7 of 7 AGREE** twice, before
  and after the runs (`report.md:190`, `:202`), and this lane re-ran all fourteen author + second-party
  log hashes at 16:53 on 2026-11-17: **14 of 14 reproduce** the short hashes §13 prints below.
- **What was single-witness on 2026-11-16 — SUPERSEDED for the three self-check legs on 2026-11-17, and
  kept in its own words because the limit it describes was real when it was written.** The self-check legs —
  exit 6 (`probe source not found`), exit 6 (`FORBIDDEN_METHODS` neutered in a derived copy) and exit 7
  (a deliberately wrong mutation expectation) — were **not** reproduced by the second reader, and cannot
  be reproduced by a plain re-run: `--help` prints
  `usage: py -3.11 -B probe-harness.py [--mutation NAME | --mutations | --help]`, and
  `grep -c selfcheck probe-harness.py` returns **0**, so no switch induces those legs from this folder.
  They come from *scratch* instead, and the three are not equally expensive: leg A is an arrangement — a
  scratch copy folder whose `AutoStemProbe\__init__.py` is simply absent — while legs B and C need an
  edited harness, and `HARNESS.md` says of B and C that they *"**cannot** run on the shipped bytes"*,
  because a neutered allowlist and a deliberately wrong expectation are the whole point. An edited copy
  is by definition not the pinned bytes, which is exactly why the author hashed each derived copy
  (`2e8c969c…` for the neutered-allowlist leg, `200aaa45…` for the wrong-expectation leg).
  So the two-witness set is **seven legs**: the five that re-run from the shipped `probe-harness.py` in
  this folder — green, the three single mutations, and `--mutations`, of which `HARNESS.md` says *"at
  16:41 each of those five was re-run from the shipped `probe-harness.py` and the fresh output matched
  the recorded body byte for byte, ignoring only the random `tempfile.mkdtemp()` folder name…"* — plus
  `--help`, plus pin-drift, which needs a *drifted probe* beside the harness but no edit to the harness.
  The three self-check legs are the single-witness family and the least load-bearing one for the click
  decision: they test the harness, not the probe. The Team should not quote them as accepted.
  **[SUPERSEDED 2026-11-17.** The "second reader" in that last sentence is the PM, who indeed never
  reproduced them; the Test Engineer's set did, and its logs now sit in this repository rather than in
  `%TEMP%` — see the next bullet. The instruction "do not quote them as accepted" is retired; the
  instruction "they test the harness, not the probe" is not, and remains the reason none of this bears
  on what a live Live answers.**]**
  **verified** (grep of the harness's argv surface and of `selfcheck` in its source, the acceptance
  record's §4, and `HARNESS.md`'s *Recorded runs* index plus its *Two logs that carry a superseded
  section* note). The **inferred** that this lane carried here — that leg A needs no edit to the harness,
  read from the leg's own one-line description in `probe-harness-selfcheck.log` rather than from anything
  re-run — is **retired as inferred and now verified**: `acceptance-2p-1656/selfcheck-A/` holds
  `probe-harness.py` at the shipped `5de18524…` and **no `AutoStemProbe/` folder at all**, so the exit 6
  in that leg is a folder-state induction against unmodified bytes.
- **The third witness, and what actually moved on 2026-11-17.** `acceptance-2p-1656/` is now in this
  folder: the Test Engineer's independent run of the same pinned bytes, copied in by the PM's addendum
  because it had been living in `%TEMP%\harness-accept-1656\`, which this project does not accept as a
  record. The report states the result this section used to deny: **10 of 10 exit codes measured, 0
  copied** — `0 / 3 / 4 / 5 / 3 / 0`, pin-drift `2`, and `6 / 6 / 7` for the three self-check inductions
  (`report.md:74-83`), each taken from the process (`$?` / `$LASTEXITCODE`), never through a pipe. This
  lane did not re-run any of it and does not claim to; what it did was read the codes out of the files
  that set left behind and re-hash the artifacts:
  `selfcheck-A.log` `00f245f8…4a96be1` (VERDICT `exit 6 … probe source not found`), `selfcheck-B.log`
  `1f5e7977…157bd437` (`exit 6 … harness self-check`), `selfcheck-C.log` `a85cabf8…9174fd79` (`exit 7`,
  reached as `MUTATION_NOT_CAUGHT` — the `--help` exit table at `help.log:53` is the definition).
  The derived copies reproduce the author's recorded hashes **3 of 3**: `selfcheck-B/probe-harness.py`
  = `2e8c969c0d10a35360a9430b88cdc937ec1af9ca619e57c64519320b999f54c3`, `selfcheck-C/probe-harness.py` =
  `200aaa4550e35aa83b9081beefc6ad7a0c750cbe0f8dc5a580411a6e5338cc14`, and `pin-drift/AutoStemProbe/__init__.py`
  = `a927a63ee39b03e099dbaedca17afd63256a38a15ed7cd238a4e8d530d654024` — which is *SECTION 2's* drift
  value, not SECTION 1's superseded `1c571b2d…`. The probe beside the edited harnesses is unmodified
  (`b995063b…` in both `selfcheck-B/` and `selfcheck-C/`), so the legs tested the harness and left the
  pinned artifact alone. The replay tool is in the folder too (`replay-diff.py` `8439ee1a…`) and
  printed `REPLAY_MISMATCHES=0 of 5` over 119 / 232 / 232 / 225 / 437 compared lines.
  **Two things do not move with this.** (1) Legs B and C still *cannot* be produced from the shipped
  bytes — the neutered allowlist and the deliberately wrong expectation are the whole point — so exit
  `6` and exit `7` here describe **derived** bytes, and that is precisely why the copies are hashed and
  why the sentence above theirs stays. (2) ~~The harness still does not attest its own bytes~~ —
  **superseded 2026-11-19**: harness `1d59b88f…` prints its own sha256, and
  `acceptance-g4-selfhash/` is the first table produced by self-attesting bytes; the next
  bullet's history stands as written. Two reading cautions, both mine, from reading those files rather than from
  the report's prose: `acceptance-2p-1656/help.log` is **byte-identical** to `pm-acceptance-help.log`
  (both `3a945d51687bc51c32a00a1254bcac1e18737941a674305d3de66676ec7b4bf3`), which is what a
  deterministic `--help` capture looks like — that output is the harness's own static header, with no
  temp path and no timestamp in it — so the match is *not* evidence that anyone copied a log, and just as
  certainly not evidence that two people ran it. `--help` is the weakest leg to lean on, and nothing in
  the click decision does. And `selfcheck-C.log:216` contains a real `FALSIFIER: FAIL` line; it is the
  leg's *expected* output (a wrong expectation set to `4 UNDOCUMENTED_CALL` against a mutant that
  correctly reddens at `3`), not a defect in the shipped harness — a reader who quotes that line as
  evidence about `5de18524…` has quoted evidence about `200aaa45…`.
  **verified** (this lane: `sha256sum` of all twenty files, folder walk, VERDICT/FALSIFIER lines grepped
  out of the logs, `--help` exit table read out of `help.log:44-57`); **attested, not measured here**
  (every exit code that report presents).
- **Why the acceptance *record*, not the logs, is the attestation.** The harness prints the **probe's**
  sha256 on every run and prints only its own *path* of itself (`say("  harness file    %s" %
  os.path.abspath(__file__))`), so **no log can prove from inside which harness produced it** — including
  the seven the author recorded, whose `harness : … sha256 5de18524…` line is a hand-written claim in his
  own `PROVENANCE` block, not harness output. The closure is replay, and on 2026-11-16 it had been done
  twice: by the author at 16:41 (five logs re-run, output byte-identical apart from the temp name), and by
  a second reader whose raw logs sit beside them. A third closure now exists **and is in the repo**: the
  Test Engineer's `replay-diff.py` re-ran the same five legs against the recorded bodies and got
  `REPLAY_MISMATCHES=0 of 5` (`report.md:144-150`), and `report.md:23-33` proves the missing-self-hash
  mechanically rather than from this README — `sha256_file(` is applied only to the probe, and `__file__`
  appears at three places, one of which is the *path* line quoted above. What moved here is only the
  count of substitute attestations; the substance of this bullet does not, and neither does the deferred
  two-line fix. `ACCEPTANCE-pm-2026-11-16.md` was therefore the external substitute for the fourteen logs
  this section described; with the third set the log population is **twenty-four**, and the substitute for
  the ten newest of them is `acceptance-2p-1656/report.md` sitting beside them. The real fix is the
  two-line harness change the same document asks
  for — print its own sha256 in the preamble — and it is **deliberately deferred to after the click**:
  those bytes are what the green run describes, and moving them now would invalidate the very evidence
  the click decision rests on. Do not "improve" the harness before the row-7 pick.
  **The reason for that deferral is spent, and this instruction is superseded.** It assumed the click
  would rest on the recorded bytes; the click could never have used them, because the probe it would
  have exercised was not loadable. G2 moved the harness anyway (`5de18524…` → `a5e99188…`), so the
  evidence this sentence protected has already been re-created rather than preserved. **G4 — printing
  the harness's own sha256 in its preamble — is no longer deferred;** it is the only mechanism that lets
  a future log prove which harness wrote it, and `acceptance-repin-f34292cf/INDEX.md` §4 shows the gap
  is real: the ten-leg table in that folder is attested by hand-added headers around harness output,
  exactly as before. **Landed 2026-11-19** as harness `1d59b88f…`; the ten-leg table re-run on
  self-attesting bytes is `acceptance-g4-selfhash/`, which supersedes the G3 table for the current
  bytes.
- **The second-party set, and what I measured myself.** `ACCEPTANCE-pm-2026-11-16.md` records exit codes
  measured from the process rather than from a pipe: **0** green, **3** prompt-bind, **4**
  conversions-flag, **5** command-blind, **3** `--mutations`, **0** `--help`, **2** pin-drift. Its seven
  logs are raw harness output with no hand-added header. I re-hashed all seven against its table (all
  match), and I re-measured its replay-equivalence claim rather than copying it:
  `diff pm-acceptance-green.log probe-harness-green.log` → **exactly 12 changed lines** out of 119 raw /
  129 headed, of which **10** are the author's PROVENANCE block and **one `<`/`>` pair** is the
  `autostem-probe-harness-…` scratch folder name. So the recorded green log is a faithful capture of
  `5de18524…` running against `b995063b…`, and the harness body output matches byte for byte.
  I also read the verdict lines out of each pm log directly: the combined `--mutations` log carries
  **three** `FALSIFIER: PASS` lines plus a GREEN control; the author's own combined log carries **four**
  hits for that phrase because one is inside his hand-written note — a counting trap worth naming, since
  "4 falsifiers" would be wrong about a file that says 4. **verified** (diff, `sha256sum`, per-log
  `grep -c`).
- **The hashes, so a reader can tell whether the bytes they are reading are the bytes described above.**
  First recomputed in this worktree at 17:04 on 2026-11-16, and re-computed by this lane on 2026-11-17 at
  16:52-16:54 — the two passes agree on every file they both covered — against the files as they sit on
  disk. Note the caveat that
  §10 already carries: `tools/live-probe/` is **untracked** (`git status --porcelain -- tools/live-probe`
  → `?? tools/live-probe/`, `git ls-files tools/live-probe` → **0 lines**, HEAD `5b1c340` as I read it at
  16:54 today), this repo runs `core.autocrlf=true` with **no `.gitattributes`**, so the first
  commit converts these files to LF blobs and a fresh clone's checked-out worktree will not reproduce the
  numbers below for any file git decides is text. These are worktree SHA-256s, not blob IDs.
  Author's seven logs: `fb5aa199…84210b0b` green, `3232ad9b…45942ec4` prompt-bind, `5c6d3f3e…4653e9d2`
  conversions-flag, `c57773cc…ce684d51` command-blind, `b72244d8…feacf965` mutations-all,
  `4424d0ff…fabb46b4` pin-drift, `3e7d8b2d…cddb6ac2` selfcheck.
  Second party's seven: `4673d2ab…f0d69c67` green, `181b2ef5…fecb3a64` prompt-bind,
  `b3db5b7b…9dfe1a65` conversions-flag, `835882ae…4f686872` command-blind, `606ec8fc…6ae8d081`
  mutations-all, `3a945d51…ec7b4bf3` help, `068fbc97…49dcaf6e` pin-drift — **all fourteen of these
  reproduced exactly in the 2026-11-17 pass, 14 of 14.**
  Third witness, in `acceptance-2p-1656/`: `74a93106…dd3af7f` green, `3a945d51…ec7b4bf3` help
  (the same digest as the second party's help log — see the caution two bullets up), `e8cf0951…3f5efe`
  command-blind, `5e4a6273…b4ee0c` conversions-flag, `20ceca3f…1ec29d` prompt-bind, `67e1b7cc…53d0e7`
  mutations-all, `0ec5237e…a64cfa` pin-drift-run, `00f245f8…4a96be1` selfcheck-A, `1f5e7977…157bd437`
  selfcheck-B, `a85cabf8…9174fd79` selfcheck-C; plus `report.md` `22d4ede0…efa4135` (429 lines, 30,034 B),
  `replay-diff.py` `8439ee1a…139d231` (2,677 B) and `seam-read.out` `b010b713…88e5e7` (1,070 B).
  The seven derived copies in that folder hash to `5de18524…` twice (`selfcheck-A/` and `pin-drift/`,
  i.e. the shipped harness), `2e8c969c…` once, `200aaa45…` once, and `b995063b…` twice with
  `a927a63e…` once — full digests in the third-witness bullet above.
  Documents and code: `HARNESS.md` `c32dd510…c8f51a2e`,
  `ACCEPTANCE-pm-2026-11-16.md` — pinned here on 2026-11-16 as `ba13dc30…7610e6f2` and now
  `2c173dcb…c514b3e`, a moving file this lane does not own, `probe-harness.py` `5de18524…df9102b1`,
  `probe-reaim-2026-11-16.diff` `e1cbb9e6…4856665b`,
  `AutoStemProbe/__init__.py` `b995063b…447220f5` (the pin, unchanged by this lane and re-verified at
  16:52 on 2026-11-17).
  **Separators were byte-verified, not grep-verified**: a Python byte count over **every regular file in
  this folder** returns **42** files, of which **41 are `CRLF=0, CR=0`** and exactly **one is not** —
  `acceptance-2p-1656/seam-read.out` carries **5** CR bytes — all five of its lines are CRLF-terminated,
  on a file of five lines. The old sentence here said "the eighteen
  above, plus this README, the probe itself and the two `.ps1` helpers, **22 files**, CRLF=0 for each
  one", and both halves of that are now wrong: the population is 42 (the eighteen + the twenty third-witness
  files + this README + the probe + the two `.ps1` helpers = 18+20+4 = 42), and the CRLF zero held only
  for the 2026-11-16 population. The exception is a PowerShell capture of a loopback read
  (`seam-read.ps1`, 1,131 B, `b884947b…`, run per `report.md:264`), not harness output, and nothing gates
  on it. Excluded from the census and named so a reader is not
  surprised by it: `AutoStemProbe/__pycache__/__init__.cpython-312.pyc`, 12,757 B, mtime 2026-11-15
  20:25:07 — a stray that `HARNESS.md` does not mention and that the third witness flagged as anomaly
  A1 (`report.md:299-310`); it predates every run in this folder, since `-B` on `py -3.11` could not
  have produced a `cpython-312` artifact. That is still the owner's call to delete before the probe is
  installed, not any lane's. The rest of the caution stands: the cheap instrument lied on the first try
  here — a shell loop that lost its ANSI-C quoting ran `grep -c ''` and printed
  every file's *line count* under a label that read `cr=`; the positive control (`grep -c $'\r'` on one
  file at a time → 0) and the byte census are what settled it. Do not trust a CRLF figure from a loop.
  **verified** (`sha256sum` and a Python byte census over all 42 regular files, twice in this lane, with
  one-file-at-a-time grep controls).
- **The re-aim diff, and the cross-check that costs nothing.** `probe-reaim-2026-11-16.diff` is the
  change that produced the build under test, and it is now in this folder rather than in `%TEMP%`:
  602 lines, 30,616 B, `sha256 e1cbb9e6b0b8d3b053f3f78beadd868e28252e713079828dcefc508d4856665b`, which
  this lane recomputed and which matches `HARNESS.md`'s own *Provenance of `probe-reaim-2026-11-16.diff`*
  section exactly. Three things fall out of it for free, and all three are the kind of check a reader can
  repeat in one command. (1) Its index line reads `index cd9435a..eab9b99 100644`, and
  `git hash-object AutoStemProbe/__init__.py` on the file the pin is about returns
  **`eab9b99458779377fe2d65c2bd93a105685ddeca`** — so the *post-image* of that diff is byte-for-byte the
  build §10 pins and the harness gates on. That is a git **blob ID**, a SHA-1 over
  `blob <size>\0` + bytes, not a second SHA-256 of anything: same warning §10 already carries about
  mixing the two kinds of number. (2) `HARNESS.md` records that an earlier copy of the same change lived
  at `%TEMP%\probe-sim-orig\task12.diff` and could not be reverse-applied because its header lines
  pointed at a temp path, and that `tail -n +5` of the two is identical. This lane re-measured all of
  that rather than trusting it: the temp copy is still on disk (30,774 B, 602 lines,
  `sha256 33d3cad688906dde4ca80d24fa76ed49fd83a0cf636a910aa2e74f50199bf03f`, LF-only), its `index` line is
  the same `cd9435a..eab9b99`, `diff <(tail -n +5 temp) <(tail -n +5 shipped)` is **empty**, and its first
  four headers are the only difference — which accounts for the whole 158-byte size gap. (3) One caution
  that falls out of the same headers: in the commit-ready copy the paths are `a/pre/__init__.py` and
  `b/shipped/__init__.py`, **not** `AutoStemProbe/__init__.py`, so this file is an **audit record of the
  re-aim, not a patch that applies cleanly into this folder** — anyone who wants to replay it has to
  re-name the paths first. **verified** (`sha256sum`, `git hash-object`, `wc`, `head`, `diff` of both
  copies, `HARNESS.md` read); **not verified here**: nobody reverse-applied either copy, and this lane
  does not claim that `cd9435a` is any particular superseded build — only that `eab9b99` is the current
  one.
- **Two log details that will trip a reader, so they are written down here.**
  (1) `probe-harness-pin-drift.log` and `probe-harness-selfcheck.log` each contain **two sections**: the
  original leg, now declared *superseded for provenance only* (its verdict and exit code were right; it
  was launched from a copy that did not tie itself to the shipped harness), and a 16:41 re-run from a
  hashed copy. Both SECTION 1 bodies are still byte-identical prefixes of the file on disk — nothing was
  deleted. (2) Consequently the *drifted probe* hash is not one value: **`1c571b2d…`** in SECTION 1 and
  **`a927a63e…`** in SECTION 2, both being "the shipped probe plus one appended comment line" with
  different comment text. Either is a valid drift and the pin gate fires on both, but a report that
  quotes one as *the* drift hash is quoting a superseded section. The second reader's leg adds a
  **third**, `daac96cf…`, so three different drifted probes now sit across the two witnesses and none of
  the three is the pinned build — which is the assertion the leg is testing, not a discrepancy between
  them. Each of the three logs prints the same pinned value, `b995063b…`, as the thing it was compared
  against. What changed on 2026-11-17 is that one of the three drift values now has **bytes in this
  folder**: `acceptance-2p-1656/pin-drift/AutoStemProbe/__init__.py` hashes to `a927a63e…`, so SECTION 2's
  drifted probe is the one value a reader can verify without taking anyone's word, and the third witness
  agrees with the author on it exactly. The PM's `daac96cf…` is not in the repo — it survives only as a
  printed value in `pm-acceptance-pin-drift.log` and `ACCEPTANCE-pm-2026-11-16.md`, which is the same
  `%TEMP%`-provenance problem the third set was copied in to fix. Same care applies to
  `probe-harness-selfcheck.log`, whose header discloses that **six CRLF terminators landed in its own
  hand-written labels** and were normalised — nothing under the verbatim marker was touched. If you need
  a hash for a claim about these files, take it from `HARNESS.md`'s table or recompute it; do not take it
  from a filename. **verified** (both logs read in full; hashes recomputed).
- **What remains stale on purpose.** The `get_minor_version` arity defect at `AutoStemProbe/__init__.py:143`
  is **not** fixed and is not this lane's to fix: it costs nothing (stage 0 does not read
  `environment.live_version`, §5), and editing the probe would break the pin that every one of these
  fourteen logs (twenty-four as of 2026-11-17) and every document around them is tied to. The harness's
  missing self-hash is **no longer one of these** — it landed 2026-11-19 as harness `1d59b88f…`
  (`acceptance-g4-selfhash/`); the arity defect alone remains
  queued for the post-click batch, and the reason is the same — **the click decision rests on
  bytes that are already recorded, and re-pinning before it would leave the decision citing evidence that
  no longer exists.** §5's "do not read `environment.live_version` as a finding" is what a reader needs
  until then.
- **The witness split, which is the sentence to quote.** Not "everything has been independently
  reproduced" — but as of 2026-11-17 that caveat is narrower than the one this bullet used to carry, and
  the counts below are re-derived from the log listing taken in this lane (**24** log files: seven
  `probe-harness-*.log`, seven `pm-acceptance-*.log`, ten in `acceptance-2p-1656/`), not carried from the
  previous pass.
  There are **ten legs**. **Six** have three witnesses each — green (**0**), `prompt-bind` (**3**),
  `conversions-flag` (**4**), `command-blind` (**5**), `--mutations` (**3**) and pin-drift (**2**) —
  because the author, the PM and the Test Engineer all ran them. **Four** have two each: `--help`
  (**0**, the PM and the Test Engineer — the author left no help log, and the two captures that exist are
  byte-identical static output, which is the weakest kind of second witness there is) and the self-check
  family at **6**, **6** and **7** (the author's hashed SECTION 2, and the Test Engineer's three separate
  logs). No leg is single-witness any more, and the code coverage is now **all seven distinct exit codes:
  0, 2, 3, 4, 5, 6, 7**, each with at least two witnesses. What is still true of the `6 / 6 / 7` family,
  and is the reason it stays the least load-bearing leg: legs B and C describe **derived** harness bytes,
  not the pinned build, and every one of these twenty-four logs is synthetic-stub output.
  And **no stage and no seam query** of our probe has ever been witnessed against a
  running Ableton Live: every log above is synthetic-stub output, and the `Live.Conversions` answer still
  comes only from the row-7 pick. (Narrowed 2026-11-18: "no code at all" overstates it — a CPython 3.11
  *compiled* the installed probe at 2026-11-16 19:32:18 inside Live's startup window, so module-level
  import executed; that the writer was Live is **inferred**, the header does not name it —
  `SEAM-CAPABILITIES-2026-11-18.md` §2b.) The one live observation that exists — the Test Engineer's
  zero-write seam read, whose raw replies sit in `acceptance-2p-1656/seam-read.out` — proves that
  Live's in-process Python answers *reads*, which is a different question from what stage 0 will
  conclude on a real interpreter. **blocked**, as ever in this section: I did not execute the harness,
  so every exit code I report is another process's measurement, recorded in a log I re-hashed rather
  than generated.

