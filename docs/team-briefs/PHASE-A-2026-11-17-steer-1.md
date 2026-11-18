# Steer 1 to Phase A — 2026-11-17, 15:50 UTC — the probe has never been loadable

Source: the coordinating session ("Setting up Live set") measured this on the bench and passed it
down. **The PM re-derived every fact independently from disk before relaying it. All five are
confirmed, and one control was added.** No socket was used by anyone, in either direction.

Affects **A1** and the A1/A2 README work. **Does not affect A3 or A4** — those lanes keep running
unchanged.

## 1. Verified facts (PM's own measurements, this session)

| # | Claim | PM verification | Result |
|---|---|---|---|
| 1 | Live 12.3.2 is running now | `tasklist //FI "IMAGENAME eq Ableton Live 12 Suite.exe"` | `Ableton Live 12 Suite.exe` **pid 26720**, 868,288 K |
| 1b | AutoStemProbe is absent from the surface table | `Roaming/Ableton/Live 12.3.2/Preferences/Log.txt`, 8,178,082 B, mtime 2026-11-17 16:50:26; `grep -c AutoStemProbe` | **0 occurrences**; last startup table at **line 64896**, `2026-11-17T16:34:10.834618` |
| 1c | The table content | `sed -n '64896,64912p'` | Rows 1-6 = `Push`, `LK_Bridge`, `MIDI_Mix`, `Keystation_Pro_88`, `Roland_A_PRO`, `AbletonMCP` (Input/Output `"poop"`); **row 7 = `Control Surface="None"`** |
| 2 | Our probe defines no factory | `grep -n create_instance` on the repo file | **exit 1, zero hits.** Class is `AutoStemProbe(ControlSurface)` at `:120`, `__init__(self, c_instance)` at `:122`. Not even an import alias. |
| 2b | The surfaces that DO load all expose the name | `grep -c '^def create_instance'` per installed script | `AbletonMCP` **1** (`:62 def create_instance(c_instance)` → `return AbletonMCP(c_instance)`), `Plugindex` **1**, `Tap` **1**, `LK_Bridge` **0 defs but `:8 from .lk_bridge import create_instance` + `__all__`** |
| 3 | Not a stale install | `sha256sum` both copies | repo `tools/live-probe/AutoStemProbe/__init__.py` and installed `Documents/Ableton/User Library/Remote Scripts/AutoStemProbe/__init__.py` both **`b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`** — the value pinned at `probe-harness.py:106` |
| 4 | The probe opens no socket | `grep -n '^REPORT_PATH'` | `:41 REPORT_PATH = os.path.join(` — it is a **file-report** surface |
| 4b | 9877 belongs to the third-party bridge | `netstat -ano //p TCP` | `TCP 127.0.0.1:9877 0.0.0.0:0 LISTENING **26720**` — Live's own pid, i.e. row 6 `AbletonMCP` |
| 5 | **No lane touched the pinned bytes** | `sha256sum` + `ls -l --time-style` | repo probe `b995063b…` mtime **2026-11-16 15:42:09**; `probe-harness.py` `5de18524…` mtime **2026-11-16 16:24:47**. Both unmoved since yesterday. **Nothing to unwind.** |

The control worth naming: `LK_Bridge` proves a surface can appear in that table with
`Input/Output="None"`, so the table lists surfaces Live **instantiated**. Ours has never appeared
in it, in 8.1 MB of log across every session on this install.

## 2. What this invalidates

- **"The click is unblocked."** False, and it was written by the PM in
  `ACCEPTANCE-pm-2026-11-16.md` §6. The row-7 pick cannot be made, because Live never builds the
  object. Being corrected by the PM in the same file.
- **The load leg has never been verified.** That **widens** the unverified set beyond
  "takeover + refusal". The honest line is now: the offline safety harness is two-witness accepted;
  the probe has never been demonstrated to load in Live; nothing has ever been witnessed against a
  running Live.
- **README §11's "the seam that is already established"** is a seam established through the
  third-party `AbletonMCP` bridge. The one corroborated read-only call proves only that Live's
  in-process Python answers reads **via AbletonMCP**.
- **Every "19 capabilities / 11 writers / script_version 1.7.1" figure** is an observation of that
  bridge's handshake, never of our probe — which has no observable capability list, because it does
  not instantiate.

