# probe-harness.py - the permanent, self-proving offline harness for
# tools/live-probe/AutoStemProbe/__init__.py (the Live Object Model probe the owner activates with
# one pick in Preferences).
#
# =============================================================================
# THE CONTRACT THIS HARNESS PROTECTS
# =============================================================================
# Stage 0 of the probe - AutoStemProbe._lom_seam() - is the stage that decides the product's fork,
# and it must be READ-ONLY. Concretely:
#
#   * It may resolve and introspect Live.Conversions, Live.Clip and Live.MidiMap with hasattr,
#     dir(), repr(), __doc__, sys.modules, importlib.util.find_spec and an import. The import is the
#     only state it can touch inside Live, and it is interpreter state, not the owner's set.
#   * It must never call a mapping or menu executor: map_midi_control_to_prompt, remove_midi_map,
#     add_auto_mappable_control, send_midi_cc, map_midi_cc, execute_menu_item, trigger_menu_item.
#   * It must never call the separation command itself (separate_stems, separate_stems_of_clip) or
#     anything else that could change the set, a device, a mapping or the transport.
#   * The only Live API members it may call at all are the documented readers
#       Live.Application.get_major_version
#       Live.Application.get_minor_version
#       Live.Application.get_menu_info
#     plus the one handle getter it cannot do without, Live.Application.get_application(), which
#     takes no arguments and reads nothing from the set. That fourth name is called out in
#     HARNESS.md: the ad-hoc harness this one replaces left it un-instrumented and therefore
#     understated the observed set as three names. It is listed here as permitted-with-reason, not
#     dropped.
#
# All of it is enforced, not asserted: every stub member records its own invocation, and the harness
# exits nonzero if the recorded set contains a forbidden name or any name beyond the four permitted
# above. An always-exit-0 harness would be a guard, not a falsifier.
#
# =============================================================================
# THE BYTES THIS PROOF IS ABOUT
# =============================================================================
# Pinned sha256 of AutoStemProbe/__init__.py as validated by this harness:
#   f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29
# Re-pinned 2026-11-18 from b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5. The
# bytes behind the old pin could never be loaded by Live - the module had no create_instance
# factory - so the proof this harness gives was about a probe that cannot run. The change is the
# factory plus one arity fix; the two anchor sites the mutations splice into are unchanged.
# The hash is recomputed from disk on every run and a mismatch is exit 2, because a green run over
# different bytes proves nothing about the file the owner is about to click against. When the probe
# is deliberately changed, update the pin in the same change - that red is the point of this file.
#
# =============================================================================
# EXIT CODES (owned by this file; read the code, not the prose)
# =============================================================================
#   0  GREEN - every shape answered as documented and the observed call set never left the four
#      permitted names.
#   2  PIN_MISMATCH - the probe's bytes are not the bytes this proof is about.
#   3  FORBIDDEN_CALL - a mapping/menu executor or the separation command was called.
#   4  UNDOCUMENTED_CALL - some other Live API member was called; the allowlist is the contract.
#   5  VERDICT_MISMATCH - a shape printed an answer other than the one the census documents.
#   6  HARNESS_INVALID - a stub-safety or self-check failed: the recorders cannot see calls, an
#      import tried to reach a real Live install, the probe's folder or the owner's real report
#      file moved, or a mutation anchor is not present exactly once.
#   7  MUTATION_NOT_CAUGHT - falsifier failure: the deliberately broken copy stayed green, so no
#      green from this harness would mean anything.
#
# In --mutation runs the exit code is the gate's own verdict on the mutant: 3, 4 or 5 there is the
# EXPECTED result and means the falsifier caught the mutation; 0 or 7 means it did not.
#
# =============================================================================
# WHY THIS CANNOT REACH LIVE
# =============================================================================
#   1. Nothing here is a Live API call: sys.modules is pre-populated with synthetic Live,
#      Live.Application, Live.MidiMap, Live.Song, Live.Conversions, Live.Clip and
#      _Framework.ControlSurface, all built by this file, and the probe source is loaded with
#      compile() + exec into a hand-made module - no loader, no import of the probe, so no
#      __pycache__ is written next to it either.
#   2. sys.path is scrubbed of Ableton-shaped entries before anything runs and checked again after.
#   3. A guard finder sits at sys.meta_path[0] and refuses (ImportError, recorded) any import whose
#      root is Live, _Framework, ableton, _Generic, Push or _TaskBar, except the one name a shape
#      explicitly declares as synthetic-served. So even a probe bug that reaches for an unstubbed
#      Live module cannot touch a disk path.
#   4. The probe's report is redirected to a scratch file; the harness records mtime/size/sha256 of
#      the owner's real %LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json before and after and fails with
#      exit 6 if it moved.
#   5. No socket, no subprocess, no process enumeration, no window, no registry, and no write
#      anywhere except the scratch folder, which is removed again.
#
# Run (Live 12.3.2 ships CPython 3.11, so 3.11 is the interpreter this is validated on):
#   py -3.11 -B probe-harness.py                        # expect exit 0
#   py -3.11 -B probe-harness.py --mutation prompt-bind # expect exit 3, and the log says so
#   py -3.11 -B probe-harness.py --mutation conversions-flag   # expect exit 4
#   py -3.11 -B probe-harness.py --mutation command-blind      # expect exit 5
#   py -3.11 -B probe-harness.py --mutations                    # all three legs, expect exit 3
# Every --mutation run also executes the eight shapes on the unmodified shipped source first, as a
# control leg that must stay green, so a mutant's red is attributable to the patch and not to the
# mode. The recorded logs sit beside this file; HARNESS.md maps each log to its command.

