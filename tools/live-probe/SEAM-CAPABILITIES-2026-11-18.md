# The seam, stated as three layers — measured from files on 2026-11-18

Scope: this is a **file-read** derivation. No socket was opened, nothing was sent to
`127.0.0.1:9877`, Live was not attached, launched, restarted or asked to do anything, and no file
outside `tools/live-probe/` was written. Every number below came from a command in this session; the
"what moved" section names each assertion in the existing records that this replaces.

The single sentence this file exists to prevent: **"AbletonMCP has 19 capabilities" and "the wrapper
has 37 tools" are not two measurements of one thing.** They are different layers, and neither one is
what our own probe is allowed to call.

## 1. The three layers

| layer | what it is | count measured here | where it lives |
|---|---|---|---|
| **1. installed Live-side script** | `AbletonMCP` remote script, a `ControlSurface` running **inside Live's pid**, listening on loopback TCP 9877 | **19 advertised**, **37 dispatched** | `C:\Users\Sebastien\Documents\Ableton\User Library\Remote Scripts\AbletonMCP\__init__.py` |
| **2. Qoder MCP wrapper** | `ableton_mcp` 1.4.5, launched by `uvx`, speaks JSON to layer 1 and exposes MCP tools to the model | **37 MCP tools** | uv cache + `~\.qoder\settings.json` |
| **3. our harness allowlist** | what **our** probe may call in the offline stub harness | **4 permitted readers** | `tools/live-probe/probe-harness.py` (`PERMITTED_READERS`) |

Layer 3 is unaffected by either of the other two. Layers 1 and 2 happen to both be 37 today for
different reasons — see §4 — and the coincidence is why the numbers must never be quoted without
their layer.

## 2. Layer 1 — what the installed script actually is and does

Bytes on disk (read this session):

| file | bytes | sha256 | mtime |
|---|---|---|---|
| `Remote Scripts\AbletonMCP\__init__.py` | 111,485 | `11d3eb5da3e246c2fa304839082af3e0da00a6d9cbe2b438085b261302f4002b` | 2026-10-24 03:41:49 |
| `Remote Scripts\AbletonMCP\__pycache__\__init__.cpython-311.pyc` | 125,855 | `077e635a8b03ebf4ab2d02dd0d60fded6fd4c7d19126da1ba083fc86ac56181a` | — (header: src 111,485, src-mtime 2026-10-24 03:41:49) |

Constants, read from the file: `SCRIPT_VERSION = "1.7.1"` (`:37`), `PROTOCOL_VERSION = 1` (`:38`),
`DEFAULT_PORT = 9877` (`:19`), `HOST = os.environ.get("ABLETON_MCP_HOST", "127.0.0.1")` (`:29`),
`MAX_REQUEST_BYTES = 16 * 1024 * 1024` (`:33`). The comment above `SCRIPT_VERSION` (`:35-36`) says it
is "bumped whenever the TCP command surface changes; the MCP server compares this to
`EXPECTED_REMOTE_SCRIPT_VERSION`" — i.e. **`script_version` is the script's own command-surface
version, not the package version**. Package 1.4.5 shipping script 1.7.1 is correct, not a
contradiction, and it is proven below rather than assumed.

**Advertised: 19 names** — `SCRIPT_CAPABILITIES` at `:40-60`, returned verbatim by
`_get_script_info()` (`:517-527`). Partition, checked name by name against the list:

- 7 reads: `get_session_info`, `get_track_info`, `get_script_info`, `get_clip_notes`,
  `get_device_parameters`, `get_session_snapshot`, `get_arrangement_clips`
- 1 passive drain: `drain_passive_events`
- 11 writers: `set_device_parameter`, `create_midi_track`, `create_audio_track`, `create_clip`,
  `create_audio_clip`, `add_notes_to_clip`, `load_instrument_or_effect`,
  `duplicate_session_clip_to_arrangement`, `create_locator`, `delete_clip`, `clear_notes_from_clip`

**Dispatched: 37 names.** The dispatcher is one leading `if command_type == "get_script_info"`
(`:283`) followed by **36** `elif command_type …` lines; those 36 lines hold 38 literal
occurrences / 36 distinct names (one line tests three names with `in [...]`), and the leading `if`
adds the 37th. The 18 names the handshake does **not** advertise:

- 6 more reads: `get_browser_tree`, `get_browser_categories`, `get_browser_item`,
  `get_browser_items`, `get_browser_items_at_path`, `inspect_rack`