## 3. Instructions

**Researcher (A1 + A2):**

1. Correct the above as a **DISCOVERED DEFECT dated 2026-11-17**, not as a typo, wherever README /
   `HARNESS.md` / the acceptance records assert the probe is installed **and loadable**, or that the
   row-7 pick is ~15 s away. Grep for the load/pick claims before writing; `README.md:83-90`, §10,
   §11 and §13 are the known clusters.
2. Split three things §11 may currently run together: (a) AbletonMCP's `9877` socket seam, live
   inside the Live pid, unauthenticated, with writer commands; (b) our probe's **file-report** seam,
   never loaded; (c) the harness's four-member allowlist of what our probe **may** call — **moot
   while the probe cannot instantiate**.
3. A1's deliverable gains one item: state what the capability list actually **is**, given the
   installed script cannot be instantiated, and say for each figure whether it was ever an
   observation of our probe or only of the AbletonMCP bridge.
4. Still **do not edit** `tools/live-probe/AutoStemProbe/**` or `probe-harness.py`. Docs only. The
   fix moves pinned bytes and that is the owner's ordering decision (§5), not a lane's initiative.
5. If you have already written to either file, **stop and say so immediately**.

**A3 / A4 (Full-stack):** unaffected, continue. The queue-store, comment, `ErrorKind` and copy work
do not touch the probe.

## 4. Consequence for the plan

- **B2 (the row-7 pick) is dead as briefed.** It cannot precede the factory fix.
- **C3 / task #17 changes character.** It was "two deferred pin-moving fixes" (`:143` arity, harness
  self-attestation), deliberately deferred to *after* the click. The click can no longer happen
  first, so the pin now has to move for a **third** and load-bearing reason: add the module-level
  `create_instance`.
- **The risk of moving the pin is now unavoidable and must be priced.** Those bytes exist in exactly
  **two disk copies** — repo and User Library — and are **untracked** (`?? tools/live-probe/`) until
  D1. Until then, `b995063b…` is one `git clean -fdx`, one disk fault or one User Library rescan
  away from being unreproducible, and every green harness log cites that hash as its subject.
- **A5 is unchanged** in content but its evidence base is now this steer plus the corrected README.

## 5. The ordering decision, put to the owner in the open

The pin must move before any click can be attempted. Reasonable orders, with the PM's
recommendation first:

1. **(Recommended) Commit `tools/live-probe/` as-is now (D1, scoped to that folder), then land the
   `create_instance` fix as its own pin-moving change with the harness re-pinned and the whole exit
   table re-run by a second person.** Rationale: commit first so the old `b995063b…` bytes and all
   fourteen logs become a durable, replayable record *before* they are superseded — otherwise we
   destroy the only witness that the safety harness ever ran green against the shipped probe.
2. Fix first, commit once. Cheaper, but it leaves no durable record of the bytes the entire safety
   argument was measured against.
3. Re-aim the probe at a load test that needs no factory — not available: Live builds surfaces only
   through `create_instance`, so there is no load path without it.

Whatever he picks, the honest product statement in the meantime is unchanged and gets shorter:
**autoSTEM's Live route remains the UI-automation one we have already run, and the LOM seam question
is still open.**

---

# Addendum, 2026-11-17 ~17:45 — what this steer got wrong, and the citations that actually hold

Written by the PM after the owner's second steer, against measurements taken in this session. This
addendum exists because §1 and §4 of the document above **carry a wrong line range and two
base-less numbers**, and I propagated them into a live dispatch. Order of severity, worst first.

## 1. `README.md:894-904` is not the attestation region — it never was

§4 of the Phase A brief (`docs/team-briefs/PHASE-A-2026-11-17.md:42`) points at `:894-904` for "the
harness still does not attest its own bytes (`… stays as-is`)". I verified the two *sentences* the
brief quotes, did not verify the *range*, and repeated `:894-904` here and in the dispatch. Reading
the file: `:889-908` is the **single-witness supersession** bullet (exit 6/6/7, derived-copy hashes,
"seven legs"), and the self-attestation statement is the bullet that **starts at `:957`** and runs to
`:975`. The Phase A brief's own citation is wrong, and my copy of it was wrong a second time.