from __future__ import print_function

import ast
import contextlib
import hashlib
import importlib.abc
import importlib.machinery
import json
import os
import re
import shutil
import sys
import tempfile
import types

HERE = os.path.dirname(os.path.abspath(__file__))
PROBE_PATH = os.path.join(HERE, "AutoStemProbe", "__init__.py")
PROBE_DIR = os.path.join(HERE, "AutoStemProbe")
PINNED_SHA256 = "f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29"

REAL_REPORT = os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "SkeerAutoSTEM", "live-probe.json")

# ---------------------------------------------------------------------------
# The two sets the gate is built from.
# ---------------------------------------------------------------------------
# Labels are "<module-ish>.<member>" as the stubs record them.
PERMITTED_READERS = {
    "Application.get_application",      # handle getter: no arguments, reads nothing from the set
    "Application.get_major_version",
    "Application.get_minor_version",
    "Application.get_menu_info",        # availability of a menu path, not its execution
}

FORBIDDEN_METHODS = {
    # mapping, prompt and MIDI writes
    "map_midi_control_to_prompt", "remove_midi_map", "add_auto_mappable_control",
    "send_midi_cc", "map_midi_cc",
    # menu execution
    "execute_menu_item", "trigger_menu_item",
    # the command itself, and the sibling that rewrites a stem's source
    "separate_stems", "separate_stems_of_clip", "reassign_stems_source",
}

CODE_OK, CODE_PIN, CODE_FORBIDDEN, CODE_UNDOCUMENTED = 0, 2, 3, 4
CODE_VERDICT, CODE_INVALID, CODE_NOT_CAUGHT = 5, 6, 7

CODE_NAMES = {
    CODE_OK: "GREEN",
    CODE_PIN: "PIN_MISMATCH",
    CODE_FORBIDDEN: "FORBIDDEN_CALL",
    CODE_UNDOCUMENTED: "UNDOCUMENTED_CALL",
    CODE_VERDICT: "VERDICT_MISMATCH",
    CODE_INVALID: "HARNESS_INVALID",
    CODE_NOT_CAUGHT: "MUTATION_NOT_CAUGHT",
}

# The eight interpreter shapes and the answer each one is documented to produce. This is the
# evidence that the probe's verdict lines are real sentences rather than a coin flip: the yes needs
# the command, the partial needs a flag without a command, the two dead ends differ in whether the
# module resolves at all, a separation-shaped stray must not be inflated into a yes, and a module
# that will not be introspected must come back inconclusive instead of an answer.
SHAPES = [
    ("attribute", "yes"),
    ("class-member", "yes"),
    ("import-only", "yes"),
    ("flag-only", "partial"),
    ("no-members", "no"),
    ("nothing", "no"),
    ("stray-only", "no"),
    ("fatal-dir", "inconclusive"),
]

# What each shape declares synthetic-served: the Live module names this harness's own loader is
# allowed to produce. Anything else under a Live-side root is refused by the guard.
SERVED = {"import-only": {"Live.Conversions"}}

STUB_ROOTS = {"Live", "_Framework", "ableton", "Ableton", "_Generic", "Push", "_TaskBar"}

# Only Ableton markers, on purpose: scrubbing "Program Files" would break the local interpreter.
ABLETON_PATH = re.compile(r"ableton|remote scripts", re.I)

CALLS = []
CALL_ARGS = {}
IMPORT_ATTEMPTS = []
REFUSED_IMPORTS = []
LOG_LINES = []
OBSERVED_UNION = set()


def signature(args, kwargs):
    """How a member was actually addressed, so a wrong-arity call cannot hide behind a label."""
    parts = [repr(a) for a in args] + ["%s=%r" % kv for kv in sorted(kwargs.items())]
    return ", ".join(parts)

# ---------------------------------------------------------------------------
# Mutations. Each is an anchor that must occur exactly once in the shipped source, the single line
# spliced in above it, the gate that is expected to trip, and why. The shipped bytes are never
# written: the patch is applied to the in-memory string the mutant is compiled from.
# ---------------------------------------------------------------------------
ANCHOR = "        try:\n            for module_name, names, decides in LOM_TARGETS:"

MUTATIONS = [
    {
        "name": "prompt-bind",
        "kind": "call",
        "anchor": ANCHOR,
        "inject": '        Live.MidiMap.map_midi_control_to_prompt('
                  '40, 14, 0, "Live.Create.AudioSeparation")',
        "expect_code": CODE_FORBIDDEN,
        "expect_label": "MidiMap.map_midi_control_to_prompt",
        "why": "stage 0's whole premise is that it does not bind a prompt; this puts the deleted "
               "map_midi_control_to_prompt call back at the top of _lom_seam, which is exactly the "
               "edit a well-meaning later change would make",
    },
    {
        "name": "conversions-flag",
        "kind": "call",
        "anchor": ANCHOR,
        "inject": "        Live.Song.get_song()",
        "expect_code": CODE_UNDOCUMENTED,
        "expect_label": "Song.get_song",
        "why": "the allowlist is the contract, not the forbidden list: an undocumented call that is "
               "innocuous on its own still has to redden the harness, or the gate is only checking "
               "for the two names someone thought of",
    },
    {
        "name": "command-blind",
        "kind": "answer",
        # A replacement, not an insertion: the eight answer expectations are the other half of what
        # this harness guards, so a mutation that only changes what the probe looks for has to be
        # seen to change the sentences it prints.
        "sub": ('COMMAND_NAMES = ["separate_stems", "separate_stems_of_clip"]',
                'COMMAND_NAMES = ["nonexistent_command_a", "nonexistent_command_b"]'),
        "expect_code": CODE_VERDICT,
        "expect_label": "the three shapes that need a command entry point drop from yes to partial",
        "expect_answers": {"attribute": "partial", "class-member": "partial",
                           "import-only": "partial"},
        "why": "the census picked those two names out of Ableton's own Conversions.pyc; if the probe "
               "ever stops looking for them it must not keep claiming it tested the seam",
    },
]


