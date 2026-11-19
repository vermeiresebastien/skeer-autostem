"""G4 derived-copy legs: pin-drift and the three self-check legs, against the self-attesting
harness (it prints its own sha256; see HARNESS.md, gap list item 1, marked DONE).

Every leg runs a *copy* of the folder. The shipped harness is never edited to make a leg fail;
the neuter lives only in the derived copy, which is hashed, run, deleted, and proved deleted.
Exit codes are owned here: subprocess.run returns them, nothing is piped.
"""
import hashlib
import os
import shutil
import subprocess
import sys

SRC = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(SRC, "acceptance-g4-selfhash")
DERIVED = os.path.join(OUT, "_derived")
PY = [sys.executable, "-B"]


def sha(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def fresh(name):
    target = os.path.join(DERIVED, name)
    if os.path.exists(target):
        shutil.rmtree(target)
    shutil.copytree(os.path.join(SRC, "AutoStemProbe"),
                    os.path.join(target, "AutoStemProbe"))
    shutil.copy2(os.path.join(SRC, "probe-harness.py"), os.path.join(target, "probe-harness.py"))
    return target


def rewrite(path, old, new, label):
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    if text.count(old) != 1:
        raise SystemExit("derived leg %s: splice target occurs %d times, not once" % (label,
                                                                                      text.count(old)))
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text.replace(old, new, 1))


def run(target, args, want, label, notes):
    """Runs the copy's harness, records preamble + verdict + true exit code."""
    proc = subprocess.run(PY + [os.path.join(target, "probe-harness.py")] + args,
                          cwd=target, capture_output=True, text=True)
    got = proc.returncode
    harness_sha = sha(os.path.join(target, "probe-harness.py"))
    probe_full = os.path.join(target, "AutoStemProbe", "__init__.py")
    probe_sha = sha(probe_full) if os.path.exists(probe_full) else "ABSENT"
    lines = [
        "G4 derived-copy leg: %s" % label,
        "run at          : %s" % __import__("datetime").datetime.now().isoformat(timespec="seconds"),
        "shipped probe   : %s" % sha(os.path.join(SRC, "AutoStemProbe", "__init__.py")),
        "shipped harness : %s" % sha(os.path.join(SRC, "probe-harness.py")),
        "derived harness : %s   <- THIS is the copy that ran, not the shipped bytes" % harness_sha,
        "derived probe   : %s" % probe_sha,
        "command         : py -3.11 -B %s %s" % (
            os.path.join(target, "probe-harness.py"), " ".join(args)),
        "tamper applied  : %s" % notes,
        "expected exit   : %d" % want,
        "actual exit     : %d   %s" % (got, "MATCH" if got == want else "*** MISMATCH ***"),
        "",
        "=== verbatim stdout ===",
        proc.stdout.rstrip("\n"),
        "=== verbatim stderr ===",
        proc.stderr.rstrip("\n") or "(empty)",
    ]
    with open(os.path.join(OUT, "%s.log" % label), "w", encoding="utf-8", newline="\n") as out:
        out.write("\n".join(lines) + "\n")
    return got, want


results = []
if os.path.exists(DERIVED):
    shutil.rmtree(DERIVED)
os.makedirs(DERIVED)

# --- 08 pin-drift: a drifted probe beside an UNEDITED harness. No harness change at all. ---
t = fresh("08-pin-drift")
with open(os.path.join(t, "AutoStemProbe", "__init__.py"), "a", encoding="utf-8") as handle:
    handle.write("\n# drifted by exactly one comment line, to prove the pin is a real gate\n")
results.append(run(t, [], 2, "08-pin-drift",
                   "probe only: one trailing comment line appended. probe-harness.py is byte-"
                   "identical to the shipped one (%s), so this leg proves the pin without editing "
                   "the gate." % sha(os.path.join(SRC, "probe-harness.py"))))

# --- 09 self-check A: the probe source is simply not there. ---
t = fresh("09-selfcheck-A")
os.remove(os.path.join(t, "AutoStemProbe", "__init__.py"))
results.append(run(t, [], 6, "09-selfcheck-A",
                   "AutoStemProbe/__init__.py removed from the copy; harness unedited"))

# --- 10 self-check B: the allowlist neutered in a derived copy. ---
t = fresh("10-selfcheck-B")
rewrite(os.path.join(t, "probe-harness.py"),
        'FORBIDDEN_METHODS = {\n    # mapping, prompt and MIDI writes\n'
        '    "map_midi_control_to_prompt", "remove_midi_map", "add_auto_mappable_control",\n'
        '    "send_midi_cc", "map_midi_cc",\n'
        '    # menu execution\n'
        '    "execute_menu_item", "trigger_menu_item",\n'
        '    # the command itself, and the sibling that rewrites a stem\'s source\n'
        '    "separate_stems", "separate_stems_of_clip", "reassign_stems_source",\n}',
        'FORBIDDEN_METHODS = set()  # SELF-CHECK B: the forbidden set emptied on purpose',
        "10-selfcheck-B")
results.append(run(t, [], 6, "10-selfcheck-B",
                   "FORBIDDEN_METHODS emptied in the copy's harness"))

# --- 11 self-check C: a deliberately wrong mutation expectation. ---
t = fresh("11-selfcheck-C")
rewrite(os.path.join(t, "probe-harness.py"),
        '        "expect_code": CODE_FORBIDDEN,\n'
        '        "expect_label": "MidiMap.map_midi_control_to_prompt",',
        '        "expect_code": CODE_UNDOCUMENTED,  # SELF-CHECK C: wrong on purpose\n'
        '        "expect_label": "MidiMap.map_midi_control_to_prompt",',
        "11-selfcheck-C")
results.append(run(t, ["--mutation", "prompt-bind"], 7, "11-selfcheck-C",
                   "prompt-bind's expect_code changed from CODE_FORBIDDEN(3) to CODE_UNDOCUMENTED(4)"))

print("legs:")
for got, want in results:
    print("  want=%d got=%d %s" % (want, got, "MATCH" if got == want else "MISMATCH"))

survivors = []
for root, _dirs, files in os.walk(DERIVED):
    for name in files:
        if name.endswith(".py"):
            survivors.append(os.path.join(root, name))
shutil.rmtree(DERIVED)
print("derived .py copies deleted:", not os.path.exists(DERIVED))
print("re-check folder gone:", not os.path.exists(DERIVED))
for path in survivors:
    print("  was:", path)
print("shipped harness is the self-attesting revision:",
      sha(os.path.join(SRC, "probe-harness.py")) == "1d59b88fd8cf80b571ed35d1e12c3b14126b0c313c9c70578d284a6a34fd5bb9")
print("shipped probe unchanged:",
      sha(os.path.join(SRC, "AutoStemProbe", "__init__.py")) ==
      "f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29")
