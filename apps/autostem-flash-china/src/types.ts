export type DeckId = 'A' | 'B' | 'C' | 'D';

export type HotcueType = 'cue' | 'loop' | 'fade-in' | 'fade-out' | 'grid';

export interface Hotcue {
  id: number;
  time: number; // in seconds
  type: HotcueType;
  color: string;
  name: string;
}

export interface TrackEntity {
  id: string;
  title: string;
  artist: string;
  album?: string;
  bpm: number;
  key: string; // e.g. "8A" or "Am"
  duration: number; // in seconds
  beatgridOffset: number; // in seconds to first downbeat
  hotcues: Hotcue[];
  waveformPeaks?: {
    lows: number[];
    mids: number[];
    highs: number[];
    peaks: number[];
  };
  audioBuffer?: AudioBuffer;
  url?: string;
  source: 'local' | 'drive' | 'demo' | 'firebase';
  sizeBytes?: number;
}

export interface AnalysisBackupFile {
  version: '1.0';
  exportedAt: string;
  appName: string;
  tracks: Record<
    string,
    {
      title: string;
      artist: string;
      bpm: number;
      key: string;
      duration: number;
      beatgridOffset: number;
      hotcues: Hotcue[];
    }
  >;
}

export interface DeckState {
  deckId: DeckId;
  track: TrackEntity | null;
  isPlaying: boolean;
  playbackTime: number; // in seconds
  pitchPercent: number; // e.g. -8% to +8%
  effectiveBpm: number;
  sync: boolean;
  master: boolean;
  keyLock: boolean;
  loopActive: boolean;
  loopLength: number; // in beats: 0.03125 (1/32) to 32
  loopStart: number;
  loopEnd: number;
  fluxMode: boolean;
  cuePoint: number;
  // Channel controls
  gain: number; // 0 to 2.0 (1.0 = nominal)
  eqHigh: number; // -24 to +6 dB
  eqMid: number;
  eqLow: number;
  killHigh: boolean;
  killMid: boolean;
  killLow: boolean;
  filter: number; // -1.0 (LP) to +1.0 (HP), 0 is flat
  volume: number; // 0 to 1.0
  cuePfl: boolean;
  xfadeAssign: 'A' | 'B' | 'Thru';
  fxAssign: boolean[]; // [fx1, fx2, fx3, fx4]
  vuLevel: number; // 0 to 1.0 (peak level for meter)
  tempoRange: number; // 6, 10, 16, or 100%
  isScratching: boolean;
}

export type FXType = 'delay' | 'reverb' | 'flanger' | 'beatmasher';

export interface FXUnitState {
  id: number; // 1, 2, 3, 4
  enabled: boolean;
  type: FXType;
  dryWet: number; // 0 to 1.0
  param1: number; // 0 to 1.0
  param2: number; // 0 to 1.0
  param3: number; // 0 to 1.0
}

export type CrossfaderCurve = 'smooth' | 'linear' | 'scratch';

export interface MixerState {
  crossfader: number; // -1.0 (Deck A) to +1.0 (Deck B)
  crossfaderCurve: CrossfaderCurve;
  masterVolume: number; // 0 to 1.0
  masterVuLeft: number;
  masterVuRight: number;
  headphoneVolume: number;
  headphoneMix: number; // 0 (Cue) to 1 (Master)
  masterBpm: number;
  layoutMode: '2decks' | '4decks' | 'browser';
  activeFxUnits: FXUnitState[];
}

export interface GoogleTaskItem {
  id: string;
  title: string;
  notes?: string;
  status: 'needsAction' | 'completed';
  due?: string;
}

export interface GoogleKeepSetlistNote {
  id: string;
  title: string;
  text: string;
  updatedAt: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: number;
  modelUsed?: string;
}

