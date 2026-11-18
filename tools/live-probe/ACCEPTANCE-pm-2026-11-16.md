# Second-party acceptance of `probe-harness.py` — 2026-11-16

Written by the Project Manager, in its own lane, because the first independent-acceptance pass
returned numbers with **no evidence on disk**: the six files that pass named (`reviewer-*.log`)
exist nowhere in the repo or in `%TEMP%`, and `git worktree list` shows one worktree. Until this
record, the only person who had ever run the harness was the person who wrote it, which is
attestation, not acceptance.

Every number below was measured, not copied. The seven engineer logs were left untouched.

## 1. Bytes this acceptance is about

Read from disk immediately before and after the run, in `tools/live-probe/`:

| file | bytes | mtime | sha256 |
| --- | --- | --- | --- |
| `probe-harness.py` | 48,884 | 16:24:47 | `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1` |
| `AutoStemProbe/__init__.py` | 25,992 | 15:42:09 | `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` |
| `HARNESS.md` | 25,102 | 16:45:04 | `c32dd51083c76cd0f631076c59d3aac13fae19dd4ed942014e64b0e7c8f51a2e` |
| `README.md` | 73,785 | 16:33:22 | `2933aff94cc4ebb006436d6119218ba1ac02263c47b1c44f61a95c5db9bd938f` |

The probe hash equals the pin in the harness header, so the green leg below is a proof about the
same bytes that are installed in the owner's Live User Library (repo copy and installed copy were
verified byte-identical earlier at 15:42:09 / 15:44:15). Interpreter: `py -3.11` → CPython 3.11.9
(`C:\Users\Sebastien\AppData\Local\Python\pythoncore-3.11-64\python.exe`), always with `-B`.

## 2. Commands and measured exit codes

Run from `D:\Projects\skeer-audio\autostem\tools\live-probe`, stdout+stderr redirected to the named
log, exit status taken from the process itself (no pipes):

| command | expected | **measured** | log |
| --- | --- | --- | --- |
| `py -3.11 -B probe-harness.py` | 0 | **0** | `pm-acceptance-green.log` (13,741 B) |
| `… --mutation prompt-bind` | 3 | **3** | `pm-acceptance-prompt-bind.log` (26,600 B) |
| `… --mutation conversions-flag` | 4 | **4** | `pm-acceptance-conversions-flag.log` (25,550 B) |
| `… --mutation command-blind` | 5 | **5** | `pm-acceptance-command-blind.log` (25,325 B) |
| `… --mutations` (all three legs) | 3 | **3** | `pm-acceptance-mutations-all.log` (49,003 B) |
| `… --help` | 0 | **0** | `pm-acceptance-help.log` (6,386 B) |
| harness copied to `%TEMP%` with a one-line-drifted probe copy beside it | 2 | **2** | `pm-acceptance-pin-drift.log` (963 B) |

The pin-drift leg names both hashes in its own output: pin
`b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`, bytes on disk
`daac96cffc0551e5694758b7ebceac93ad22b827bb4ae85822e4f211afea8e9f`, verdict line reading
`RED [exit 2 PIN_MISMATCH] … so this proof is not about the file the owner would click`. That
sentence is the whole reason the pin exists, and it now has a second witness.

Log hashes (sha256, LF-only, `cr=0` in each; these logs are raw harness output with **no**
hand-added header, so nothing in them is my prose):

```
4673d2abe8077bbc8773d3f0fa6626b3ac17efcf96a928704c09bf88f0d69c67  pm-acceptance-green.log
181b2ef555e75dbb297b26aec31332a789e8035b0cbf1e57e3f1796efecb3a64  pm-acceptance-prompt-bind.log
b3db5b7b89582d310ba6f237a5d3bc85dafac8a1d9bafbec0d16c7769dfe1a65  pm-acceptance-conversions-flag.log
835882aec20587ae9ac4bbb1a3a18ab751c0e867be89e28edc2df5a84f686872  pm-acceptance-command-blind.log
606ec8fccf99df51dbfbfdaca452510682bf0057579f9c90e01684f06ae8d081  pm-acceptance-mutations-all.log
3a945d51687bc51c32a00a1254bcac1e18737941a674305d3de66676ec7b4bf3  pm-acceptance-help.log
068fbc9706be24995d5c0b3dde7a34aa8dfe435cdc38f797a3d774f249dcaf6e  pm-acceptance-pin-drift.log
```