Current carriers of the "deferred to after the click / post-click batch / do not improve the harness
before the row-7 pick" instruction — the text a reader acts on, so these are what the G3 doc pass
must re-point, and they are recorded in `G-BATCH-2026-11-17-probe-loadable.md`:

| phrase | line, measured 2026-11-17 ~17:40 |
|---|---|
| bullet start "Why the acceptance *record*, not the logs, is the attestation" | `README.md:957` |
| "deliberately deferred to after the click" | `README.md:973` |
| "Do not 'improve' the harness before the row-7 pick" | `README.md:975` |
| "queued for the post-click batch" | `README.md:1092` |
| PM's own deferral sentence | `ACCEPTANCE-pm-2026-11-16.md:87` — already re-pointed to G4 |

These numbers are a **timestamped snapshot, not a contract**: A2 landed while I was measuring, and
`README.md` went 86,029 B / `5beff937…` / 2026-11-16 17:26 → **97,460 B / `9aa805d5…` / 1,119 lines /
2026-11-17 17:02**, moving all four carriers by **+2 lines** between my two readings inside one hour.
A lane cannot hold stable line numbers into a file another lane is editing — grep the phrase, then
cite.

## 2. "33 files" had no base, and the correction I first wrote had a *different* wrong base

The deferral-carrier count was narrated as "33 files". No base makes it true. My first correction
said "**32 files inside `tools/live-probe/`**, 35 repo-wide" and read as if that were the folder's
file census. It is not — it is the number of files **containing the old hash**. Re-measured, each
figure with its base and its command:

| figure | base | command shape |
|---|---|---|
| **42** | payload files in `tools/live-probe/`, excluding `__pycache__` (= exactly what G0 froze) | `find … ! -path "*__pycache__*" \| wc -l` |
| **43** | same, including `AutoStemProbe/__pycache__/__init__.cpython-312.pyc` | `find … \| wc -l` |
| **32** | files **inside the folder containing `b995063b`** | `grep -rl b995063b tools/live-probe \| wc -l` |
| **35** | same, repo-wide: 32 + `docs/team-briefs/PHASE-A-2026-11-17.md`, `-steer-1.md`, `G-BATCH-…md` | `grep -rl … . --exclude-dir=.git \| wc -l` |
| **28** | the immutable evidence set = 32 − the 4 changeable files (`README.md`, `HARNESS.md`, `ACCEPTANCE-pm-2026-11-16.md`, `probe-harness.py`) | enumeration printed beside the count |
| **21** | old-hash **occurrences** in those 4 files, prefix pattern | `grep -o b995063b … \| wc -l` per file: 8 + 3 + 8 + 2 |
| **10** | the same 4 files, **full 64-char** pattern | 2 + 2 + 4 + 2 |

The pattern length changes the answer by more than 2×, and the prefix additionally matches a **path**
— `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/`, which G0 deliberately wrote into the
record. So the gate must name its pattern beside its count, and check (b) uses the full new hash.

## 3. The gate I specified was unsatisfiable, and the corrected form is two checks

I told the lane: post-state, "**no occurrence of the old hash**". 28 of the 37 protected evidence
files quote `b995063b…` as the bytes their runs used and are forbidden ever to change, so that gate is
red forever — or, worse, satisfiable only by scrubbing a witness. Correct form, written as two
separate checks with separate exit statuses in `G-BATCH-2026-11-17-probe-loadable.md` §G3-gate:
**(a)** old-hash occurrences *outside* the 28 hash-carrying evidence files drop to a stated, non-zero
residual, per-file with the lines listed; **(b)** new-hash occurrences outside the changed set equal
zero — that one is legitimately a zero.

Worst of all, my **first** version of the fix was still the same defect in a subtler dress: "files
containing the old hash **outside the immutable set = 0**". Zero is wrong at both ends, because after a
correct batch the four changeable files *must* still quote the old value — that is what a
"superseded 2026-11-17" annotation is. See `ACCEPTANCE-pm-2026-11-16.md` §11 item 3.

## 4. The census of a prose record moves when you write the census

`ACCEPTANCE-pm-2026-11-16.md` §9 records "per prose file: README 8, HARNESS 3, ACCEPTANCE **6**".
Re-measured after §9 landed, its own file is **8**; after the §11 correction section landed on top of
it, **10**. The increments are the lines that quote the hash while reporting how many times the hash
is quoted. It is not drift and it is not someone else's edit: **any count of a document that records
counts is a snapshot taken before the edit it describes.** The honest form is therefore "N, as
measured before the paragraph that states N", never "this file contains N". Timestamp it, or the next
reader logs a defect that does not exist — and note this very section is one more of those lines.

