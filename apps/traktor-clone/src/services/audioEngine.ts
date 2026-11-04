import {
  CrossfaderCurve,
  DeckId,
  DeckState,
  FXType,
  FXUnitState,
  MixerState,
  TrackEntity,
} from '../types';
import { BackendBeatAnalyzer } from './backendBeatAnalyzer';
import { StemService } from './stemService';

interface ChannelNodes {
  sourceNode: AudioBufferSourceNode | null;
  gainNode: GainNode;
  eqLow: BiquadFilterNode;
  eqMid: BiquadFilterNode;
  eqHigh: BiquadFilterNode;
  filterLowPass: BiquadFilterNode;
  filterHighPass: BiquadFilterNode;
  volumeNode: GainNode;
  pannerNode: StereoPannerNode;
  cueGainNode: GainNode;
  analyserNode: AnalyserNode;
  fxSends: GainNode[]; // fx1 to fx4
  xfadeLeftGain: GainNode;
  xfadeRightGain: GainNode;
  thruGain: GainNode;
}

export class AudioEngine {
  private static instance: AudioEngine;
  private ctx: AudioContext;

  private masterGain: GainNode;
  private masterAnalyserLeft: AnalyserNode;
  private masterAnalyserRight: AnalyserNode;
  private splitter: ChannelSplitterNode;

  private cueBusGain: GainNode;
  private headphoneGain: GainNode;

  private xfadeLeftBus: GainNode;
  private xfadeRightBus: GainNode;
  private crossfaderGainA: GainNode;
  private crossfaderGainB: GainNode;

  private channels: Map<DeckId, ChannelNodes> = new Map();
  private deckStates: Map<DeckId, DeckState> = new Map();
  private mixerState: MixerState;

  // Track playback tracking
  private trackStartTimes: Map<DeckId, number> = new Map();
  private trackPausedAt: Map<DeckId, number> = new Map();
  private wasPlayingBeforeTouch: Map<DeckId, boolean> = new Map();

  // Stem service for separation
  private stemService: StemService;

  // Track loading tracking
  private trackLoaders: Map<DeckId, { file: File; buffer: AudioBuffer }> = new Map();

  private isInitialized = false;
  private fxInputs: GainNode[] = [];
  private fxOutputs: GainNode[] = [];
  private fxDspNodes: Array<{
    input: GainNode;
    output: GainNode;
    dryGain: GainNode;
    wetGain: GainNode;
    type: FXType;
    delayNode?: DelayNode;
    feedbackGain?: GainNode;
    dampFilter?: BiquadFilterNode;
    convolverNode?: ConvolverNode;
    reverbDampFilter?: BiquadFilterNode;
    lfo?: OscillatorNode;
    lfoGain?: GainNode;
    filterNode?: BiquadFilterNode;
  }> = [];

  private isInitialized = false;

  private constructor() {
    const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    this.ctx = new AudioCtx({ latencyHint: 'interactive' });

    // Master Bus
    this.masterGain = this.ctx.createGain();
    this.masterGain.gain.value = 0.85;

    // Master VU meters
    this.splitter = this.ctx.createChannelSplitter(2);
    this.masterAnalyserLeft = this.ctx.createAnalyser();
    this.masterAnalyserRight = this.ctx.createAnalyser();
    this.masterAnalyserLeft.fftSize = 256;
    this.masterAnalyserRight.fftSize = 256;

    this.masterGain.connect(this.splitter);
    this.splitter.connect(this.masterAnalyserLeft, 0);
    this.splitter.connect(this.masterAnalyserRight, 1);
    this.masterGain.connect(this.ctx.destination);

    // Crossfader Busses
    this.xfadeLeftBus = this.ctx.createGain();
    this.xfadeRightBus = this.ctx.createGain();
    this.crossfaderGainA = this.ctx.createGain();
    this.crossfaderGainB = this.ctx.createGain();

    this.xfadeLeftBus.connect(this.crossfaderGainA);
    this.xfadeRightBus.connect(this.crossfaderGainB);
    this.crossfaderGainA.connect(this.masterGain);
    this.crossfaderGainB.connect(this.masterGain);

    // Headphone Cue / PFL Bus
    this.cueBusGain = this.ctx.createGain();
    this.headphoneGain = this.ctx.createGain();
    this.headphoneGain.gain.value = 0.8;
    this.cueBusGain.connect(this.headphoneGain);

    // Default mixer state (initialize before setupFxUnits & initChannel)
    this.mixerState = {
      crossfader: 0.0,
      crossfaderCurve: 'smooth',
      masterVolume: 0.85,
      masterVuLeft: 0,
      masterVuRight: 0,
      headphoneVolume: 0.8,
      headphoneMix: 0.5,
      masterBpm: 126.0,
      layoutMode: '2decks',
      activeFxUnits: this.fxUnits,
    };

    // Setup stem service
    this.stemService = new StemService();

    // Setup FX Units
    this.setupFxUnits();
    this.mixerState.activeFxUnits = this.fxUnits;

    // Setup 4 Channels (A, B, C, D)
    const deckIds: DeckId[] = ['A', 'B', 'C', 'D'];
    for (const id of deckIds) {
      this.initChannel(id);
    }

    this.updateCrossfader(0.0);
  }

  public static getInstance(): AudioEngine {
    if (!AudioEngine.instance) {
      AudioEngine.instance = new AudioEngine();
    }
    return AudioEngine.instance;
  }

  public async resume(): Promise<void> {
    if (this.ctx.state === 'suspended') {
      await this.ctx.resume();
    }
  }