## 3. Replay equivalence of the green leg — the engineer's replay claim, now independently checked

`diff pm-acceptance-green.log probe-harness-green.log` reports 12 changed lines out of 119 / 129:

* 10 lines are the PROVENANCE block the engineer added by hand to their log (command, cwd,
  interpreter, harness hash, probe hash, separator note, exit code, expectation, and the
  `HARNESS OUTPUT VERBATIM BELOW` marker). It is not produced by the harness.
* 1 pair is the scratch directory name, which the harness takes from `tempfile.mkdtemp()`
  (`…\autostem-probe-harness-4rtvi6ja\…` in theirs, `…-q5qardya\…` in mine).

Every other byte of the harness's own output matches. So the recorded green log is a faithful
capture of `5de18524…` running against `b995063b…`, and the earlier "replayed, body matched except
the random temp name" statement is now corroborated by a second machine-state and a second run.

## 4. What this acceptance does **not** cover

* **Corrected at 17:40 (superseded by §7): the self-check legs are no longer single-witness.**
  `--help` exposes only `--mutation NAME | --mutations | --help`; there is **no `--selfcheck`
  switch** - that flag was my invention in the brief, and is a defect in the brief, not in the
  harness. The second party therefore reached the same three legs by re-inducing them in `%TEMP%`
  scratch copies, one line each, and all three derived-copy hashes reproduced the recorded values
  exactly (`2e8c969c…`, `200aaa45…`; leg A's copy is the unmodified harness `5de18524…`, reddened by
  the missing probe source instead). See §7 for the logs, now held in this folder.
* **The harness still does not attest its own bytes.** It prints the *probe's* sha256 and nothing
  about itself, so no log — mine included — can prove from inside which harness produced it. This
  record is the external substitute for all eight logs, and the real closure is the two-line fix
  already deferred — **re-pointed 2026-11-17 from "the post-click batch", which is now an
  unreachable trigger, to batch G4, the batch that runs after the G3 table is re-accepted (§9)**:
  moving those bytes now would invalidate the green run
  the click decision rests on.
