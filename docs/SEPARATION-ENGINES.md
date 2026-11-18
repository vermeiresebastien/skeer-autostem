# Separation without Ableton Live — the plan

Question: can autostem separate stems without launching Ableton Live?
Short answer: yes, and on this machine the Live-free route is probably **faster than Live**,
because Live ran the job on the CPU while a 2080 Ti sat idle.

This was a plan when it was written. Phase 1 (the engine seam) is now built, and the §6 bug is
fixed; the rest is still a plan, and the phase that decides whether the rest is worth building
is Phase 0.

---

## 1. What Live's engine actually is (measured tonight)

From `Live 12.3.5`'s own log while autostem drove a real job:

```
Stem Separation: separator: musicai            library-version: 1.2.1
Stem Separation: model: adaeb0537fc900e13438fd87cf4ba43b.dat
Stem Separation: accelerator: CPU              effective-provider: ORT/CPU EP
Stem Separation: sample-duration-ms: 293314   clip-source: Session
Stem Separation (4-stems): result: success
Stem Separation: processing-time-ms: 218213   (3 min 38 s for a 4 min 53 s track)
Stem Separation: memory-usage-max: R=2.9 GB, V=8.9 GB
```

So the feature is: **Music AI's separator, running inside ONNX Runtime**, from a 445 MB model
in `C:\ProgramData\Ableton\.Live 12 Suite_updated\Resources\Builtin\Models\`. Real-time factor
0.74 on CPU. `onnxruntime_abl.dll` (18 MB) and `musicai.dll` (3.7 MB) sit in the Program folder.

Two consequences:

- The engine is *not* something only Ableton can do. It is an ONNX graph + a Demucs-lineage
  hybrid-transformer separator. Anything that can run ONNX can run that family of model.
- Live is not even using the GPU. The whole Live detour costs a 3:38 render, ~45 s of process
  start-up, one exclusive desktop, and all of the automation fragility documented in
  `docs/LIVE-AUTOMATION.md`.

## 2. What we cannot do

**Reusing Ableton's model files is off the table.** The `.dat` files are protected: the large
ones start with a `.MIA` container header and their payload is high-entropy (no readable ONNX
structure in the first 8 MB), and the smaller ones start with `Salted__`, i.e. an OpenSSL
encrypted blob. Reading them means circumventing a technical protection measure and breaching
the Ableton EULA. For a product that is sold, that is not a risk worth taking for any quality
gain. This route is closed, and the plan does not consider it further.

## 3. The options

Numbers marked *(est)* are engineering estimates, not measurements. They get measured in
Phase 0 before anything is chosen.

| | A. ONNX Runtime in .NET | B. Python sidecar (demucs) | C. Cloud API | D. DSP-only |
| --- | --- | --- | --- | --- |
| What | `Microsoft.ML.OnnxRuntime` + an open separator model from the Demucs family — code is MIT, the weights' licence is the §9 problem — C# STFT/iSTFT wrapper | Ship/fetch a frozen `demucs`/`audio-separator` exe and shell out to it | Music AI (the vendor behind Live's engine), LALAL.AI, Audioshake | mid/side vocal extraction, EQ-gated "stems" |
| Needs Live | no | no | no | no |
| Quality *(est)* | at or above Live's, because we pick the model | the reference for open separation | same family as Live's | not stem separation |
| Speed on this rig *(est)* | 15–40 s/track GPU (DirectML/CUDA), 3–8 min CPU — **the GPU half is no longer an estimate for the Python route: §8 measures 16.1 s CUDA / 112.6 s CPU** | same engine, similar | network-bound, minutes | instant |
| Shipped weight *(est)* | 25–60 MB runtime + 80–800 MB model, downloaded on first run | 3–5 GB (PyTorch + CUDA) or a first-run download | nothing | nothing |
| Runs offline | yes | yes | no | yes |
| Cost per track | none | none | per-minute pricing | none |
| Main risk | the DSP wrapper is real work; licences are checked per file and the check does **not** cover the weights (§9) | packaging, Python-version drift, antivirus, installer size | data leaves the machine; a business model change | it does not actually separate |

Recommendation: **A**, with **B** used only as the Phase 0 yardstick and **C** kept as an
optional "cloud quality" tier later. A is the only option that keeps autostem a single
installable Windows app, offline, with no per-track cost, and it is the same class of engine
Live is using.

GPU route inside A: **DirectML** first (`Microsoft.ML.OnnxRuntime.DirectML`, ~30 MB, any
D3D12 card, no CUDA/cuDNN DLLs to ship, works on this 2080 Ti), CUDA as an optional engine
pack for people who already have it. CPU is the always-available fallback.

## 4. The seam that makes this cheap

The codebase already has the right hole. `ILiveRunner`
(`src/StemForge.Core/Live/SeparationRequest.cs:52`) is

```csharp
string Name { get; }
Task<SeparationOutcome> SeparateAsync(SeparationRequest request, CancellationToken ct);
```

`LiveAutomationRunner` and `SimulatedLiveRunner` both implement it, and the choice is made in
one place from `settings.UseSimulatedRunner`. A new engine is therefore one more implementation
plus a picker — the queue, verification, publishing, folder layout and Firebase side do not
change. The name becomes wrong once a non-Live engine exists; renaming it to `ISeparator` (and
`LiveStemsFolder` to something honest) is part of Phase 1, not a prerequisite.

A local engine also *improves* the product in three ways the Live path cannot:

- progress is real (segments done / total), not a folder being polled;
- cancellation is immediate, not "kill the DAW and hope";
- the desktop is free — no exclusive foreground, no locked-screen rule, no Display Dimmer.

## 5. Phases

**Phase 0 — yardstick (no product code).**
Take the four WAVs Live just produced for `Akcept - Howl (Original Mix)` as the reference. Run
open Demucs on the same file two ways: CPU, and GPU through Python. Measure RTF, peak memory,
and build a level-matched listening folder (same loudness per stem, so a louder render is never
mistaken for a better one). Deliverable: a table of candidates — `htdemucs`, `htdemucs_ft`,
`hdemucs_mmi`, `mdx_q` — with time, size and an ear verdict, plus each model's licence text.
(§9 is the licence half of that deliverable; the ear half is still with you.)
Gate: one model must clearly beat or match Live's output, or the whole idea is dropped and Live
stays the only engine.

**Phase 1 — engine seam.**
Rename `ILiveRunner` → `ISeparator` (keep a deprecated alias), add `EngineKind { Live, Local,
Cloud }` to settings and the UI, and make the runner registry explicit. Move the Live-specific
`ErrorKind` values into a Live-only failure path so a local engine cannot raise "Live lost
focus". Gate: existing tests still pass, and `--engine live` behaves exactly as today.

**Phase 2 — the C# inference core.**
`StemForge.Core/Local/`. Not a separate `StemForge.Local` assembly: `SeparatorFactory` lives in
Core, so a child project would have to reference Core and be referenced by it at the same time.
The Live engine's own folder inside Core is the shape that already works.

*Where does the graph end?* Answered twice, and the second answer is the one that ships.
`HybridTransformerDemucs.forward` takes raw waveform and returns raw waveform: its spectrogram
(`_spec`/`_ispec` - `torch.stft`, normalised, **n_fft 4096, hop 1024**, reflect padding; an earlier
draft of this section said 512/128, which is the *default in `spec.py`'s signature* rather than
what the model passes, and htdemucs asserts `hop == n_fft // 4`), the per-input mean/std
normalisation, the masking and the time-branch skip are all inside `forward`. Exported at that
shape, the C# side would need no STFT. It cannot be exported at that shape: TorchScript refuses
`torch.stft` outright - "STFT does not currently support complex types" at opset 17, 18 *and* 20 -
and the dynamo exporter - whose first failure was a console-encoding crash hiding the real one -
then failed properly, on `GuardOnDataDependentSymNode: Eq(u0, 1)`: `torch.stft`'s own centre-padding
does a `.item()` comparison that no symbolic tracer can guard. Both exporters are genuinely blocked
on the FFT, not on tooling. Getting waveform-in anyway meant **replacing** the FFT with a conv-DFT,
and that graph was
built, exported and measured (§8): 305 MB, 30 576 nodes, 8 s to load, and **−82.5 dB** from Python.
Stopping the graph at the spectrogram instead measured **−100.4 dB** from Python in 181 MB, 2 634
nodes, 0.8 s. So the graph ends at the spectrogram, and "FFT round-trip within float noise" is back
on this phase's test list: the C# STFT is on the critical path, and `mix-spectrum.npy` /
`masked-spectrum.npy` - torch's own tensors for the same window - are the golden values it has to
hit.

