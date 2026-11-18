# -*- coding: utf-8 -*-
"""
Offline instantiation gate for the autoSTEM Live probe.

Why this exists: Live builds a control surface by calling a module-level
``create_instance(c_instance)`` factory. A probe without it never appears in
Settings -> Link/Debug -> Remote Control Surfaces, so it cannot be picked, so it can
never run - and the only way to find that out the normal way is to restart Ableton Live
and lose whatever the operator had open. This gate answers the same question in
milliseconds, with no Live and no restart.

It is red-first by design. Run it against bytes that lack the factory and it must fail;
a gate that goes green on a script Live cannot build is a broken gate, not a good script.

SAFETY INVARIANT: the probe's whole purpose is to write
``%LOCALAPPDATA%\\SkeerAutoSTEM\\live-probe.json``, and that file is the evidence the
product's engine decision is read from. This gate must never produce it. ``_write`` is
wrapped in a tripwire that raises, and the path is asserted absent before and after.

Usage:  py -3.11 -B instantiation-gate.py
Exit:   0 all stages pass, 1 a stage failed, 2 the gate itself could not run.
"""

from __future__ import absolute_import, print_function, unicode_literals

import contextlib
import hashlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROBE_PARENT = HERE
PROBE_PKG = os.path.join(HERE, "AutoStemProbe")
PROBE_FILE = os.path.join(PROBE_PKG, "__init__.py")
HARNESS = os.path.join(HERE, "probe-harness.py")


def use_probe_dir(path):
    """Point the gate at another AutoStemProbe folder, so the red leg can be re-induced
    against the G0 snapshot without touching the working tree."""
    global PROBE_PARENT, PROBE_PKG, PROBE_FILE
    PROBE_PKG = os.path.normpath(os.path.abspath(path))
    PROBE_PARENT = os.path.dirname(PROBE_PKG)
    PROBE_FILE = os.path.join(PROBE_PKG, "__init__.py")

REPORT_PATH = os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
    "SkeerAutoSTEM", "live-probe.json")

VERSION_MARKER = '"live_version"'
FIXED_MAJOR_CALL = 'self._call(app, "get_major_version")'
STALE_TWO_NAME_SHAPE = '"get_major_version", "get_minor_version"'


