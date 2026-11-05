import { AudioEngine } from './audioEngine';
import { DeckId } from '../types';

export interface MidiDeviceStatus {
  supported: boolean;
  connected: boolean;
  deviceName: string;
  isPioneerDDJT1: boolean;
  lastMessageTime: number;
  shiftHeld: boolean;
  currentJogMode: 'scratch' | 'pitchbend';
}

export interface WebMidiInput {
  id: string;
  name?: string;
  state: string;
  onmidimessage: ((event: WebMidiMessageEvent) => void) | null;
}

export interface WebMidiOutput {
  id: string;
  name?: string;
  state: string;
  send: (data: number[] | Uint8Array, timestamp?: number) => void;
}

export interface WebMidiMessageEvent {
  data: Uint8Array;
}

export interface WebMidiAccess {
  inputs: {
    values: () => IterableIterator<WebMidiInput>;
  };
  outputs: {
    values: () => IterableIterator<WebMidiOutput>;
  };
  onstatechange: (() => void) | null;
}

export class MidiControllerService {
  private static instance: MidiControllerService;
  private engine: AudioEngine;
  private midiAccess: WebMidiAccess | null = null;
  private midiOutputs: WebMidiOutput[] = [];
  private statusListeners: Array<(status: MidiDeviceStatus) => void> = [];
  private activityListeners: Array<(detail: { channel: number; type: string; data1: number; data2: number; name?: string }) => void> = [];
  private browserScrollListeners: Array<(delta: number) => void> = [];
  private browserLoadListeners: Array<(deckId: DeckId) => void> = [];
  private browserToggleListeners: Array<() => void> = [];

  // Track shift state and platter touch state per deck
  private shiftState = false;
  private platterTouchState: Map<DeckId, boolean> = new Map([
    ['A', false],
    ['B', false],
    ['C', false],
    ['D', false],
  ]);
  private pitchBendTimeout: Map<DeckId, number> = new Map();

  private currentStatus: MidiDeviceStatus = {
    supported: false,
    connected: false,
    deviceName: 'None',
    isPioneerDDJT1: false,
    lastMessageTime: 0,
    shiftHeld: false,
    currentJogMode: 'pitchbend',
  };

  private constructor() {
    this.engine = AudioEngine.getInstance();
    this.initMidi();
    this.startLedFeedbackLoop();
  }

  public static getInstance(): MidiControllerService {
    if (!MidiControllerService.instance) {
      MidiControllerService.instance = new MidiControllerService();
    }
    return MidiControllerService.instance;
  }

  public onStatusChange(callback: (status: MidiDeviceStatus) => void): () => void {
    this.statusListeners.push(callback);
    callback(this.currentStatus);
    return () => {
      this.statusListeners = this.statusListeners.filter((cb) => cb !== callback);
    };
  }

  public onMidiActivity(
    callback: (detail: { channel: number; type: string; data1: number; data2: number; name?: string }) => void
  ): () => void {
    this.activityListeners.push(callback);
    return () => {
      this.activityListeners = this.activityListeners.filter((cb) => cb !== callback);
    };
  }

  public onBrowserScroll(callback: (delta: number) => void): () => void {
    this.browserScrollListeners.push(callback);
    return () => {
      this.browserScrollListeners = this.browserScrollListeners.filter((cb) => cb !== callback);
    };
  }

  public onBrowserLoad(callback: (deckId: DeckId) => void): () => void {
    this.browserLoadListeners.push(callback);
    return () => {
      this.browserLoadListeners = this.browserLoadListeners.filter((cb) => cb !== callback);
    };
  }

  public onBrowserToggle(callback: () => void): () => void {
    this.browserToggleListeners.push(callback);
    return () => {
      this.browserToggleListeners = this.browserToggleListeners.filter((cb) => cb !== callback);
    };
  }

  public getStatus(): MidiDeviceStatus {
    return { ...this.currentStatus };
  }

