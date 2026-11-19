#!/usr/bin/env bash
# Re-runs the harness exit table against the re-pinned bytes. Exit codes are owned here:
# each leg redirects to a file (no pipe) and takes $? from the harness process itself.
cd "D:/Projects/skeer-audio/autostem/tools/live-probe" || exit 1
OUT=acceptance-g4-selfhash
PROBE=$(py -3.11 -B -c "import hashlib;print(hashlib.sha256(open('AutoStemProbe/__init__.py','rb').read()).hexdigest())")
HARNESS=$(py -3.11 -B -c "import hashlib;print(hashlib.sha256(open('probe-harness.py','rb').read()).hexdigest())")

leg () { # name, expected, args...
  local name="$1" want="$2"; shift 2
  py -3.11 -B probe-harness.py "$@" > "$OUT/$name.log" 2>&1
  local got=$?
  printf '%-26s want=%s got=%s %s\n' "$name" "$want" "$got" \
    "$( [ "$got" = "$want" ] && echo MATCH || echo MISMATCH )"
}

{
  echo "G4 exit table on the self-attesting harness"
  echo "run at        : $(date -Iseconds)"
  echo "probe sha256  : $PROBE"
  echo "harness sha256: $HARNESS"
  echo "interpreter   : $(py -3.11 -V 2>&1)"
  echo "cwd           : $(pwd)"
  echo
} > "$OUT/01-table-index.txt"

{
leg 02-green-plain            0
leg 03-help                   0 --help
leg 04-mutation-prompt-bind   3 --mutation prompt-bind
leg 05-mutation-conversions   4 --mutation conversions-flag
leg 06-mutation-command-blind 5 --mutation command-blind
leg 07-mutations-all          3 --mutations
} >> "$OUT/01-table-index.txt"

echo >> "$OUT/01-table-index.txt"
cat >> "$OUT/01-table-index.txt" <<EOF
legs 02-07 above were run from the shipped folder. No file on disk was mutated:
the harness splices each mutation into the in-memory source it compiles the mutant
from, and says so in its own preamble.
EOF
tail -3 "$OUT/02-green-plain.log" | head -1
