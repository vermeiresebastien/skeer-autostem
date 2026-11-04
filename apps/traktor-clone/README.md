# Traktor Clone Integration

This directory contains a Traktor Pro Web DJ clone that's been connected to our **Skeer autoSTEM** back-end.

## Architecture

```
apps/traktor-clone/
├── server.ts              # Express server wrapping our autostem CLI
├── src/
│   ├── services/
│   │   ├── backendBeatAnalyzer.ts   # Replaces in-browser BeatAnalyzer
│   │   ├── backendBridge.ts         # HTTP client for our back-end
│   │   ├── stemService.ts           # Stem separation via our back-end
│   │   └── audioEngine.ts           # Audio engine (updated to use back-end)
│   └── ... (rest of Traktor clone)
└── package.json
```

## How it works

1. **Track loading**: User drags a file → `AudioEngine.loadTrackFromLocalFile()` → calls `BackendBeatAnalyzer.analyzeFile()` → server.ts calls `autostem prepare` → returns analysis (bpm, key, duration, waveform)

2. **Stem separation**: User clicks "Separate stems" → `AudioEngine.separateStems()` → `StemService.separate()` → server.ts calls `autostem run` → returns stem paths → front-end loads each stem as a separate deck

3. **Library**: Our back-end's queue.json becomes the library. `autostem enqueue` adds files without running.

## Running

```bash
cd apps/traktor-clone

# Install dependencies (Bun)
bun install

# Start the server (wraps autostem CLI)
bun run dev

# Or with our back-end
bun run dev --backend http://localhost:3000
```

The server at `http://localhost:3000` exposes:
- `POST /api/analyze` — analyze a track
- `POST /api/queue/add` — add files to queue
- `POST /api/run` — run the queue
- `POST /api/verify` — verify a result folder
- `POST /api/bench` — benchmark comparison
- `POST /api/gemini/chat` — Gemini AI chat (existing)

## Connection points

| Traktor clone's back-end | Our back-end replaces |
| --- | --- |
| `BeatAnalyzer.analyzeTrack()` | `BackendBeatAnalyzer.analyzeFile()` → `autostem prepare` |
| `AudioEngine.loadTrack()` (local file → AudioBuffer) | `AudioEngine.loadTrackFromLocalFile()` → our back-end analysis |
| Library storage (Firebase/JSON) | Our `queue.json` persistence |
| No stem separation | `StemService.separate()` → `autostem run` → deck loading |

## What's not connected yet

- **Firebase sync** — still uses the original Firebase config; our queue.json is local-only
- **MIDI controller** — not wired to our back-end (intentionally; MIDI is front-end only)
- **3D spatial booth** — works independently; could play stems from our back-end
- **Copilot** — still uses Gemini API; could be enhanced with stem-separation context

## Next steps

1. Wire up the UI to call `AudioEngine.loadTrackFromLocalFile()` on file drop
2. Add a "Separate stems" button that calls `AudioEngine.separateStems()`
3. Load stems as decks A/B/C/D after separation
4. Add a library view that reads our queue.json
5. Add a "Run separation" button in the command bar

## Testing

```bash
# Analyze a track
curl -X POST http://localhost:3000/api/analyze \
  -H "Content-Type: application/json" \
  -d '{"sourcePath": "/path/to/track.wav"}'

# Add to queue
curl -X POST http://localhost:3000/api/queue/add \
  -H "Content-Type: application/json" \
  -d '{"paths": ["/path/to/track.wav"], "dest": "/path/to/output", "stems": "all", "mode": "speed"}'

# Run queue
curl -X POST http://localhost:3000/api/run \
  -H "Content-Type: application/json" \
  -d '{"dest": "/path/to/output", "simulate": false}'

# Verify results
curl -X POST http://localhost:3000/api/verify \
  -H "Content-Type: application/json" \
  -d '{"folder": "/path/to/output/Track"}'
```

## Branch

This integration is on branch `feature/skeer-autostem-traktor-interface`.

The original Traktor clone is at `apps/traktor-clone/`. Our back-end (Skeer autoSTEM) is at `src/` and `tools/`.
