# Re-pin acceptance — probe bytes `f34292cf…`, 2026-11-18

This folder is the evidence for G1→G2→G3: the probe was fixed so Live can load it, the harness's
pin was moved to the new bytes, and the whole exit table was re-run against them.

Nothing here has been committed. Nothing here proves Live loads the surface — see *What this does
not prove*.

## 1. The bytes

| file | sha256 | status |
|---|---|---|
| `AutoStemProbe/__init__.py` | `f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29` | **current** — 550 lines, 26,112 B, LF-only |
| `AutoStemProbe/__init__.py` | `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` | superseded — 545 lines, 25,992 B; **Live could never load these** |
| `probe-harness.py` | `a5e991884f508209801581928e033a27184cf9886bcc6ae2c7951ff0514d3d7c` | **current** — 1,020 lines, 49,264 B; pin moved, two sites |
| `probe-harness.py` | `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1` | superseded — 1,016 lines, 48,884 B; pinned the unloadable bytes |

The old probe bytes still exist and are still verifiable: the G0 snapshot at
`D:/Projects/tools/autostem-pre-fix-b995063b/live-probe/` — manifest `4292f6a16eb741d4d6011b56cc1a0642aa03c22b25ff88a1b78c28663d08ccf3`
covering **42 files, 42 verified ok, 0 missing, 0 mismatched** (`12-g0-snapshot-verify.log`,
re-runnable via `verify-g0.py`). The folder holds 43 files because the manifest counts itself not at
all; 45 counting the two verification outputs beside it.

### This folder has never been in git

`tools/live-probe/` is untracked, not ignored — `git check-ignore` says so and `git ls-files tools/`
returns only `StemForge.Cli` and `capture-window.ps1`. `git cat-file -e
HEAD:tools/live-probe/AutoStemProbe/__init__.py` fails: the probe, the harness, README, HARNESS, the
acceptance records and every log in this folder exist **only as working-tree files**.

Two consequences the owner should weigh at D1, not at some later incident:
1. Without this batch being committed, the pre-fix bytes survive in exactly one place — an
   out-of-repo folder that nothing in the product protects. That is now the *only* witness for the
   claim every superseded log rests on.
2. Conversely, `b995063b…` was never in history, so committing `f34292cf…` does not put a bad blob
   in the record and creates no cleanup obligation.

Two changes to the probe, both required:
1. a module-level `create_instance(c_instance)` factory — Live instantiates a Remote Script only
   through this name, so without it the surface never appears in *Remote Control* dropdowns;
2. `self._call(app, "get_major_version")` / `self._call(app, "get_minor_version")` as two calls
   instead of passing `"get_minor_version"` as an *argument* to a zero-arg accessor. The old shape
   raised `TypeError`, which `_call` swallowed into an `"error: …"` string, so the version field was
   silently wrong on every run.

## 2. The exit table, re-run on the new bytes

`01-table-index.txt` is the machine-written summary. Ten legs, ten matches.

| leg | command | expect | got | log |
|---|---|---|---|---|
| green | `probe-harness.py` | 0 | 0 | `02-green-plain.log` |
| help | `--help` | 0 | 0 | `03-help.log` |
| mutation | `--mutation prompt-bind` | 3 | 3 | `04-mutation-prompt-bind.log` |
| mutation | `--mutation conversions-flag` | 4 | 4 | `05-mutation-conversions.log` |
| mutation | `--mutation command-blind` | 5 | 5 | `06-mutation-command-blind.log` |
| mutations | `--mutations` | 3 | 3 | `07-mutations-all.log` |
| pin-drift | drifted probe, unedited harness | 2 | 2 | `08-pin-drift.log` |
| self-check A | probe source absent | 6 | 6 | `09-selfcheck-A.log` |
| self-check B | `FORBIDDEN_METHODS` emptied | 6 | 6 | `10-selfcheck-B.log` |
| self-check C | wrong mutation expectation | 7 | 7 | `11-selfcheck-C.log` |

`00-pin-drift-before-repin.log` is the pre-G2 witness: the harness at its *old* pin, run against
the *new* probe, so the gate redded on the drift before anyone moved the number. Read its last line
with care — the `0` there is the exit status of the shell's `echo`, not of the harness, whose verdict
is the line above it: `RED [exit 2 PIN_MISMATCH]`. Legs 02–11 take their codes from `subprocess.run`
for exactly that reason, which is also why `01-table-index.txt` is trustworthy in a way that log is not.