  public async initMidi(): Promise<void> {
    const nav = navigator as any;
    if (typeof nav === 'undefined' || !nav.requestMIDIAccess) {
      this.currentStatus.supported = false;
      this.notifyStatus();
      return;
    }

    this.currentStatus.supported = true;
    try {
      this.midiAccess = await nav.requestMIDIAccess({ sysex: true });
      this.detectDevices();

      if (this.midiAccess) {
        this.midiAccess.onstatechange = () => {
          this.detectDevices();
        };
      }
    } catch (err) {
      console.warn('Web MIDI Access request fallback without sysex:', err);
      try {
        this.midiAccess = await nav.requestMIDIAccess({ sysex: false });
        this.detectDevices();
      } catch (e) {
        this.currentStatus.connected = false;
        this.notifyStatus();
      }
    }
  }

  private detectDevices(): void {
    if (!this.midiAccess) return;

    let foundConnected = false;
    let foundPioneer = false;
    let deviceLabel = 'No MIDI Controller';
    this.midiOutputs = [];

    const inputs = this.midiAccess.inputs.values();
    for (const input of inputs) {
      if (input.state === 'connected') {
        foundConnected = true;
        deviceLabel = input.name || 'Generic MIDI Device';
        const nameLower = deviceLabel.toLowerCase();
        if (
          nameLower.includes('ddj-t1') ||
          nameLower.includes('pioneer') ||
          nameLower.includes('ddj') ||
          nameLower.includes('asio')
        ) {
          foundPioneer = true;
        }

        input.onmidimessage = (event: WebMidiMessageEvent) => this.handleMidiMessage(event, foundPioneer);
      }
    }

    const outputs = this.midiAccess.outputs.values();
    for (const output of outputs) {
      if (output.state === 'connected') {
        this.midiOutputs.push(output);
      }
    }

    this.currentStatus.connected = foundConnected;
    this.currentStatus.deviceName = foundConnected ? deviceLabel : 'None';
    this.currentStatus.isPioneerDDJT1 = foundPioneer;
    this.notifyStatus();
  }