  private setupFxUnits() {
    this.fxUnits = [
      { id: 1, enabled: true, type: 'delay', dryWet: 0.35, param1: 0.5, param2: 0.45, param3: 0.6 },
      { id: 2, enabled: true, type: 'reverb', dryWet: 0.25, param1: 0.6, param2: 0.5, param3: 0.7 },
      { id: 3, enabled: false, type: 'flanger', dryWet: 0.4, param1: 0.3, param2: 0.7, param3: 0.5 },
      { id: 4, enabled: false, type: 'beatmasher', dryWet: 0.5, param1: 0.25, param2: 0.5, param3: 0.8 },
    ];

    this.fxInputs = [];
    this.fxOutputs = [];
    this.fxDspNodes = [];

    for (let i = 0; i < 4; i++) {
      const unit = this.fxUnits[i];
      const input = this.ctx.createGain();
      const output = this.ctx.createGain();
      const dryGain = this.ctx.createGain();
      const wetGain = this.ctx.createGain();

      output.connect(this.masterGain);
      output.gain.setValueAtTime(unit.enabled ? 1.0 : 0.0, this.ctx.currentTime);

      // Input splits into Dry and Wet
      input.connect(dryGain);
      dryGain.connect(output);
      dryGain.gain.setValueAtTime(1.0 - unit.dryWet * 0.5, this.ctx.currentTime);
      wetGain.gain.setValueAtTime(unit.dryWet, this.ctx.currentTime);
      wetGain.connect(output);

      const dspInfo: {
        input: GainNode;
        output: GainNode;
        dryGain: GainNode;
        wetGain: GainNode;
        type: FXType;
        delayNode?: DelayNode;
        feedbackGain?: GainNode;
        dampFilter?: BiquadFilterNode;
        convolverNode?: ConvolverNode;
        reverbDampFilter?: BiquadFilterNode;
        lfo?: OscillatorNode;
        lfoGain?: GainNode;
        filterNode?: BiquadFilterNode;
      } = {
        input,
        output,
        dryGain,
        wetGain,
        type: unit.type,
      };

      this.fxInputs.push(input);
      this.fxOutputs.push(output);
      this.fxDspNodes.push(dspInfo);

      this.buildDspForUnit(i, unit.type);
    }
  }

  private buildDspForUnit(idx: number, type: FXType): void {
    const dsp = this.fxDspNodes[idx];
    const unit = this.fxUnits[idx];
    dsp.type = type;

    // Disconnect any prior DSP if present
    try {
      dsp.delayNode?.disconnect();
      dsp.feedbackGain?.disconnect();
      dsp.dampFilter?.disconnect();
      dsp.convolverNode?.disconnect();
      dsp.reverbDampFilter?.disconnect();
      dsp.lfo?.stop();
      dsp.lfo?.disconnect();
      dsp.lfoGain?.disconnect();
      dsp.filterNode?.disconnect();
    } catch {
      // Ignored if not connected
    }

    if (type === 'delay') {
      const delay = this.ctx.createDelay(2.0);
      const masterBpm = this.mixerState?.masterBpm ?? 126.0;
      const beatSec = 60 / masterBpm;
      const delayTime = (0.125 + unit.param1 * 0.75) * beatSec;
      delay.delayTime.setValueAtTime(delayTime, this.ctx.currentTime);

      const feedback = this.ctx.createGain();
      feedback.gain.setValueAtTime(Math.min(0.9, unit.param2 * 0.88), this.ctx.currentTime);

      const damp = this.ctx.createBiquadFilter();
      damp.type = 'lowpass';
      const cutoff = 600 * Math.pow(25, unit.param3);
      damp.frequency.setValueAtTime(cutoff, this.ctx.currentTime);

      dsp.input.connect(delay);
      delay.connect(damp);
      damp.connect(feedback);
      feedback.connect(delay);
      damp.connect(dsp.wetGain);

      dsp.delayNode = delay;
      dsp.feedbackGain = feedback;
      dsp.dampFilter = damp;
    } else if (type === 'reverb') {
      const duration = 1.0 + unit.param1 * 3.5;
      const convolver = this.createSyntheticReverb(duration);
      const damp = this.ctx.createBiquadFilter();
      damp.type = 'lowpass';
      damp.frequency.setValueAtTime(800 * Math.pow(15, unit.param2), this.ctx.currentTime);

      dsp.input.connect(damp);
      damp.connect(convolver);
      convolver.connect(dsp.wetGain);

      dsp.convolverNode = convolver;
      dsp.reverbDampFilter = damp;
    } else if (type === 'flanger') {
      const delay = this.ctx.createDelay(0.05);
      delay.delayTime.setValueAtTime(0.003, this.ctx.currentTime);

      const feedback = this.ctx.createGain();
      feedback.gain.setValueAtTime(Math.min(0.92, unit.param2 * 0.85), this.ctx.currentTime);

      const lfo = this.ctx.createOscillator();
      const lfoGain = this.ctx.createGain();
      lfo.frequency.setValueAtTime(0.1 + unit.param1 * 4.0, this.ctx.currentTime);
      lfoGain.gain.setValueAtTime(0.0005 + unit.param3 * 0.0025, this.ctx.currentTime);
      lfo.connect(lfoGain);
      lfoGain.connect(delay.delayTime);
      lfo.start();

      dsp.input.connect(delay);
      delay.connect(feedback);
      feedback.connect(delay);
      delay.connect(dsp.wetGain);

      dsp.delayNode = delay;
      dsp.feedbackGain = feedback;
      dsp.lfo = lfo;
      dsp.lfoGain = lfoGain;
    } else {
      // Beatmasher / resonant filter stutter
      const biquad = this.ctx.createBiquadFilter();
      biquad.type = 'bandpass';
      const centerFreq = 200 * Math.pow(30, unit.param1);
      biquad.frequency.setValueAtTime(centerFreq, this.ctx.currentTime);
      biquad.Q.setValueAtTime(1.0 + unit.param2 * 12.0, this.ctx.currentTime);

      dsp.input.connect(biquad);
      biquad.connect(dsp.wetGain);

      dsp.filterNode = biquad;
    }
  }