The three mutation legs each print a green control line first, then their red. `--mutations`
produces three distinct codes (3, 4, 5) in one run. That distinctness is the proof that the
factory insertion did not break the anchors the mutations splice into: a dead anchor raises
`HarnessInvalid` (6), not the documented code.

Derived copies (legs 08–11) ran in `_derived/`, each harness hash recorded in its own log, then
the folder was deleted and re-proved absent. No neutered harness survives as a runnable file.
(`grep -rl "FORBIDDEN_METHODS = set()" .` returns only `run-derived-legs.py`, which holds the
replacement as a *literal* it splices into a temp copy — it is a driver, not a harness.) The shipped
bytes were re-hashed after the run: unchanged.

## 3. The defect this table was blind to

**The harness cannot see whether Live can load the probe, and never could.** It references
`create_instance` zero times — `grep -c` returns 1, and that one hit is the comment added in §1.
It builds the surface at `probe-harness.py:625`, `probe = probe_module.AutoStemProbe(CInstance())`,
constructing the class directly and so bypassing the only entry point Live uses.

Consequence, stated plainly: **every green run recorded before 2026-11-18, including the 16:41
re-run and the second reader's set, was green on bytes that could not be loaded.** The proof was
sound and its subject was a probe that cannot run. This is why the row-7 pick in Phase B was
impossible, and why "15 seconds, no restart" was false.

The two gates now cover different halves:

| gate | proves | cannot prove |
|---|---|---|
| `probe-harness.py` | the four-member call contract, the eight shapes, the answers | that Live can instantiate the surface |
| `instantiation-gate.py` | that the module imports, exposes `create_instance`, returns a surface, arms `schedule_message(40, _run_probe)`, and never writes the real verdict file | what Live actually does with it |

`instantiation-gate.py` runs against a `--probe-dir`, so red is demonstrable on the pre-fix bytes
without touching the working tree. `instantiation-gate-red-final.log` (exit 1, fails at `S2
module defines create_instance`) is red against `b995063b…`; `instantiation-gate-green.log` is 9/9
against `f34292cf…`.

## 4. G2's self-attestation consequence

Editing the harness moved the bytes that every log describes. The shipped harness prints its own
**path** in the preamble, not its own hash, so **no log produced by it can prove which harness
produced it.** `a5e99188…` is therefore attested only by this file and by the hashes in the `00`
and `08`–`11` logs — which are printed by my runner, not by the harness.

The pre-existing logs are not invalidated, but they are now **about a different harness** than the
one in this folder. They stay valid against `b995063b…`, whose bytes are held by the G0 snapshot.

The fix is G4: have the harness print its own sha256 in the preamble. It was deferred *past the
click* because moving those bytes would invalidate the evidence the click decision rested on. That
reason is now spent — the click could never have used those bytes, and this batch already moved
them. **G4 should no longer be deferred**; it is the only thing that closes §4 for future runs.

## 5. Witness status — read this before quoting any of it

All ten legs were run by the same pair of hands that wrote the fix. The standing instruction for
G3 was *"by a second pair of hands"*. That instruction is **not satisfied.** Every leg here is
single-witness.

The repo's own rule applies: a green table produced by the author of the change it certifies is
not an acceptance. It is a report, and it is reproducible — `bash
acceptance-repin-f34292cf/run-table.sh` and `py -3.11 -B
acceptance-repin-f34292cf/run-derived-legs.py` re-run all ten. Expect differences only in
timestamps and in the random `tempfile.mkdtemp()` folder name inside each log.

## 6. What this does not prove

- **That Live loads the surface.** Not tested; requires installing the new bytes into
  `Documents\Ableton\User Library\Remote Scripts\AutoStemProbe\` and restarting Live. The installed
  copy is still `b995063b…`, deliberately — the install is not authorised.
- **That the LOM seam exists.** `live-probe.json` has never been produced by a live run.
- **That the C# suite is unaffected.** It is: the probe is Python and `dotnet test` never imports
  it. Last full green was 364/364 at `docs/team-briefs/suite-check-2026-11-18T0927.log`, which
  predates this batch.
- **Anything about Phase B/C readiness beyond the code being loadable.**