*What exactly is that FFT?* Pinned from source, because this is where a port goes quietly wrong.
`_spec` reflect-pads the segment by `hop//2*3` = 512 samples at the head and out to
`ceil(N/1024)*1024` at the tail, runs `torch.stft(n_fft=4096, hop=1024, win_length=4096, hann,
normalized=True, center=True, pad_mode='reflect')`, then **drops the last frequency bin** - the
Nyquist, hence 2048 bins and not 2049 - drops the last frame, and keeps frames `[2, 2+le)`.
`_ispec` puts a zero Nyquist bin back, pads two frames at each end, inverts to
`1024*ceil(N/1024) + 1024` samples and trims `[512, 512+N)`. Both directions are exercised by the
rebuild test in §8, which reproduces Python's audio to −100 dB, so the contract is proven even
though nobody has written the C# version yet.

*What must the arithmetic be?* Ported literally from `apply.py`/`api.py` and already tested:
`DemucsPlan` (10 s windows, stride 330 750, the window the reference centres and pads, the
triangle fade), `DemucsOverlapAccumulator` (weighted sum divided by the weight that actually
landed, `center_trim` included), `DemucsLevel` (mono reference, scalar mean, unbiased std, applied
and undone) and `DemucsStemOrder` (0 = drums, 1 = bass, 2 = other, 3 = vocals - *not* Ableton's
dialog order, with a test that asserts the two orders differ so nobody "tidies" it into silently
mislabelled files). The centre of that suite feeds the accumulator exactly what it was given and
requires the mix back, sample for sample: an error in the stride, the fade or the trim is not an
exception, it is a stem with a seam in it, and this is the only way to see it before there is a
model to argue with.