  /**
   * Complete Native Pioneer DDJ-T1 Traktor Mapping Implementation:
   * Channels:
   * - Deck A = Channel 1 (Status 0x90 / 0x80 / 0xB0)
   * - Deck B = Channel 2 (Status 0x91 / 0x81 / 0xB1)
   * - Deck C = Channel 3 (Status 0x92 / 0x82 / 0xB2)
   * - Deck D = Channel 4 (Status 0x93 / 0x83 / 0xB3)
   * - Global / Master / FX / Browser = Channel 1, 7, 9, 15, 16
   */
  private handleMidiMessage(event: WebMidiMessageEvent, isPioneer: boolean): void {
    const data = event.data;
    if (!data || data.length < 3) return;

    const statusByte = data[0];
    const data1 = data[1]; // Note number or CC number
    const data2 = data[2]; // Velocity or CC value (0 - 127)

    const channel = (statusByte & 0x0f) + 1; // 1 to 16
    const messageType = statusByte & 0xf0; // 0x90 = Note On, 0x80 = Note Off, 0xB0 = CC

    this.currentStatus.lastMessageTime = performance.now();

    // Map deck assignment according to channel
    const deckId: DeckId = channel === 1 ? 'A' : channel === 2 ? 'B' : channel === 3 ? 'C' : 'D';

    // 1. SHIFT BUTTON HANDLING
    // Pioneer DDJ-T1 Shift button sends Note 0x3F (63), Note 0x40 (64), or Note 0x3C (60)
    if (data1 === 0x3f || data1 === 0x40 || data1 === 0x3c) {
      const isShiftDown = messageType === 0x90 && data2 > 0;
      this.shiftState = isShiftDown;
      this.currentStatus.shiftHeld = isShiftDown;
      this.notifyStatus();
      this.notifyActivity({ channel, type: 'Shift', data1, data2, name: `Shift ${isShiftDown ? 'DOWN' : 'UP'}` });
      return;
    }

    // 2. CONTROL CHANGE (Knobs, Faders, Jog Wheel, Needle Search)
    if (messageType === 0xb0) {
      const normalized = data2 / 127; // 0.0 to 1.0

      // A. JOG WHEEL ROTATION (SCRATCH vs PITCH BEND)
      // Pioneer DDJ-T1 sends relative encoder values on CC 0x21 (33), CC 0x22 (34), CC 0x20 (32), or CC 0x23:
      // Values: 1..63 = Forward / Clockwise; 65..127 = Backward / Counter-Clockwise (2's complement relative)
      if (data1 === 33 || data1 === 34 || data1 === 32 || data1 === 35) {
        const delta = data2 < 64 ? data2 : data2 - 128;
        const isPlatterTouched = this.platterTouchState.get(deckId) ?? false;

        if (isPlatterTouched) {
          // SCRATCH MODE: Direct audio scrubbing with scratch inertia
          this.engine.scratch(deckId, delta);
          this.notifyActivity({ channel, type: 'JogScratch', data1, data2, name: `Deck ${deckId} Jog Scratch (delta: ${delta})` });
        } else {
          // PITCH BEND / NUDGE MODE: Outer rim temporary pitch alteration
          const dir = delta > 0 ? 'nudge_up' : 'nudge_down';
          this.engine.pitchBend(deckId, dir);

          // Clear previous timeout and schedule release
          const existing = this.pitchBendTimeout.get(deckId);
          if (existing) window.clearTimeout(existing);
          const tId = window.setTimeout(() => {
            this.engine.pitchBend(deckId, 'release');
          }, 110);
          this.pitchBendTimeout.set(deckId, tId);

          this.notifyActivity({ channel, type: 'JogNudge', data1, data2, name: `Deck ${deckId} Jog Pitchbend (${dir})` });
        }
        return;
      }

      // B. NEEDLE SEARCH / NEEDLE DROP STRIP
      // Pioneer DDJ-T1 Ribbon controller: CC 0x4B (75), CC 0x49 (73), CC 0x50 (80), CC 0x51 (81)
      if (data1 === 75 || data1 === 73 || data1 === 80 || data1 === 81) {
        this.engine.needleSearch(deckId, normalized);
        this.notifyActivity({
          channel,
          type: 'NeedleSearch',
          data1,
          data2,
          name: `Deck ${deckId} Needle Search to ${(normalized * 100).toFixed(1)}%`,
        });
        return;
      }

      // C. CROSSFADER: CC 31 (0x1F) or CC 10
      if (data1 === 31 || data1 === 10) {
        const xfadeVal = (normalized - 0.5) * 2; // -1.0 to +1.0
        this.engine.updateCrossfader(xfadeVal);
        this.notifyActivity({ channel, type: 'Crossfader', data1, data2, name: `Crossfader: ${xfadeVal.toFixed(2)}` });
        return;
      }

      // D. MASTER VOLUME: CC 7 on channel 16 or CC 8
      if ((data1 === 7 && channel === 16) || data1 === 8) {
        this.engine.setMasterVolume(normalized);
        this.notifyActivity({ channel, type: 'MasterVol', data1, data2, name: `Master Vol: ${(normalized * 100).toFixed(0)}%` });
        return;
      }

      // E. HEADPHONE MIX & VOLUME
      if (data1 === 9 || data1 === 28) {
        this.engine.setHeadphoneMix(normalized);
        return;
      }
      if (data1 === 29) {
        this.engine.setHeadphoneVolume(normalized);
        return;
      }

      // F. CHANNEL LINE FADER: CC 19 (0x13) or CC 7 per channel
      if (data1 === 19 || data1 === 7) {
        this.engine.setVolume(deckId, normalized);
        this.notifyActivity({ channel, type: 'VolFader', data1, data2, name: `Deck ${deckId} Fader: ${(normalized * 100).toFixed(0)}%` });
        return;
      }

      // G. 3-BAND EQ KNOBS
      // High EQ: CC 16 (0x10) or CC 15
      if (data1 === 16 || data1 === 15) {
        const dbVal = (normalized - 0.5) * 30; // -15 to +15 dB
        this.engine.setEq(deckId, 'high', dbVal);
        this.notifyActivity({ channel, type: 'EQHigh', data1, data2, name: `Deck ${deckId} EQ High: ${dbVal.toFixed(1)}dB` });
        return;
      }
      // Mid EQ: CC 17 (0x11) or CC 14
      if (data1 === 17 || data1 === 14) {
        const dbVal = (normalized - 0.5) * 30;
        this.engine.setEq(deckId, 'mid', dbVal);
        this.notifyActivity({ channel, type: 'EQMid', data1, data2, name: `Deck ${deckId} EQ Mid: ${dbVal.toFixed(1)}dB` });
        return;
      }
      // Low EQ: CC 18 (0x12) or CC 13
      if (data1 === 18 || data1 === 13) {
        const dbVal = (normalized - 0.5) * 30;
        this.engine.setEq(deckId, 'low', dbVal);
        this.notifyActivity({ channel, type: 'EQLow', data1, data2, name: `Deck ${deckId} EQ Low: ${dbVal.toFixed(1)}dB` });
        return;
      }

      // H. GAIN / TRIM: CC 4 or CC 9
      if (data1 === 4 || data1 === 9) {
        const gainVal = normalized * 2.0;
        this.engine.setGain(deckId, gainVal);
        this.notifyActivity({ channel, type: 'Gain', data1, data2, name: `Deck ${deckId} Trim: ${(gainVal * 100).toFixed(0)}%` });
        return;
      }

      // I. BIPOLAR FILTER KNOB: CC 23 (0x17) or CC 26
      if (data1 === 23 || data1 === 26) {
        const filterVal = (normalized - 0.5) * 2; // -1.0 to +1.0
        this.engine.setFilter(deckId, filterVal);
        this.notifyActivity({ channel, type: 'Filter', data1, data2, name: `Deck ${deckId} Filter: ${filterVal.toFixed(2)}` });
        return;
      }

      // J. PITCH / TEMPO FADER: CC 0 or CC 1
      if (data1 === 0 || data1 === 1) {
        const deckState = this.engine.getDeckState(deckId);
        const range = deckState.tempoRange ?? 8;
        // Invert / match standard DJ orientation (down = faster)
        const pitchPercent = (normalized - 0.5) * (range * 2);
        this.engine.setPitch(deckId, pitchPercent);
        this.notifyActivity({ channel, type: 'PitchFader', data1, data2, name: `Deck ${deckId} Tempo: ${pitchPercent > 0 ? '+' : ''}${pitchPercent.toFixed(2)}%` });
        return;
      }

      // K. BROWSER ROTARY ENCODER: CC 64 (0x40) or CC 27 (0x1B)
      if (data1 === 64 || data1 === 27) {
        const delta = data2 < 64 ? data2 : data2 - 128;
        for (const cb of this.browserScrollListeners) cb(delta);
        this.notifyActivity({ channel, type: 'BrowseScroll', data1, data2, name: `Browse Scroll (${delta > 0 ? 'Down' : 'Up'})` });
        return;
      }

      // L. LOOP SIZE ROTARY ENCODER: CC 32 (0x20)
      if (data1 === 32) {
        const delta = data2 < 64 ? 1 : -1;
        const currentLen = this.engine.getDeckState(deckId).loopLength;
        const newLen = delta > 0 ? Math.min(32, currentLen * 2) : Math.max(0.03125, currentLen / 2);
        this.engine.setLoopLength(deckId, newLen);
        this.notifyActivity({ channel, type: 'LoopSize', data1, data2, name: `Deck ${deckId} Loop Size: ${newLen} beats` });
        return;
      }

      // M. FX UNIT CONTROLS (Knobs: Dry/Wet & Params 1, 2, 3)
      // FX Unit 1: Dry/Wet = CC 2 or CC 92, Params = CC 3, CC 5, CC 6
      if (data1 === 2 || data1 === 92) {
        this.engine.setFxDryWet(1, normalized);
        return;
      }
      if (data1 === 3) {
        this.engine.setFxParam(1, 1, normalized);
        return;
      }
      if (data1 === 5) {
        this.engine.setFxParam(1, 2, normalized);
        return;
      }
      if (data1 === 6) {
        this.engine.setFxParam(1, 3, normalized);
        return;
      }
    }

    // 3. NOTE ON & NOTE OFF (Buttons, Jog Touch, Hotcues, Transport)
    if (messageType === 0x90 || messageType === 0x80) {
      const isPress = messageType === 0x90 && data2 > 0;

      // A. JOG PLATTER TOUCH (Top metal surface touch for scratch activation)
      // Pioneer DDJ-T1 sends Note 0x36 (54), Note 0x48 (72), Note 0x34 (52), or Note 0x35 (53)
      if (data1 === 54 || data1 === 72 || data1 === 52 || data1 === 53) {
        this.platterTouchState.set(deckId, isPress);
        this.currentStatus.currentJogMode = isPress ? 'scratch' : 'pitchbend';
        this.engine.touchPlatter(deckId, isPress);
        this.notifyStatus();
        this.notifyActivity({
          channel,
          type: 'JogTouch',
          data1,
          data2,
          name: `Deck ${deckId} Platter ${isPress ? 'TOUCH DOWN (Scratch Active)' : 'TOUCH RELEASE (Scratch Inactive)'}`,
        });
        return;
      }

      // Only handle button actions on Press (Note On)
      if (!isPress) return;

      // B. TRANSPORT: PLAY / PAUSE
      // Pioneer DDJ-T1 Play button: Note 11 (0x0B) or Note 0
      if (data1 === 11 || data1 === 0) {
        this.engine.togglePlay(deckId);
        this.notifyActivity({ channel, type: 'Play', data1, data2, name: `Deck ${deckId} Play/Pause` });
        return;
      }

      // C. TRANSPORT: CUE
      // Pioneer DDJ-T1 Cue button: Note 12 (0x0C) or Note 1
      if (data1 === 12 || data1 === 1) {
        this.engine.cue(deckId);
        this.notifyActivity({ channel, type: 'Cue', data1, data2, name: `Deck ${deckId} Cue Jump` });
        return;
      }

      // D. TRANSPORT: SYNC
      // Pioneer DDJ-T1 Sync button: Note 88 (0x58) or Note 2
      if (data1 === 88 || data1 === 2) {
        this.engine.sync(deckId);
        this.notifyActivity({ channel, type: 'Sync', data1, data2, name: `Deck ${deckId} Sync BPM & Phase` });
        return;
      }

      // E. TRANSPORT: MASTER DECK SET
      if (data1 === 89 || data1 === 3) {
        const state = this.engine.getDeckState(deckId);
        state.master = !state.master;
        return;
      }

      // F. KEY LOCK (Master Tempo)
      if (data1 === 26 || data1 === 82) {
        this.engine.toggleKeyLock(deckId);
        this.notifyActivity({ channel, type: 'KeyLock', data1, data2, name: `Deck ${deckId} Key Lock Toggled` });
        return;
      }

      // G. TEMPO RANGE CYCLE (+-6%, +-10%, +-16%, +-100%)
      if (data1 === 90 || data1 === 83) {
        const nextRange = this.engine.cycleTempoRange(deckId);
        this.notifyActivity({ channel, type: 'TempoRange', data1, data2, name: `Deck ${deckId} Tempo Range: ±${nextRange}%` });
        return;
      }

      // H. LOOP IN / 4 BEAT
      if (data1 === 16 || data1 === 14) {
        this.engine.setLoopIn(deckId);
        this.notifyActivity({ channel, type: 'LoopIn', data1, data2, name: `Deck ${deckId} Loop In Set` });
        return;
      }

      // I. LOOP OUT
      if (data1 === 17 || data1 === 15) {
        this.engine.setLoopOut(deckId);
        this.notifyActivity({ channel, type: 'LoopOut', data1, data2, name: `Deck ${deckId} Loop Out Set & Active` });
        return;
      }

      // J. AUTO LOOP TOGGLE
      if (data1 === 18 || data1 === 13 || data1 === 4) {
        this.engine.toggleLoop(deckId);
        this.notifyActivity({ channel, type: 'AutoLoop', data1, data2, name: `Deck ${deckId} Auto Loop Toggled` });
        return;
      }

      // K. BEATJUMP / MOVE
      // Move BWD: Note 21 (0x15); Move FWD: Note 22 (0x16)
      if (data1 === 21) {
        this.engine.beatjump(deckId, -4);
        this.notifyActivity({ channel, type: 'Beatjump', data1, data2, name: `Deck ${deckId} Beatjump -4 beats` });
        return;
      }
      if (data1 === 22) {
        this.engine.beatjump(deckId, 4);
        this.notifyActivity({ channel, type: 'Beatjump', data1, data2, name: `Deck ${deckId} Beatjump +4 beats` });
        return;
      }

      // L. HOTCUES 1 - 8 (or DELETE with Shift)
      // Notes 24 to 31 (0x18 to 0x1F) or Notes 0 to 7 (0x00 to 0x07)
      let cueIndex = -1;
      if (data1 >= 24 && data1 <= 31) {
        cueIndex = data1 - 24 + 1;
      } else if (data1 >= 0x00 && data1 <= 0x07) {
        cueIndex = data1 + 1;
      }

      if (cueIndex >= 1 && cueIndex <= 8) {
        if (this.shiftState) {
          // SHIFT + HOTCUE = DELETE HOTCUE
          this.engine.deleteHotcue(deckId, cueIndex);
          this.notifyActivity({ channel, type: 'HotcueDelete', data1, data2, name: `Deck ${deckId} Deleted Hotcue ${cueIndex}` });
        } else {
          // TRIGGER / SET HOTCUE
          this.engine.triggerHotcue(deckId, cueIndex);
          this.notifyActivity({ channel, type: 'HotcueTrigger', data1, data2, name: `Deck ${deckId} Triggered Hotcue ${cueIndex}` });
        }
        return;
      }

      // M. HEADPHONE CUE (PFL Monitor)
      if (data1 === 84 || data1 === 24 || data1 === 5) {
        const currentPfl = this.engine.getDeckState(deckId).cuePfl;
        this.engine.setCuePfl(deckId, !currentPfl);
        this.notifyActivity({ channel, type: 'CuePfl', data1, data2, name: `Deck ${deckId} Cue Monitor: ${!currentPfl}` });
        return;
      }

      // N. FX 1 & FX 2 ASSIGN BUTTONS
      // FX 1 Assign: Note 70 (0x46) or Note 85
      if (data1 === 70 || data1 === 85) {
        const active = this.engine.setFxAssign(deckId, 0);
        this.notifyActivity({ channel, type: 'Fx1Assign', data1, data2, name: `Deck ${deckId} FX1: ${active}` });
        return;
      }
      // FX 2 Assign: Note 71 (0x47) or Note 86
      if (data1 === 71 || data1 === 86) {
        const active = this.engine.setFxAssign(deckId, 1);
        this.notifyActivity({ channel, type: 'Fx2Assign', data1, data2, name: `Deck ${deckId} FX2: ${active}` });
        return;
      }

      // O. BROWSER PUSH BUTTON (Toggle library drawer or select folder)
      // Note 65 (0x41)
      if (data1 === 65) {
        for (const cb of this.browserToggleListeners) cb();
        this.notifyActivity({ channel, type: 'BrowsePush', data1, data2, name: 'Browser Push / Library Toggle' });
        return;
      }

      // P. LOAD DECK BUTTONS: Load A, B, C, D
      // Load A = Note 70; Load B = Note 71; Load C = Note 72; Load D = Note 73
      if (data1 >= 70 && data1 <= 73) {
        const targetDeck: DeckId = data1 === 70 ? 'A' : data1 === 71 ? 'B' : data1 === 72 ? 'C' : 'D';
        for (const cb of this.browserLoadListeners) cb(targetDeck);
        this.notifyActivity({ channel, type: 'LoadDeck', data1, data2, name: `Load selected track into Deck ${targetDeck}` });
        return;
      }
    }
  }