def apply_mutation(source, mutation):
    """Return (patched_source, description). Refuses to report a red that may not have run."""
    if "sub" in mutation:
        old, new = mutation["sub"]
        if source.count(old) != 1:
            raise HarnessInvalid("mutation %s: its target line occurs %d times in the shipped "
                                 "source, not once" % (mutation["name"], source.count(old)))
        patched = source.replace(old, new, 1)
        return patched, "replaced  %s\n                 with     %s" % (old, new)
    anchor = mutation["anchor"]
    if source.count(anchor) != 1:
        raise HarnessInvalid("mutation %s: its anchor occurs %d times in the shipped source, not "
                             "once" % (mutation["name"], source.count(anchor)))
    patched = source.replace(anchor, mutation["inject"] + "\n" + anchor, 1)
    return patched, "injected into _lom_seam: %s" % mutation["inject"].strip()


class HarnessInvalid(Exception):
    """Something this harness depends on to mean anything has failed."""


def say(line=""):
    print(line)
    sys.stdout.flush()


def use_lf_stdout():
    """Python translates \\n to \\r\\n on a redirected Windows stdout. This folder's other files -
    the probe, the README, the scripts - are LF, and a log that mixes separators is awkwarder to
    diff than one that states its separator, so the harness emits LF everywhere."""
    try:
        sys.stdout.reconfigure(newline="\n")
    except Exception:
        pass