What is left: the spectrogram, which §8 just promoted from "maybe not needed at all" to the hardest
remaining piece; the ONNX session wrapper (DirectML → CUDA → CPU, probed once and logged); the
resample/channel boundary (§7); and the `ISeparator` that ties it to the queue. Two properties are
already bought with it: `Windows(total)` knows the segment count before the first one runs, which
is the "real progress" §4 promised rather than a folder being polled, and the accumulator holds
the finished track in memory — 4 stems × 2 channels × 44 100 samples is ~85 MB per minute of
music, which is a cost to stream away later, not a correctness problem. Gate unchanged: the C#
engine reproduces the Python engine's output within a stated tolerance on one track — and §8 now
gives that tolerance a floor to be judged against: at or under −77 dB relative to a stem, which is
what the *same* demucs engine already pays to run on a different device. Anything worse than the
floor is not "the same stems" and has to be argued by ear instead of by arithmetic. The reference
graph already clears it (−100.4 dB overall, −79.7 dB on its worst stem), so the burden of proof has
moved off the runtime and onto the C# FFT.

One expectation to correct here rather than after it is written into a pitch: on CPU, ONNX Runtime
was **not** faster than the Python it would replace - 3.2 s against torch CPU's 3.07 s for the same
10 s window. The speed case for Phase 2 rests entirely on DirectML on the 2080 Ti, which Phase 0
measured at 8.3 s for a whole track. And these probes' peak RSS - 6.5 GB for a *single* window on
24 threads, where demucs used 2.0 GB for an entire run - is unexplained, so this phase quotes no
memory figure until it is.

**Phase 3 — packaging the model.**
Models are not committed. An "engine pack" is downloaded on first use into
`%LOCALAPPDATA%\SkeerAutoSTEM\engines\<name>\` with a pinned URL, SHA-256, size and licence text,
verified before use, and offered offline once present. The UI shows which engine, which device (GPU
or CPU) and which model is active, and warns when it fell back. Gate: a clean machine installs and
runs without Python, without CUDA and without Ableton — and a pack without a recorded licence is
refused (§9).

**Phase 4 — the Live engine stays, demoted.**
It becomes "Ableton Live (reference)" for people who want exactly Live's sound. Independently of
this plan, fix the bug found tonight (§6) — the Live path is currently *working* but autostem
cannot see its output.

**Phase 5 — proof.**
`bench` already exists in the CLI. Extend it to A/B engines on the same source with level
matching, and run it over a mixed set (dense mix, sparse vocal, 808, live band). Only after that
does the local engine become the default.

## 6. Bug found tonight, unrelated to the plan but blocking the Live path

Live **succeeded** (`result: success`, 4 × 49.3 MB WAV) but autostem reported nothing, because
Live did not write into the working folder. Opening a loose `.als` makes Live copy it into a
temporary project, and the stems land there:

```
Documents\Ableton\Live Recordings\2026-11-14 041458 Temp Project\Samples\Processed\Stems\
    Akcept - Howl (Original Mix) [2026-11-14 031511] (Bass).wav   …(Drums)(Others)(Vocals)