  /**
   * Sends LED feedback to the physical Pioneer DDJ-T1 controller to illuminate buttons
   */
  private startLedFeedbackLoop(): void {
    window.setInterval(() => {
      if (!this.currentStatus.connected || this.midiOutputs.length === 0) return;

      const decks: DeckId[] = ['A', 'B', 'C', 'D'];
      decks.forEach((deckId, index) => {
        const state = this.engine.getDeckState(deckId);
        const channelByte = 0x90 | index;

        // Play/Pause LED: Note 11 (solid when playing, flashing/off when paused)
        const playVelocity = state.isPlaying ? 127 : 0;
        this.sendMidi([channelByte, 11, playVelocity]);

        // Cue LED: Note 12 (lit when track has cue or stopped)
        const cueVelocity = state.track && !state.isPlaying ? 127 : 0;
        this.sendMidi([channelByte, 12, cueVelocity]);

        // Sync LED: Note 88
        this.sendMidi([channelByte, 88, state.sync ? 127 : 0]);

        // Loop Active LED: Note 18
        this.sendMidi([channelByte, 18, state.loopActive ? 127 : 0]);

        // Headphone Cue PFL: Note 84
        this.sendMidi([channelByte, 84, state.cuePfl ? 127 : 0]);

        // Hotcues 1-8 LEDs: Notes 24 to 31
        for (let i = 1; i <= 8; i++) {
          const hasCue = state.track?.hotcues?.some((h) => h.id === i);
          this.sendMidi([channelByte, 24 + (i - 1), hasCue ? 127 : 0]);
        }
      });
    }, 150);
  }