  private createSyntheticReverb(duration: number): ConvolverNode {
    const convolver = this.ctx.createConvolver();
    const rate = this.ctx.sampleRate;
    const length = rate * duration;
    const impulse = this.ctx.createBuffer(2, length, rate);
    const left = impulse.getChannelData(0);
    const right = impulse.getChannelData(1);

    for (let i = 0; i < length; i++) {
      const decay = Math.exp(-i / (rate * 0.45));
      left[i] = (Math.random() * 2 - 1) * decay;
      right[i] = (Math.random() * 2 - 1) * decay;
    }
    convolver.buffer = impulse;
    return convolver;
  }

  private initChannel(deckId: DeckId) {
    const gainNode = this.ctx.createGain(); // Gain knob (Trim)
    gainNode.gain.value = 1.0;

    // 3-Band Isolator EQ (Traktor signature curve)
    const eqLow = this.ctx.createBiquadFilter();
    eqLow.type = 'lowshelf';
    eqLow.frequency.value = 250;
    eqLow.gain.value = 0;

    const eqMid = this.ctx.createBiquadFilter();
    eqMid.type = 'peaking';
    eqMid.frequency.value = 1000;
    eqMid.Q.value = 0.9;
    eqMid.gain.value = 0;

    const eqHigh = this.ctx.createBiquadFilter();
    eqHigh.type = 'highshelf';
    eqHigh.frequency.value = 2500;
    eqHigh.gain.value = 0;

    // Dual resonant filter (LP / HP ladder emulation)
    const filterLowPass = this.ctx.createBiquadFilter();
    filterLowPass.type = 'lowpass';
    filterLowPass.frequency.value = 20000;
    filterLowPass.Q.value = 1.2;

    const filterHighPass = this.ctx.createBiquadFilter();
    filterHighPass.type = 'highpass';
    filterHighPass.frequency.value = 20;
    filterHighPass.Q.value = 1.2;

    // Channel Volume Fader
    const volumeNode = this.ctx.createGain();
    volumeNode.gain.value = 0.9;

    const pannerNode = this.ctx.createStereoPanner();
    pannerNode.pan.value = 0;

    // Routing chain: Gain -> EQ Low -> EQ Mid -> EQ High -> Filter LP -> Filter HP -> Volume -> Panner
    gainNode.connect(eqLow);
    eqLow.connect(eqMid);
    eqMid.connect(eqHigh);
    eqHigh.connect(filterLowPass);
    filterLowPass.connect(filterHighPass);
    filterHighPass.connect(volumeNode);
    volumeNode.connect(pannerNode);

    // Channel VU Analyser
    const analyserNode = this.ctx.createAnalyser();
    analyserNode.fftSize = 256;
    pannerNode.connect(analyserNode);

    // Cue PFL send
    const cueGainNode = this.ctx.createGain();
    cueGainNode.gain.value = 0;
    pannerNode.connect(cueGainNode);
    cueGainNode.connect(this.cueBusGain);

    // Crossfader and Thru routing
    const xfadeLeftGain = this.ctx.createGain();
    const xfadeRightGain = this.ctx.createGain();
    const thruGain = this.ctx.createGain();

    pannerNode.connect(xfadeLeftGain);
    pannerNode.connect(xfadeRightGain);
    pannerNode.connect(thruGain);

    xfadeLeftGain.connect(this.xfadeLeftBus);
    xfadeRightGain.connect(this.xfadeRightBus);
    thruGain.connect(this.masterGain);

    // Default assign: Deck A -> Left, Deck B -> Right, Decks C/D -> Thru
    const defaultXfade = deckId === 'A' ? 'A' : deckId === 'B' ? 'B' : 'Thru';
    this.setCrossfaderAssignNodes(defaultXfade, xfadeLeftGain, xfadeRightGain, thruGain);

    // FX Sends
    const fxSends: GainNode[] = [];
    for (let i = 0; i < 4; i++) {
      const send = this.ctx.createGain();
      send.gain.value = 0; // Off initially
      pannerNode.connect(send);
      send.connect(this.fxInputs[i]);
      fxSends.push(send);
    }

    this.channels.set(deckId, {
      sourceNode: null,
      gainNode,
      eqLow,
      eqMid,
      eqHigh,
      filterLowPass,
      filterHighPass,
      volumeNode,
      pannerNode,
      cueGainNode,
      analyserNode,
      fxSends,
      xfadeLeftGain,
      xfadeRightGain,
      thruGain,
    });

    this.deckStates.set(deckId, {
      deckId,
      track: null,
      isPlaying: false,
      playbackTime: 0,
      pitchPercent: 0,
      effectiveBpm: 126.0,
      sync: false,
      master: deckId === 'A',
      keyLock: true,
      loopActive: false,
      loopLength: 4,
      loopStart: 0,
      loopEnd: 0,
      fluxMode: false,
      cuePoint: 0,
      gain: 1.0,
      eqHigh: 0,
      eqMid: 0,
      eqLow: 0,
      killHigh: false,
      killMid: false,
      killLow: false,
      filter: 0,
      volume: 0.9,
      cuePfl: false,
      xfadeAssign: defaultXfade,
      fxAssign: [false, false, false, false],
      vuLevel: 0,
      tempoRange: 8,
      isScratching: false,
    });

    this.trackPausedAt.set(deckId, 0);
  }

  private setCrossfaderAssignNodes(
    assign: 'A' | 'B' | 'Thru',
    left: GainNode,
    right: GainNode,
    thru: GainNode
  ) {
    left.gain.value = assign === 'A' ? 1.0 : 0.0;
    right.gain.value = assign === 'B' ? 1.0 : 0.0;
    thru.gain.value = assign === 'Thru' ? 1.0 : 0.0;
  }

  // --- Deck Loading & Transport ---

