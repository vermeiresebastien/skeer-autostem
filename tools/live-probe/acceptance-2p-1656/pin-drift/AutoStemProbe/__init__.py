# Probes whether Ableton Live's Python (Live Object Model) seam can invoke
# Create > Separate Stems without any mouse or keyboard input.
#
# Lives in a Remote Scripts folder of Live's User Library, which only puts its name in Live's
# Control Surface list: Live loads a script when, and only when, that script is named in one of its
# seven control-surface slots, so one pick in Preferences is what activates this. See
# tools/live-probe/README.md for the slot it rides in, the exact click, what the census already
# settled, and how to take it back out.
#
# READ-ONLY, AND STAGE 0 IS THE ONE THAT DECIDES. Ableton's own shipped model layer puts the command
# on Live.Conversions: separate_stems, separate_stems_of_clip and can_separate_stems_in_high_quality
# are defined in live_model\Live\Conversions.pyc, can_stems_be_separated is named by
# live_model\Live\Clip.pyc, and Push2\convert.pyc proves a script running inside Live does reach a
# Conversions object through _conversions_provider. Whether Live's *script-facing* Python exposes the
# same names is what no static read of this machine can settle - a shipped script that never imports
# a module says nothing about whether that module is there. So stage 0 asks Live's own interpreter
# with hasattr, dir(), find_spec and an import, and never with a call that does something. The report
# says plainly, in decided_by and verdict, that stage 0 is the answer and stages 1-4 only corroborate.
#
# This build never calls map_midi_control_to_prompt, remove_midi_map, add_auto_mappable_control,
# execute_menu_item or trigger_menu_item, and never calls anything that could touch the set, the
# transport, a device or a MIDI mapping. The helpers that used to make those calls are deleted rather
# than disabled, so the refusal is readable in the code, not only in the report. Each stage still
# names what it refuses to do, under policy, so a blank cannot be mistaken for a negative. The one
# piece of state this script can change inside Live is which Live.* submodules stage 0 has imported,
# which is interpreter state, not the owner's set.
#
# Everything is reported to a JSON file and to Live's log, and every stage is
# independently guarded so a dead end names itself instead of killing the script.

from __future__ import absolute_import, print_function, unicode_literals

import json
import os
import sys
import traceback

import Live
from _Framework.ControlSurface import ControlSurface

REPORT_PATH = os.path.join(
    os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
    "SkeerAutoSTEM", "live-probe.json")

MENU_PATHS = [
    ("Create", "Separate Stems to New Audio Tracks"),
    ("Create", "Separate Stems in Place"),
    ("Edit", "Duplicate"),
]

# Live validates a prompt name when it is mapped, so the accepted set could only ever be read off
# the errors of a mapping attempt. This build does not attempt them: the list is reported as what the
# old experiment would have tried, so the JSON still shows the shape of a route it refuses to test.
PROMPT_CANDIDATES = [
    "Live.Create.AudioSeparation",
    "Create.AudioSeparation",
    "Live.Create.SeparateStems",
    "Live.Create.SeparateStemsToNewAudioTracks",
    "Live.Edit.AudioSeparation",
    "Live.Sequencer.AudioSeparation",
    "AudioSeparation",
    "Live.Create.InsertAudioClip",
    "Live.Undo.UndoCurrentState",
]

# Stage 1's historical names, kept as the record of the prompt-and-menu route. They are now
# inspected for what they are and never invoked: a zero-argument call was only ever a bet on how
# CPython parses arguments, and "no call that mutates a mapping" is not a thing to bet on.
CANDIDATE_NAMES = [
    ("MidiMap", "map_midi_control_to_prompt"),
    ("MidiMap", "add_auto_mappable_control"),
    ("MidiMap", "remove_midi_map"),
    ("MidiMap", "send_midi_cc"),
    ("MidiMap", "map_midi_cc"),
    ("Application", "get_menu_info"),
    ("Application", "execute_menu_item"),
    ("Application", "trigger_menu_item"),
]

# What a yes looks like, in Ableton's own words: the entry points that take the action.
COMMAND_NAMES = ["separate_stems", "separate_stems_of_clip"]

# What a partial looks like: a capability a script could read, with no command behind it.
FLAG_NAMES = ["can_separate_stems_in_high_quality", "can_stems_be_separated"]