  private sendMidi(data: number[]): void {
    for (const out of this.midiOutputs) {
      try {
        out.send(data);
      } catch (e) {
        // Ignore dropped output frames
      }
    }
  }

  /**
   * Virtual simulation of any Pioneer DDJ-T1 control for browser testing
   */
  public simulateMidiMessage(channel: number, type: 'NoteOn' | 'NoteOff' | 'CC', data1: number, data2: number): void {
    const statusByte = (type === 'NoteOn' ? 0x90 : type === 'NoteOff' ? 0x80 : 0xb0) | ((channel - 1) & 0x0f);
    const fakeEvent: WebMidiMessageEvent = {
      data: new Uint8Array([statusByte, data1, data2]),
    };

    this.currentStatus.connected = true;
    if (this.currentStatus.deviceName === 'None') {
      this.currentStatus.deviceName = 'Pioneer DDJ-T1 (Virtual Hardware Connected)';
      this.currentStatus.isPioneerDDJT1 = true;
      this.notifyStatus();
    }
    this.handleMidiMessage(fakeEvent, true);
  }

  public async requestMidi(): Promise<void> {
    await this.initMidi();
  }

  private notifyStatus(): void {
    for (const listener of this.statusListeners) {
      listener(this.currentStatus);
    }
  }

  private notifyActivity(detail: { channel: number; type: string; data1: number; data2: number; name?: string }): void {
    for (const listener of this.activityListeners) {
      listener(detail);
    }
  }
}