  public loadTrack(deckId: DeckId, track: TrackEntity): void {
    this.stop(deckId);
    const state = this.deckStates.get(deckId)!;
    state.track = track;
    state.playbackTime = 0;
    state.effectiveBpm = track.bpm;
    state.cuePoint = track.beatgridOffset || 0;
    this.trackPausedAt.set(deckId, state.cuePoint);

    // If master BPM not set or this deck is master, sync master BPM
    if (state.master || this.mixerState.masterBpm === 126.0) {
      this.mixerState.masterBpm = track.bpm;
    }
  }

  /**
   * Load a local audio file using our back-end for analysis.
   * Replaces the in-browser BeatAnalyzer.analyzeTrack().
   */
  public async loadTrackFromLocalFile(deckId: DeckId, file: File): Promise<TrackEntity | null> {
    try {
      // Decode the file to AudioBuffer
      const arrayBuffer = await file.arrayBuffer();
      const audioBuffer = await this.ctx.decodeAudioData(arrayBuffer);

      // Analyze using our back-end (falls back to in-browser if back-end is unavailable)
      const track = await BackendBeatAnalyzer.analyzeFile(file, audioBuffer, 'local');

      // Store the file/buffer for later playback
      this.trackLoaders.set(deckId, { file, buffer: audioBuffer });

      // Load into the deck
      this.loadTrack(deckId, track);

      return track;
    } catch (err: any) {
      console.error(`Failed to load track from file for deck ${deckId}:`, err);
      return null;
    }
  }

  /**
   * Separate a track into stems using our back-end.
   * Returns the stem paths after separation is complete.
   */
  public async separateStems(
    deckId: DeckId,
    destPath: string,
    stems = 'all',
    mode = 'speed',
    simulate = false
  ): Promise<{ success: boolean; stems: any; resultFolder: string | null; error?: string }> {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track?.url) {
      return { success: false, stems: null, resultFolder: null, error: 'No track loaded on deck' };
    }

    const sourcePath = state.track.url;

