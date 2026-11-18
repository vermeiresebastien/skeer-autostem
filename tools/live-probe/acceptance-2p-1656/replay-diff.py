"""Replay diff: my harness stdout vs the recorded VERBATIM bodies.

The index promises that a re-run matches the recorded body byte for byte, ignoring only
the random tempfile.mkdtemp() folder name printed in the run preamble. This script is the
machine version of that promise. It writes nothing.
"""
import re
import sys

REPO = r"D:\Projects\skeer-audio\autostem\tools\live-probe"
SCRATCH = r"C:\Users\Sebastien\AppData\Local\Temp\harness-accept-1656"
MARKER = "HARNESS OUTPUT VERBATIM BELOW"

# The one variable the index says may differ: the mkdtemp scratch folder name.
SCRATCH_DIR = re.compile(r"autostem-probe-harness-[A-Za-z0-9_]+")

PAIRS = [
    ("probe-harness-green.log", "green.log"),
    ("probe-harness-mutation-prompt-bind.log", "mutation-prompt-bind.log"),
    ("probe-harness-mutation-conversions-flag.log", "mutation-conversions-flag.log"),
    ("probe-harness-mutation-command-blind.log", "mutation-command-blind.log"),
    ("probe-harness-mutations-all.log", "mutations-all.log"),
]


def normalise(text):
    text = SCRATCH_DIR.sub("autostem-probe-harness-SCRATCH", text)
    return text.splitlines()


def recorded_body(path):
    with open(path, encoding="utf-8", errors="replace") as handle:
        lines = handle.read().splitlines()
    for index, line in enumerate(lines):
        if line.strip() == MARKER:
            return lines[index + 1:]
    return None


mismatched = 0
for recorded_name, mine_name in PAIRS:
    recorded_path = REPO + "\\" + recorded_name
    mine_path = SCRATCH + "\\" + mine_name
    body = recorded_body(recorded_path)
    if body is None:
        print("%-44s NO MARKER FOUND" % recorded_name)
        mismatched += 1
        continue
    with open(mine_path, encoding="utf-8", errors="replace") as handle:
        mine = handle.read().splitlines()

    # SECTION 2 appendices exist in some logs; compare only the first verbatim block,
    # which is the section the index ties to the shipped bytes.
    trimmed = []
    for line in body:
        if line.startswith("=============== SECTION 2"):
            break
        trimmed.append(line)

    a, b = normalise("\n".join(trimmed)), normalise("\n".join(mine))
    if a == b:
        print("%-44s IDENTICAL  (%d lines)" % (recorded_name, len(a)))
    else:
        mismatched += 1
        print("%-44s DIFFERS" % recorded_name)
        import difflib
        diff = list(difflib.unified_diff(a, b, "recorded", "mine", lineterm="", n=0))
        for line in diff[:24]:
            print("      %s" % line)
        print("      ... %d diff lines total" % len(diff))

print("REPLAY_MISMATCHES=%d of %d" % (mismatched, len(PAIRS)))
sys.exit(0 if mismatched == 0 else 1)