- 12 more mutators: `start_playback`, `stop_playback`, `fire_clip`, `stop_clip`, `set_tempo`,
  `set_current_song_time`, `set_track_name`, `set_clip_name`, `set_arrangement_clip_name`,
  `load_browser_item`, `switch_to_arrangement_view`, `map_rack_magnitude`

So the shipped surface is 7 + 1 + 11 + 6 + 12 = **37**; the handshake covers 19 of them. An unknown
name returns `{"status":"error", "message":"Unknown command: <name>"}` (`:505-506`), so the
dispatcher is a closed list, not an evaluator: `importlib`, `__import__`, `exec(`, `eval(`,
`compile(` and `sys.path` each occur **0** times in the file (positive controls in the same read:
`import` statements 11, `def` 86 — the same instrument the README §11 uses).

`_safe_song_property()` (`:529-535`) is a fixed-attribute getter (`getattr(self._song, attr)` called
with literal attribute names), not a generic "read any LOM member" entry point.

### 2b. What Live did with our own probe — the pyc, and what it does and does not prove

Our probe, installed beside it:

| file | bytes | sha256 | mtime |
|---|---|---|---|
| `Remote Scripts\AutoStemProbe\__init__.py` | 25,992 | `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` | 2026-11-16 15:44:15 |
| `Remote Scripts\AutoStemProbe\__pycache__\__init__.cpython-311.pyc` | 32,031 | `5d8af3d852a9be8909415c1e18cd04f4270ad5f25ce95dcb5335353023a14df4` | **2026-11-16 19:32:18** |

The installed source **is** the pinned build (`b995063b…`), so the open question the acceptance
report left at its §11 — "whether the probe installed in the owner's User Library is byte-identical
to `b995063b…`" — is answered from files: **yes**.

**Moved under this report, 2026-11-18 09:45:11:** the *repo* copy at `tools/live-probe/AutoStemProbe/
__init__.py` is now **26,112 B / 550 lines / `sha256 f34292cf12df8d27a0bc235da5870837abe143509a9a267f2b03950542299c29`**
— the factory change, which the brief said was dispatched to another lane. The *installed* copy and
its cache are unchanged (25,992 B / `b995063b…` / pyc `5d8af3d8…`, header still naming 25,992 B and
15:44:15). So the "yes" above is true of the 25,992 B build and **not** of the repo tree now; the new
bytes are not characterised here, and `probe-harness.py` is still `5de18524…` as of this read (no
re-pin has landed).

The installed cache is current, not stale: magic `a70d0d0a` (CPython 3.11), `flags=0`, header
`src_size = 25,992`, `src_mtime = 2026-11-16 15:44:15` — an exact header match for the file beside
it. The README §10 bullet that said both caches "belong to the first build" and named a 10,593-byte
source describes the *previous* pyc; it was overwritten on 2026-11-16.

**What the write time lands inside.** `Log.txt` at 19:32:14-19:32:31 on 2026-11-16 records a Live
startup: crash-recovery prompt for an untitled set, **"Restore Document: Rejected by the user"**,
`LoadPrefs` finishing at 19:32:18.948, then `AMidiIO: Midi Remote Scripts:` at 19:32:29.556 and every
surface initialising at 19:32:30 (`Push`, `LK_Bridge`, `MIDI_Mix`, `Roland_A_PRO`, `AbletonMCP …
script v1.7.1 … Server started on port 9877`). The pyc's write time 19:32:18 sits between `LoadPrefs`
and surface construction — the window in which Live enumerates the Remote Scripts folder.

**The honest bound.** A bytecode cache proves *some* CPython 3.11 compiled the file against that
source; it does not name the writer. Live's embedded interpreter is the plausible one — the folder is
Live's own script folder, the write falls in Live's enumeration window, and three sibling scripts in
the same folder carry the same 311 magic (`LK_Bridge` ×3, `Plugindex` ×20, `Tap` ×2) — but a
standalone `pythoncore-3.11` exists on this machine (`py -0p`) and its `py_compile` would write an
identical header. **Inferred: Live imported it. Not proven: the writer's identity.** The instrument
that would settle it is Live's own logging of the enum, which does not name candidates.

Two consequences, both narrower than "the probe has run":

