# G-batch brief — make the probe loadable, and prove it offline (2026-11-17)

Prerequisite, **done and verified**: G0 put a hash-verified pre-fix snapshot outside the repo at
`D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/` — 42 files, `MANIFEST.sha256` sha256
`4292f6a16eb741d4d6011b56cc1a0642aa03c22b25ff88a1b78c28663d08ccf3`, `sha256sum -c` exit **0** with
**42 `: OK` lines against 42 files**, 0 CR bytes in the manifest, 0 blank lines, empty stderr. That
snapshot carries the exact bytes this batch supersedes, so **the commit decision no longer carries
the evidence risk** and this batch is not gated on it.

Why the batch exists: `AutoStemProbe/__init__.py` defines **no module-level `create_instance`**, so
Live has never built the surface — 0 mentions in 8,178,082 bytes of Live log, row 7 = `None` on the
current pid 26720 startup table (`2026-11-17T16:34:10`). Full evidence and the three-way seam split:
`docs/team-briefs/PHASE-A-2026-11-17-steer-1.md`. Task **#22**; the batch is task **#17** extended.

## Ownership and hard boundaries (violating these invalidates the batch)

| File | Owner for this batch |
|---|---|
| `tools/live-probe/AutoStemProbe/__init__.py` | **you** (G1) |
| `tools/live-probe/instantiation-gate.py` + its two logs | **you** (G1.5, new files) |
| `tools/live-probe/probe-harness.py` `:35` and `:106` only | **you** (G2) |
| `tools/live-probe/README.md`, `HARNESS.md` | **Researcher lane — do not edit.** They are editing both right now |
| `tools/live-probe/ACCEPTANCE-pm-2026-11-16.md` | **coordinator — do not edit** (its §8 already records this batch's witness) |
| `src/**`, `tests/**` | untouched by this batch (that is A3/A4, a different dispatch) |

- **No commits, no push, no `git clean`.**
- **Do not launch, restart, attach to, click in, or connect to Live.** No socket, nothing on
  `127.0.0.1:9877`. The owner is working in Live on this machine; if anything on the desktop
  contradicts you, it is him — re-read, never race.
- **Do not touch the user's installed copy** in
  `C:/Users/Sebastien/Documents/Ableton/User Library/Remote Scripts/AutoStemProbe/`. Installing the
  fixed bytes and picking row 7 are **after** this batch and after his restart decision.
- Never rewrite a historical log or a derived scratch copy. The partition below is **measured**, with
  its base named, because the first version of this brief said "33 files" and **no base makes that
  number true**. Re-measured 2026-11-17 with the base beside each figure:

| number | what it actually counts | how measured |
|---|---|---|
| **42 / 43** | files in `tools/live-probe/`, excluding / including `AutoStemProbe/__pycache__/__init__.cpython-312.pyc` | `find tools/live-probe -type f [! -path "*__pycache__*"] \| wc -l` |
| **32** | files **inside `tools/live-probe/` containing the old hash** — NOT a folder census | `grep -rl b995063b tools/live-probe \| wc -l` |
| **35** | the same, repo-wide (32 + the 3 briefs under `docs/team-briefs/`) | `grep -rl b995063b . --exclude-dir=.git \| wc -l` |
| **28** | the immutable set = 32 − the 4 changeable files | `grep -rl … \| grep -vw -e README.md -e HARNESS.md -e ACCEPTANCE-pm-2026-11-16.md -e probe-harness.py \| wc -l` |

  Do not reuse the word "files" for two different bases in one sentence: "32 files in the folder" is
  **wrong** (the folder is 42), "32 files carrying the old hash inside the folder" is right.

| class | files | rule for this batch |
|---|---|---|
| **harness code** | `probe-harness.py` `:35` (quoted hash in the comment) and `:106` (`PINNED_SHA256`) | **2 lines change** |
| **the probe** | `AutoStemProbe/__init__.py` — changed by G1, and it carries **0** occurrences of the hash | rewritten by G1, not part of the partition |
| **prose records** | `README.md`, `HARNESS.md`, `ACCEPTANCE-pm-2026-11-16.md` | **re-aim + annotate dated 2026-11-17 — three files, not two** |
| **everything else** | **37 files** — you do not touch any of them | **must not change, ever** |

  The 37 are the whole remainder of the 42 payload files, and the arithmetic is checkable:
  **42 = 37 + 3 prose + 1 harness + 1 probe**. Enumerated, so your exclusion list is generated and
  not remembered: the **14 top-level `*.log`** files (7 `probe-harness-*` = the author's run, 7
  `pm-acceptance-*` = the PM's second-witness run — say which is which, "14 author logs" is wrong),
  the **20 files under `acceptance-2p-1656/`** (10 logs, `report.md`, `replay-diff.py`,
  `seam-read.out` and **7 derived scratch copies** — `pin-drift/{AutoStemProbe/__init__.py,
  probe-harness.py}`, `selfcheck-A/probe-harness.py`, `selfcheck-B/{…}`, `selfcheck-C/{…}`), plus
  `modal-no-mouse.ps1`, `seam-read.ps1` and `probe-reaim-2026-11-16.diff`.

  **Do not define the immutable class as "the files containing the old hash": it is 28 of those 37,
  not 37.** Nine evidence files legitimately carry no hash at all — `selfcheck-A.log` (leg A has no
  probe on disk to hash; its log prints a probe *path* and the verdict `probe source not found`), the
  three derived `AutoStemProbe/__init__.py` copies, `replay-diff.py`, `seam-read.out`,
  `modal-no-mouse.ps1`, `seam-read.ps1`, `probe-reaim-2026-11-16.diff` — and a not-must-change list
  built from the hash census would silently leave them unprotected. Use `grep -rl` for check (a)'s
  **count**; use the 37-file enumeration for the **touch ban**.

  **And prove the touch ban the deterministic way: `cmp`, not a grep.** Witness taken by the PM on
  2026-11-17, before this batch starts: all **37** protected files are byte-identical to the G0
  snapshot (`cmp -s` per file against `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/`,
  result `identical=37 differs=0 missing_in_G0=0`). Re-run the same loop at the end of your batch and
  put the three numbers in your report. A file that must not change is proved by comparing bytes, and
  that check does not care whether the file contains a hash.

  Touching any of the 28 destroys §7 and the G0 manifest that freezes them; touching any of the other
  9 destroys a witness that nothing else reproduces.
- **The folder is no longer the 42-file set G0 froze, and that is not drift.** `tools/live-probe/`
  holds **43** files today; G0's manifest lists 42 payload files (not itself), and the single extra is
  `AutoStemProbe/__pycache__/__init__.cpython-312.pyc`, deliberately excluded. **Record the post-batch
  listing on its own line in the acceptance record. Never diff it against 42 and call the delta
  corruption** — this batch adds `instantiation-gate.py`, its two logs and the G3 logs on top.
- If you write with a bulk script, use **each file's own line separator**; a byte-level write that
  normalises endings creates `w/mixed` files and re-aims every citation.

## G1 — the factory

Add the module-level factory the way Live resolves it, matching the neighbour that works
(`AbletonMCP/__init__.py:62`: `def create_instance(c_instance):` → `return AbletonMCP(c_instance)`).
Either form is acceptable — a top-level `def create_instance(c_instance): return AutoStemProbe(c_instance)`,
or a re-export in `LK_Bridge`'s style (`from .x import create_instance` + `__all__`). Prefer the
one-line top-level def: this file has no sibling module.

Record in the code comment, because it is an inference people will get wrong: **`LK_Bridge` is
listed in Live's table as a built surface with `Input="None" Output="None"`, so the absence of MIDI
ports is never evidence of a failed load.** The table lists what Live constructed.

## G1.5 — the offline instantiation gate, built BEFORE any claim about the fix

The owner must not restart Live on a fix that is only proven to compile. So:

1. Write `tools/live-probe/instantiation-gate.py`: stub the `Live` and `_Framework.*` packages Live
   injects, plus a fake `CInstance`, then **import the script the way Live imports a Remote Script
   and call `create_instance(fake)`**. Require a constructed surface and no exception. Exit nonzero
   on anything else, print the probe's sha256, and own the exit status with a redirect to a file —
   never a pipe in front of the command whose status you report.
2. **Red first, against the CURRENT bytes.** Run the gate on the unmodified probe and show it fails
   **specifically on the missing factory**, not on your stub's shape. Save
   `tools/live-probe/instantiation-gate-red.log`, and put the pre-mutation probe hash
   (`b995063b…`) **inside** that log so the red is self-proving.
   If the gate goes green on the broken probe, the gate is wrong — fix the gate, not the expectation.
3. Then apply G1, re-run, and save `instantiation-gate-green.log` with the new hash.
4. **Prove `:143` in the same gate, do not assume it.** `get_major_version` / `get_minor_version` are
   passed as one call's arguments (`_call(self, target, method, *args)` at `:461`), which is the
   known arity defect. Make the gate drive the stage that reaches it, fix the arity in the same
   batch, and show the difference in the green log. This is task #17's item, folded here because it
   is the same file and the same region.
5. The harness self-attestation item of #17 is **not** in this batch — it moves `probe-harness.py`
   bytes beyond the two pin lines and would invalidate the G3 table you are about to re-run. It is
   **G4**, and its trigger is now written down: *deferred to after the G3 table is re-accepted*.
   Say so in your report rather than silently bundling it. The prose carriers that still say
   "deferred to after the click" are re-pointed to that sentence by the **G3 doc pass** (§G3-pass),
   not by you — you do not own README.md.

## G2 — re-pin, both sites

Measured today, so you are not guessing: the old hash is quoted at **`probe-harness.py:35`** (inside
the `THE BYTES THIS PROOF IS ABOUT` comment) **and** at **`:106`** (`PINNED_SHA256 = "…"`). Update
**both** in the same change — a stale quoted hash beside a live constant is exactly how a pin stops
meaning anything while still printing green. Then run `py -3.11 -B probe-harness.py` once and confirm
exit **0** over the new bytes, and confirm it still returns **2** when the probe drifts (that red is
the point of the file).

State in your report, plainly: because the harness prints its own *path* and not its own bytes, once
this file is edited **no log can prove which harness ran**, and the pre-fix harness hash
`5de18524…` is recoverable only from the G0 snapshot plus a hand-written claim. Disclose it as that.

## G3 — hand off, do not self-certify

Do **not** declare the table re-accepted. Hand the new bytes and your two gate logs to the Test
Engineer for the **full** exit table (green 0, `prompt-bind` 3, `conversions-flag` 4,
`command-blind` 5, `--mutations` 3, `--help` 0, pin-drift 2, self-check legs 6/6/7) with derived-copy
hashes recorded and **logs written into the repo, never a temp folder**. The old table is
superseded-for-the-new-bytes and remains **valid for `b995063b…`**, with G0 as its witness. Do not
delete or re-title it. The self-check legs have no `--selfcheck` flag: use the induction the harness
documents in its own header, and if it does not document one, report that instead of inventing it —
that flag was invented once already, by this coordinator, in an earlier brief.

## G3-gate — the post-state gate is TWO separate checks, not one

The first version of this instruction was "**no occurrence of the old hash** anywhere". That gate
is **unsatisfiable and must not be shipped**: 28 of the 37 protected evidence files (the 14 top-level
logs and 14 of the 20 under `acceptance-2p-1656/`) record `b995063b…` as the bytes their runs actually
used, and all 37 are forbidden to change. A gate that requires the witnesses to be quiet either goes
red forever or invites someone to scrub a record. The satisfiable form is two checks, written and run
**as separate checks with separate exit statuses**, never ANDed into one line:

- **(a) occurrences of the OLD hash outside the 28 hash-carrying evidence files drop to the expected
  residual — and "residual" is not zero.** Those 28 legitimately keep the value; the four changeable files legitimately keep
  quoting it as superseded-marker text. Pre-state, re-measured by the PM **2026-11-18 09:2x local**,
  prefix pattern `b995063b`, occurrence mode (`grep -o … | wc -l`):
  `README.md` **8** + `HARNESS.md` **3** + `ACCEPTANCE-pm-2026-11-16.md` **12** +
  `probe-harness.py` **2** = **25 occurrences in the 4 changeable files**. `ACCEPTANCE-pm` is at 12 and
  not at the 6 or 8 quoted in earlier drafts of this brief, because that record keeps writing about
  itself. The expected post-state
  residual is the sum of what the annotation text still quotes, stated **per file with the lines
  listed beside it** — never as a bare total, and never as 0.
- **(b) occurrences of the NEW hash outside the changed set equal zero.** This one *is* legitimately
  a zero: the new value may appear only in the files this batch legitimately writes — the two G1.5
  logs, the G3 logs, `probe-harness.py` `:35`/`:106`, and the prose records the G3 doc pass updates.
  Anywhere else means a copy drifted or a claim was written into a witness.

Pattern discipline, because both checks silently change meaning with the grep string. Measured:
with the **prefix** `b995063b` there are **32** carrier files inside `tools/live-probe/` and **35**
repo-wide; with the **full 64-char** hash, **32** and **33** — and inside the four changeable files the
prefix yields **25** occurrences where the full string yields **10**. Worse, the prefix also matches a
**path**, `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/`, which G0 deliberately put in the
record. So: **check (b) must grep the full new hash** (a truncated value would match a path and a
mention), and **check (a) must state which pattern it used and re-print the per-file lines**, not
just a number. A gate whose answer changes 2× depending on the length of the string you typed is not
a gate — write the pattern into the log next to the count.

One more trap that is real, not hypothetical, and it is in the numbers I just handed you:
`ACCEPTANCE-pm-2026-11-16.md` §9 recorded the census of its **own** file as 6, that file measured 8
after §9 landed and **12 after §10 and §11 landed**, because each census line quotes the value it
counts. §11 states it in that form. So the **25** above is true of the tree as of
this run and of no later one: a census of a document that records censuses is a snapshot taken *before*
the paragraph reporting it. Use it as the pre-state base, re-measure at your own gate time, and
expect this file's and `ACCEPTANCE-pm`'s numbers to move without that being drift.

Both checks run from the repo root, print their counts beside the file list they counted, and carry
**separate exit statuses** — never ANDed into one line. If (a) and (b) are folded into a single
assertion, the gate is broken again.

## G3-pass — the re-points the prose records need (Researcher lane owns these files)

Trigger: **after** the Test Engineer returns the re-accepted table. Four carriers still tell the
reader the deferral was keyed to the row-7 click, which is no longer the sequence. They must say
*"deferred to after the G3 table is re-accepted"* and name **G4** as the batch that owns it:

| carrier | file | measured line at 2026-11-17 17:4x |
|---|---|---|
| bullet start ("Why the acceptance *record*, not the logs, is the attestation") | `tools/live-probe/README.md` | `:957` |
| "deliberately deferred to after the click" | `tools/live-probe/README.md` | `:973` |
| "Do not 'improve' the harness before the row-7 pick" | `tools/live-probe/README.md` | `:975` |
| "queued for the post-click batch" | `tools/live-probe/README.md` | `:1092` |
| PM's own deferral sentence | `tools/live-probe/ACCEPTANCE-pm-2026-11-16.md` | `:87` — **already re-pointed to G4 by the coordinator, do not re-do it** |

**Re-derive these before writing.** `README.md` is being edited by the Researcher lane right now: it
went 86,029 B / `5beff937…` (2026-11-16 17:26) → **97,460 B / `9aa805d5…`, 1,119 lines
(2026-11-17 17:02)** when A2 landed, and every one of the four carriers above moved by **+2 lines**
between my two measurements inside this same hour. Nobody can hand another lane stable line numbers
into a file that lane is actively changing: the numbers in this table are a **snapshot with a
timestamp**, not a contract. Grep the phrase, then cite.

Citation warning, earned the hard way: `README.md:894-904` / `:901-904` is the range that appears in
`docs/team-briefs/PHASE-A-2026-11-17.md:42` and in the first version of my own steer, for the
harness-self-attestation region. **It is wrong** — `:889-908` is the *single-witness supersession*
text, not the attestation text; the attestation statement is the `:957-975` bullet. Verified by
reading the file, not by arithmetic. Use the table above.

## G4 — named here so it is not lost, NOT in this batch

Harness **self-attestation**: `probe-harness.py` prints the probe's sha256 but only its own *path*,
so no log proves which harness bytes ran. Fixing it changes harness code beyond the two pin lines,
which is why it was held back. Owner: the same lane that does G1/G2. Gate: red on the symptom in the
test's final shape, then the full exit table again by a second person — G4 supersedes G3's table, it
does not amend it. Trigger: G3 accepted.

## Return

Per step: landed or not; files touched, with sha256 before and after; the red log's exact failure
reason and the green log's proof; the `:143` evidence; every number measured in this run re-derived
from the items printed beside it; anything in this brief you found wrong (it is a claim too); what
stayed a residual; and explicit confirmation you touched neither Live nor the installed copy nor the
derived scratch folders.
