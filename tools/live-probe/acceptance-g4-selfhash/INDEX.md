# G4 — the exit table on the self-attesting harness

The fix G4 owns: `probe-harness.py` now prints **its own sha256** in the preamble, next to the
probe's. Every log produced from harness `1d59b88f…` therefore self-attests to the bytes that
wrote it, and the replay-index workaround that HARNESS.md's log table exists for is no longer
needed for new runs.

## The bytes this table is about

| | sha256 |
|---|---|
| probe (`AutoStemProbe/__init__.py`) | `f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29` |
| harness (`probe-harness.py`) | `1d59b88fd8cf80b571ed35d1e12c3b14126b0c313c9c70578d284a6a34fd5bb9` |

The harness hash is not only stated here: every leg log's own preamble prints
`harness sha256 1d59b88f…` from inside the run. That is the difference from the G3 table, whose
logs could only name their harness path and needed an external replay to bind output to bytes.

## The legs

Ran 2026-11-19, Python 3.11.9, from `tools/live-probe`. Expectations are the same ten as G3.

| leg | command | expect | got | |
|---|---|---|---|---|
| 02 | `py -3.11 -B probe-harness.py` | 0 | 0 | MATCH |
| 03 | `… --help` | 0 | 0 | MATCH |
| 04 | `… --mutation prompt-bind` | 3 | 3 | MATCH |
| 05 | `… --mutation conversions-flag` | 4 | 4 | MATCH |
| 06 | `… --mutation command-blind` | 5 | 5 | MATCH |
| 07 | `… --mutations` | 3 | 3 | MATCH |
| 08 | derived copy, probe drifted by one comment line | 2 | 2 | MATCH |
| 09 | derived copy, probe source removed | 6 | 6 | MATCH |
| 10 | derived copy, `FORBIDDEN_METHODS` emptied | 6 | 6 | MATCH |
| 11 | derived copy, wrong mutation expectation | 7 | 7 | MATCH |
| 12 | `verify-g0.py` (pre-fix snapshot manifest) | 42/42 ok | 42/42 ok | MATCH |
| 13 | checkout-index EOL proof (staged blobs re-hashed) | harness `1d59b88f…` CR=0, probe `f34292cf…` CR=0 | all six checks True | MATCH |

Legs 08–11 ran derived copies in `_derived/`, each copy hashed, run, deleted, and proved deleted
(`derived-legs-stdout.log` holds the runner's own stdout). Legs 02–07 ran from the shipped folder
and mutated nothing on disk: the harness splices mutations into the in-memory source it compiles
the mutant from, and says so in its own preamble. Leg 13 (`13-eol-proof-g4.log`) extracts the
staged blobs through `git checkout-index` and proves a fresh clone smudges both files to exactly
the bytes above — the self-hash the harness prints is clone-stable, not an accident of this
working tree.

## What this table supersedes

This table **supersedes** the G3 ten-leg table in `acceptance-repin-f34292cf/` for the current
bytes. That folder is left byte-identical as history: it records harness `a5e99188…` (no
self-attestation) exercising probe `f34292cf…`, and its legs are the same ten with the same
expectations — every leg above matches what G3 recorded, now from a harness that names itself.

## Second-person verification

**Done, 2026-11-19.** An independent agent re-ran the table from the committed bytes and
confirmed the reproduction. Method: `git archive HEAD tools/live-probe` extracted into an empty
scratch directory (the worktree was never consulted, the repo untouched); run-table.sh was
**not** executed — the agent invoked the legs directly to avoid its hardcoded `cd` back into the
real repo. Results: scratch harness/probe hashed to `1d59b88f…` / `f34292cf…` (49,337 B /
26,112 B, CR=0 each); legs 02–07 exit codes 0/0/3/4/5/3 with logs **byte-identical** to the
committed ones (diff empty, including the preamble's self-attested `harness sha256` line);
legs 08–11 want/got 2/2, 6/6, 6/6, 7/7 all MATCH with derived copies deleted and both final
self-attestation lines True; leg 12 recounted at 42 ok / 0 missing; leg 13's clone-stability
claim re-proved by the `git archive` extraction itself. Permitted differences actually observed:
tempfile paths in the derived runs and the verifier's own CRLF capture of leg 12 (identical
under `diff --strip-trailing-cr`, same manifest sha256). The standing *"by a second pair of
hands"* instruction is **satisfied** for this table.

## Reproducing

From `tools/live-probe`:

```bash
bash acceptance-g4-selfhash/run-table.sh          # legs 02-07 + 01-table-index.txt
py -3.11 -B acceptance-g4-selfhash/run-derived-legs.py   # legs 08-11, copies are deleted afterwards
py -3.11 -B acceptance-g4-selfhash/verify-g0.py > acceptance-g4-selfhash/12-g0-snapshot-verify.log
```

The two leg logs under `_derived/` differ from the shipped ones only in the derived copy's hash;
everything else in a re-run should match the recorded logs except the `tempfile` paths the harness
prints and the timestamps.