# (module attribute on Live, the names worth finding on it or on a class inside it, and whether
# finding one of them answers the fork). MidiMap is dir-dumped only: it is what stages 1 and 3 are
# about, and its attribute list is what makes a "no prompt API" line readable off the report.
# Resolved lazily with every step guarded, so a module that is not there becomes a named dead end
# rather than an ImportError that stops the script.
LOM_TARGETS = [
    ("Conversions", COMMAND_NAMES + ["can_separate_stems_in_high_quality"], True),
    ("Clip", ["can_stems_be_separated"], True),
    ("MidiMap", [], False),
]

# A yes does not have to carry the exact name the census predicted, so any member of a Live module
# already in sys.modules whose name contains one of these fragments is reported too. sys.modules and
# dir() only: this sweep imports nothing and calls nothing.
SEEK_FRAGMENTS = ["separat", "stem"]

# Reported so a reader can tell "Live refused" from "this script refused".
FORBIDDEN_CALLS = [
    "Live.MidiMap.map_midi_control_to_prompt",
    "Live.MidiMap.remove_midi_map",
    "Live.MidiMap.add_auto_mappable_control",
    "Live.Application.execute_menu_item",
    "Live.Application.trigger_menu_item",
]

# A module exposing more classes than this is not a LOM module; the cap keeps the report bounded.
MAX_HOLDERS_PER_MODULE = 8

# Bound on the stray sweep, which walks dir() over every Live module the interpreter holds.
MAX_STRAYS = 120

PROBE_DELAY_TICKS = 40


