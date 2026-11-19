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

## Single-witness status

**Pending.** Every leg in this table was run by the same pair of hands that made the one-line
harness change, so the standing *"by a second pair of hands"* instruction (G3's, carried into G4
by the brief) is not yet satisfied. The independent re-run and its verdict will be recorded in
this section when it happens; this paragraph will then be rewritten in past tense. Until then,
treat the table as single-witness.

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