```

`SeparationRequest.LiveStemsFolder` points at `<work folder>\Samples\Processed\Stems`, which
Live never used. Three launches, three temp projects (`Indexer.txt` → `Configure:
CurrentProject: '… Temp Project'`). Both remedies from that list are now in:

1. the set is written as a real project folder — `<job>\autoSTEM Working Set\autoSTEM Working
   Set.als`, folder name equal to set name — so Live has a home and stops treating it as loose;
2. completion detection no longer trusts a single folder. `StemCandidateFolders` snapshots the
   working project *plus* the three newest `… Temp Project\Samples\Processed\Stems`, baseline
   and poll taken the same way, so a temp project is survivable rather than fatal.

Also recorded that night: the library scan does not stop when paused (it resumes at the next
launch), and a foreign foreground window — here, Display Dimmer — used to abort a run on the
first glimpse; the runner now reclaims focus up to five times before pausing. Two further
failures with the same symptom, "Live did not open a window in time", turned out to be a
startup recovery modal blocking the untitled main window, and a message box whose title-bar
`Close` was pressed instead of its `No`. See `docs/LIVE-AUTOMATION.md`.

A third trap sat directly behind the one above, and would have consumed the next four-minute
run: those four WAVs each get a `.wav.asd` sidecar, written *after* the audio because Live
analyses its own stems, and the collector orders candidates newest-first. Matching on the stem
word therefore found the sidecar for every stem, and the converter refused them all. Collection
now requires a real audio container and reads the stem from the last parenthesised group of the
name, which also stops a track called *Vox Populi* from having its drum stem labelled as its
vocal stem. `LiveStemFolders` and `StemFileMapper` are unit-tested against the names and the
ordering Live actually produced.

## 7. Decisions that need an answer before Phase 2

1. **Model licence discipline.** Demucs *code* is MIT; its *weights* have no stated licence, and
   community checkpoints (BS-Roformer, UVR/MDX bundles) are unlicensed or CC-BY-NC. Proposal, after
   §9: ship nothing but the Demucs family — not because it is licensed, but because it is the only
   family with an MIT code release and a first-party vendor who can be asked in writing — and make
   "weights licence confirmed" a Phase 3 gate rather than an assumption. That is also the family
   Live is in.
2. **First-run download vs installer size.** A ~80–450 MB model does not belong in an .exe.
   Proposal: download on first use, with an offline path for machines without internet.
3. **Minimum spec.** Does autostem have to work on a machine with no GPU at all? If yes, the
   CPU path must be fast enough to be usable, which pushes toward a smaller Demucs variant.
4. **Cloud tier — now or never.** It is the only route that is literally the same vendor as
   Live's engine, but it changes what the product is.
5. **Determinism, i.e. what `--shifts` is really for.** The demucs CLI separates with a *random*
   time offset by default — `--shifts 1`, and `apply.py` draws `random.randint(0, 22050)` afresh
   every run — and Phase 0 ran it exactly that way, which is the whole story behind the retraction
   in §8. Measured on a 30 s clip (`shift_probe.py`): pinned to `--shifts 0`, two CPU runs are
   **bit-identical**; at the default, two runs of the *same engine on the same device* differ by
   up to 1.06e-01 FS at individual samples — the difference 12.7–32.4 dB below the stem — and a
   default run differs from a pinned one by up to 1.9e-01 FS, 10.5–30.0 dB below. So the real
   choice is not "deterministic or better", it is how many passes to average. Proposal: the local
   engine defaults to one pass at a **fixed** offset, so a re-run reproduces the file the verifier
   already checked, and extra passes are an explicit, visible control that states its own time cost
   and can always be overridden. The golden-tensor gate is then run at `--shifts 0`, because
   nothing that averages a random offset can be compared to a reference twice.
6. **Sample rate and channel count.** Demucs wants 44.1 kHz stereo. Mono→stereo is a copy, but
   48 kHz needs `convert_audio`, whose polyphase Kaiser filter is part of what makes a Python
   render what it is — so a C# resampler of our own choosing puts the golden-tensor gate out of
   reach for every file that is not already 44.1 kHz. Options: gate the local engine to 44.1 kHz
   and say so in the UI, resample with a stated filter and validate those files by ear, or port
   the filter. Not decided, and deliberately not coded while it is undecided.
7. **Output bit depth.** Every stem this product has published — from Live, and from demucs in the
   yardstick — is 16-bit PCM, because 16-bit is what each engine happens to write rather than
   because anyone chose it. That choice is load-bearing for §8: one 16-bit LSB is the floor under
   every delta in that section, and it is why the CPU/GPU agreement lands at −77 dB rather than
   somewhere embarrassing. A local engine in C# could write 24-bit instead, which puts the
   quantisation step 48 dB lower — 256× smaller, so a stem that Live re-imports, gain-stages and
   renders again picks up that much less noise per hop — and nothing in the pipeline would stop it.
   Note what that is *not*: linear PCM has no headroom at any depth, so 24-bit makes the round-trip
   quieter, never louder, and protects nothing from clipping; the ±12 dB cap and the never-clip
   rule in §8 stand unchanged at every depth. Proposal: keep 16-bit as the default, so a local
   engine is never judged against a different file format than the reference it is compared to;
   expose depth as an explicit per-profile choice with its cost stated (~1.5× the bytes at 24-bit);
   and never change the default quietly. Undecided, and deliberately not coded while it is.

## 8. Phase 0 measured so far

Rig: `D:\Projects\tools\separation-yardstick\yardstick.py`, two uv venvs so that the CPU
baseline stays reproducible next to the CUDA one — `.venv` (demucs 4.1.0, torch 2.15.0+cpu) and
`.venv-gpu` (demucs 4.1.0, torch 2.9.1+cu126, which is the newest wheel the installed
560.94 / CUDA 12.6 driver can be trusted with). sphn for I/O; psutil samples peak RSS from a
second thread while the child lives, and on a CUDA run `nvidia-smi` is polled alongside it for
peak VRAM **above the pre-run baseline** — nvidia-smi answers for the whole card, so without the
subtraction Live's renderer and the desktop get billed to Demucs.
Source: `Akcept - Howl (Original Mix).mp3`, 293.3 s. Reference: the four WAVs Live 12.3.5
produced for the same file. Each candidate stem is **gain-matched to Live's stem RMS before
listening** (cap ±12 dB, and never allowed to clip), so a louder render can never be mistaken
for a better one.

> **The reference is 12.3.5 and the tool now runs 12.3.2.** The locator stopped treating Ableton's
> update-staging folder as an install, so the build that produced the numbers above is no longer
> the build `--engine live` starts. A second reference set for the same MP3 now exists, made by
> the shipped runner on 12.3.2 on 2026-11-15: four 51 740 796-byte WAVs in
> `C:\Users\Sebastien\Desktop\test\Akcept - Howl (Original Mix)`. Re-measure Live's own row before
> comparing anything to it; never mix the two sets in one comparison.

| candidate | device | seconds | RTF | peak RSS | peak VRAM | notes |
|---|---|---|---|---|---|---|
| Live 12.3.5 (`musicai`) | CPU | 218.2 | 0.744 | 2.9 GB (+8.9 GB virtual) | — | reference |
| demucs `htdemucs` | CPU | 112.6 | 0.384 | 2.00 GB | — | 1.9× faster than Live on the same silicon |
| demucs `htdemucs` | CUDA | 16.1 | 0.055 | 2.39 GB | 0.97 GB | **13.6× faster than Live** |
| demucs `htdemucs_ft` | CUDA | 43.5 | 0.148 | 3.63 GB | 1.04 GB | bag of 4 models |
| demucs `hdemucs_mmi` | CUDA | 51.4 | 0.175 | 2.92 GB | 3.70 GB | 6 sources, remapped to 4 |

Live's row is not a stopwatch reading: it is the gap between the logged *Separate* click
(03:15:11 UTC) and the mtimes of the four WAVs it wrote (03:18:49), so it includes whatever Live
did around the separation. The demucs rows are wall seconds of the child process, measured twice —
once to warm the checkpoint download, once for the number above — on an otherwise quiet machine.

One more thing every demucs row did, without being asked: the yardstick called
`python -m demucs` with no `--shifts`, so the CLI default of 1 gave the model a **random** 0–0.5 s
offset before separating. Timings and memory are unaffected — it is still one pass over the track —
but the audio is not repeatable, and two runs of the same row are different files. §7 item 5 is
about deciding that, and it is why the cross-device claim below had to be re-measured.

That is the plan's central claim holding up, and holding up far better than the CPU case
suggested: plain open Demucs is already faster than Live's separator on Live's own CPU path, and
on the RTX 2080 Ti that is sitting in this machine it is **13.6×** faster, using under a gigabyte
of VRAM for `htdemucs`. It is not yet a *decision*, because the ear verdict is outstanding and
because Python/Demucs is not what would ship — Phase 2 is C# running ONNX, which will land
somewhere between these numbers and Live's.

Two things to note rather than hide. First, the earlier version of this table said the CPU
half of the GPU comparison was 149.1 s and that Demucs' `other` stem was near silence
(−71.2 dBFS) where Live's carried real energy, and drew a conclusion from it about how the two
engines file the growl and the FX. **That conclusion was wrong and is retracted.** The
`other.wav` from that first CPU run was a bad file — the same model re-run wrote −39.1 dBFS, in
line with Live's −38.2 and with the GPU's −38.8 — and its timing had been inflated by the
checkpoint download. Both came from measurement tooling that could destroy its own records,
which is fixed below.

Second, and this one is a retraction with something better underneath it. The levels below are
real; the sample-level conclusion that used to sit next to them was not.
Per-stem RMS against Live's stem of the same name:

| stem | Live | `htdemucs` CPU | `htdemucs` CUDA | `htdemucs_ft` CUDA | `hdemucs_mmi` CUDA |
|---|---|---|---|---|---|
| vocals | −34.1 | −32.6 | −35.4 | −33.2 | −35.3 |
| drums | −19.5 | −22.7 | −22.7 | −22.8 | −19.8 |
| bass | −10.1 | −10.1 | −10.1 | −10.1 | −11.4 |
| others | −38.2 | −39.1 | −38.8 | −37.9 | −27.6 |

Three stems land within a whisker of each other across devices; vocals sit 2.8 dB apart, and that
spread is between the *models*, not between the machines. The `hdemucs_mmi` row is the one to
listen to first: its `others` is 10.6 dB hotter than Live's, so if that model wins on separation
quality it will also be the one that most obviously changes what a Skeer set sounds like.

What used to sit next to these numbers — "the two renders differ by up to full scale at individual
samples, therefore nothing is reproducible, blame cuDNN's TF32" — was measured with the shift
unpinned. Re-measured with `--shifts 0` and two runs per device, it says something better and a
lot duller:

| comparison | result |
|---|---|
| CPU run A vs CPU run B, `--shifts 0` | all four stems bit-identical |
| CUDA run A vs CUDA run B, `--shifts 0` | all four stems bit-identical |
| CPU vs CUDA, `--shifts 0` | biggest sample difference 3.052e-05, and −77 to −106 dB below the stem |
| `--shifts 1` vs `--shifts 1` (CLI default) | not identical: up to 1.06e-01, 12.7–32.4 dB below the stem |
| `--shifts 0` vs `--shifts 1` | same order: up to 1.87e-01, 10.5–30.0 dB below the stem |

3.052e-05 is exactly 2⁻¹⁵ — the weight of one step in the signed 16-bit PCM that demucs writes
(`save_audio(..., bits_per_sample=16)`), and that is what Live's stems are too: 16-bit, 44.1 kHz,
stereo. The two engines this product has ever published are quantised identically, so with the
shift pinned, CPU and GPU agree to within the last bit they are both allowed to write. Their
per-stem levels print the same to 0.1 dB (vocals −39.3, drums −20.7, bass −9.2, others −37.9 dBFS;
peaks 0.533 / 0.990 / 0.952 / 0.279 FS). The "full-scale difference" was the random offset
sliding the model's whole analysis frame by up to half a second. Whether there is also a TF32
effect *under* one LSB, this measurement cannot see — there is no room left in the file.

These come from two scripts in the yardstick folder, each writing its own log so a concurrent job
cannot swallow it: `shift_probe.py` (four CPU runs with the shift as the only variable, log at
`out/shift-probe-4runs.log`) and `device_probe.py` (two runs per device at `--shifts 0`, log at
`out/device-probe.log`). Both use the same 30 s clip taken from 60 s into the Phase 0 source, so
they are cheap enough to re-run before any tolerance claim in this document is trusted.

So the sentence about Phase 2 is now decided by measurement rather than by guesswork. An ONNX
port is accepted against a tolerance in decibels, never against equality, and the floor of
"the same stems" has a number: −77 dB relative to the stem is what the *identical engine on a
different device* already costs, so a port must be at least that faithful before anyone calls it
the same separation — and the thing that actually threatens repeatability is not floating-point
precision, it is whatever the port inherits about shifts. That is why §7 item 5 asks for one pass
at a fixed offset plus an explicit, visible extra-passes control rather than a CLI default.

The last row is also a product question the measurement surfaced on the way: every stem a user
has ever taken out of autoSTEM is 16-bit, from both engines, and a local engine is the one place
that could quietly change. §7 item 7 is about not doing it quietly.

The compute-path half of the old sentence survives, but for a different reason than it was
written: pinning device, precision and shift is still what makes a shipped local engine
answerable for "these are the same stems", now because it is cheap and because the shift is a
real variable — not because the devices disagree in any way that reaches the ear.

Getting these numbers also caught three problems in the yardstick itself. Its peak function
returned a linear amplitude while its RMS function returned decibels, and the note that mixed the
two printed the headroom correction with the wrong sign, so "moved 6.4 dB instead of 3.2" actually
meant "+0.1 dB applied". Cross-checking one file with Python's own `wave` module is what showed
the printed figures could not be true; the reporting is fixed and `--relevel` regenerates the
listening folder without re-running the separation. `--relevel` also rewrote `out/yardstick.csv`
from an empty row list, deleting the only machine-readable copy of the CPU measurement — it is now
merged by model × device, so a pass that measures nothing cannot erase something that did. And
the first CUDA pass spent most of its wall time downloading the checkpoint, which was recorded as
if it were a separation; every row above is a second, warm pass.

The CPU row that was restored from `yardstick-htdemucs-cpu.log` is why the retraction above was
nearly missed: the log carried a timing inflated by that same download and a stem set containing
one bad file, and both looked like findings. The numbers in the table are re-measured, and the
`listen/` folders are rebuilt from them, so what is offered for the ear matches what is claimed.

### The export experiment

Phase 0's table answers "is open Demucs good enough". Phase 2 has its own question - *what would
the C# engine actually load, and how far is it from Python?* - and that is now measured rather than
assumed, so it goes here, next to the rig. One 10 s window of the same track, `htdemucs`'
`forward` (so no CLI, no shifts), exported two ways, run in ONNX Runtime's CPU provider, compared
with torch's own output for that window.

| graph | where the graph ends | size | nodes | load | per chunk | vs Python |
|---|---|---|---|---|---|---|
| `htdemucs-convstft.onnx` | waveform in, waveform out - the FFT replaced by a conv-DFT so TorchScript can export it | 305 MB | 30 576 | 8.0 s | 3.9 s | **−82.5 dB**, max 1.6e-04 |
| `htdemucs-specin.onnx` | waveform + spectrogram in, spectrogram + time residual out - the iSTFT moves to C# | 181 MB | 2 634 | 0.8 s | 3.2 s | **−100.4 dB**, max 5.1e-05, worst stem −79.7 dB |

torch's own CPU takes 3.07 s for that window, so ONNX Runtime on CPU buys nothing by itself; every
second of the speed argument for Phase 2 is DirectML on the GPU, and that has not been measured.

The export's own log names `htdemucs-convstft.onnx` the winner. That is defensible by its stated
criterion - it is the graph that needs no FFT downstream - and wrong by every number in the table
above. The task ended on a turn limit having never rebuilt audio from the smaller graph, so the
comparison that reverses its ranking is `onnx_verify_specin_rebuild.py`, not its report.

Three things in the table are worth more than the row that won. **TorchScript cannot export
`torch.stft`** - "STFT does not currently support complex types" at opset 17, 18 *and* 20 - and
dynamo, once the cp1252 console crash that was hiding it is worked around, stops 5.9 s later on a
data-dependent guard inside `torch.stft`'s own padding (a `.item()` on a traced boolean). That is a
property of the operator, not of the exporter, and it should not be re-attempted as a tooling
problem. So the
attractive story, "the graph returns waveform and C# writes no DSP", is only reachable by
substituting a hand-built DFT for the FFT, and that substitution is exactly what the first row
costs: 124 MB more, 12× the nodes, 10× the load time, and 18 dB *less* accuracy than the smaller
graph. **The tolerance floor stopped being a guess**: −77 dB is what this same engine pays to move
from CPU to GPU, and the spectrum graph is already inside that, so the gate is reachable and the
risk has moved somewhere unit-testable - the C# FFT. And the first report of this experiment never
made the measurement that settles it: it compared the smaller graph's *time residual* and its
*mask* separately, each against its own reference, which is how a port can look accurate in two
halves while nobody has ever heard the audio. Pushing ORT's spectrum back through demucs' own
`_ispec` and adding the residual is what produced −100.4 dB, and it lives in
`onnx_verify_specin_rebuild.py` so it can be re-run against any future export.

Two loose ends came out of this, and one of them is now closed - not the way it looked. Peak RSS was
**6.5 GB for a single 10 s window** on 24 threads, where demucs used 2.0 GB for a whole track; that
is still unexplained and Phase 2 quotes no memory figure until it is. The other looked like a
contradiction: the conv-DFT executed *inside torch* came out **−34.7 dB** from Python where ORT
comes out −82.5 dB for the same arithmetic, which pointed at an fp32 `conv1d` over a 4096 × 8192
kernel being a poor way to do a DFT. It isn't. Measured against fp64 and against `torch.stft`
itself, that convolution is accurate to 2.3e-05 (analysis), 2.9e-06 (synthesis), 2.7e-06 with
mkldnn switched off - numbers two orders of magnitude below the difference being argued about. So
the liar is the **torch-side patched model**, whose wrapper substitutes more arithmetic than the
one being tested and then saves its output as a reference. Two things follow: `torch-patched-ref.npy`
is a reference for nothing, which also disqualifies the band-by-band error table the first report
drew from it, and a waveform-boundary graph is more accurate than those parity numbers implied -
its real distance from Python is the −82.5 dB that ORT measured twice, independently.

## 9. What a local engine is allowed to ship — licence audit

Phase 3 downloads a model into a paid product, so this is a gate, not a footnote. Checked against
primary sources on 2026-11-14: the repository's own `LICENSE` and `README`, the file server that
serves the checkpoints, the dataset's published terms, and the NuGet index.

| component | what the source actually says | verdict for a sold installer |
|---|---|---|
| Demucs code (`facebookresearch/demucs`) | MIT; `README` closes with "Demucs is released under the MIT license as found in the LICENSE file" | ship |
| the pretrained `.pth` weights, served from `dl.fbaipublicfiles.com` | **no licence text at all.** The MIT file licences "the software and associated documentation files"; the README sentence describes the release, not the tensors; the download server attaches no terms and refuses a bucket listing | usable **by inference only** — ask Meta in writing before Phase 3 |
| MUSDB18, which those weights learned on | Zenodo-hosted, access by request, "since the tracks can only be used for academic purposes"; several tracks are separately CC BY-NC-SA 4.0 / 3.0 | not our licence to grant, and not a docs question — see below |
| BS-Roformer, `audio-separator`/UVR models, PyMDX, VIP centres | no licence, or CC BY-NC on the community bundles | **out**, whatever they sound like; they are not in the candidate set |
| Meta's own `mdx_q` / `mdx` family | same posture as `htdemucs`: MIT code, unstated weight licence | same one question to the same one vendor |
| ONNX Runtime + DirectML | MIT | ship, but MIT obliges us to carry `LICENSE` and `ThirdPartyNotices.txt` in the installer |

Two consequences that are already mechanical rather than legal.

The runtime packages must be **pinned as a matched pair**. `Microsoft.ML.OnnxRuntime` is at 1.32.0
while `Microsoft.ML.OnnxRuntime.DirectML` tops out at 1.24.4 — the DirectML feed lags the base feed
by eight releases and does not track it, so a manifest that floats both versions will eventually
install a combination nobody has tested. Pin both, and verify the pair against the NuGet index at
build time.

The engine pack from Phase 3 needs a licence field in its manifest. A model directory under
`%LOCALAPPDATA%\SkeerAutoSTEM\engines\<name>\` should not be installable without a `LICENSE` file
whose provenance is recorded next to the SHA-256 — because the honest case, Demucs', is a directory
where the hash is known and the permission is not, and that difference is exactly what a future
maintainer must not have to rediscover from this document.

Checked while writing this, because it is a gap we already have rather than one Phase 3 would
create: **today's installer ships no licence file at all.** `packaging\SkeerAutoSTEM.iss` copies
exactly two artifacts — the app and the headless CLI — and `artifacts\SkeerAutoSTEM\` contains
nothing but those two `.exe`s and their `.pdb`s, so the self-contained .NET 10 runtime that rides
inside them reaches the customer's machine with no notices attached. MIT asked that of us before
any model was in the picture, and it is the cheapest item in this document: take
`ThirdPartyNotices.txt` from the .NET release the build pins, add it to `[Files]` next to the
exe, and point the installer's `LicenseFile=` at a licence for our own code.

And the one thing this audit cannot close: the exposure that matters is **training data, not
weights**. MUSDB18's terms are academic-only and its artists licensed recordings for research, not
for machine-learning training in a commercial product. Nothing about that is fixed by Meta's
code licence or by our own, and a written answer about the `.pth` would not answer it either. It is
a question for counsel with the numbers in §8 attached — a 13.6× speed gain is only an asset if the
model behind it can be sold — and until someone answers it the fallback that needs no answer at all
stays open: Live remains the engine the product actually ships with.