## 5. Count corrections to §4 of this steer

- §4 said C3/#17 was "two deferred pin-moving fixes" and that the factory made it a **third** reason.
  Confirmed, and now named: the batch is **G1** (factory), **G1.5** (offline instantiation gate, red
  first against the current bytes), **G2** (re-pin `probe-harness.py:35` **and** `:106`), **G3**
  (independent table re-run by the Test Engineer, logs in the repo). The `:143` arity fix is folded
  into G1.5's gate, not deferred.
- The **harness self-attestation** fix is now its own batch, **G4**, triggered *after the G3 table is
  re-accepted* — not "after the click", which is no longer a step that can happen first. Task #17's
  wording and the four README carriers in the table above are what G4 supersedes.
- **A3 is three changes, not four.** Item 4 (the "unguarded click" note) is a non-finding: the two
  candidate sites are a by-design `PostKey` statement at
  `src/StemForge.Core/Live/Automation/InputSimulator.cs:19` and subjunctive prose beside
  `GuardedInput.Click` at `src/StemForge.Core/Live/LiveAutomationRunner.cs:610`. Recorded in
  `ACCEPTANCE-pm-2026-11-16.md` §8 and in the Phase A return.
- G0's snapshot is **verified** (owner independently re-ran it): 42 payload files, manifest sha256
  `4292f6a1…`, `sha256sum -c` exit 0 with 42 `: OK` lines, 0 CR bytes, 0 blank lines, empty stderr.
  The ordering question in §5 above is therefore **moot in our favour** — the old bytes and all
  fourteen top-level logs (seven author, seven PM second-witness, per §6) are durable outside the repo
  already, so D1 no longer carries evidence risk and remains the owner's call on **scope and timing
  only**.

## 6. The partition restated once more, because §2's "28" was still a hash census doing a touch ban's job

Enumerating all 42 payload files under `tools/live-probe/` (excluding `__pycache__`): this batch may
write **exactly 5** — `AutoStemProbe/__init__.py` (G1), `probe-harness.py` (G2, two lines), and the
three prose records `README.md`, `HARNESS.md`, `ACCEPTANCE-pm-2026-11-16.md`. The remaining **37 are
protected evidence and must not change**, and only **28 of those 37 contain the old hash** — 9
legitimately do not: `selfcheck-A.log`, the three derived `AutoStemProbe/__init__.py` copies,
`replay-diff.py`, `seam-read.out`, `modal-no-mouse.ps1`, `seam-read.ps1`,
`probe-reaim-2026-11-16.diff`. 28 = 14 top-level logs (7 `probe-harness-*` author + 7
`pm-acceptance-*` PM second-witness; "14 author logs" was wrong wherever it appears in my records) +
14 of the 20 files under `acceptance-2p-1656/` (= 10 logs + `report.md` + `replay-diff.py` +
`seam-read.out` + 7 derived scratch copies).

So the two lists are different tools and must not be substituted for one another: `grep -rl` builds
check (a)'s **count**, the 37-file enumeration builds the **touch ban**, and the ban is proved
deterministically with `cmp` against G0 — measured by the PM on 2026-11-17 before the batch started:
**`identical=37 differs=0 missing_in_G0=0`**. If the "must not change" set had been generated from the
hash census, those 9 witnesses would have been left unguarded, which is precisely how a scrub becomes
legal by accident.

## 7. What the lanes were told, and what is still open

Researcher: A2 landed as described above; A1 continues, disk-only, now also corrected by this steer —
the "19 capabilities / 11 writers / `script_version 1.7.1`" figures are observations of the
**third-party AbletonMCP bridge**, never of our probe, and our probe has no observable capability
list because it does not instantiate. Full-stack: A3 in flight, G-batch queued behind it, then A4.
Reviewer: A5 after A3+A4+G-batch with the exact diff named. Test Engineer: G3 after the new bytes.
Owner-blocked and unchanged: **D1 commit scope**, **Live restart timing**, **the install itself**
(that is three gates, not two), the throwaway-set live run, and the standing product decisions.