def sha(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def version_entry(path):
    """The live_version entry, found by phrase and not by line number.

    A number into a file this batch is editing is a timestamp, not a contract: the fix
    below shifted every line after the factory. Grep the phrase, then cite the number.
    """
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        lines = handle.readlines()
    for index, line in enumerate(lines):
        if VERSION_MARKER in line:
            block = "".join(lines[index:index + 3]).strip()
            return index + 1, block
    return 0, ""


class Failure(object):
    def __init__(self, stage, message):
        self.stage = stage
        self.message = message


class FakeApplication(object):
    """Stands in for Live.Application.get_application().

    The accessors are zero-argument, exactly as Live's are. That is the point: the
    arity defect calls get_major_version("get_minor_version"), and only a zero-arg
    stand-in turns that mistake into the TypeError that Live really raises.
    """

    def get_major_version(self):
        return 12

    def get_minor_version(self):
        return 3


class FakeCInstance(object):
    """The bare minimum Live's C++ instance wrapper offers a script at construction."""

    def __init__(self):
        self.midi_map = object()


def install_stubs():
    """Stub Live and _Framework the way Live's interpreter supplies them."""

    live = type(sys)("Live")
    live.Application = type(sys)("Live.Application")
    live.Application.get_application = lambda: FakeApplication()
    sys.modules["Live"] = live
    sys.modules["Live.Application"] = live.Application

    framework = type(sys)("_Framework")
    framework.__path__ = []
    sys.modules["_Framework"] = framework

    cs_mod = type(sys)("_Framework.ControlSurface")

    class ControlSurface(object):
        def __init__(self, c_instance):
            self._c_instance = c_instance
            self.scheduled = []
            self.logged = []

        @contextlib.contextmanager
        def component_guard(self):
            yield

        def log_message(self, message, *a):
            self.logged.append(message)

        def schedule_message(self, delay, func, *a):
            self.scheduled.append((delay, func))

        def connect(self):
            pass

    cs_mod.ControlSurface = ControlSurface
    sys.modules["_Framework.ControlSurface"] = cs_mod
    framework.ControlSurface = cs_mod
    return ControlSurface


def main():
    results = []
    note = []

    def check(stage, ok, detail):
        results.append((stage, bool(ok), detail))
        return bool(ok)

    print("=== autoSTEM probe instantiation gate ===")
    print("python          : %s" % sys.version.split()[0])
    print("probe file      : %s" % PROBE_FILE)
    print("probe sha256    : %s" % (sha(PROBE_FILE) if os.path.isfile(PROBE_FILE) else "ABSENT"))
    print("harness sha256  : %s" % (sha(HARNESS) if os.path.isfile(HARNESS) else "ABSENT"))
    print("verdict file    : %s (must stay absent: %s)"
          % (REPORT_PATH, "ABSENT" if not os.path.exists(REPORT_PATH) else "PRESENT"))
    print("")

    if not os.path.isfile(PROBE_FILE):
        print("GATE COULD NOT RUN: probe source not found")
        return 2
    forged_before = os.path.exists(REPORT_PATH)
    check("S0 no pre-existing verdict file", not forged_before,
          "live-probe.json absent at entry" if not forged_before
          else "REFUSING TO RUN: live-probe.json already exists; a pass here would be "
               "indistinguishable from this gate writing it")
    if forged_before:
        return _report(results, note)

    base = install_stubs()
    sys.path.insert(0, PROBE_PARENT)
    for name in list(sys.modules):
        if name == "AutoStemProbe" or name.startswith("AutoStemProbe."):
            del sys.modules[name]

    try:
        import importlib
        probe = importlib.import_module("AutoStemProbe")
        check("S1 module imports under Live's stubs", True, "imported as %s" % probe.__name__)
    except Exception as error:
        check("S1 module imports under Live's stubs", False,
              "%s: %s" % (type(error).__name__, error))
        return _report(results, note)

    # S2 - the defect that cost a restart-free afternoon: without a factory Live has
    # nothing to call, so the script is never built and never listed.
    factory = getattr(probe, "create_instance", None)
    check("S2 module defines create_instance", callable(factory),
          "present and callable" if callable(factory)
          else "ABSENT - Live cannot instantiate this surface, so it will not appear in "
               "the Remote Control Surfaces list")
    if not callable(factory):
        return _report(results, note)

    # S3 - constructing it must not raise, and must arm the delayed run.
    try:
        instance = factory(FakeCInstance())
        ok = isinstance(instance, probe.AutoStemProbe)
        armed = bool(getattr(instance, "scheduled", []))
        check("S3 create_instance returns the surface", ok,
              "returns AutoStemProbe" if ok else "returned %r" % (type(instance).__name__,))
        check("S4 construction arms the delayed probe run", armed,
              "scheduled=%r" % (getattr(instance, "scheduled", []),))
    except Exception as error:
        check("S3 create_instance returns the surface", False,
              "raised %s: %s" % (type(error).__name__, error))
        return _report(results, note)

    # S5 - the arity defect. Static, so the gate cannot pass on a file that still has
    # the two-name call, and dynamic, so the fix is shown to return a version.
    line_no, entry = version_entry(PROBE_FILE)
    check("S5a live_version entry uses one accessor per call",
          bool(entry) and FIXED_MAJOR_CALL in entry and STALE_TWO_NAME_SHAPE not in entry,
          "line %d: %s" % (line_no, " ".join(entry.split())[:150] if entry else "MARKER NOT FOUND"))

    def tripwire(self, report):
        raise AssertionError("gate tried to write the verdict file")

    probe.AutoStemProbe._write_orig = probe.AutoStemProbe._write
    probe.AutoStemProbe._write = tripwire
    try:
        app = FakeApplication()
        major = instance._call(app, "get_major_version")
        minor = instance._call(app, "get_minor_version")
        stale = instance._call(app, "get_major_version", "get_minor_version")
        ok = (major == 12 and minor == 3
              and isinstance(major, int) and isinstance(minor, int))
        check("S5b fixed call shape returns real version numbers", ok,
              "get_major_version->%r get_minor_version->%r" % (major, minor))
        stale_is_error = isinstance(stale, str) and stale.startswith("error:")
        check("S5c stale two-name shape still yields an error string", stale_is_error,
              "old shape reports %r - proves the defect was a live TypeError, not a "
              "cosmetic preference" % (stale,) if stale_is_error
              else "old shape returned %r; the defect could not be reproduced, so S5b "
                   "is not proving anything" % (stale,))
    finally:
        probe.AutoStemProbe._write = probe.AutoStemProbe._write_orig
        del probe.AutoStemProbe._write_orig

    check("S6 verdict file still absent at exit", not os.path.exists(REPORT_PATH),
          "live-probe.json was not created by this gate"
          if not os.path.exists(REPORT_PATH) else "THE GATE CREATED THE VERDICT FILE")

    return _report(results, note)


def _report(results, note):
    print("")
    print("--- stages ---")
    failed = 0
    for stage, ok, detail in results:
        print("%-4s %-52s %s" % ("PASS" if ok else "FAIL", stage, detail))
        if not ok:
            failed += 1
    print("")
    print("GATE RESULT: %s (%d of %d stages passed)"
          % ("FAIL" if failed else "PASS", len(results) - failed, len(results)))
    print("probe sha256 at exit : %s" % (sha(PROBE_FILE) if os.path.isfile(PROBE_FILE) else "ABSENT"))
    return 1 if failed else 0


if __name__ == "__main__":
    if "--help" in sys.argv[1:] or "-h" in sys.argv[1:]:
        print(__doc__)
        print("usage: py -3.11 -B instantiation-gate.py [--probe-dir PATH] [--help]")
        sys.exit(0)
    try:
        if "--probe-dir" in sys.argv[1:]:
            _i = sys.argv[1:].index("--probe-dir") + 1
            if _i >= len(sys.argv) - 1:
                print("GATE COULD NOT RUN: --probe-dir needs a path")
                sys.exit(2)
            use_probe_dir(sys.argv[_i + 1])
        sys.exit(main())
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        print("GATE RESULT: ERROR (the gate itself failed, which is not a verdict on the probe)")
        sys.exit(2)