def sha256_file(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def classify(label):
    method = label.split(".")[-1]
    if method in FORBIDDEN_METHODS:
        return "forbidden"
    if label in PERMITTED_READERS:
        return "permitted"
    return "undocumented"


# ---------------------------------------------------------------------------
# Stub safety: the guard finder and the sys.path scrub.
# ---------------------------------------------------------------------------

class StubGuardFinder(object):
    """First on sys.meta_path. Refuses to let a Live-side name resolve to a disk path."""

    def __init__(self):
        self.served = set()

    def find_spec(self, fullname, path=None, target=None):
        IMPORT_ATTEMPTS.append(fullname)
        root = fullname.split(".")[0]
        if root not in STUB_ROOTS:
            return None
        if fullname in self.served:
            return None  # this harness's own synthetic loader answers it; nothing else does
        REFUSED_IMPORTS.append(fullname)
        raise ImportError("probe-harness: refusing to import %r - stubs only, no path that could "
                          "reach a real Live install" % (fullname,))

    def invalidate_caches(self):
        pass


GUARD = StubGuardFinder()


def install_guard():
    sys.meta_path.insert(0, GUARD)
    removed = [p for p in sys.path if p and ABLETON_PATH.search(p)]
    for p in removed:
        sys.path.remove(p)
    return removed


def path_is_clean():
    return [p for p in sys.path if p and ABLETON_PATH.search(p)]


def modules_reaching_disk():
    """A Live-side module object carrying a real file behind it is a stub-safety breach."""
    bad = []
    for name, module in list(sys.modules.items()):
        if name.split(".")[0] not in STUB_ROOTS:
            continue
        origin = getattr(module, "__file__", None)
        if origin:
            bad.append("%s -> %s" % (name, origin))
    return bad


# ---------------------------------------------------------------------------
# The stubs.
# ---------------------------------------------------------------------------

def recorder(label, value=None, raises=None):
    """A member that records that it was called, then does nothing that could matter."""

    def _fn(*args, **kwargs):
        CALLS.append(label)
        CALL_ARGS.setdefault(label, set()).add(signature(args, kwargs))
        if raises is not None:
            raise raises
        return value

    return _fn


class ExplodingDir(types.ModuleType):
    """A module whose dir() raises, to exercise the guarded-stage path."""

    def __dir__(self):
        raise RuntimeError("simulated: this module will not be introspected")


def build_live(case):
    Live = types.ModuleType("Live")
    Live.__path__ = []  # a builtin package: submodules are importable, not pre-attached

    live_app = types.ModuleType("Live.Application")

    class App(object):
        # staticmethod, not a plain function: a bound method would inject self into the recorded
        # arguments and the arity line below would describe Python instead of describing the script.
        get_major_version = staticmethod(recorder("Application.get_major_version", 12))
        get_minor_version = staticmethod(recorder("Application.get_minor_version", 3))
        # A read of availability, permitted; only the executors are forbidden.
        get_menu_info = staticmethod(recorder("Application.get_menu_info", ("Create", 1, True)))
        get_menu_item_info = staticmethod(
            recorder("Application.get_menu_item_info", ("Create", 1, True)))
        execute_menu_item = staticmethod(recorder("Application.execute_menu_item"))
        trigger_menu_item = staticmethod(recorder("Application.trigger_menu_item"))

    app = App()
    live_app.get_application = recorder("Application.get_application", app)
    live_app.Application = App

    midi_map = types.ModuleType("Live.MidiMap")
    for name in ("map_midi_control_to_prompt", "remove_midi_map", "add_auto_mappable_control",
                 "send_midi_cc", "map_midi_cc"):
        setattr(midi_map, name, recorder("MidiMap." + name))
    midi_map.num_devices = 0

    song = types.ModuleType("Live.Song")
    song.get_song = recorder("Song.get_song", object())

    Live.Application = live_app
    Live.MidiMap = midi_map
    Live.Song = song
    sys.modules["Live"] = Live
    sys.modules["Live.Application"] = live_app
    sys.modules["Live.MidiMap"] = midi_map
    sys.modules["Live.Song"] = song

    for finder in [f for f in sys.meta_path if isinstance(f, _SyntheticFinder)]:
        sys.meta_path.remove(finder)

    if case not in ("import-only", "nothing"):
        conversions = ExplodingDir("Live.Conversions") if case == "fatal-dir" \
            else types.ModuleType("Live.Conversions")
        if case == "attribute":
            conversions.separate_stems = recorder("Conversions.separate_stems")
            conversions.separate_stems_of_clip = recorder("Conversions.separate_stems_of_clip")
            conversions.can_separate_stems_in_high_quality = recorder(
                "Conversions.can_separate_stems_in_high_quality", True)
            conversions.reassign_stems_source = recorder("Conversions.reassign_stems_source")
        elif case == "stray-only":
            # A separation-shaped name the census never predicted: the sweep must report it, and the
            # machine answer must still refuse to call it the yes.
            conversions.separate_audio_to_stems = recorder("Conversions.separate_audio_to_stems")
        elif case == "class-member":
            class Conversions(object):
                separate_stems = recorder("Conversions.separate_stems")

                @property
                def can_stems_be_separated(self):  # instance-only; class lookup still finds it
                    return True
            conversions.Conversions = Conversions
        elif case == "flag-only":
            conversions.can_separate_stems_in_high_quality = recorder(
                "Conversions.can_separate_stems_in_high_quality", True)
        Live.Conversions = conversions
        sys.modules["Live.Conversions"] = conversions

    if case != "nothing":
        clip = types.ModuleType("Live.Clip")

        class Clip(object):
            pass
        if case in ("attribute", "class-member", "fatal-dir"):
            Clip.can_stems_be_separated = property(lambda self: True)
        clip.Clip = Clip
        Live.Clip = clip
        sys.modules["Live.Clip"] = clip

    if case == "import-only":
        # Nothing on the package, nothing in sys.modules, find_spec says None: only an actual import
        # produces the module, which is how a builtin Live submodule behaves.
        sys.meta_path.insert(1, _SyntheticFinder())

    return Live


def build_framework():
    framework = types.ModuleType("_Framework")
    control_surface = types.ModuleType("_Framework.ControlSurface")

    class ControlSurface(object):
        def __init__(self, c_instance):
            self._c_instance = c_instance

        @contextlib.contextmanager
        def component_guard(self):
            yield

        def schedule_message(self, delay, fn):
            pass

        def log_message(self, message):
            LOG_LINES.append(message)

    control_surface.ControlSurface = ControlSurface
    framework.ControlSurface = control_surface
    sys.modules["_Framework"] = framework
    sys.modules["_Framework.ControlSurface"] = control_surface


class _SyntheticFinder(importlib.abc.MetaPathFinder):
    """Loader for the import-only shape. It serves one name and it is the only path to it."""

    def find_spec(self, name, path=None, target=None):
        if name != "Live.Conversions":
            return None
        module = types.ModuleType(name)
        module.separate_stems = recorder("Conversions.separate_stems")
        module.can_separate_stems_in_high_quality = recorder(
            "Conversions.can_separate_stems_in_high_quality", True)
        return importlib.machinery.ModuleSpec(name, _SyntheticLoader(name, module))

    def invalidate_caches(self):
        pass


class _SyntheticLoader(object):
    def __init__(self, name, module):
        self.name = name
        self.module = module

    def create_module(self, spec):
        return self.module

    def exec_module(self, module):
        sys.modules[self.name] = module


class CInstance(object):
    """A stand-in for Live's C-side instance handle."""

    def __init__(self):
        self.midi_map = object()
        self.enable_prompt_device_support = recorder("c_instance.enable_prompt_device_support")
        self.create_prompt_device = recorder("c_instance.create_prompt_device")


# ---------------------------------------------------------------------------
# Reading the shipped source without importing it.
# ---------------------------------------------------------------------------

def source_of():
    with open(PROBE_PATH, encoding="utf-8") as handle:
        return handle.read()


def top_level_constant(source, name):
    tree = ast.parse(source, filename=PROBE_PATH)
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id == name:
                    return ast.literal_eval(node.value)
    raise HarnessInvalid("%s is not a top-level constant of %s" % (name, PROBE_PATH))


def check_probe_declaration(source):
    """The harness has to gate at least everything the probe says it refuses."""
    declared = [str(n).split(".")[-1] for n in top_level_constant(source, "FORBIDDEN_CALLS")]
    commands = [str(n) for n in top_level_constant(source, "COMMAND_NAMES")]
    ungated = sorted({n for n in declared + commands if n not in FORBIDDEN_METHODS})
    return declared, commands, ungated


# ---------------------------------------------------------------------------
# Self-check: a harness that cannot see a call must not get to report that there were none.
# ---------------------------------------------------------------------------

def self_check(source):
    problems = []
    del CALLS[:]
    CALL_ARGS.clear()
    del IMPORT_ATTEMPTS[:]
    del REFUSED_IMPORTS[:]
    build_live("attribute")
    Live = sys.modules["Live"]
    Live.MidiMap.map_midi_control_to_prompt(1, 2, 3, "x")
    app = Live.Application.get_application()  # the handle getter, then a reader on the handle
    app.get_major_version()
    seen = sorted(set(CALLS))
    if "MidiMap.map_midi_control_to_prompt" not in seen:
        problems.append("a recorder did not see a forbidden call it was handed: any green from "
                        "here would be vacuous")
    elif classify("MidiMap.map_midi_control_to_prompt") != "forbidden":
        problems.append("the classifier does not grade map_midi_control_to_prompt forbidden")
    if "Application.get_major_version" not in seen:
        problems.append("the recorders do not see permitted calls either")
    if classify("Application.get_application") != "permitted":
        problems.append("the handle getter is not on the permitted set, so the shipped probe could "
                        "never go green")
    if classify("Song.get_song") != "undocumented":
        problems.append("the classifier does not grade an undocumented read as a breach")
    del CALLS[:]

    try:
        GUARD.find_spec("Live.Conversions")
        problems.append("the guard let Live.Conversions through with nothing declared synthetic")
    except ImportError:
        pass
    if REFUSED_IMPORTS != ["Live.Conversions"]:
        problems.append("the guard refused an import without recording it: %s" % REFUSED_IMPORTS)
    del REFUSED_IMPORTS[:]
    del IMPORT_ATTEMPTS[:]
    GUARD.served = {"Live.Conversions"}
    try:
        if GUARD.find_spec("Live.Conversions") is not None:
            problems.append("the guard claimed to serve a synthetic name itself instead of "
                            "declining so the harness's own loader could")
    except ImportError:
        problems.append("the guard refused a name this harness declared synthetic: the import-only "
                        "shape would then test nothing")
    GUARD.served = set()
    del IMPORT_ATTEMPTS[:]

    if path_is_clean():
        problems.append("sys.path still holds Ableton-shaped entries: %s" % path_is_clean())
    for mutation in MUTATIONS:
        key = mutation["sub"][0] if "sub" in mutation else mutation["anchor"]
        if source.count(key) != 1:
            problems.append("the search text for mutation %s occurs %d times, not once, so that "
                            "mutation might not apply" % (
                                mutation["name"], source.count(key)))
    return problems


# ---------------------------------------------------------------------------
# One shape, one run.
# ---------------------------------------------------------------------------

def run_shape(case, source, report_path):
    for key in [k for k in list(sys.modules)
                if k.split(".")[0] in STUB_ROOTS or k == "AutoStemProbe"]:
        sys.modules.pop(key, None)
    del CALLS[:]
    CALL_ARGS.clear()
    del LOG_LINES[:]
    del IMPORT_ATTEMPTS[:]
    del REFUSED_IMPORTS[:]
    GUARD.served = set(SERVED.get(case, ()))

    build_live(case)
    build_framework()

    probe_module = types.ModuleType("AutoStemProbe")
    probe_module.__file__ = PROBE_PATH
    sys.modules["AutoStemProbe"] = probe_module
    # compile() + exec, not a loader: no __pycache__ is written next to the probe, so the only
    # byte-compiled copies on this machine stay the ones Live and the local checks made.
    exec(compile(source, PROBE_PATH, "exec"), probe_module.__dict__)
    probe_module.REPORT_PATH = report_path

    probe = probe_module.AutoStemProbe(CInstance())
    probe._run_probe()

    if not os.path.isfile(report_path):
        raise HarnessInvalid("shape %s produced no report at %s: the probe could not write, so "
                             "there is nothing to grade" % (case, report_path))
    with open(report_path) as handle:  # same default encoding the probe's own writer used
        report = json.load(handle)

    stage0 = report["stages"]["0_lom_seam"]
    observed = sorted(set(CALLS))
    grades = {}
    for label in observed:
        grades.setdefault(classify(label), []).append(label)
    return {
        "case": case,
        "answer": report["answer"],
        "verdict": report["verdict"],
        "decided_by": report.get("decided_by"),
        "fatal": "fatal" in report,
        "interpreter": report["environment"]["python"],
        "observed": observed,
        "signatures": dict((label, sorted(CALL_ARGS.get(label, []))) for label in observed),
        "forbidden": grades.get("forbidden", []),
        "undocumented": grades.get("undocumented", []),
        "permitted": grades.get("permitted", []),
        "counts": dict((label, CALLS.count(label)) for label in observed),
        "import_attempts": sorted(set(IMPORT_ATTEMPTS)),
        "refused": sorted(set(REFUSED_IMPORTS)),
        "found": sorted(f["scope"] for f in stage0.get("found", [])),
        "strays": sorted(s["scope"] for s in stage0.get("strays", [])),
        "resolved_by": [t.get("resolved_by", "not resolved") for t in stage0.get("targets", [])],
        "policy_read_only": report.get("policy", {}).get("read_only"),
        "stage4_attempted": report["stages"]["4_prompt_names"]["attempted"],
        "log_lines": len(LOG_LINES),
    }


# ---------------------------------------------------------------------------
# Printing and grading.
# ---------------------------------------------------------------------------

def print_shape(result, expected):
    say()
    say("shape %-13s answer %-12s expected %-12s %s" % (
        result["case"], result["answer"], expected,
        "OK" if result["answer"] == expected else "MISMATCH"))
    say("  Live API calls observed (%d): %s" % (
        len(result["observed"]),
        ", ".join("%s x%d" % (label, result["counts"][label]) for label in result["observed"])
        or "none"))
    say("  forbidden=%s  undocumented=%s  outside the allowlist: %s" % (
        result["forbidden"] or "none",
        result["undocumented"] or "none",
        sorted(set(result["forbidden"]) | set(result["undocumented"])) or "none"))
    for label in result["observed"]:
        say("  called as     %s(%s) x%d" % (
            label, "|".join(result["signatures"][label]) or "no arguments",
            result["counts"][label]))
    say("  imports the guard saw: %s | refused: %s" % (
        result["import_attempts"] or "none", result["refused"] or "none"))
    say("  found=%s  strays=%s  resolved_by=%s" % (
        result["found"] or "none", result["strays"] or "none", result["resolved_by"]))
    say("  policy.read_only=%s  stage4.attempted=%s  report.fatal=%s  probe log lines=%d" % (
        result["policy_read_only"], result["stage4_attempted"], result["fatal"], result["log_lines"]))
    say("  probe verdict: %s" % (result["verdict"] or "(empty)"))


def evaluate(results, expectations):
    """Precedence: a forbidden call outranks everything the run can say."""
    forbidden = [(r["case"], label) for r in results for label in r["forbidden"]]
    undocumented = [(r["case"], label) for r in results for label in r["undocumented"]]
    wrong = [(r["case"], r["answer"], expected) for r, expected in zip(results, expectations)
             if r["answer"] != expected]
    if forbidden:
        return CODE_FORBIDDEN, forbidden, undocumented, wrong
    if undocumented:
        return CODE_UNDOCUMENTED, forbidden, undocumented, wrong
    if wrong:
        return CODE_VERDICT, forbidden, undocumented, wrong
    return CODE_OK, forbidden, undocumented, wrong


def verdict_line(code, results, forbidden, undocumented, wrong, note=""):
    total = len(results) if results else len(SHAPES)
    green = code == CODE_OK
    return "VERDICT: %s [exit %d %s] - %d shapes, calls outside the four permitted: %s; forbidden: " \
           "%s; answer mismatches: %s%s" % (
        "GREEN" if green else "RED", code, CODE_NAMES[code], total,
        sorted(set(lbl for _, lbl in forbidden) | set(lbl for _, lbl in undocumented)) or "none",
        sorted(set(lbl for _, lbl in forbidden)) or "none", wrong or "none",
        (" - " + note) if note else "")


def line_of(source, needle):
    for number, line in enumerate(source.splitlines(), 1):
        if needle in line:
            return number
    return None


def print_findings(source):
    """Limits of the evidence, named so a reader is not misled by a green. These do not change the
    exit code: a call the probe does not make cannot breach anything, but it does mean a claim about
    it is not what this harness proved."""
    say()
    say("=============== FINDINGS (evidence limits; no exit-code effect) ==============")
    missing = sorted(PERMITTED_READERS - OBSERVED_UNION)
    if "Application.get_minor_version" in missing:
        say("  - Live.Application.get_minor_version is never called, in any shape. Cause: %s:%s "
            "reads report[\"environment\"][\"live_version\"] as "
            "self._call(app, \"get_major_version\", \"get_minor_version\"), and _call's signature is "
            "(target, method, *args) - so the second name arrives as an ARGUMENT to "
            "get_major_version(), which takes none. On a real Live _call catches the TypeError and "
            "the report carries an error line instead of a version; the call recorded here shows the "
            "wrong arity. Stage 0 does not read live_version, so the fork answer is unaffected. "
            "REPORTED, NOT FIXED: this brief forbids editing the probe." % (
                PROBE_PATH, line_of(source, '_call(app, "get_major_version", "get_minor_version"')))
    elif missing:
        say("  - permitted but never called: %s" % missing)
    say("  - Live.Application.get_application IS called and is not one of the three documented "
        "readers: the probe cannot reach an Application object without it. It takes no arguments, "
        "returns the handle and reads nothing from the set, so it is permitted with that reason. The "
        "ad-hoc harness this file replaces never instrumented it, so its \"three names\" could not "
        "have been complete; the honest allowlist for the shipped bytes is four.")
    say("  - Live.Application.get_menu_item_info is stage 2's fallback read (%s:%s). The loop breaks "
        "once get_menu_info answers, so it never fires on a Live that has get_menu_info, and it is "
        "deliberately NOT in the allowlist: if a build lacks get_menu_info, this harness reddens "
        "with exit 4 by design and that name has to be added with the same read-only reason." % (
            PROBE_PATH, line_of(source, 'for method in ("get_menu_info", "get_menu_item_info")')))
    say("  - stub signatures are not Live's: the recorders accept any arguments, which is how the "
        "wrong-arity read above was visible as a call rather than as a TypeError. A real Live answers "
        "for itself; nothing here proves what Live's builtins accept, only what this script asks for.")


def record_of(path):
    try:
        stat = os.stat(path)
        return (round(os.path.getmtime(path), 6), stat.st_size, sha256_file(path))
    except OSError:
        return None


def run_suite(source, report_path, label):
    expectations = [expected for _, expected in SHAPES]
    results = []
    for case, expected in SHAPES:
        result = run_shape(case, source, report_path)
        print_shape(result, expected)
        results.append(result)
    code, forbidden, undocumented, wrong = evaluate(results, expectations)
    say()
    union = sorted({label2 for r in results for label2 in r["observed"]})
    if not label.startswith("mutation"):
        OBSERVED_UNION.update(union)
    say("run '%s': %d shapes, %d distinct Live API members observed across all of them: %s" % (
        label, len(results), len(union), union))
    say(verdict_line(code, results, forbidden, undocumented, wrong, note=label))
    return code, results, forbidden, undocumented, wrong


# ---------------------------------------------------------------------------
# Main.
# ---------------------------------------------------------------------------

def main(argv):
    use_lf_stdout()
    if "--help" in argv or "-h" in argv:
        # The contract lives in this file's leading comment block, not in a docstring: printing a
        # second copy in a docstring is how the two versions drift.
        header = []
        with open(os.path.abspath(__file__), encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("from __future__") or line.startswith("import "):
                    break
                header.append(line.rstrip("\n").rstrip("\r"))
        say("\n".join(header))
        say("usage: py -3.11 -B probe-harness.py [--mutation NAME | --mutations | --help]")
        say("mutations available: %s" % [m["name"] for m in MUTATIONS])
        return CODE_OK
    mutation_names = []
    for index, argument in enumerate(argv):
        if argument == "--mutation" and index + 1 < len(argv):
            mutation_names.append(argv[index + 1])
        elif argument.startswith("--mutation="):
            mutation_names.append(argument.split("=", 1)[1])
    if "--mutations" in argv:
        mutation_names = [m["name"] for m in MUTATIONS]

    say("probe-harness.py - offline stub harness for AutoStemProbe/__init__.py")
    say("  cwd             %s" % os.getcwd())
    say("  harness file    %s" % os.path.abspath(__file__))
    say("  probe file      %s" % PROBE_PATH)
    say("  interpreter     %s (%s)" % (sys.version.split()[0], sys.executable))
    if sys.version_info[:2] != (3, 11):
        say("  NOTE            Live 12.3.2 ships CPython 3.11; this run is on %d.%d, so it is a "
            "portability check of the harness, not the shipped-interpreter check. Re-run with "
            "py -3.11." % sys.version_info[:2])

    removed = install_guard()
    say("  sys.path        %d entries; Ableton-shaped entries removed before anything imported: %s"
        % (len(sys.path), removed or "none needed"))

    if not os.path.isfile(PROBE_PATH):
        say(verdict_line(CODE_INVALID, [], [], [], [], note="probe source not found"))
        return CODE_INVALID
    observed_sha = sha256_file(PROBE_PATH)
    say("  probe sha256    %s" % observed_sha)
    if observed_sha != PINNED_SHA256:
        say(verdict_line(CODE_PIN, [], [], [], [],
                         note="PIN_MISMATCH: this harness pins %s, the bytes on disk are %s, so "
                              "this proof is not about the file the owner would click" % (
                                  PINNED_SHA256, observed_sha)))
        return CODE_PIN
    say("  pin             MATCHES the bytes this proof is about")

    try:
        source = source_of()
        declared, commands, ungated = check_probe_declaration(source)
    except (HarnessInvalid, SyntaxError, ValueError) as error:
        say("  SELF-CHECK FAILED: the shipped source did not parse or its constants moved: %s"
            % error)
        say(verdict_line(CODE_INVALID, [], [], [], [], note="source unreadable"))
        return CODE_INVALID
    say("  probe refuses   FORBIDDEN_CALLS(%d): %s" % (len(declared), declared))
    say("  probe commands  COMMAND_NAMES: %s | every one gated here: %s" % (
        commands, "yes" if not ungated else "NO - ungated: %s" % ungated))

    probe_dir_before = sorted(os.listdir(PROBE_DIR))
    real_report_before = record_of(REAL_REPORT)
    say("  owner's report  %s before this run: %s" % (REAL_REPORT, real_report_before or "absent"))

    scratch = tempfile.mkdtemp(prefix="autostem-probe-harness-")
    report_path = os.path.join(scratch, "live-probe.json")
    say("  scratch report  %s (removed at the end)" % report_path)

    try:
        problems = self_check(source)
    finally:
        del CALLS[:]
    say("  self-check      %s" % ("OK - recorders see calls, the guard refuses a Live-side import, "
                                 "sys.path is clean, every mutation anchor is unique"
                                 if not problems else "FAILED"))
    if problems:
        for problem in problems:
            say("  SELF-CHECK FAILED: %s" % problem)
        say(verdict_line(CODE_INVALID, [], [], [], [], note="harness self-check"))
        shutil.rmtree(scratch, ignore_errors=True)
        return CODE_INVALID

    codes = []
    try:
        if not mutation_names:
            code, _, _, _, _ = run_suite(source, report_path, "shipped bytes")
            codes.append(code)
        else:
            say()
            say("=============== MUTATION PROBE ===============")
            say("No file is mutated. Each mutation splices one line into AutoStemProbe._lom_seam() "
                "in the")
            say("in-memory source the mutant is compiled from; the shipped bytes on disk stay "
                "%s." % observed_sha)
            say()
            say("--- control leg: the unmodified shipped source ---")
            control_code, _, _, _, _ = run_suite(source, report_path, "control (unmodified)")
            for name in mutation_names:
                mutation = next((m for m in MUTATIONS if m["name"] == name), None)
                if mutation is None:
                    say("unknown mutation %r (known: %s)" % (
                        name, [m["name"] for m in MUTATIONS]))
                    codes.append(CODE_INVALID)
                    continue
                try:
                    patched, description = apply_mutation(source, mutation)
                except HarnessInvalid as error:
                    say("MUTATION %s: %s" % (name, error))
                    codes.append(CODE_INVALID)
                    continue
                if patched == source:
                    say("MUTATION %s: the patch was a no-op, refusing to report a red that never "
                        "ran" % name)
                    codes.append(CODE_INVALID)
                    continue
                say()
                say("--- mutant leg: %s ---" % name)
                say("  in memory only: %s" % description)
                say("  why: %s" % mutation["why"])
                say("  expected: exit %d %s - %s" % (
                    mutation["expect_code"], CODE_NAMES[mutation["expect_code"]],
                    mutation["expect_label"]))
                say("  source length %d -> %d (delta %+d bytes); the shipped file is never rewritten"
                    % (len(source), len(patched), len(patched) - len(source)))
                code, results, forbidden, undocumented, wrong = run_suite(
                    patched, report_path, "mutation " + name)
                if mutation["kind"] == "answer":
                    observed = dict((r["case"], r["answer"]) for r in results)
                    flipped = sorted(case for case, _, _ in wrong)
                    predicted = sorted(mutation["expect_answers"])
                    exact = flipped == predicted and all(
                        observed[case] == mutation["expect_answers"][case] for case in predicted)
                    if code == mutation["expect_code"] and exact:
                        say("FALSIFIER: PASS - mutation %s reddened the answer gate with exit %d %s,"
                            " flipping exactly %s to %s and leaving the other %d shapes as"
                            " documented." % (
                                name, code, CODE_NAMES[code], flipped,
                                [mutation["expect_answers"][case] for case in predicted],
                                len(results) - len(predicted)))
                        codes.append(code)
                    else:
                        say("FALSIFIER: FAIL - mutation %s gave exit %d %s (expected %d %s);"
                            " flipped=%s predicted=%s observed=%s. The eight answer expectations are"
                            " not load-bearing, so this harness cannot certify the verdict lines." % (
                                name, code, CODE_NAMES.get(code, "?"), mutation["expect_code"],
                                CODE_NAMES[mutation["expect_code"]], flipped, predicted,
                                sorted(observed.items())))
                        codes.append(CODE_NOT_CAUGHT)
                    continue
                caught = sorted({case for case, _ in forbidden + undocumented})
                labels = sorted({lbl for _, lbl in forbidden + undocumented})
                red = code in (CODE_FORBIDDEN, CODE_UNDOCUMENTED)
                if red and code == mutation["expect_code"] and len(caught) == len(results) \
                        and mutation["expect_label"] in labels:
                    say("FALSIFIER: PASS - mutation %s reddened %d/%d shapes with exit %d %s, "
                        "catching %s" % (name, len(caught), len(results), code,
                                         CODE_NAMES[code], mutation["expect_label"]))
                    codes.append(code)
                else:
                    say("FALSIFIER: FAIL - mutation %s gave exit %d %s (expected %d %s), caught in "
                        "%d/%d shapes, labels %s. The gate did not do its job, so no green from "
                        "this harness means anything." % (
                            name, code, CODE_NAMES.get(code, "?"), mutation["expect_code"],
                            CODE_NAMES[mutation["expect_code"]], len(caught), len(results), labels))
                    codes.append(CODE_NOT_CAUGHT)
            if control_code != CODE_OK:
                say("CONTROL LEG NOT GREEN (exit %d %s): the shipped bytes already trip the gate, so "
                    "a mutant's red is not attributable to the patch." % (
                        control_code, CODE_NAMES[control_code]))
            else:
                say("CONTROL LEG GREEN (exit 0): the shipped bytes were clean in the same process, "
                    "same interpreter, same stubs, so each mutant's red above is the patch.")

        print_findings(source)
        say()
        say("=============== POST-RUN SAFETY ===============")
        after_sha = sha256_file(PROBE_PATH)
        probe_dir_after = sorted(os.listdir(PROBE_DIR))
        real_report_after = record_of(REAL_REPORT)
        say("  probe sha256    after the run: %s (unchanged: %s)" % (
            after_sha, after_sha == observed_sha))
        say("  probe folder    entries before/after: %s (unchanged: %s)" % (
            probe_dir_after, probe_dir_before == probe_dir_after))
        say("  owner's report  after the run: %s (untouched: %s)" % (
            real_report_after or "absent", real_report_before == real_report_after))
        say("  sys.path        still clean: %s" % ("yes" if not path_is_clean() else path_is_clean()))
        say("  disk-backed Live-side modules loaded: %s" % (modules_reaching_disk() or "none"))
        say("  imports refused by the guard during the run: %s" % (REFUSED_IMPORTS or "none"))
        safety = []
        if after_sha != observed_sha:
            safety.append("the shipped probe bytes moved during the run")
        if probe_dir_before != probe_dir_after:
            safety.append("the probe folder gained or lost an entry: %s -> %s" % (
                probe_dir_before, probe_dir_after))
        if real_report_before != real_report_after:
            safety.append("the owner's real report %s changed during the run" % REAL_REPORT)
        if path_is_clean():
            safety.append("an Ableton-shaped sys.path entry came back: %s" % path_is_clean())
        if modules_reaching_disk():
            safety.append("a Live-side module resolved to a file: %s" % modules_reaching_disk())
        for problem in safety:
            say("  SAFETY FAILED: %s" % problem)
        if safety:
            return CODE_INVALID
        say("  nothing was written, imported or reported outside the scratch folder")
    except HarnessInvalid as error:
        say("  SELF-CHECK FAILED: %s" % error)
        say(verdict_line(CODE_INVALID, [], [], [], [], note="harness invalid mid-run"))
        return CODE_INVALID
    finally:
        shutil.rmtree(scratch, ignore_errors=True)

    if not mutation_names:
        return codes[0]
    # Mutation mode reports the gate's own verdict on the mutant: 7 if any falsifier failed or the
    # control leg was not green, otherwise the most severe code the gates caught (3 outranks 4).
    if CODE_NOT_CAUGHT in codes or CODE_INVALID in codes:
        return CODE_NOT_CAUGHT
    if control_code != CODE_OK:
        return control_code if control_code != CODE_OK else CODE_NOT_CAUGHT
    for severity in (CODE_FORBIDDEN, CODE_UNDOCUMENTED, CODE_VERDICT):
        if severity in codes:
            return severity
    return CODE_NOT_CAUGHT


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