* **Nothing here touches Live.** No socket was opened, no window was touched, no process was
  started or stopped, no `.pyc` was written beside anything, and
  `%LOCALAPPDATA%\SkeerAutoSTEM\` still contains no `live-probe.json`. **The enumeration that
  claimed this folder held only `logs/`, `queue.json` and two backups was itself incomplete** -
  re-derived at 17:40, it holds nine entries: `logs/`, `recovered/`, `work/`, `queue.json`,
  `queue.json.bak-041202`, `queue.json.bak-043140`, `settings.json`, `startup.log`,
  `ui-profile.json`. The load-bearing negative is unaffected: **no `live-probe.json`**. The row-7
  remote-script pick remains the owner's one action, and it remains the only route to the
  `Live.Conversions` answer.
* `HARNESS.md` and `README.md` were not edited by this lane.

## 5. Disposition of the three claims from the first acceptance pass

The returned report asserted a blocking documentation defect. It is refuted against the shipped
bytes, so **no README change was made and the click is not held by it**:

1. *"§6's third pre-belief check — `It is corroborated by ≥ 2 modules or holders` — is
   unsatisfiable."* That check does not exist.
   `grep -rn "modules or holders\|corroborated by" README.md HARNESS.md` → **zero hits**. The
   pre-belief list is `README.md:268` — *"Four checks to run before you believe a `yes`"* — and its
   third bullet, `:277-281`, already states the trap that claim was built on, including that
   `found[].script_facing` is set `true` unconditionally by `_resolve_target` (`:285`) and "cannot
   make this judgement for you". Section 6 begins at `:320` and is *"Removal — the one-command
   route"*; `:252-258` is item 4 of section 5's reading order.
2. *"`corroborating_only` is always `[]`, computed from `len(found) > 1`."*
   `AutoStemProbe/__init__.py:163-168` assigns a hard-coded four-element list of stage keys
   (`stages.1_api_surface`, `2_menu_info`, `3_prompt`, `4_prompt_names`) and `README:249-251`
   describes exactly that. There is no `corroborated_by` field and no derived-empty-list.
3. *"HARNESS.md:294 restates the three-check rule."* `HARNESS.md:288-300` is the
   *"`policy.read_only` is a claim, not a measurement"* block followed by the disclosed
   `AutoStemProbe/__init__.py:143` defect. No such restatement exists.

Also corrected: the reason the engineer's pin-drift log carries `SECTION 2` is recorded in that
log itself at `:20-30` — SECTION 1 was superseded **for provenance** (it was launched from a copy
that did not tie itself to the shipped harness), with exit 2 in both sections. It was not an
untested assertion.

## 6. Conclusion

The offline safety proof is now accepted by a second pair of hands for the six legs the harness
exposes plus the pin-drift leg, on the exact bytes that are installed in the owner's Live, with the
logs and hashes above. `decided_by` is a constant and cannot disagree with `answer`; the answer is
computed only from stage 0. Nothing in this record changes the recommendation:

**The click is unblocked.** Pick row 7 of Settings → Link/Debug → Remote Control Surfaces →
`AutoStemProbe`, no restart, wait ~15 s, read
`%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json` — or have the Team read it — and take the verdict
from `decided_by == "stages.0_lom_seam"` plus the four checks at `README:268-285`.

> **RETRADED 2026-11-17, the paragraph above. It was wrong and the error was mine.** The click is
> **not** available, because **the probe has never been loadable by Live**:
> `AutoStemProbe/__init__.py` defines **no `create_instance` factory** (zero hits for the name in
> either the repo copy or the installed copy, and not even as an import), and Live builds a control
> surface only through that module-level function. Measured 2026-11-17: Live 12.3.2 running as
> `Ableton Live 12 Suite.exe` pid **26720**; `Log.txt` 8,178,082 B with **0** occurrences of
> `AutoStemProbe`; last startup table at line **64896**, `2026-11-17T16:34:10`, showing rows 1-6
> (`Push`, `LK_Bridge`, `MIDI_Mix`, `Keystation_Pro_88`, `Roland_A_PRO`, `AbletonMCP`) and
> **row 7 = `None`**. The three surfaces in that folder that *do* instantiate each expose the name —
> `AbletonMCP/__init__.py:62`, `Plugindex`, `Tap` define it, `LK_Bridge/__init__.py:8` imports it.
> **This is not a stale install**: repo and installed bytes are identical at
> `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`, the value pinned at
> `probe-harness.py:106`, so what the harness proves green is exactly what Live cannot load.
> What survives this record unchanged: the **offline** safety proof of the harness, two-witness, on
> the shipped bytes. What does not survive: any inference that those bytes can reach a running Live.
> Corrected scope of the unverified set, which is now wider than "takeover + refusal": **the probe
> load leg, the takeover path and the refusal path are all unverified**, and the "seam already
> established" in `README` §11 is the **third-party AbletonMCP bridge** — `127.0.0.1:9877` is
> `LISTENING` on pid 26720 — not our probe, which opens no socket and writes a report file
> (`AutoStemProbe/__init__.py:41`). Full evidence: `docs/team-briefs/PHASE-A-2026-11-17-steer-1.md`.

> **RESOLVED IN THE TREE 2026-11-18 09:52, and still blocked on the owner.** The defect retracted
> above is fixed in `AutoStemProbe/__init__.py` — module-level `create_instance(c_instance)` added,
> plus a second bug this record did not know about: `get_minor_version` was being passed as an
> *argument* to `get_major_version`, whose `TypeError` `_call` swallowed into an `"error: …"` string,
> so the version field was silently wrong on every run. The harness pin moved with the bytes
> (`b995063b…` → `f34292cf…`, harness `5de18524…` → `a5e99188…`) and the full ten-leg exit table was
> re-run against them: all ten match. `acceptance-repin-f34292cf/INDEX.md`.
>
> Two things this does **not** buy. **The click is still not available**, because the *installed*
> copy is untouched and still `b995063b…` — installing the new bytes and restarting Live are
> prerequisites, and both are the owner's call. And the re-run is **single-witness**: the instruction
> for this batch was a second pair of hands, and the hands that ran the table wrote the fix. This
> record's two-witness status applies to the **offline safety proof on `b995063b…`**, which stands on
> its own; nothing here extends it to `f34292cf…`.
>
> What this record's retraction also exposed, now worth naming as a finding rather than an error: the
> harness builds the surface at `probe-harness.py:625` with `probe_module.AutoStemProbe(CInstance())`,
> bypassing `create_instance` entirely — zero references in the harness. It could not have caught this,
> and it did not. Loadability now has its own gate, `instantiation-gate.py`, red on the pre-fix bytes
> and 9/9 on the current ones.

Durability caveat, unchanged and owned by the owner's pending decision: `tools/live-probe/` is
**untracked** (`git status --porcelain` → `?? tools/live-probe/`), so none of these eight logs, the
harness, the probe or this record is a durable record until the commit is authorised.

## 7. Addendum, 17:40 — the second witness made durable, one stale claim here corrected, and one
stale claim in README §13

**Why this section exists.** The second party's logs were written to
`%TEMP%\harness-accept-1656\` — my brief demanded zero repo writes, which is the opposite of this
project's rule that evidence lands in the repo. `%TEMP%` is cleaned without asking. So the whole set
is now copied into this folder, byte-for-byte, under `acceptance-2p-1656/` (16 entries: 6 harness
logs, `pin-drift-run.log`, 3 `selfcheck-*.log`, the 3 induction copy folders, `replay-diff.py`,
`seam-read.out`, `report.md`). Hashes verified against the report's own §7 table before and after
the copy — the three self-check legs:

| file | sha256 | bytes | CR lines |
|---|---|---|---|
| `acceptance-2p-1656/selfcheck-A.log` | `00f245f8eab2932c8a0ffb14d8d3bb14c07c360188e6d61303737c0844a96be1` | 728 | 0 |
| `acceptance-2p-1656/selfcheck-B.log` | `1f5e7977d95a871a3438130c657eae8558cd4e0544b311b25fe22fd9157bd437` | 1,511 | 0 |
| `acceptance-2p-1656/selfcheck-C.log` | `a85cabf84f5770e5f15c4891281cd03d2a781ec79db3b16a9e3aacda9174fd79` | 26,799 | 0 |
| `acceptance-2p-1656/green.log` | `74a9310648a7076eab4220a169e70830d4336fc2394d2b2de4bcae501dd3af7f` | — | 0 |
| `acceptance-2p-1656/pin-drift-run.log` | `0ec5237eb969a01ffa403a6e3806e4c5bc5c40a64142107f91d09e0626a64cfa` | — | 0 |
| `acceptance-2p-1656/report.md` | `22d4ede01c07e09218a4598a733f72838643985336a16a643ddc74a91efa4135` | 30,034 | 429 lines |

**Consequence for the witness split.** Ten of ten exit codes (0 / 3 / 4 / 5 / 3 / 0, pin-drift 2,
self-check 6 / 6 / 7) are now **two-witness with logs on disk in this repository folder**, and the
five replayed legs are byte-identical (`REPLAY_MISMATCHES = 0 of 5`). §4's first bullet is corrected
above for the same reason.

**Stale claim to correct in `README.md` §13**, where the split is stated as
*7 legs / 5 codes two-witness vs 3 legs at 6 / 6 / 7 one-witness*: it was true when written and is
now superseded by the files above. The sentence that does **not** move is the load-bearing one —
**no code at all has ever been witnessed against a running Ableton Live**, and the seam read proves
only that Live's in-process Python answers *reads*.

**PM-side re-derivation of the copy gate (product code, not the harness).** The gate's green count
and one red leg were re-run here at 17:35–17:40 against the tree as it stands, HEAD `5b1c340`:

- full suite, unmodified tree: `dotnet test StemForge.slnx` → **exit 0, 364 passed / 0 failed**
  (`artifacts/pm-verify-test-173x.log`, repeated post-restore as `artifacts/pm-verify-postrestore-green.log`).
- leg C re-induced by the coordinator (all lanes were idle, so no second writer): the refusal
  sentence at `LiveRunCopy.cs:40-42` deleted → `--filter FullyQualifiedName~TakeoverCopyTests` →
  **exit 1, Failed: 1, Passed: 1** (`artifacts/pm-falsifier-legC.log`).
- restore verified **byte-exact**: `LiveRunCopy.cs` sha256 back to
  `3fa702f343132bf4cc3c922c6056943ef0a4f05c2335492e915a52754d5da3e0`, `TakeoverCopyTests.cs`
  `bda5d3d371cf414c603ea263f8e96371b281220198f7e89db8103518572bf962` (unchanged throughout), then
  364/364 green again.
- These three logs sit in gitignored `artifacts/`, so they are a witness, not a record. The record
  for this change is the commit message, and **nothing is committed — commit authorisation is the
  owner's**, with `tools/live-probe/` still `?? untracked` in its entirety.

## 8. G0 pre-fix witness, taken 2026-11-17 15:55 — and the file-ownership claim that goes with it

The order chosen for the probe fix does not put the evidence at the mercy of the commit decision, so
this section is the witness. A **hash-verified snapshot of the exact pre-fix state** now sits
**outside the repository**, where `git clean -fdx` cannot reach it:

- **Location:** `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/`
- **Manifest:** `MANIFEST.sha256`, its own sha256
  `4292f6a16eb741d4d6011b56cc1a0642aa03c22b25ff88a1b78c28663d08ccf3`
- **Verification, exit status owned (no pipe in front of `sha256sum -c`):** `VERIFY_EXIT=0`,
  **42 `: OK` lines against 42 files on disk**, manifest CR bytes `0`, blank lines `0`, stderr `0`
  bytes. Both traps that make `-c` lie were probed, not assumed.
- **The pinned bytes inside it:** `AutoStemProbe/__init__.py` =
  `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5`,
  `probe-harness.py` = `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1`.
  All 14 author logs (7 `probe-harness-*.log` + 7 `pm-acceptance-*.log`), all 16 second-party
  entries **including their four scratch folders**, both acceptance records, `HARNESS.md` and the
  re-aim diff are in the 42.

**Free re-derivation, and it closes a gap §7 left open.** Because the second party's derived copies
travelled with the snapshot, §4/§7's three *recorded* derived hashes are now re-measured from files
rather than quoted from a report: `selfcheck-B/probe-harness.py` = `2e8c969c…`,
`selfcheck-C/probe-harness.py` = `200aaa45…`, `pin-drift/AutoStemProbe/__init__.py` = `a927a63e…`,
and leg A's harness copy is the unmodified `5de18524…` — **all four agree with the report.**

**What the snapshot does not hold, stated rather than left to be discovered.** `__pycache__` was
excluded deliberately (0 `.pyc` files copied), so the untracked `cpython-312.pyc` residue the Test
Engineer flagged exists only in the repo. `README.md` (`5beff937…`) and `HARNESS.md` (`c32dd510…`)
were snapshotted at the pre-lane state, and a running lane is editing `README.md` right now — so if
that edit damages the file, `5beff937…` here is the restore point.

**Consequence for G2, disclosed as exactly what it is.** The harness prints its own *path*, not its
own bytes. The moment we edit it to fix that, **no log can prove which harness ran**, and the pre-fix
harness hash `5de18524…` will be recoverable from **this snapshot and this hand-written claim** — not
from inside the evidence. Anyone auditing the post-fix table should read §6's retraction and this
paragraph together.

**One inference to kill before it becomes repo doctrine.** `LK_Bridge` appears in Live's startup
table as a **built** surface with `Input="None" Output="None"`. So **the absence of MIDI ports is not
evidence of a failed load** — the table lists what Live constructed. If any doc here reads
"no ports, therefore not loaded", it is wrong.

**Ownership, to prevent two writers.** This file (`ACCEPTANCE-pm-2026-11-16.md`) is the
coordinator's; its state at snapshot time was `2c173dcb…` and §6's retraction plus this section are
mine. The `README.md` / `HARNESS.md` corrections belong to the Researcher lane.

**Count correction to the Phase A brief, accepted.** A3 produces **three** changes, not four: both
"unguarded click" candidates are confirmed non-findings (a by-design statement about `PostKey`, and
subjunctive prose beside a `GuardedInput.Click` call), so item 4 closes with no edit. The lane's own
report is the confirmation of record.

## 9. Steer 2 corrections, 2026-11-17 — my partition was short, one count of mine was never measured,
and one gate about to be written was unsatisfiable

All three came from the coordinating session and all three are accepted. Measured again by the PM
before being written here, because the defect being corrected is mine.

**A count I narrated without counting.** The G-batch brief said the old hash appears in "33 files".
There is **no base in which that number is true**. Measured on named bases: **32 files inside
`tools/live-probe/`** (both at the earlier census and now) and **35 repo-wide today**, the difference
being the three coordinator/PM briefs under `docs/team-briefs/` written after the folder census. The
number was an uncounted impression, and the lesson is the one this project keeps re-learning: **name
the base beside the number or two correct counts look like a mis-count.**

**The partition is three classes, not two, and the prose class includes this file.** Occurrences of
`b995063b…` per prose file, measured: `README.md` **8**, `HARNESS.md` **3**,
`ACCEPTANCE-pm-2026-11-16.md` **6** (six stale pin assertions in the record a reader trusts when the
others disagree — that is the one §8 of this file was supposed to protect),
`acceptance-2p-1656/report.md` **7**. The corrected split:

| class | files | rule for the G batch |
|---|---|---|
| **harness code** | `probe-harness.py` — `:35` quoted comment and `:106` `PINNED_SHA256` | **2 lines change** |
| **prose records** | `README.md`, `HARNESS.md`, this file | **re-aim + annotate dated 2026-11-17** (3 files, not 2) |
| **immutable evidence** | **28 files** — 14 author logs, the second-party folder incl. `report.md` and the four derived scratch copies | **must not change, ever** |

32 = 28 immutable + 3 prose + 1 harness code. `AutoStemProbe/__init__.py` itself carries **0**
occurrences, so it is not part of the partition at all.

**The gate that was about to be written unsatisfiable.** G3 must not assert "no occurrence of the old
hash": after a correct batch the old hash **must still appear in those 28 immutable files**, and the
G0 manifest is what guarantees it. The satisfiable form is two separate checks, never merged:
**(a)** files containing `b995063b…` **outside** the immutable-evidence set = **0**; and
**(b)** files containing the **new** hash **outside** the changed set (harness code + 3 prose + the
probe + the new gate/G3 logs) = **0**. One "old gone / new everywhere" assertion fails forever and
then gets quietly relaxed.

**G4 now owns the self-attestation fix.** Its stated trigger is unreachable: the deferral was written
as "after the click", and after this batch the click cannot precede the fix — a doc that defers to an
event that now happens *later* than the thing it gates is a permanent freeze wearing a date. Successor
batch: **G4, after the G3 table is re-accepted.** §4 of this file has been re-pointed accordingly.

**Line-citation correction, and the blame is shared in the right order.** The deferral text is **not**
at `README.md:901-904`. Measured: the self-attestation bullet begins at `README.md:956`, the
unreachable trigger is `:972` and the instruction is `:974`; a second carrier is `README.md:1090`
("queued for the post-click batch"), and a third is this file's `:87`. The `:894-904` range came from
`docs/team-briefs/PHASE-A-2026-11-17.md:42` and **I propagated it into the steer and the live lane
dispatch after verifying only the quoted lines around it** — a region pointer is a relational claim,
and it is the class of claim my own brief-reading habit says to re-derive. Corrected in
`PHASE-A-2026-11-17-steer-1.md`'s addendum; `PHASE-A-2026-11-17.md:42` itself is left as written and
annotated rather than rewritten, since it is another session's record of what it believed.

**The folder is no longer the 42-file set G0 froze, and the delta is explained, not drift.** `find .
-type f` in `tools/live-probe/` = **43** today. G0's manifest covers 42 payload files (excluding its
own `MANIFEST.sha256`). The single extra is
`AutoStemProbe/__pycache__/__init__.cpython-312.pyc`, which G0 excluded deliberately. Diffing the
post-batch folder against 42 and calling the delta corruption would therefore be wrong; the batch
adds `instantiation-gate.py`, its red and green logs, and the G3 logs on top. **Record the listing on
its own line; never reconcile it to 42.**

**Environment note that belongs to A1, recorded here so it is not lost:** the Qoder-side AbletonMCP
connector was connected at 15:43 (37 tools listed) and is **disconnected as of 16:10**, with no change
made by any lane. A wrapper-surface count is therefore a reading of a live configuration, not a
property of the installed script — which is the strongest possible argument for A1's three-layer
separation.

## 10. The owner looked at Live's control-surface list and the script was not in it — and the bytecode cache says why, which is not the reason I had been giving

2026-11-17 ~18:05, PM's own read-only measurements. No socket, no window touched, nothing clicked.

**What he reported:** `AutoStemProbe` does not appear in Live's control-surface list. That is the
first *GUI* witness of defect #22, and it is what the missing factory predicts.

**New physical evidence, which I did not have before** (`__pycache__` in the *installed* folder):

| artifact | pyc magic | interpreter | header `src_size` | header `src_mtime` | vs current source |
|---|---|---|---|---|---|
| `Remote Scripts/AutoStemProbe/__pycache__/__init__.cpython-311.pyc` (32,031 B, written **2026-11-16 19:32:18**) | `a70d0d0a` | **3.11 = Live 12.3's embedded Python** | **25,992** | **2026-11-16 15:44:15** | **exact match** to the installed `__init__.py` (25,992 B, 15:44:15) |
| `Remote Scripts/Tap/__pycache__/__init__.cpython-311.pyc` | `a70d0d0a` | 3.11 | 157 | 2024-03-04 | match — Live compiles every folder it enumerates |
| `tools/live-probe/AutoStemProbe/__pycache__/__init__.cpython-312.pyc` (12,757 B) | `cb0d0d0a` | **3.12 — NOT Live's interpreter** | **10,593** | 2026-11-15 20:25:00 | **stale and foreign**: the repo source is 25,992 B today |

Read carefully, because it cuts against my own earlier framing:

1. **Live reached the installed folder and compiled the current bytes.** So "it isn't installed",
   "wrong path", "Live needs a restart to see it" are all **ruled out** — the install and the scan are
   not the blockers. The only thing standing between the probe and that dropdown is the factory.
2. **A `.pyc` proves compilation, not execution.** CPython writes the cache after `source_to_code`
   succeeds and *before* the module body runs, so this witness says Live compiled our file; it does
   **not** say `import Live` / `from _Framework.ControlSurface import ControlSurface` at `:38-39`
   succeeded. Do not let item 1 slide into "Live loaded it".
3. **My fact 1b was weaker than I presented it.** "0 mentions of `AutoStemProbe` in 8,178,082 bytes of
   log" was offered as evidence Live never built the object. The differential says otherwise: `Tap`
   has a proper `create_instance`, was compiled by Live, and has **0 log mentions too** — log mentions
   track *selection and runtime chatter*, not listing. The 0 still means "never selected, never ran",
   which is what we already knew from row 7 = `None`; it does **not** discriminate a loadable script
   from an unloadable one. The load-bearing evidence for #22 is (i) zero `create_instance` in bytes
   identical to the installed copy, (ii) every surface Live does offer has the name reachable, and
   (iii) now the owner's own eyes on the list. **Corrected in the steer and in #22.**
4. **The repo's 3.12 cache is a trap for the next reader.** A stale `.pyc` from a *different*
   interpreter, built from a *different* source size, sitting in the folder G0 froze. G0 excluded it
   and so must the batch — and it must not be cited as "Live imported the repo copy".
5. **Constraint added to G1.5, and it is not stylistic:** the instantiation gate runs under
   **`py -3.11`**, matching Live 12.3's embedded interpreter (`a7 0d 0d 0a`). Proving the factory
   under 3.12 proves the wrong bytecode path, and the machine demonstrably has a 3.12 that will
   quietly do that for you. The gate must also run with `-B` or clean up its own `__pycache__`, or it
   leaves exactly the artifact in item 4.

Open control, cheap, only he can read it: **does `Tap` appear in the same dropdown?** If yes, the list
filters on the factory and the causal chain closes with a live differential; if no, the list is
filtered by something we have not named and the row-7 assumption needs re-deriving even after G1.
Asked, not assumed.

## 11. §9 was wrong in four places, and the mistake is the same one §9 was written to fix

Re-measured 2026-11-17 ~17:40 by the PM. §9 is kept as written above — it is the record of what I
believed at 17:2x — and these are the corrections:

1. **"32 files inside `tools/live-probe/`" (§9 ¶1) is not a folder census.** It is the number of
   files **containing the old hash**. The folder's own census is **42** payload files / **43** with
   `__pycache__` (which §9 ¶5 gets right). §9's sentence reads as a file count and therefore repeats
   the base error it names. Both are true, only one sentence at a time: 42 files in the folder, 32 of
   them carrying the hash, 28 of those immutable, 4 changeable.
2. **"this file = 6 occurrences" (§9 table) has moved 6 → 8 → 10 → 12, and it will move again.**
   `grep -o b995063b` on this file gave 6 when §9 measured it, 8 after §9's own lines landed, 10 after
   the first drafting of §11, and **12 measured at 2026-11-18 09:29:23 with the instrument printed
   beside it (`grep -o 'b995063b' … | wc -l`; the same file gives **4** for the full 64-char value)**.
   Each increment is a line quoting the value it is counting. Confirmed cause, not drift — §9 ¶4's
   lesson, applied to me two sections later. Two rules come out of it, and they belong on the same
   line as the number: **publish a census of a file that records censuses with its timestamp and
   instrument**, and **never state the post-edit figure in past tense before the edit has landed.**
   The figure a reader measures after this item lands will be larger than 12, because this item quotes
   the string it is counting; that is the defect named here, not someone else's edit.
3. **§9's check (a) is the unsatisfiable gate again, in a subtler dress.** I wrote "files containing
   the old hash outside the immutable set = **0**". After a correct batch the four changeable files
   **must** still quote the old value — that is what an annotation saying "superseded by `create_instance`,
   dated 2026-11-17" *is*. Zero would demand that the records stop naming the bytes they replaced.
   The coordinator's form is the right one and is what the G-brief now ships: **(a)** old-hash
   *occurrences* outside the immutable set drop to a **stated, non-zero residual**, printed per file
   with the lines beside it; **(b)** new-hash occurrences outside the changed set = **0**, and that
   one genuinely is zero. Pre-state residual for (a), measured this run: 8 + 3 + 8 + 2 = **21
   occurrences** with the prefix pattern, **10** with the full 64-char pattern.
4. **The pattern is part of the measurement, not a detail.** The prefix also matches a **path** —
   `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/`, deliberately written into §8 by G0 — so a
   prefix grep counts a folder name as a hash citation. The split in this file is measured, not
   asserted: at 2026-11-18 09:29:23 the prefix gave **12** occurrences, of which **3 are the path**
   (§8's location line, item 4's own example below, and item 6's `cmp` witness) and **9 are hash
   citations** — so a prefix gate over this file reports a figure that is part hash, part folder name.
   Carrier-file counts by pattern: prefix **32**
   in the folder / **35** repo-wide, full hash **32** / **34**; the single prefix-only carrier
   repo-wide is `docs/team-briefs/PHASE-A-2026-11-17.md`, which quotes the short form. Gate (b) must
   use the full new hash; gate (a) must print its pattern next to its count.
5. **All four `README.md` line citations in §9 ¶4 moved by +2 while I was writing §9.** A2 landed at
   17:02 — README 86,029 B / `5beff937…` → **97,460 B / `9aa805d5…`, 1,119 lines** — so
   `:956/:972/:974/:1090` are now **`:957/:973/:975/:1092`** (`nl -ba` reading of the exact lines, this
   run). §9's numbers were true when measured and are stale now; the durable statement is the
   **phrase**, and a lane editing a file another lane cites cannot be expected to hold line numbers
   still. Re-pointed, with the same warning, in `G-BATCH-2026-11-17-probe-loadable.md` §G3-pass and in
   the steer-1 addendum.
6. **"28 immutable evidence files" is a hash census, not the touch ban — and I had them doing the same
   job in §9.** Enumerating the folder's 42 payload files: 3 prose + 1 harness + 1 probe leaves
   **37 protected evidence files**, of which exactly **28 contain the old hash** and **9 legitimately
   do not** (`selfcheck-A.log` — leg A has no probe on disk to hash; the three derived
   `AutoStemProbe/__init__.py` copies; `replay-diff.py`; `seam-read.out`; `modal-no-mouse.ps1`;
   `seam-read.ps1`; `probe-reaim-2026-11-16.diff`). 28 = 14 top-level logs (**7 `probe-harness-*`
   author + 7 `pm-acceptance-*` PM second-witness — so "14 author logs", as §9 and two briefs said, is
   wrong**) + 14 of the 20 under `acceptance-2p-1656/`. The consequence is not pedantic: if the
   "must not change" list is generated from the hash census, the 9 silent witnesses are left
   unprotected while the gate counts them. The G-brief now separates the two lists, and its
   `acceptance-2p-1656/` breakdown is corrected to **10 logs + `report.md` + `replay-diff.py` +
   `seam-read.out` + 7 derived scratch copies = 20** (I first wrote 8 derived copies and corrected it
   by counting, not by arithmetic).

   **And the ban is proved with `cmp`, not with a grep.** Witness taken by the PM 2026-11-17, before
   the G batch starts: every one of the 37 protected files compared byte-for-byte against the G0
   snapshot at `D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/` —
   **`identical=37 differs=0 missing_in_G0=0`**. Nothing has touched a witness, and that is the form
   the end-of-batch check takes, because it does not care whether a file contains a hash. The pinned
   bytes themselves re-confirmed in the same run: `probe-harness.py` `5de1852479f74b6f…` and
   `AutoStemProbe/__init__.py` `b995063b665eb170…`, both still at their 2026-11-16 mtimes.

Nothing in this section changes a pinned byte, a log body, or any of the 37 protected evidence files.
This file is mine to write; writing it moved its own count, which is disclosed above rather than
smoothed over.