- Module-level code **did** execute inside some 3.11 interpreter at that instant (imports, constants).
  If that interpreter is Live's, then "no code of ours has ever executed inside Live's pid" is false
  as stated — what is true is that **no stage has ever run, no seam query has ever been made, and no
  socket was ever opened by our probe**. It has no module-level factory (`create_instance` is absent),
  so Live cannot construct the surface or offer it in the dropdown — which is consistent with
  `AutoStemProbe` occurring **0** times in `Preferences.cfg` in either encoding.
- The earlier argument from log silence is **refuted as an argument**, see §5.

## 3. Layer 2 — the wrapper, and what "37 tools" is a measurement of

Configuration, read this session: `C:\Users\Sebastien\.qoder\settings.json` (652 B,
`sha256 1880c09c9299beda58f87aa83e955b93258e0e296da284f87c540fd92e6472d9`):

```json
"AbletonMCP": { "type": "stdio", "command": "uvx", "args": ["ableton-mcp"],
                "env": { "ABLETON_MCP_DISABLE_TELEMETRY": "true" } }
```

**No version pin**, so the resolved version is whatever `uvx` picks. What it has picked on this
machine: `%LOCALAPPDATA%\uv\cache\archive-v0\` holds **359** archive directories, of which **30**
contain `Lib\site-packages\ableton_mcp-1.4.5.dist-info` — all 30 are `ableton_mcp-1.4.5`, no other
version present. Each of those 30 also carries
`MCP_Server\bundled_ableton_remote_script\AbletonMCP_init.py`, and **all 30 are byte-identical to the
installed script**, `sha256 11d3eb5d…f4002b`, `SCRIPT_VERSION = "1.7.1"`. That is the provenance of
layer 1's bytes: the package ships exactly the script that is installed, and 1.4.5 → 1.7.1 is one
package version and one script version, not two claims about one number.

**The tool count, measured from the package:** `MCP_Server\server.py` (60,426 B,
`sha256 938345b509aef7cdc6fcaabad4b00c85e7338b5dc4786751193c8957fe4515a4`) contains **37**
`@mcp.tool()` decorators and no other tool registrations anywhere in the package (counted across
`MCP_Server\**\*.py`: 37 total). The first one, `set_dataset_consent` (`:358-359`), is deliberately
undecorated by the telemetry wrappers (its own comment at `:371-372` explains why).

**The live state of the connector is a separate fact with its own timestamps**, and it must travel
with any quote of 37: the PM observed the connector connected with 37 tools at 15:43, disconnected at
16:10, reconnected around 19:00 with the same 37 and disconnected minutes later (2026-11-17). In this
session's tool catalogue the `abletonmcp` server is currently **not** connected. So "37" is now a
property of the 1.4.5 package bytes **and** was a property of the live connector on those four
occasions; it is not a property of the installed script, which has 19 advertised and 37 dispatched.

**Layer 2 → layer 1 routing.** `server.py` calls the script through `send_command("<name>", …)`; the
literal names it sends number **29**. The handshake path (`script_handshake.py`, 4,017 B,
`sha256 a05ad8c0b88ceb46866ca6f7fe6dab0bda6c7755582569a7a8f374404621d012`) sends `get_script_info`
at connect time (`:70`) and has a fallback for scripts too old to know it. Some tool names differ
from the command they send (`duplicate_to_arrangement` → `duplicate_session_clip_to_arrangement`,
`set_arrangement_time` → `set_current_song_time`, `get_remote_script_info` → `get_script_info`), which
is why the two 37s are not the same 37.

## 4. Layer 3 — what our probe may call (unchanged by any of the above)

`PERMITTED_READERS` in `tools/live-probe/probe-harness.py` (48,884 B,
`sha256 5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1`):

- `Application.get_application`
- `Application.get_major_version`
- `Application.get_minor_version`
- `Application.get_menu_info`

Four members. This is a property of our harness, not of AbletonMCP; no count from layers 1 or 2
changes it, and the harness gate (exit 3/4) is what enforces it.

## 5. What moved — each corrected assertion, with the file that carries it

1. **`README.md:10`** — "`get_script_info` (`script_version 1.7.1`, `protocol_version 1`, 19
   capabilities)". Still true of layer 1, but it invited the wrapper's count to be read as the same
   number. Amended in place with the layer label and the 37-dispatched figure, pointing here.
2. **`README.md` §10, the "Two read commands answered" bullet** — "**19** entries in
   `SCRIPT_CAPABILITIES`" needs the second number beside it: 37 dispatched. Amended.
3. **`README.md` §10, the bytecode-cache bullet** — "The bytecode caches on disk belong to the first
   build … who compiled the 311 file remains blocked". Superseded for the *installed* cache: it now
   names the current source and was written at 19:32:18 inside a Live startup. Narrowed to: compiled
   by a CPython 3.11 at that instant, writer not provable from the header. The repo's
   `__init__.cpython-312.pyc` (12,757 B, 2026-11-15 20:25:07) is still the unrelated stray.
4. **`README.md` §10, the "click has not been spent" bullet** — its argument included "`AutoStemProbe`
   occurs **0** times in `Log.txt`, with `AbletonMCP`/`Push` as positive controls". **That argument is
   refuted by the Tap control**: `Tap\__pycache__\Tap.cpython-311.pyc` exists (117,404 B, source
   114,763 B, written 2026-02-13 02:06:31), and `Tap` occurs **0** times in `Log.txt` case-sensitively
   (the case-insensitive hits are all `InitApplication`) — so a compiled
   script can leave no name in the log at all. Log mentions track selection and runtime chatter, not
   enumeration. (In `Preferences.cfg` `Tap`'s only hit is a MIDI *port* name, not a surface entry, so
   "not selected" is a second reason it is unlogged; that distinction is now stated rather than
   blurred.) Replaced with the evidence that does carry the claim: `AutoStemProbe` **0** in
   `Preferences.cfg` in both encodings, with `AbletonMCP` **3** (two surface-list entries plus its
   port) and `LK_Bridge` **1** (surface list) as the matched controls — that instrument answers "is
   it selected?" and returns 0 for the probe — plus no module-level factory, and no
   `%LOCALAPPDATA%\SkeerAutoSTEM\live-probe.json`.
5. **`README.md` §12, the "advertised capability list under-reads the surface" bullet** — "…and more"
   is replaced by the exact 18-name list and the 37 count.
6. **`README.md` §13, "no code at all has ever been witnessed against a running Ableton Live"** —
   qualified: no *stage* and no *seam query* has ever run against Live, but module-level import did
   happen at 2026-11-16 19:32:18 if the cache is Live's (inferred, §2b).
7. **`acceptance-2p-1656/report.md:313`** — "**11 writers** (out of 19 capabilities, which partition
   as 7 reads + `drain_passive_events` + these 11)". Correct as a statement about the handshake, and
   **not edited here** (that file is the Test Engineer's). It must not be quoted as the whole surface:
   the dispatcher accepts 37 names, of which 23 change the set, the transport or the view (11
   advertised + 12 of the extras).
8. **The PM's Fact 1 wording** ("Live … COMPILED our probe") — narrowed to "a CPython 3.11 compiled
   it during Live's startup window; the header does not name the writer".
9. **The PM's Fact 2 framing** ("37 is a reading of a live configuration") — still true, and now also
   a static property of `ableton_mcp` 1.4.5's `server.py` (`37` decorators). Both readings should be
   quoted with their own base.
10. **`README.md` §11, the pid/continuity bullet** — "the 14:35 socket, the 16:13:19 MIDI re-init and
   this minute are one continuous Live session" is superseded: `Log.txt` records a fresh launch at
   **2026-11-18 08:45:13.062**, crash recovery **rejected by the user** at **08:46:08.388** with
   `DEFAULT.als` then loaded (and an earlier launch at 2026-11-17 16:33:36). Annotated in place; the
   pid-`35676` question is deliberately left to a fresh `netstat`/`tasklist` read rather than asserted.

Post-edit measurement (this lane, 2026-11-18): after the README edits above, `README.md` is
**1,180 lines / 103,270 B / `sha256 a13e6376f876a4d12c85ce29559f25512130d62a13ce71b14787d8f0cf0c62f0` /
0 CR bytes** — it was 1,120 / 97,567 / `4d4a9e0d…` before them.
`probe-harness.py` is `5de18524…` and has not moved. `AutoStemProbe/__init__.py` was **not touched by
this lane** but did move under it at 09:45:11 (see §2b); the folder is still untracked
(`?? tools/live-probe/`). The README is LF-only, so a staged blob's `git show <sha>:README.md |
sha256sum` would reproduce its number, while a fresh checkout's worktree hash would not under
`core.autocrlf=true`.

## 6. The security line, stated once

The seam is **unauthenticated JSON over loopback TCP 9877, inside Live's process**. Any process
running as this user can connect and send any of the 37 dispatched commands; the script's own comment
(`:20-28`) says so and explains why the bind is narrowed to `127.0.0.1` rather than `0.0.0.0` — an
environment variable (`ABLETON_MCP_HOST`) widens it, and nothing verifies the change. The 19-name
handshake **understates** the surface by 18 names, 12 of which mutate the set, the transport or the
view; there is no token, no per-connection authorisation, and no peer check. The script is
third-party (`ableton_mcp` 1.4.5, MIT, upstream `github.com/ahujasid/ableton-mcp`), installed
deliberately for this bench — nothing here is a defect report against it. Our probe is the opposite
shape: four permitted readers, no socket, no writer, and it never even constructs a surface.

## 7. Files consulted, with sha256 (all read this session)

| path | bytes | sha256 |
|---|---|---|
| `…\Remote Scripts\AbletonMCP\__init__.py` | 111,485 | `11d3eb5da3e246c2fa304839082af3e0da00a6d9cbe2b438085b261302f4002b` |
| `…\Remote Scripts\AbletonMCP\__pycache__\__init__.cpython-311.pyc` | 125,855 | `077e635a8b03ebf4ab2d02dd0d60fded6fd4c7d19126da1ba083fc86ac56181a` |
| `…\Remote Scripts\AutoStemProbe\__init__.py` | 25,992 | `b995063b665eb170442007c5f039baa2d7d44e12c9184e86b37d8879447220f5` |
| `…\Remote Scripts\AutoStemProbe\__pycache__\__init__.cpython-311.pyc` | 32,031 | `5d8af3d852a9be8909415c1e18cd04f4270ad5f25ce95dcb5335353023a14df4` |
| `…\Remote Scripts\Tap\__pycache__\Tap.cpython-311.pyc` | — | header `a70d0d0a`, src 114,763, src-mtime 2025-11-20 15:41:00 |
| `C:\Users\Sebastien\.qoder\settings.json` | 652 | `1880c09c9299beda58f87aa83e955b93258e0e296da284f87c540fd92e6472d9` |
| `…\uv\cache\archive-v0\<30 dirs>\Lib\site-packages\MCP_Server\server.py` (1.4.5) | 60,426 | `938345b509aef7cdc6fcaabad4b00c85e7338b5dc4786751193c8957fe4515a4` |
| `…\MCP_Server\script_handshake.py` | 4,017 | `a05ad8c0b88ceb46866ca6f7fe6dab0bda6c7755582569a7a8f374404621d012` |
| `…\MCP_Server\remote_script_install.py` | 11,683 | `6eb6483698789e28ec3dd5665d168a1e52daf5add7b25736e9257308fff7f4a1` |
| `…\MCP_Server\bundled_ableton_remote_script\AbletonMCP_init.py` (×30, identical) | 111,485 | `11d3eb5da3e246c2fa304839082af3e0da00a6d9cbe2b438085b261302f4002b` |
| `…\Ableton\Live 12.3.2\Preferences\Log.txt` | 9,119,644 (mtime 2026-11-18 09:31:37) | `bde58abec7f24c12…` (prefix; the file is live and grows) |
| `…\Ableton\Live 12.3.2\Preferences\Preferences.cfg` | 131,382 (mtime 2026-11-18 09:27:29) | `05b9cfc25648603f…` (prefix) |
| `tools/live-probe/probe-harness.py` | 48,884 | `5de1852479f74b6f09f6516c9972aad82079c4429b19a915989e7ad5df9102b1` |

Instrument notes: every hash is `hashlib.sha256` over the raw bytes (a Python read, not `sha256sum`,
so no text-mode translation of any kind); the pyc headers are `struct.unpack` on bytes 4-16 of the
file (`flags`, `src_mtime`, `src_size`); the counts are `re.findall` over the decoded source with the
pattern printed in the sentence that uses it.

## 8. What stayed unverified

- **Who wrote the 311 cache** for our probe (Live vs a standalone `py -3.11`). Live is running at
  that timestamp; the header cannot distinguish.
- **Whether the live wrapper enumerated exactly these 37 tools** on 2026-11-17 at 15:43 and ~19:00 —
  the PM observed it; this lane could not re-read the connector (it is disconnected).
- **What happens when Live's enumeration meets a folder with no `create_instance`** — observed
  indirectly (0 cfg mentions, nothing offered), not observed in Live's UI.
- **`Live.Conversions` / `Live.Clip` on a real interpreter** — unchanged and still the click's
  question; nothing in this file bears on it.
- **The next `uvx` resolution** — no pin, so 1.4.5 is what the cache has, not what it must pick.
