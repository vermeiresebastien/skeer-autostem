# Independent acceptance report — read-only probe harness + one zero-click corroboration

Lane: Test Engineer (Software Dev Team) · Date: 2026-11-16 · Report written 17:06
Repo: `D:\Projects\skeer-audio\autostem` · Evidence folder: `tools\live-probe\`
Contract: `D:\Projects\tools\briefs\autostem-harness-independent-acceptance.md` (118 lines, read first)
Interpreter used throughout: `py -3.11` with `-B` (Live 12.3.2 ships CPython 3.11)

**Verdict up front: PASS on Lane A, PASS on Lane B, one documented deviation in the brief itself
(no `--selfcheck` flag exists), two anomalies for the leader's attention, zero writes to the repo.**

---

## 0. Substitute attestation statement (the point of this lane)

> **A second person — the Test Engineer of this Team, not the Full-stack engineer who wrote the
> harness — on 2026-11-16 between 16:58:04 and 17:00:36 local time, on this machine, ran the bytes
> `probe-harness.py` sha256 `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1`
> against the probe bytes `AutoStemProbe/__init__.py` sha256
> `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` and independently measured
> exit codes 0 / 3 / 4 / 5 / 3 / 0 (help), 2 (pin drift) and 6 / 6 / 7 (the three self-check
> inductions).**

Why that sentence has to exist in words: **the seven `probe-harness-*.log` files do not contain a
hash of `probe-harness.py` itself.** Verified mechanically, not from the doc:

* `probe-harness.py` never calls a hash function on its own file. `sha256_file(` is used only on
  the probe, and the string `__file__` appears at exactly three places — `:103 HERE =
  os.path.dirname(...)`, `:792` (reading its own header for `--help`) and `:812`
  (`say("  harness file %s" % ...)` — a *path*, not a digest). There is no `sha256_file(__file__)`.
* Therefore the line `harness    : probe-harness.py sha256 5de18524…` that appears at the top of
  each recorded log sits **above** the `HARNESS OUTPUT VERBATIM BELOW` marker, i.e. it was
  hand-written by the engineer. It is his record of what was on disk, not the harness's own
  attestation. `HARNESS.md` says as much; I confirmed it in the bytes.

My reproduction, plus the byte-identity replays in §3, is the substitute for that missing
self-attestation. The two deferred fixes (harness self-hash, and `AutoStemProbe/__init__.py:143`
arity) remain out of scope and untouched.

---

## 1. Step 2 — pin check, before anything was run

Measured 16:55:33, from `tools\live-probe`, with `sha256sum`:

| file | expected (brief) | measured | result |
|---|---|---|---|
| `probe-harness.py` | `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1` | identical | **MATCH** |
| `AutoStemProbe/__init__.py` | `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` | identical | **MATCH** |
| `HARNESS.md` | `c32dd51083c76cd0f631076c59d3aac13fae19dd4ed942014e64b0e7c8f51a2e` | identical | **MATCH** |

Nothing was stopped: all three pins matched, so I continued. Sizes/line counts also agree with
`HARNESS.md` (`probe-harness.py` 1,016 lines / 48,884 B; probe 545 lines / 25,992 B; both LF-only).

Extra pins I took for my own provenance (not required by the brief):

| file | sha256 |
|---|---|
| `seam-read.ps1` | `b884947bb328fcda927f228c567156d266ef4be90039b391045f2e2e1ae26bc9` (1,131 B) |
| `probe-reaim-2026-11-16.diff` | `e1cbb9e6b0b8d3b053f3f78beadd868e28252e713079828dcefc508d4856665b` (agrees with the folder's own provenance section) |

Re-verified **after** all runs (§6): every one of these hashes is unchanged.

---

## 2. Step 3 — the eight legs, exit codes owned by me

Method: each leg was launched as its own process with stdout/stderr redirected to a file in my
scratch folder and the exit code taken straight from the process (`$?` / `$LASTEXITCODE`), never
through a pipe — so no `head`/`grep` stole a status. No leg was copied from a log; every number
below is what the process returned to me.

| # | leg | command (run from `tools\live-probe`) | expected | **measured** | time | verdict |
|---|---|---|---|---|---|---|
| 1 | help | `py -3.11 -B probe-harness.py --help` | 0 | **0** | 16:58:04 | PASS |
| 2 | green | `py -3.11 -B probe-harness.py` | 0 | **0** | 16:58:05 | PASS |
| 3 | mutation prompt-bind | `py -3.11 -B probe-harness.py --mutation prompt-bind` | 3 | **3** | 16:58:05 | PASS |
| 4 | mutation conversions-flag | `py -3.11 -B probe-harness.py --mutation conversions-flag` | 4 | **4** | 16:58:05 | PASS |
| 5 | mutation command-blind | `py -3.11 -B probe-harness.py --mutation command-blind` | 5 | **5** | 16:58:06 | PASS |
| 6 | all mutations | `py -3.11 -B probe-harness.py --mutations` | 3 | **3** | 16:58:06 | PASS |
| 7 | pin drift | scratch copy, see §4 | 2 | **2** | 16:59:22 | PASS |
| 8 | self-check A/B/C | three scratch inductions, see §4 | 6 / 6 / 7 | **6 / 6 / 7** | 16:59:22 | PASS |

That is **10 of 10 exit codes measured** (leg 8 is three codes), 0 copied.

Content checks inside my *own* green output (not the recorded log's), because an exit code alone
does not show the gate graded the right thing:

* All **eight shapes** present and `OK`: `attribute`, `class-member`, `import-only`, `flag-only`,
  `no-members`, `nothing`, `stray-only`, `fatal-dir`.
* Observed call set in every shape: `Live.Application.get_application` ×1,
  `Live.Application.get_major_version` ×1, `Live.Application.get_menu_info` ×3, **nothing else**.
  No `Live.Conversions`, `Live.Clip`, `Live.MidiMap` or `Live.Song` member is ever *called*.
* `stage4.attempted = False` — the prompt-name attempt is reported as refused.
* The arity defect the brief told me not to fix is visible in my own run as the call being recorded
  `get_major_version('get_minor_version')` — i.e. `get_minor_version` is reached only as a string
  argument to `get_major_version`, so **`get_minor_version` is never actually called**. This
  confirms the brief's contract note: the *permitted* set is four names, the *observed* set is a
  different three. I quote neither list as "the calls the probe makes".
* `--mutations` log: exactly **3 `FALSIFIER: PASS` lines** (at lines 216, 322, 421) and exactly
  **1 `CONTROL LEG GREEN (exit 0)`**. Exit is 3 by the documented precedence
  forbidden > undocumented > answer, which matches the index.
* `command-blind` asymmetry verified per shape: exactly `['attribute', 'class-member', 'import-only']`
  flip `yes → partial`; the other five shapes stay as documented. Matches the brief's criterion.
* Leg B printed `SELF-CHECK FAILED: the classifier does not grade map_midi_control_to_prompt forbidden`.
* Leg C printed `FALSIFIER: FAIL - mutation prompt-bind gave exit 3 FORBIDDEN_CALL (expected 4 UNDOCUMENTED_CALL)`.

Mechanical check of the harness's own blast radius (grep over `probe-harness.py`): no `socket`, no
`subprocess`, no `Popen`, no `ctypes`, no `winreg`, no `9877`. The only hit is a *comment* at `:75`
that states the harness never opens a socket. So the harness cannot have touched Live, and cannot
have written anywhere but its `tempfile.mkdtemp()` scratch dir — corroborated by §6.

### Deviation from the brief (the one thing that could not be followed literally)

Brief step 3 says: *"Self-check legs A/B/C (the script documents them; use `--selfcheck` per its own
header)"*. **There is no `--selfcheck` flag.** `main(argv)` at `probe-harness.py:786` accepts only
`--help`/`-h`, `--mutation NAME`/`--mutation=NAME` and `--mutations`; anything else falls through to
the usage path. `HARNESS.md`'s "Running it" block does not list it either.

Substitution, and why it is byte-equivalent rather than a reinterpretation: the three self-check
legs are *described as inductions* in `probe-harness-selfcheck.log` SECTION 2, each with the
one-line change and the derived copy's own sha256. I reproduced those inductions in `%%TEMP%%`
scratch copies — **no repo file edited** — and the copies hashed to the recorded values exactly
(§4). So I ran the same bytes the log claims, got the same codes, and can name the harness copy
that produced them. The brief's own instruction "Do not edit the repo copy" is honoured; only its
flag name is wrong.

---

## 3. Cross-check of my runs against the recorded logs — byte-identity replay

The brief asked me to cross-check what I measured against the index "per log". Hash comparison
against the index (§5) only proves the *recorded* files are the recorded files. It does not prove
the current harness still produces them. So I did the stronger check: **replay-diff**.

Tool: `%TEMP%\harness-accept-1656\replay-diff.py`
(sha256 `8439ee1a0a6a4b3e91e6bf0ff3e395efbd47045059023841bb2f1b01d139d231`, mine, scratch, 2,677 B).
For each of the five logs that carry harness output, it takes everything after the
`HARNESS OUTPUT VERBATIM BELOW` marker, takes my run's captured stdout, normalises **only** the
random `tempfile.mkdtemp()` folder name printed in the run preamble
(`autostem-probe-harness-[A-Za-z0-9_]+`), and compares line by line.

| recorded log | my run | lines compared | result |
|---|---|---|---|
| `probe-harness-green.log` | `green.log` | 119 | **BYTE-IDENTICAL** |
| `probe-harness-mutation-prompt-bind.log` | `mutation-prompt-bind.log` | 232 | **BYTE-IDENTICAL** |
| `probe-harness-mutation-conversions-flag.log` | `mutation-conversions-flag.log` | 232 | **BYTE-IDENTICAL** |
| `probe-harness-mutation-command-blind.log` | `mutation-command-blind.log` | 225 | **BYTE-IDENTICAL** |
| `probe-harness-mutations-all.log` | `mutations-all.log` | 437 | **BYTE-IDENTICAL** |

Script printed `REPLAY_MISMATCHES=0 of 5` and exited **0**.

Consequence: `HARNESS.md`'s replay claim is true, and the engineer's "diff against everything below
the marker and expect a difference only in that one path" instruction holds up under an independent
hand. It also ties the five logs to the *current* harness bytes far more strongly than the mtime
ordering argument in the index does.

Two narrower notes on the preamble each log carries, since a reader will quote them:

* The green preamble states `owner's report …\SkeerAutoSTEM\live-probe.json before this run: absent`
  — I independently confirmed that file is absent at 16:55 and still absent at 17:01 (§6). My runs
  did not create it, because stage 0's read-only path does not write a report.
* The same preamble prints `self-check OK` and `probe sha256 b995063b… / pin MATCHES`, which is why
  the green leg cannot be green against drifted bytes.

---

## 4. Legs 7–8 — the scratch inductions, and their hash reproduction

All in `%TEMP%\harness-accept-1656\`, never in the repo. Each copy is `probe-harness.py` with the
documented single-line change (or, for A, a folder with the probe source removed) and each harness
copy that was *not* modified hashes to the shipped `5de18524…`.

| leg | folder | induction | expected | measured | derived-copy sha256 | recorded in log | copy agreement |
|---|---|---|---|---|---|---|---|
| 7 pin drift | `pin-drift\` | shipped harness + probe with one appended comment line | 2 | **2** | probe `a927a63ee39b03e099dbaedca17afd63256a38a15ed7cd238a4e8d530d654024`; harness `5de18524…` | `a927a63e…` | **EXACT** |
| 8 A | `selfcheck-A\` | probe source missing from the resolved folder | 6 | **6** | harness `5de18524…` (unmodified) | n/a | n/a |
| 8 B | `selfcheck-B\` | delete `map_midi_control_to_prompt` from the harness's own `FORBIDDEN_METHODS` (`:124`) | 6 | **6** | `2e8c969c0d10a35360a9430b88cdc937ec1af9ca619e57c64519320b999f54c3` | `2e8c969c…` | **EXACT** |
| 8 C | `selfcheck-C\` | `prompt-bind` `"expect_code": CODE_FORBIDDEN` → `CODE_UNDOCUMENTED` (`:197`) | 7 | **7** | `200aaa4550e35aa83b9081beefc6ad7a0c750cbe0f8dc5a580411a6e5338cc14` | `200aaa45…` | **EXACT** |

**3 of 3 derived-copy hashes reproduced exactly**, which means my one-line change landed on the
same bytes as the engineer's and at the same place — this is a real re-induction, not a similar one.
Note leg A's harness copy is unmodified (`5de18524…`): exit 6 there is triggered by the *folder*
state, not by an edit.

---

## 5. Step 4 — per-log agreement with the `HARNESS.md` index

Agreement is reported per log, **not as a verdict**. Measured twice — 16:56:19 (before the runs) and
17:01:28 (after) — identical both times, so nothing I did touched these files.

| log | index claims | measured | agreement |
|---|---|---|---|
| `probe-harness-green.log` | `fb5aa199b8a84891929ce2cf0165cd6b4708c87689956ab1690f6da984210b0b` | same | **AGREE** |
| `probe-harness-mutation-prompt-bind.log` | `3232ad9b02476b2459970aba586ec80eef35bfe1a4b2b6a144b1a6bb45942ec4` | same | **AGREE** |
| `probe-harness-mutation-conversions-flag.log` | `5c6d3f3e7d8840e68907458612c0e573af7788fba4fcd56523a9996c4653e9d2` | same | **AGREE** |
| `probe-harness-mutation-command-blind.log` | `c57773cc473dae6c5b964468e15b4ff0590c5c20228d76681ba51d10ce684d51` | same | **AGREE** |
| `probe-harness-mutations-all.log` | `b72244d86f4ff9e5a603ed27ba9e679f9062f2198e62fd57117b9994feacf965` | same | **AGREE** |
| `probe-harness-pin-drift.log` | `4424d0ffbb1b8754de6f3e5f197efd7e65fb19846f86bff9ea8f5aabfabb46b4` | same | **AGREE** |
| `probe-harness-selfcheck.log` | `3e7d8b2debb39dd05d970f4d6bc2380b26dd78df6eb33536ae924dedcddb6ac2` | same | **AGREE** |

**7 of 7 AGREE.** Caveat the index itself is honest about, which I keep in front of the numbers:
agreement proves these are the files the index describes. It proves nothing about who ran the
harness that produced them — that is precisely the gap §0 and §3 close.

Also verified from the index's own claims, and consistent: `HARNESS.md` `c32dd510…`, harness
`5de18524…`, probe `b995063b…` all match what I measured, and the drift probe in SECTION 2 is
`a927a63e…` (not SECTION 1's `1c571b2d…`) — I reproduced the SECTION 2 one.

---

## 6. Zero-write / zero-side-effect evidence

| check | method | result |
|---|---|---|
| Repo untouched | `git status --porcelain \| wc -l` before (16:54) and after (17:05) | **42 → 42, identical** (I did not print paths — content filters are configured in this repo, so a cleanliness claim would be misleading; the *set* is unchanged) |
| Evidence files untouched | §5 pre/post hashes + `probe-harness.py`, `AutoStemProbe/__init__.py`, `HARNESS.md`, `seam-read.ps1`, the `.diff` re-hashed at 17:05 | all unchanged |
| No `__pycache__` from my runs | `ls AutoStemProbe/` post-run | harness's own pre/post `os.listdir(PROBE_DIR)` gate exited green in every leg; **but see anomaly A1** — a `__pycache__` *is* present, from before my lane |
| No owner report written | `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` at 16:55 and 17:01 | **absent both times** |
| No scratch leaked into temp | `ls -d %TEMP%\autostem-probe-harness-*` | **0 dirs** (harness cleans its own `mkdtemp`) |
| Harness wrote nothing outside scratch | grep for `socket`/`subprocess`/`Popen`/`ctypes`/`winreg`/`9877` in `probe-harness.py` | **none** (only the comment at `:75`) |
| My artifacts | all under `%TEMP%\harness-accept-1656\`, listed in §7 | **outside the repo**, per the brief's preference |

I deliberately wrote this report to `D:\Projects\tools\` rather than into `tools\live-probe\`, to
satisfy "zero writes to `tools/live-probe/` evidence files and zero writes anywhere else in the
repo" without needing the "unless you deliberately add your own log — then name it and hash it"
exception. If the leader wants this report inside the evidence folder, that is a one-command copy
and an explicit decision — say so and I will not guess.

---

## 7. My run captures (all outside the repo), with hashes

`%TEMP%\harness-accept-1656\` :

| file | sha256 | bytes |
|---|---|---|
| `green.log` | `74a9310648a7076eab4220a169e70830d4336fc2394d2b2de4bcae501dd3af7f` | 13,741 |
| `help.log` | `3a945d51687bc51c32a00a1254bcac1e18737941a674305d3de66676ec7b4bf3` | 6,386 |
| `mutation-prompt-bind.log` | `20ceca3f0884adbdc914b08ed9b23263cd98cc13f6fb00c8d1303033461ec29d` | 26,600 |
| `mutation-conversions-flag.log` | `5e4a62734376e69f071c0aadd7ccf7426741ab3d48e3e24d8180d724c8b4ee0c` | 25,550 |
| `mutation-command-blind.log` | `e8cf095121925175191322e24efb98c90998ae137fa3c74344321dad5c3f5efe` | 25,325 |
| `mutations-all.log` | `67e1b7cc5c8037284dd8ef1e22cc68b2cd124d594b858d8cf1acfc11ba53d0e7` | 49,003 |
| `pin-drift-run.log` | `0ec5237eb969a01ffa403a6e3806e4c5bc5c40a64142107f91d09e0626a64cfa` | 1,023 |
| `selfcheck-A.log` | `00f245f8eab2932c8a0ffb14d8d3bb14c07c360188e6d61303737c0844a96be1` | 728 |
| `selfcheck-B.log` | `1f5e7977d95a871a3438130c657eae8558cd4e0544b311b25fe22fd9157bd437` | 1,511 |
| `selfcheck-C.log` | `a85cabf84f5770e5f15c4891281cd03d2a781ec79db3b16a9e3aacda9174fd79` | 26,799 |
| `seam-read.out` | `b010b713453b23f713dee11e8d0b9e9efe00d581faab7811f20a50404788e5e7` | 1,070 |
| `replay-diff.py` | `8439ee1a0a6a4b3e91e6bf0ff3e395efbd47045059023841bb2f1b01d139d231` | 2,677 |

These are ephemeral `%TEMP%` files: they will not survive a clean-up, and the folder is mine, so the
leader may delete it once this report is accepted. The durable record is this file plus the hashes above.

---

## 8. Lane B — the read-only seam corroboration (one attempt, taken)

Pre-flight, per the brief: I **read `seam-read.ps1` before running it** (1,131 B, `b884947b…`) and
confirmed it is exactly what was described — a `TcpClient` to `127.0.0.1:9877`, `ReadTimeout=4000`,
`WriteTimeout=2000`, sending only
`{"type":"get_script_info","params":{}}` and `{"type":"get_session_info","params":{}}`, one `Read`
per send, then `$client.Close()`. No third payload, no writes, no UI calls. So I was permitted to run it.

* Command, unmodified, **once**: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\live-probe\seam-read.ps1`
* Window: 17:00:35 → 17:00:36 · process exit **0** · **1 attempt used of the 2 allowed; no retry, no
  other channel, no other command.**
* Boundaries honoured: no UI automation, no clicks, no keystrokes, no cursor movement, no focus
  change, no window enumeration, no Preferences, **no slot pick**, no process start/stop/kill,
  nothing written to his User Library or project folder. Live pid 35676 untouched; the unsaved
  `Untitled` set is still his.

### The two raw replies, verbatim

```
=== get_script_info ===
{"status": "success", "result": {"name": "AbletonMCP", "script_version": "1.7.1", "protocol_version": 1, "port": 9877, "capabilities": ["get_session_info", "get_track_info", "get_script_info", "get_clip_notes", "get_device_parameters", "get_session_snapshot", "set_device_parameter", "drain_passive_events", "create_midi_track", "create_audio_track", "create_clip", "create_audio_clip", "add_notes_to_clip", "load_instrument_or_effect", "get_arrangement_clips", "duplicate_session_clip_to_arrangement", "create_locator", "delete_clip", "clear_notes_from_clip"], "snapshot_schema": "ableton_mcp_snapshot_v2", "passive_listeners": true}}
=== get_session_info ===
{"status": "success", "result": {"tempo": 129.0, "signature_numerator": 4, "signature_denominator": 4, "track_count": 1, "return_track_count": 2, "master_track": {"name": "Master", "volume": 0.8500000238418579, "panning": 0.0}, "is_playing": false, "current_song_time": 378.11843078796204, "song_length": 816.0, "loop": false, "loop_start": 8.0, "loop_length": 16.0}}
seam closed
```

### What B proves, and what it does not

Proves: Live's in-process Python on **this** machine, in **this** live session, answers read-only
LOM queries over the loopback seam — so a second zero-click observation channel into a running Live
exists without touching his cursor or his set. It corroborates the *mechanism* stage 0 of the probe
relies on.

Does **not** prove anything about stem separation. `get_script_info` returns a **fixed 19-name
capability list with no separation command and no generic "call any LOM member" entry**, so the
separation question is unreachable through this seam. Per the brief, I do not let a green B slide
into a claim about `Live.Conversions`: **the separation question is still decided only by the
owner's row-7 dropdown pick plus the probe.**

---

## 9. Anomalies (not blockers, but the leader should know)

**A1 — a pre-existing `__pycache__` in the probe folder that `HARNESS.md` does not mention.**
`AutoStemProbe\__pycache__\__init__.cpython-312.pyc`, 12,757 B, mtime **Nov 15 20:25:07**.
It was **not** created by my runs: I used `-B` (no bytecode) on `py -3.11` (so any bytecode I could
produce would be `cpython-311`), the harness lists `AutoStemProbe/` before and after each leg and
would exit 6 on a change, and my post-run listing shows the file's mtime unchanged and no `311`
sibling. Two readings are possible and I cannot distinguish them from here: either it predates this
proof, or a `3.12` import of the probe happened at some point. Why it matters: **the harness's
pre/post listing gate is about the folder's contents, so a stray `.pyc` inside a subdirectory
beside the shipped bytes is exactly the kind of artifact the owner could later mistake for part of
the deliverable** — and `tools\live-probe\` is entirely untracked, so git cannot tell him either.
Recommendation (his call, out of my lane): decide whether `AutoStemProbe/__pycache__` gets deleted
before the probe is installed. I did not delete it; it is not mine.

**A2 — what the AbletonMCP script actually exposes is far wider than "read-only".** Its capability
list contains **11 writers** (out of 19 capabilities, which partition as 7 reads +
`drain_passive_events` + these 11) against the owner's unsaved set: `set_device_parameter`,
`create_midi_track`, `create_audio_track`, `create_clip`, `create_audio_clip`,
`add_notes_to_clip`, `load_instrument_or_effect`, `duplicate_session_clip_to_arrangement`,
`create_locator`, `delete_clip`, `clear_notes_from_clip`. My run sent none of them. This is a fact
about the *third-party script already loaded in row 6*, not about our probe — but it is relevant to
the safety story the Team is telling, because the seam on 9877 is not inherently read-only; only
our 31-line script was. The `.NET` takeover path and this seam are different risk surfaces and
should not be described with the same sentence.

**A3 — the arity defect at `AutoStemProbe/__init__.py:143` is observable in the green run.** As
recorded in §2, the harness sees `get_major_version('get_minor_version')`, so `get_minor_version`
is never genuinely called and the version read silently degrades. Instructed not to fix; noting
that the harness *does* surface it as a call-shape difference rather than gating it, because
`get_major_version` is permitted and argument arity is not gated.

**A4 — the brief's `--selfcheck` flag does not exist** (detailed in §2). Fixing the brief is a
leader-side docs action, not a repo action; worth correcting so the next person does not stop.

---

## 10. Acceptance-criteria scorecard

| criterion from the brief | status |
|---|---|
| Harness reproduces on a cold start by a second person: 0 / 3 / 4 / 5 / 3 and 2, with 6 / 6 / 7 on the self-check legs, all measured, none copied | **MET** — 10/10 codes measured by me; the self-check legs reached by re-induced copies rather than a non-existent flag, with all three derived hashes reproduced exactly |
| Pinned probe bytes I ran match `b995063b…` | **MET** — pinned before and re-verified after; the probe in this folder **is** the artifact whose safety claim rests on it (note: whether the copy installed in his User Library equals these bytes was *not* re-verified in this lane — see §11) |
| Zero writes to `tools/live-probe/` evidence files and zero writes anywhere else in the repo | **MET** — 7/7 log hashes unchanged pre/post, 4 pins unchanged, `git status --porcelain` 42 → 42, my captures in `%TEMP%` only, this report in `D:\Projects\tools\` |
| Zero contact with the running Live beyond the two documented reads, and no claim B could not support | **MET** — one script, unmodified, one attempt, two reads, both replies above; §8 states the limit explicitly |

---

## 11. Verified by me vs still attested by one person only

**Independently verified in this lane (measured, this machine, 16:55–17:05):**

1. The three pins in the brief, and five more folder hashes (§1, §5).
2. All 10 exit codes, owned directly from the process (§2).
3. The content of the green run: 8 shapes, the exact observed call set, `stage4.attempted=False` (§2).
4. The mutation-leg criteria: 3 falsifier PASS + 1 control green; `command-blind`'s 3-vs-5 asymmetry (§2).
5. That the current harness bytes still reproduce the five recorded logs byte-for-byte — the replay
   in §3. This is stronger than what the index claimed, since it needs no mtime argument.
6. That all three self-check inductions and the pin-drift drift probe reproduce their recorded
   derived-copy hashes (§4) — so the recorded gates were real gates.
7. That `probe-harness.py` contains no socket/subprocess/registry/Popen code path (§2) — the
   "offline" claim is now verified in the bytes, not only asserted.
8. That `probe-harness.py` does not self-hash — the missing-attestation defect is confirmed (§0).
9. That Live answers the two documented read-only LOM queries in-process, with the replies verbatim (§8).
10. Zero side effects: repo, evidence files, owner's `%LOCALAPPDATA%` report path, temp dir (§6).

**Still attested by the Full-stack engineer alone (I could not verify without leaving this lane):**

* **Whether the probe installed in the owner's User Library is byte-identical to `b995063b…`.** The
  brief's acceptance criterion is about the bytes I ran; the *installed* copy was out of scope here
  and I did not read his Library. Whoever asks the owner to pick row 7 should confirm the installed
  file's hash equals `b995063b…` at the moment of the pick.
* The claim in `HARNESS.md` that the probe's behaviour on a **real** Live interpreter matches any of
  the eight stub shapes. The harness is a proof about *writes*, not about *answers*: it can only
  tell us what the probe would report given a shape we built. Which shape Live 12.3.2 actually
  presents remains unverified — that is exactly what the owner's one click is for.
* The reverse-application proof of `probe-reaim-2026-11-16.diff` (`git apply -R` → `738083ee…`, `cmp`
  against preserved bytes). I verified the diff's current hash `e1cbb9e6…` but did **not**
  re-apply it; that requires a scratch tree and, in effect, re-running the engineer's own proof.
  Attested.
* The historical SECTION 1 runs of the pin-drift and self-check logs (drift probe `1c571b2d…`,
  unhashed scratch harness copies). `HARNESS.md` already declares those superseded for provenance;
  I agree, and my §4 re-induction is the replacement for the one that mattered.
* Everything about **stage 1 and beyond** of the real probe: what it will print on Live's actual
  interpreter, and therefore whether separation is available. Not a code question — an un-run
  question.

---

## 12. Uncovered scope (deliberate, per the brief's out-of-scope list — stated so the leader can plan)

* **The owner's row-7 dropdown pick**, and therefore the only measurement that can answer the
  separation question. Still open; it is his decision and nothing in this lane substitutes for it.
* Loading/unloading any remote script; the `AutoStemProbe` code path on a real interpreter.
* **The `.NET` suite entirely**: no builds, no `dotnet test`, no installer work. (For context only:
  my earlier baseline lane found the tree at 263 tests with one environmental flake in
  `PostedKeyTests`, and two live-edit compile errors in `src/StemForge.Core/Live/**` — that tree was
  being actively rewritten by teammates at the time, so none of it is a statement about now.)
* The takeover path and the refusal paths, and the `--simulate` behaviour.
* UI/interaction acceptance of the WPF app — none was in scope, none was attempted.
* Firebase/credentials, packaging, signing.
* The two deferred fixes: `AutoStemProbe/__init__.py:143` arity, and harness self-attestation.

## 13. Blockers

* **None on Lane A.** It is complete.
* **None on Lane B.** It is complete within its bounds (one attempt, exit 0, both replies captured).
* One **contract** defect: the brief's `--selfcheck` flag does not exist (§2, A4). Worked around by
  byte-equivalent re-induction, disclosed.
* One **tooling** limitation in this session, unrelated to this lane but it affected an earlier one:
  `qc-cli message list --discussion-id …` returns `host_operation_unavailable`, so I could not read
  the owner's authorisation text directly — relevant if the leader wants the owner's own words on
  record for the row-7 pick rather than a paraphrase.
* One **decision** the leader owes before the click, not a blocker for me: A1 (`__pycache__` beside
  the shipped probe in an untracked folder).

---

## 14. Conclusion for the Team

The read-only safety claim behind the owner's one Preferences click is **now verified, not just
attested**. On a cold start, by a second pair of hands, against the exact bytes that are the subject
of the claim, the harness goes green on the shipped source, reddens on each of the three deliberate
breaks with the predicted code, refuses drifted bytes, catches its own broken gates — and the
current harness still reproduces every recorded run byte for byte. The probe's observed call set
inside that proof is three `Live.Application` reads, and it never calls a `Conversions`, `Clip`,
`MidiMap` or `Song` member. **Nothing in this lane found a write path in the probe.**

Two things this does **not** buy the Team: it does not tell us which of the eight shapes a real
Live 12.3.2 presents — so it does not answer the separation question, only protects the question-
asking — and it does not cover the already-loaded AbletonMCP script, which exposes eleven write
capabilities against the owner's live unsaved set (A2). Keep those two claims separate in anything
shown to the owner.