class AutoStemProbe(ControlSurface):

    def __init__(self, c_instance):
        ControlSurface.__init__(self, c_instance)
        with self.component_guard():
            self.log_message("autoSTEM probe loaded (will run in ~%d ticks)" % PROBE_DELAY_TICKS)
            self.schedule_message(PROBE_DELAY_TICKS, self._run_probe)

    def _run_probe(self):
        report = {"stages": {}}
        try:
            self._collect(report)
        except Exception:
            report["fatal"] = traceback.format_exc()
        self._write(report)

    def _collect(self, report):
        app = Live.Application.get_application()
        report["environment"] = {
            "python": sys.version.split()[0],
            "module": __name__,
            "file": __file__,
            "live_dir": [n for n in dir(Live) if not n.startswith("_")],
            "live_version": self._call(app, "get_major_version", "get_minor_version"),
        }
        report["policy"] = {
            "read_only": True,
            "not_attempted": list(FORBIDDEN_CALLS),
            "reason": "No call that could change a mapping, the set, a device or the transport is "
                      "made anywhere in this script. The helpers that used to map and unmap a "
                      "prompt are deleted, not disabled.",
            "interpreter_state_it_can_change": "Stage 0 may import Live.* submodules that were not "
                                               "imported yet. That is the only thing it does.",
        }

        # Stage 0: the question the product's fork turns on. Read-only discovery over the objects
        # Ableton's own shipped code names, reported first so a reader never has to work through the
        # prompt-and-menu record to find the answer.
        stage0 = self._lom_seam()
        report["stages"]["0_lom_seam"] = stage0
        report["decided_by"] = "stages.0_lom_seam"
        report["answer"] = stage0["answer"]
        report["verdict"] = stage0["verdict"]
        report["corroborating_only"] = [
            "stages.1_api_surface",
            "stages.2_menu_info",
            "stages.3_prompt",
            "stages.4_prompt_names",
        ]

        # Stage 1: what the prompt-and-menu API actually contains, by inspection only - presence,
        # kind and docstring, with no invocation of any of these names.
        surface = {}
        for module_name, name in CANDIDATE_NAMES:
            key = module_name + "." + name
            holder = getattr(Live, module_name, None)
            if holder is None:
                surface[key] = {"module_present": False}
                continue
            entry = {"present": hasattr(holder, name)}
            if entry["present"]:
                entry.update(self._describe(getattr(holder, name, None)))
                entry["called"] = False
            surface[key] = entry
        report["stages"]["1_api_surface"] = surface
        report["dir_Live_MidiMap"] = self._dir_of(getattr(Live, "MidiMap", None))

        # Stage 2: can we ask Live, in words, whether the command is available? A read, and the only
        # Live.Application method this script calls besides the version numbers.
        menu_info = {}
        for path in MENU_PATHS:
            for method in ("get_menu_info", "get_menu_item_info"):
                if not hasattr(app, method):
                    menu_info["absent:" + method] = True
                    continue
                menu_info["%s:%s" % (method, " > ".join(path))] = self._call(app, method, path)
                break
        menu_info["note"] = "read of availability only; this script executes no menu"
        report["stages"]["2_menu_info"] = menu_info

        # Stages 3 and 4: the MIDI-prompt route, reported as presence plus a named refusal. Binding
        # the command to a synthetic control is the call this build does not make.
        report["stages"]["3_prompt"] = self._prompt_api()
        report["stages"]["4_prompt_names"] = self._prompt_names_report()
        report["dir_c_instance"] = [n for n in dir(self._c_instance) if not n.startswith("__")]

    def _lom_seam(self):
        """
        Does Live's script-facing Python reach the separation command?

        Each target module is resolved through four routes in order and every route that was tried
        is recorded with what it said: the attribute on the loaded Live package, an entry already in
        sys.modules, a find_spec lookup (pure metadata, nothing executed), then the import. The
        first two answer "is it loaded"; the last two answer "does it exist", which is the half the
        file census on this machine could not settle. Everything after resolution is hasattr and dir.
        """
        out = {
            "targets": [],
            "found": [],
            "missing": [],
            "strays": [],
            "live_submodules_in_sys_modules": sorted(
                key for key in list(sys.modules) if key == "Live" or key.startswith("Live.")),
            "answer": "inconclusive",
            "verdict": "",
        }
        try:
            for module_name, names, decides in LOM_TARGETS:
                target = self._resolve_target(module_name, names, decides)
                out["targets"].append(target["report"])
                out["found"].extend(target["found"])
                out["missing"].extend(target["missing"])
        except Exception:
            out["fatal"] = traceback.format_exc()
        try:
            out["strays"] = self._stray_names()
        except Exception as error:
            out["strays_error"] = str(error).splitlines()[0][:300]
        out.update(self._verdict(out))
        return out

    def _resolve_target(self, module_name, names, decides):
        module, routes = self._resolve_module(module_name)
        report = {
            "module": module_name,
            "looked_for": list(names),
            "resolves": module is not None,
            "routes_tried": routes,
        }
        found, missing = [], []
        if module is None:
            report["holders"] = []
            report["dir"] = {}
            for name in names:
                missing.append({
                    "scope": "Live." + module_name + "." + name,
                    "reason": "module not reachable by attribute, sys.modules, find_spec or import",
                    "routes_tried": routes,
                })
            if not names:
                missing.append({
                    "scope": "Live." + module_name,
                    "reason": "module not reachable",
                    "routes_tried": routes,
                })
            return {"report": report, "found": found, "missing": missing}

        holders = self._holders(module, module_name)
        report["resolved_by"] = self._winning_route(routes)
        report["holders"] = [label for label, _ in holders]
        report["dir"] = dict((label, self._dir_of(value)) for label, value in holders)
        for label, holder in holders:
            for name in names:
                present = self._has(holder, name)
                if present is None:
                    missing.append({"scope": label + "." + name, "reason": "hasattr raised"})
                    continue
                if not present:
                    continue
                record = {
                    "scope": label + "." + name,
                    "module": module_name,
                    "name": name,
                    "decides": bool(decides and name in COMMAND_NAMES),
                    "is_flag": name in FLAG_NAMES,
                    "script_facing": True,
                }
                record.update(self._describe(getattr(holder, name, None)))
                found.append(record)
        if names and not any(f["module"] == module_name for f in found):
            missing.append({
                "scope": "Live." + module_name,
                "reason": "module reachable (by %s) but none of %s exists on it or on its %d "
                          "class(es)" % (self._winning_route(routes), ", ".join(names),
                                          len(holders)),
                "holders_searched": [label for label, _ in holders],
            })
        return {"report": report, "found": found, "missing": missing}

    @staticmethod
    def _winning_route(routes):
        for label, said in routes:
            if said in ("present", "ok"):
                return label
        return "none"

    def _resolve_module(self, module_name):
        """Four ways in, tried in order, all guarded: returns (module_or_None, routes)."""
        routes = []
        key = "Live." + module_name
        module = getattr(Live, module_name, None)
        routes.append(("attribute on Live", "present" if module is not None else "absent"))
        if module is None:
            module = sys.modules.get(key)
            routes.append(("sys.modules", "present" if module is not None else "absent"))
        if module is None:
            try:
                import importlib.util
                spec = importlib.util.find_spec(key)
                routes.append(("find_spec", "None" if spec is None else
                               "found, origin=%r" % (getattr(spec, "origin", None),)))
            except Exception as error:
                routes.append(("find_spec", "error: " + str(error).splitlines()[0][:200]))
        if module is None:
            try:
                __import__(key)
                module = sys.modules.get(key) or getattr(Live, module_name, None)
                routes.append(("import", "ok" if module is not None else "imported, not found"))
            except Exception as error:
                routes.append(("import", "error: " + str(error).splitlines()[0][:300]))
        return module, routes

    @staticmethod
    def _holders(module, label):
        r"""
        The module itself plus every class it exposes, because a LOM name can live on either:
        Push2\convert.pyc reaches a Conversions *object*, and can_stems_be_separated is a property
        on a class. getattr over a module's own dir() list is a read, and nothing here touches a
        live song, track or clip object.
        """
        out = [("Live." + label, module)]
        for name in AutoStemProbe._dir_of(module):
            if len(out) >= MAX_HOLDERS_PER_MODULE:
                break
            value = getattr(module, name, None)
            if value is None or isinstance(value, type(module)):
                continue
            if isinstance(value, type):
                out.append(("Live.%s.%s" % (label, name), value))
        return out

    @staticmethod
    def _has(holder, name):
        try:
            return bool(hasattr(holder, name))
        except Exception:
            return None

    @staticmethod
    def _stray(scope, name, script_facing):
        return {
            "scope": scope + "." + name,
            "module": scope,
            "name": name,
            "script_facing": script_facing,
        }

    @staticmethod
    def _stray_names():
        """
        dir() over every Live-related module already in sys.modules - and over the classes each one
        exposes, one level deep - looking for a member name containing a fragment from
        SEEK_FRAGMENTS. The class level is where a LOM name actually sits (can_stems_be_separated is
        a property on a class, and Push's evidence points at a Conversions object), so a
        module-only sweep would miss the very shape the census found. A hit outside the
        script-facing Live package - live_model, Push2 - is corroboration, never the yes: it proves
        the name exists in the interpreter, not that a remote script can reach it. Everything is
        still hasattr/dir; MAX_STRAYS bounds the report.
        """
        out = []
        for key in sorted(sys.modules):
            script_facing = key == "Live" or key.startswith("Live.")
            if not script_facing and ".Live." not in key and "live_model" not in key:
                continue
            module = sys.modules.get(key)
            if module is None:
                continue
            for name in AutoStemProbe._dir_of(module):
                if len(out) >= MAX_STRAYS:
                    return out
                if any(fragment in name.lower() for fragment in SEEK_FRAGMENTS):
                    out.append(AutoStemProbe._stray(key, name, script_facing))
                value = getattr(module, name, None)
                if not isinstance(value, type):
                    continue
                for member in AutoStemProbe._dir_of(value):
                    if len(out) >= MAX_STRAYS:
                        return out
                    if any(fragment in member.lower() for fragment in SEEK_FRAGMENTS):
                        out.append(AutoStemProbe._stray(key + "." + name, member, script_facing))
        return out

    @staticmethod
    def _verdict(out):
        """
        One line, four states, each dead end named. Derived from the stage's own records so the
        sentence cannot disagree with the data above it.
        """
        if out.get("fatal"):
            return {"answer": "inconclusive", "verdict":
                    "INCONCLUSIVE (stages.0_lom_seam): stage 0 raised before it could answer; see "
                    "its fatal key. The fork question is still open, and the later stages say "
                    "nothing about it."}

        yes = [f for f in out["found"] if f["decides"]]
        flags = [f for f in out["found"] if f["is_flag"]]
        strays = [s for s in out["strays"]
                  if s["script_facing"] and s["name"] not in COMMAND_NAMES + FLAG_NAMES]
        reachable = [t for t in out["targets"] if t["resolves"] and t["looked_for"]]
        unreachable = [t["module"] for t in out["targets"]
                       if not t["resolves"] and t["looked_for"]]
        extra = ""
        if strays:
            extra = " Stray script-facing names in the same shape: " + \
                    ", ".join(s["scope"] for s in strays) + "."

        if yes:
            return {"answer": "yes", "verdict":
                    "YES (stages.0_lom_seam): Live's script-facing Python exposes %s. A remote "
                    "script can name the separation command, which is the seam the product's fork "
                    "asked for; invoking it is the next question, not this one.%s" % (
                        ", ".join(f["scope"] for f in yes), extra)}
        if flags:
            return {"answer": "partial", "verdict":
                    "PARTIAL (stages.0_lom_seam): %s %s readable from a script but no command entry "
                    "point (%s) is, so Live can be asked whether something is separable and cannot "
                    "be told to separate it.%s" % (
                        ", ".join(f["scope"] for f in flags),
                        "are" if len(flags) > 1 else "is",
                        ", ".join(COMMAND_NAMES), extra)}
        if reachable:
            also = "" if not unreachable else \
                " Live.%s was not reachable at all." % ", Live.".join(unreachable)
            plural = len(reachable) > 1
            return {"answer": "no", "verdict":
                    "NO (stages.0_lom_seam, dead end 2): %s %s reachable, but none of %s exists on "
                    "%s or on %s classes; the dir() dumps are in this report. The objects are there "
                    "and the command is not.%s%s" % (
                        ", ".join("Live." + t["module"] for t in reachable),
                        "are" if plural else "is",
                        ", ".join(COMMAND_NAMES + FLAG_NAMES),
                        "them" if plural else "it",
                        "their" if plural else "its", extra, also)}
        return {"answer": "no", "verdict":
                "NO (stages.0_lom_seam, dead end 1): Live.%s is not reachable from a remote script "
                "at all - attribute, sys.modules, find_spec and import each came back empty, with "
                "what they said recorded in targets - and no script-facing Live member names a "
                "separation entry point. There is no LOM route to the command; UI Automation stays "
                "the only one.%s" % (" or Live.".join(unreachable) or "Conversions or Live.Clip",
                                     extra)}

    def _call(self, target, method, *args):
        """
        Call and report, never raise. Used only on names that read - the version numbers and the
        menu-availability query. Nothing in FORBIDDEN_CALLS reaches here.
        """
        try:
            value = getattr(target, method)(*args)
            return list(value) if isinstance(value, tuple) else value
        except Exception as error:
            return "error: " + str(error).splitlines()[0][:300]

    @staticmethod
    def _describe(value):
        """What a member is, without asking it to do anything."""
        out = {"kind": type(value).__name__}
        doc = getattr(value, "__doc__", None)
        if doc:
            out["doc"] = " ".join(str(doc).split())[:240]
        try:
            out["repr"] = repr(value)[:200]
        except Exception as error:
            out["repr_error"] = str(error).splitlines()[0][:120]
        return out

    @staticmethod
    def _dir_of(holder):
        if holder is None:
            return []
        return [n for n in dir(holder) if not n.startswith("_")]

    def _prompt_api(self):
        """The prompt route, inspected and not attempted."""
        out = {}
        out["midi_map_handle"] = repr(getattr(self._c_instance, "midi_map", "absent"))[:80]

        for name in ("enable_prompt_device_support", "create_prompt_device", "num_midi_input_ports"):
            out["c_instance." + name] = hasattr(self._c_instance, name)

        midi_map = getattr(Live, "MidiMap", None)
        if midi_map is None:
            out["verdict"] = "no Live.MidiMap module in Live's Python"
            return out
        for name in ("map_midi_control_to_prompt", "add_auto_mappable_control", "remove_midi_map"):
            entry = {"present": hasattr(midi_map, name), "attempted": False}
            if entry["present"]:
                entry.update(self._describe(getattr(midi_map, name, None)))
            out["Live.MidiMap." + name] = entry
        out["verdict"] = ("not attempted: binding a prompt is a mapping call, and this build makes "
                          "none (see policy). Presence and shape only - stage 0 is what decides.")
        return out

    def _prompt_names_report(self):
        """
        The candidate prompt names are reported as what the old experiment would have mapped and
        unmapped in the same call. Nothing is mapped now: whether Live would accept a name can only
        be learned by asking it to map one, which is exactly the mutation this build refuses.
        """
        return {
            "attempted": False,
            "accepted": [],
            "candidates": list(PROMPT_CANDIDATES),
            "verdict": "not attempted: Live validates a prompt name only by mapping it, and mapping "
                       "is refused here. A blank on this stage is a refusal, not a negative.",
        }

    def _write(self, report):
        report["written_utc"] = self._utc()
        try:
            directory = os.path.dirname(REPORT_PATH)
            if not os.path.isdir(directory):
                os.makedirs(directory)
            with open(REPORT_PATH, "w") as handle:
                json.dump(report, handle, indent=2, default=str)
        except Exception:
            self.log_message("probe could not write its report: " + traceback.format_exc())
        # The verdict leads the log: Live's log truncates, and the answer must not be what falls off
        # the end.
        self.log_message("autoSTEM probe verdict [%s]: %s" % (
            report.get("answer", "?"), report.get("verdict", "stage 0 did not report")))
        self.log_message("autoSTEM probe report: " + json.dumps(report, default=str)[:2000])

    @staticmethod
    def _utc():
        import datetime
        return datetime.datetime.utcnow().isoformat() + "Z"
# drift for the pin gate; added by the harness proof