    return this.stemService.separate(sourcePath, destPath, stems, mode, simulate);
  }

  /**
   * Load a stem file into a deck.
   * After separation, each stem (vocals, drums, bass, others) is loaded as a separate deck.
   */
  public async loadStem(deckId: DeckId, stemPath: string, stemName: string): Promise<TrackEntity | null> {
    try {
      // Fetch the stem file
      const response = await fetch(stemPath);
      if (!response.ok) {
        console.error(`Failed to fetch stem ${stemName} from ${stemPath}:`, response.status);
        return null;
      }

      const arrayBuffer = await response.arrayBuffer();
      const audioBuffer = await this.ctx.decodeAudioData(arrayBuffer);

      // Create a track entity for this stem
      const track: TrackEntity = {
        id: `stem_${deckId}_${stemName}_${Date.now()}`,
        title: `${state.track?.title || 'Unknown'} - ${stemName}`,
        artist: state.track?.artist || 'Unknown',
        bpm: state.track?.bpm || 120,
        key: state.track?.key || '8A',
        duration: audioBuffer.duration,
        beatgridOffset: state.track?.beatgridOffset || 0,
        hotcues: state.track?.hotcues || [],
        waveformPeaks: state.track?.waveformPeaks || { lows: [], mids: [], highs: [], peaks: [] },
        audioBuffer,
        url: stemPath,
        source: 'stem',
        sizeBytes: arrayBuffer.byteLength,
      };

      // Load into the deck
      this.loadTrack(deckId, track);

      return track;
    } catch (err: any) {
      console.error(`Failed to load stem ${stemName} for deck ${deckId}:`, err);
      return null;
    }
  }

  public play(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track?.audioBuffer) return;
    if (state.isPlaying) return;

    this.resume();
    const channel = this.channels.get(deckId)!;

    // Disconnect old source if any
    if (channel.sourceNode) {
      try {
        channel.sourceNode.stop();
        channel.sourceNode.disconnect();
      } catch {
        // ignore
      }
    }

    const source = this.ctx.createBufferSource();
    source.buffer = state.track.audioBuffer;
    source.playbackRate.value = 1.0 + state.pitchPercent / 100;

    source.connect(channel.gainNode);
    channel.sourceNode = source;

    const pausedTime = this.trackPausedAt.get(deckId) || 0;
    const startTime = this.ctx.currentTime;
    this.trackStartTimes.set(deckId, startTime - pausedTime / source.playbackRate.value);

    source.start(0, pausedTime);
    state.isPlaying = true;

    source.onended = () => {
      // Loop wrapping check
      if (state.loopActive && state.loopEnd > state.loopStart) {
        this.trackPausedAt.set(deckId, state.loopStart);
        state.isPlaying = false;
        this.play(deckId);
      } else {
        state.isPlaying = false;
      }
    };
  }

  public pause(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.isPlaying) return;

    const channel = this.channels.get(deckId)!;
    const currentPos = this.getPlaybackTime(deckId);
    this.trackPausedAt.set(deckId, currentPos);

    if (channel.sourceNode) {
      try {
        channel.sourceNode.stop();
        channel.sourceNode.disconnect();
      } catch {
        // ignore
      }
      channel.sourceNode = null;
    }

    state.isPlaying = false;
    state.playbackTime = currentPos;
  }

  public togglePlay(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;
    if (state.isPlaying) {
      this.pause(deckId);
    } else {
      this.play(deckId);
    }
  }

  public cue(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;

    if (state.isPlaying) {
      // Return to cue point and pause
      this.pause(deckId);
      this.seek(deckId, state.cuePoint);
    } else {
      // If stopped at cue, preview from cue point; otherwise set new cue
      const current = this.getPlaybackTime(deckId);
      if (Math.abs(current - state.cuePoint) < 0.05) {
        // Start momentary play
        this.play(deckId);
      } else {
        state.cuePoint = current;
      }
    }
  }

  public cup(deckId: DeckId): void {
    // Cue and Play
    const state = this.deckStates.get(deckId);
    if (!state) return;
    this.seek(deckId, state.cuePoint);
    this.play(deckId);
  }

  public stop(deckId: DeckId): void {
    this.pause(deckId);
    this.seek(deckId, 0);
  }

  public seek(deckId: DeckId, timeSec: number): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;
    const wasPlaying = state.isPlaying;

    if (wasPlaying) {
      this.pause(deckId);
    }

    this.trackPausedAt.set(deckId, Math.max(0, timeSec));
    state.playbackTime = Math.max(0, timeSec);

    if (wasPlaying) {
      this.play(deckId);
    }
  }

  public getPlaybackTime(deckId: DeckId): number {
    const state = this.deckStates.get(deckId);
    if (!state) return 0;
    if (!state.isPlaying) {
      return this.trackPausedAt.get(deckId) || 0;
    }

    const startTime = this.trackStartTimes.get(deckId) || this.ctx.currentTime;
    const rate = 1.0 + state.pitchPercent / 100;
    const elapsed = (this.ctx.currentTime - startTime) * rate;

    // Check loop boundary
    if (state.loopActive && state.loopEnd > state.loopStart && elapsed >= state.loopEnd) {
      const loopLen = state.loopEnd - state.loopStart;
      const wrapped = state.loopStart + ((elapsed - state.loopStart) % loopLen);
      this.seek(deckId, wrapped);
      return wrapped;
    }

    const duration = state.track?.duration || 1000;
    return Math.min(elapsed, duration);
  }

  // --- Pitch, Tempo & Sync ---

  public setPitch(deckId: DeckId, percent: number): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;
    state.pitchPercent = Math.max(-50, Math.min(50, percent));
    if (state.track) {
      state.effectiveBpm = Math.round(state.track.bpm * (1.0 + state.pitchPercent / 100) * 100) / 100;
    }

    const channel = this.channels.get(deckId);
    if (channel?.sourceNode) {
      const rate = 1.0 + state.pitchPercent / 100;
      channel.sourceNode.playbackRate.setValueAtTime(rate, this.ctx.currentTime);
    }
  }

  public pitchBend(deckId: DeckId, direction: 'nudge_up' | 'nudge_down' | 'release'): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;
    const baseRate = 1.0 + state.pitchPercent / 100;
    const channel = this.channels.get(deckId);
    if (!channel?.sourceNode) return;

    if (direction === 'nudge_up') {
      channel.sourceNode.playbackRate.setValueAtTime(baseRate * 1.04, this.ctx.currentTime);
    } else if (direction === 'nudge_down') {
      channel.sourceNode.playbackRate.setValueAtTime(baseRate * 0.96, this.ctx.currentTime);
    } else {
      channel.sourceNode.playbackRate.setValueAtTime(baseRate, this.ctx.currentTime);
    }
  }

  public sync(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;

    // Find master BPM from master deck or mixer master BPM
    const targetBpm = this.mixerState.masterBpm;
    const requiredPitch = ((targetBpm - state.track.bpm) / state.track.bpm) * 100;
    this.setPitch(deckId, requiredPitch);
    state.sync = true;

    // Phase alignment: snap current beat phase to target downbeat grid
    const beatSec = 60 / targetBpm;
    const currentTime = this.getPlaybackTime(deckId);
    const phaseOffset = (currentTime - state.track.beatgridOffset) % beatSec;
    if (phaseOffset > 0.05 && phaseOffset < beatSec - 0.05) {
      const correction = phaseOffset > beatSec / 2 ? beatSec - phaseOffset : -phaseOffset;
      this.seek(deckId, currentTime + correction);
    }
  }

  public setMasterBpm(bpm: number): void {
    this.mixerState.masterBpm = Math.max(60, Math.min(220, bpm));
  }

  public toggleFlux(deckId: DeckId): boolean {
    const state = this.deckStates.get(deckId);
    if (!state) return false;
    state.fluxMode = !state.fluxMode;
    return state.fluxMode;
  }

  // --- Loops & Hotcues ---

  public toggleLoop(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;

    state.loopActive = !state.loopActive;
    if (state.loopActive) {
      const current = this.getPlaybackTime(deckId);
      const beatSec = 60 / state.effectiveBpm;
      state.loopStart = current;
      state.loopEnd = current + state.loopLength * beatSec;
    }
  }

  public setLoopLength(deckId: DeckId, beats: number): void {
    const state = this.deckStates.get(deckId);
    if (!state) return;
    state.loopLength = beats;
    if (state.loopActive && state.track) {
      const beatSec = 60 / state.effectiveBpm;
      state.loopEnd = state.loopStart + state.loopLength * beatSec;
    }
  }

  public beatjump(deckId: DeckId, beats: number): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    const beatSec = 60 / state.effectiveBpm;
    const jumpSeconds = beats * beatSec;
    const newPos = Math.max(0, this.getPlaybackTime(deckId) + jumpSeconds);
    this.seek(deckId, newPos);
  }

  public triggerHotcue(deckId: DeckId, cueIndex: number): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    const hotcue = state.track.hotcues.find((h) => h.id === cueIndex);

    if (hotcue) {
      this.seek(deckId, hotcue.time);
      if (!state.isPlaying) {
        this.play(deckId);
      }
    } else {
      // Set hotcue at current position
      const currentTime = this.getPlaybackTime(deckId);
      const colors = ['#00C3FF', '#00E575', '#FF9900', '#BD00FF', '#FF2A4D', '#FFFF00', '#FF66CC', '#3399FF'];
      state.track.hotcues.push({
        id: cueIndex,
        time: currentTime,
        type: 'cue',
        color: colors[(cueIndex - 1) % colors.length],
        name: `Cue ${cueIndex}`,
      });
      state.track.hotcues.sort((a, b) => a.id - b.id);
    }
  }

  public deleteHotcue(deckId: DeckId, cueIndex: number): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    state.track.hotcues = state.track.hotcues.filter((h) => h.id !== cueIndex);
  }

  // --- Pioneer DDJ-T1 Jog & Needle Drop & Transport ---

  public touchPlatter(deckId: DeckId, isTouching: boolean): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;

    if (isTouching) {
      this.wasPlayingBeforeTouch.set(deckId, state.isPlaying);
      state.isScratching = true;
      if (state.isPlaying) {
        this.pause(deckId);
      }
    } else {
      state.isScratching = false;
      const wasPlaying = this.wasPlayingBeforeTouch.get(deckId) ?? false;
      if (wasPlaying) {
        this.play(deckId);
      }
    }
  }

  public scratch(deckId: DeckId, deltaTicks: number): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    const current = this.getPlaybackTime(deckId);
    const deltaSec = deltaTicks * 0.018;
    const newPos = Math.max(0, Math.min(state.track.duration, current + deltaSec));
    this.seek(deckId, newPos);
  }

  public needleSearch(deckId: DeckId, fraction: number): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    const clamped = Math.max(0, Math.min(1.0, fraction));
    const targetTime = clamped * state.track.duration;
    this.seek(deckId, targetTime);
  }

  public setLoopIn(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    state.loopStart = this.getPlaybackTime(deckId);
    if (state.loopEnd <= state.loopStart) {
      const beatSec = 60 / state.effectiveBpm;
      state.loopEnd = state.loopStart + state.loopLength * beatSec;
    }
  }

  public setLoopOut(deckId: DeckId): void {
    const state = this.deckStates.get(deckId);
    if (!state || !state.track) return;
    const current = this.getPlaybackTime(deckId);
    if (current > state.loopStart) {
      state.loopEnd = current;
      state.loopActive = true;
    }
  }

  public cycleTempoRange(deckId: DeckId): number {
    const state = this.deckStates.get(deckId);
    if (!state) return 8;
    const ranges = [6, 10, 16, 100];
    const currentIndex = ranges.indexOf(state.tempoRange ?? 8);
    const nextRange = ranges[(currentIndex + 1) % ranges.length];
    state.tempoRange = nextRange;
    return nextRange;
  }

  public setFxAssign(deckId: DeckId, unitIndex: number, enabled?: boolean): boolean {
    const state = this.deckStates.get(deckId);
    if (!state) return false;
    const current = state.fxAssign[unitIndex] ?? false;
    const nextVal = enabled !== undefined ? enabled : !current;
    state.fxAssign[unitIndex] = nextVal;
    return nextVal;
  }


  // --- Mixer Controls: EQs, Filters, Gains, Faders ---

  public setGain(deckId: DeckId, val: number): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    state.gain = val;
    channel.gainNode.gain.setValueAtTime(val, this.ctx.currentTime);
  }

  public setEq(deckId: DeckId, band: 'high' | 'mid' | 'low', dB: number): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;

    if (band === 'high') {
      state.eqHigh = dB;
      if (!state.killHigh) channel.eqHigh.gain.setValueAtTime(dB, this.ctx.currentTime);
    } else if (band === 'mid') {
      state.eqMid = dB;
      if (!state.killMid) channel.eqMid.gain.setValueAtTime(dB, this.ctx.currentTime);
    } else {
      state.eqLow = dB;
      if (!state.killLow) channel.eqLow.gain.setValueAtTime(dB, this.ctx.currentTime);
    }
  }

  public toggleKill(deckId: DeckId, band: 'high' | 'mid' | 'low'): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;

    if (band === 'high') {
      state.killHigh = !state.killHigh;
      channel.eqHigh.gain.setValueAtTime(state.killHigh ? -70 : state.eqHigh, this.ctx.currentTime);
    } else if (band === 'mid') {
      state.killMid = !state.killMid;
      channel.eqMid.gain.setValueAtTime(state.killMid ? -70 : state.eqMid, this.ctx.currentTime);
    } else {
      state.killLow = !state.killLow;
      channel.eqLow.gain.setValueAtTime(state.killLow ? -70 : state.eqLow, this.ctx.currentTime);
    }
  }

  public setFilter(deckId: DeckId, val: number): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    state.filter = Math.max(-1.0, Math.min(1.0, val));

    // -1 = Full LowPass (200Hz), 0 = bypass (20kHz LP, 20Hz HP), +1 = Full HighPass (10kHz)
    if (state.filter < 0) {
      // Low pass active
      const freq = 20000 * Math.pow(0.01, -state.filter);
      channel.filterLowPass.frequency.setValueAtTime(Math.max(100, freq), this.ctx.currentTime);
      channel.filterHighPass.frequency.setValueAtTime(20, this.ctx.currentTime);
    } else if (state.filter > 0) {
      // High pass active
      const freq = 20 * Math.pow(500, state.filter);
      channel.filterHighPass.frequency.setValueAtTime(Math.min(12000, freq), this.ctx.currentTime);
      channel.filterLowPass.frequency.setValueAtTime(20000, this.ctx.currentTime);
    } else {
      channel.filterLowPass.frequency.setValueAtTime(20000, this.ctx.currentTime);
      channel.filterHighPass.frequency.setValueAtTime(20, this.ctx.currentTime);
    }
  }

  public setVolume(deckId: DeckId, val: number): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    state.volume = Math.max(0, Math.min(1.0, val));
    channel.volumeNode.gain.setValueAtTime(state.volume, this.ctx.currentTime);
  }

  public setCuePfl(deckId: DeckId, active: boolean): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    state.cuePfl = active;
    channel.cueGainNode.gain.setValueAtTime(active ? 1.0 : 0.0, this.ctx.currentTime);
  }

  public setCrossfaderAssign(deckId: DeckId, assign: 'A' | 'B' | 'Thru'): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    state.xfadeAssign = assign;
    this.setCrossfaderAssignNodes(assign, channel.xfadeLeftGain, channel.xfadeRightGain, channel.thruGain);
  }

  public toggleFxAssign(deckId: DeckId, fxUnitId: number): void {
    const state = this.deckStates.get(deckId);
    const channel = this.channels.get(deckId);
    if (!state || !channel) return;
    const idx = fxUnitId - 1;
    state.fxAssign[idx] = !state.fxAssign[idx];
    channel.fxSends[idx].gain.setValueAtTime(state.fxAssign[idx] ? 1.0 : 0.0, this.ctx.currentTime);
  }

  // --- FX Unit Parameters & Type Modulation ---

  public setFxParam(
    fxId: number,
    param: 'dryWet' | 'param1' | 'param2' | 'param3',
    val: number
  ): void {
    const idx = fxId - 1;
    if (idx < 0 || idx >= this.fxUnits.length) return;
    const unit = this.fxUnits[idx];
    const dsp = this.fxDspNodes[idx];
    val = Math.max(0, Math.min(1.0, val));
    unit[param] = val;

    if (!dsp) return;

    if (param === 'dryWet') {
      dsp.wetGain.gain.setValueAtTime(val, this.ctx.currentTime);
      dsp.dryGain.gain.setValueAtTime(1.0 - val * 0.5, this.ctx.currentTime);
      return;
    }

    if (unit.type === 'delay') {
      if (param === 'param1' && dsp.delayNode) {
        const masterBpm = this.mixerState?.masterBpm ?? 126.0;
        const beatSec = 60 / masterBpm;
        const delayTime = (0.125 + val * 0.75) * beatSec;
        dsp.delayNode.delayTime.setValueAtTime(delayTime, this.ctx.currentTime);
      } else if (param === 'param2' && dsp.feedbackGain) {
        dsp.feedbackGain.gain.setValueAtTime(Math.min(0.92, val * 0.88), this.ctx.currentTime);
      } else if (param === 'param3' && dsp.dampFilter) {
        const cutoff = 600 * Math.pow(25, val);
        dsp.dampFilter.frequency.setValueAtTime(cutoff, this.ctx.currentTime);
      }
    } else if (unit.type === 'reverb') {
      if (param === 'param1') {
        const duration = 1.0 + val * 3.5;
        const newConvolver = this.createSyntheticReverb(duration);
        if (dsp.reverbDampFilter) {
          try {
            dsp.convolverNode?.disconnect();
            dsp.reverbDampFilter.disconnect();
            dsp.reverbDampFilter.connect(newConvolver);
            newConvolver.connect(dsp.wetGain);
            dsp.convolverNode = newConvolver;
          } catch {
            // Safe fallback
          }
        }
      } else if (param === 'param2' && dsp.reverbDampFilter) {
        dsp.reverbDampFilter.frequency.setValueAtTime(800 * Math.pow(15, val), this.ctx.currentTime);
      }
    } else if (unit.type === 'flanger') {
      if (param === 'param1' && dsp.lfo) {
        dsp.lfo.frequency.setValueAtTime(0.1 + val * 4.0, this.ctx.currentTime);
      } else if (param === 'param2' && dsp.feedbackGain) {
        dsp.feedbackGain.gain.setValueAtTime(Math.min(0.92, val * 0.85), this.ctx.currentTime);
      } else if (param === 'param3' && dsp.lfoGain) {
        dsp.lfoGain.gain.setValueAtTime(0.0005 + val * 0.0025, this.ctx.currentTime);
      }
    } else if (unit.type === 'beatmasher') {
      if (param === 'param1' && dsp.filterNode) {
        const centerFreq = 200 * Math.pow(30, val);
        dsp.filterNode.frequency.setValueAtTime(centerFreq, this.ctx.currentTime);
      } else if (param === 'param2' && dsp.filterNode) {
        dsp.filterNode.Q.setValueAtTime(1.0 + val * 12.0, this.ctx.currentTime);
      }
    }
  }

  public toggleFxUnit(fxId: number, enabled?: boolean): boolean {
    const idx = fxId - 1;
    if (idx < 0 || idx >= this.fxUnits.length) return false;
    const unit = this.fxUnits[idx];
    const dsp = this.fxDspNodes[idx];

    unit.enabled = enabled !== undefined ? enabled : !unit.enabled;
    if (dsp) {
      dsp.output.gain.setValueAtTime(unit.enabled ? 1.0 : 0.0, this.ctx.currentTime);
    }
    return unit.enabled;
  }

  public setFxType(fxId: number, type: FXType): void {
    const idx = fxId - 1;
    if (idx < 0 || idx >= this.fxUnits.length) return;
    this.fxUnits[idx].type = type;
    this.buildDspForUnit(idx, type);
  }

  // --- Crossfader & Master Controls ---

  public updateCrossfader(val: number): void {
    // val: -1.0 (Left, Deck A) to +1.0 (Right, Deck B)
    this.mixerState.crossfader = Math.max(-1.0, Math.min(1.0, val));
    const normalized = (this.mixerState.crossfader + 1.0) / 2.0; // 0 to 1

    let gainA = 1.0;
    let gainB = 1.0;

    if (this.mixerState.crossfaderCurve === 'smooth') {
      // Equal power curve
      gainA = Math.cos(normalized * 0.5 * Math.PI);
      gainB = Math.sin(normalized * 0.5 * Math.PI);
    } else if (this.mixerState.crossfaderCurve === 'linear') {
      gainA = 1.0 - normalized;
      gainB = normalized;
    } else {
      // Scratch: sharp cut-in
      gainA = normalized > 0.95 ? 0.0 : 1.0;
      gainB = normalized < 0.05 ? 0.0 : 1.0;
    }

    this.crossfaderGainA.gain.setValueAtTime(gainA, this.ctx.currentTime);
    this.crossfaderGainB.gain.setValueAtTime(gainB, this.ctx.currentTime);
  }

  public setCrossfaderCurve(curve: CrossfaderCurve): void {
    this.mixerState.crossfaderCurve = curve;
    this.updateCrossfader(this.mixerState.crossfader);
  }

  public setMasterVolume(val: number): void {
    this.mixerState.masterVolume = Math.max(0, Math.min(1.0, val));
    this.masterGain.gain.setValueAtTime(this.mixerState.masterVolume, this.ctx.currentTime);
  }

  public setHeadphoneVolume(val: number): void {
    this.mixerState.headphoneVolume = Math.max(0, Math.min(1.0, val));
    this.headphoneGain.gain.setValueAtTime(this.mixerState.headphoneVolume, this.ctx.currentTime);
  }

  // --- VU Meter Readings (0 to 1.0) ---

  public getChannelVuLevel(deckId: DeckId): number {
    const channel = this.channels.get(deckId);
    if (!channel) return 0;
    const array = new Uint8Array(channel.analyserNode.frequencyBinCount);
    channel.analyserNode.getByteTimeDomainData(array);

    let maxVal = 0;
    for (let i = 0; i < array.length; i++) {
      const val = Math.abs((array[i] - 128) / 128);
      if (val > maxVal) maxVal = val;
    }
    return maxVal;
  }

  public getMasterVuLevels(): { left: number; right: number } {
    const leftData = new Uint8Array(this.masterAnalyserLeft.frequencyBinCount);
    const rightData = new Uint8Array(this.masterAnalyserRight.frequencyBinCount);
    this.masterAnalyserLeft.getByteTimeDomainData(leftData);
    this.masterAnalyserRight.getByteTimeDomainData(rightData);

    let maxL = 0;
    let maxR = 0;
    for (let i = 0; i < leftData.length; i++) {
      const l = Math.abs((leftData[i] - 128) / 128);
      const r = Math.abs((rightData[i] - 128) / 128);
      if (l > maxL) maxL = l;
      if (r > maxR) maxR = r;
    }
    return { left: maxL, right: maxR };
  }

  public getDeckState(deckId: DeckId): DeckState {
    return this.deckStates.get(deckId)!;
  }

  public getMixerState(): MixerState {
    return this.mixerState;
  }

  // --- Bundled Electronic Demo Tracks Generation ---

  public async generateDemoTracks(): Promise<TrackEntity[]> {
    const track1 = await this.synthesizeClubTrack(
      'Neon Horizon',
      'Traktor Prime',
      126,
      '8A',
      'electro_house'
    );
    const track2 = await this.synthesizeClubTrack(
      'Acid Velocity',
      'Traktor Apex',
      128,
      '9A',
      'tech_trance'
    );
    return [track1, track2];
  }

  private async synthesizeClubTrack(
    title: string,
    artist: string,
    bpm: number,
    key: string,
    genre: 'electro_house' | 'tech_trance'
  ): Promise<TrackEntity> {
    const duration = 76.0; // ~1 minute 16 seconds loop
    const sampleRate = 44100;
    const totalSamples = Math.floor(sampleRate * duration);
    const buffer = this.ctx.createBuffer(2, totalSamples, sampleRate);
    const left = buffer.getChannelData(0);
    const right = buffer.getChannelData(1);

    const beatSec = 60 / bpm;
    const barSec = beatSec * 4;

    // Synthesize kick, sub bass, hi-hat, clap, and synth arpeggios
    for (let i = 0; i < totalSamples; i++) {
      const t = i / sampleRate;
      const beatNum = t / beatSec;
      const barNum = Math.floor(t / barSec);
      const beatInBar = Math.floor(beatNum % 4);
      const timeInBeat = (t % beatSec) / beatSec;

      let l = 0;
      let r = 0;

      // 1. Kick Drum (Punchy 909-style pitch swept sine)
      const kickEnv = Math.exp(-timeInBeat * 14);
      const kickFreq = 160 * Math.exp(-timeInBeat * 30) + 48;
      const kickSample = Math.sin(2 * Math.PI * kickFreq * (timeInBeat * beatSec)) * kickEnv;
      l += kickSample * 0.75;
      r += kickSample * 0.75;

      // 2. Offbeat Open Hi-Hat
      const hatTime = ((timeInBeat + 0.5) % 1.0);
      const hatEnv = Math.exp(-hatTime * 18);
      const whiteNoise = (Math.random() * 2 - 1) * hatEnv;
      l += whiteNoise * 0.22;
      r += whiteNoise * 0.26;

      // 3. Claps on beats 2 and 4
      if (beatInBar === 1 || beatInBar === 3) {
        const clapEnv = Math.exp(-timeInBeat * 12);
        const clapNoise = (Math.random() * 2 - 1) * clapEnv;
        l += clapNoise * 0.35;
        r += clapNoise * 0.35;
      }

      // 4. Bassline (16th note rolling bass)
      const sixteenth = (beatNum * 4) % 1.0;
      const bassEnv = Math.exp(-sixteenth * 8);
      const bassFreq = genre === 'electro_house' ? 55 : 65.4; // A1 or C2
      const bassNote = Math.sin(2 * Math.PI * bassFreq * t) * 0.45 * bassEnv;
      l += bassNote;
      r += bassNote;

      // 5. Melodic Synth Lead / Arp
      const arpNote = Math.floor(beatNum * 2) % 8;
      const frequencies = [220, 261.63, 329.63, 392, 440, 523.25, 440, 329.63];
      const leadFreq = frequencies[arpNote] * (genre === 'tech_trance' ? 1.5 : 1.0);
      const leadEnv = Math.exp(-((t % (beatSec / 2)) / (beatSec / 2)) * 6);
      const sawWave = (2 * ((t * leadFreq) % 1.0) - 1.0) * leadEnv * 0.16;
      l += sawWave * 0.8;
      r += sawWave * 1.2;

      // Soft limiting
      left[i] = Math.tanh(l);
      right[i] = Math.tanh(r);
    }

    return BeatAnalyzer.analyzeTrack({ name: `${artist} - ${title}.wav`, size: totalSamples * 4 }, buffer, 'demo');
  }
}
