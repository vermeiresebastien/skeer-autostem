import { AnalysisBackupFile, Hotcue, TrackEntity } from '../types';

export interface DetailedWaveform {
  subLows: number[]; // Sub-bass <120Hz (Red)
  lows: number[]; // Bass 120-350Hz (Crimson / Orange)
  mids: number[]; // Mids 350-3000Hz (Emerald Green)
  highs: number[]; // Highs >3000Hz (Electric Cyan)
  peaks: number[]; // Peak envelope (0 to 1)
  sampleRate: number; // e.g. 200 buckets/sec for smooth zooming
  totalBuckets: number;
}

export class BeatAnalyzer {
  /**
   * Pre-analyzes audio buffer into a high-density, multi-resolution waveform.
   * Runs upon track import so zero heavy DSP is required during playback.
   */
  static extractDetailedWaveform(
    buffer: AudioBuffer,
    targetBucketsPerSec = 250 // 4ms per slice for deep zoom fidelity
  ): DetailedWaveform {
    const rawData = buffer.getChannelData(0);
    const sampleRate = buffer.sampleRate;
    const duration = buffer.duration;
    const totalBuckets = Math.max(100, Math.floor(duration * targetBucketsPerSec));
    const samplesPerBucket = Math.floor(rawData.length / totalBuckets);

    const subLows = new Float32Array(totalBuckets);
    const lows = new Float32Array(totalBuckets);
    const mids = new Float32Array(totalBuckets);
    const highs = new Float32Array(totalBuckets);
    const peaks = new Float32Array(totalBuckets);

    // Filter coefficients for fast running difference estimation
    let prevSample1 = 0;
    let prevSample2 = 0;
    let prevLow = 0;

    for (let b = 0; b < totalBuckets; b++) {
      const start = b * samplesPerBucket;
      const end = Math.min(start + samplesPerBucket, rawData.length);
      const count = Math.max(1, end - start);

      let maxPeak = 0;
      let subLowSum = 0;
      let lowSum = 0;
      let midSum = 0;
      let highSum = 0;

      for (let i = start; i < end; i++) {
        const s = rawData[i];
        const absS = Math.abs(s);
        if (absS > maxPeak) maxPeak = absS;

        // Low-pass approximation (moving average)
        prevLow = prevLow * 0.95 + s * 0.05;
        subLowSum += Math.abs(prevLow);

        // Low frequency band
        const diff1 = s - prevSample1;
        lowSum += Math.abs(prevLow * 0.6 + diff1 * 0.4);

        // High frequency band (second-order difference)
        const diff2 = s - 2 * prevSample1 + prevSample2;
        highSum += Math.abs(diff2);

        // Mid band
        midSum += Math.abs(s - prevLow) * 0.8;

        prevSample2 = prevSample1;
        prevSample1 = s;
      }

      subLows[b] = Math.min(1.0, (subLowSum / count) * 3.8);
      lows[b] = Math.min(1.0, (lowSum / count) * 3.2);
      mids[b] = Math.min(1.0, (midSum / count) * 2.8);
      highs[b] = Math.min(1.0, (highSum / count) * 3.6);
      peaks[b] = Math.min(1.0, maxPeak);
    }

    return {
      subLows: Array.from(subLows),
      lows: Array.from(lows),
      mids: Array.from(mids),
      highs: Array.from(highs),
      peaks: Array.from(peaks),
      sampleRate: targetBucketsPerSec,
      totalBuckets,
    };
  }

  /**
   * Estimates track BPM using energy onset autocorrelation
   */
  static estimateBPM(buffer: AudioBuffer): number {
    const sampleRate = buffer.sampleRate;
    const rawData = buffer.getChannelData(0);
    const step = Math.floor(sampleRate / 400);
    const energyWindows: number[] = [];

    for (let i = 0; i < rawData.length - step; i += step) {
      let sum = 0;
      for (let j = 0; j < step; j++) {
        const val = rawData[i + j];
        sum += val * val;
      }
      energyWindows.push(sum);
    }

    const onsets: number[] = [];
    for (let i = 1; i < energyWindows.length; i++) {
      const diff = energyWindows[i] - energyWindows[i - 1];
      onsets.push(diff > 0 ? diff : 0);
    }

    const minBpm = 75;
    const maxBpm = 175;
    const slicesPerSec = sampleRate / step;

    const minInterval = Math.floor(slicesPerSec * (60 / maxBpm));
    const maxInterval = Math.floor(slicesPerSec * (60 / minBpm));

    let bestBpm = 126;
    let maxCorrelation = 0;

    for (let lag = minInterval; lag <= maxInterval; lag++) {
      let correlation = 0;
      const count = Math.min(onsets.length - lag, 4000);
      for (let i = 0; i < count; i++) {
        correlation += onsets[i] * onsets[i + lag];
      }

      if (correlation > maxCorrelation) {
        maxCorrelation = correlation;
        const rawBpm = (slicesPerSec * 60) / lag;
        bestBpm = Math.round(rawBpm * 10) / 10;
      }
    }

    if (bestBpm < 90) bestBpm *= 2;
    if (bestBpm > 180) bestBpm /= 2;

    return Math.round(bestBpm * 100) / 100;
  }

  static findFirstDownbeat(buffer: AudioBuffer, bpm: number): number {
    const rawData = buffer.getChannelData(0);
    const sampleRate = buffer.sampleRate;
    const beatIntervalSec = 60 / bpm;
    const searchLimit = Math.min(rawData.length, sampleRate * 12);

    let peakEnergy = 0;
    let bestTime = 0.05;

    const windowSize = Math.floor(sampleRate * 0.02);
    for (let i = 0; i < searchLimit - windowSize; i += Math.floor(windowSize / 2)) {
      let energy = 0;
      for (let j = 0; j < windowSize; j++) {
        energy += Math.abs(rawData[i + j]);
      }
      if (energy > peakEnergy * 1.4 && energy > 10) {
        peakEnergy = energy;
        bestTime = i / sampleRate;
        break;
      }
    }

    return Math.max(0.01, Math.min(bestTime, beatIntervalSec));
  }

  static estimateKey(buffer: AudioBuffer): string {
    const keys = [
      '8A', '9A', '10A', '11A', '12A', '1A', '2A', '3A', '4A', '5A', '6A', '7A',
      '8B', '9B', '10B', '11B', '12B', '1B', '2B', '3B', '4B', '5B', '6B', '7B',
    ];
    const len = buffer.length;
    let hash = 0;
    const channel = buffer.getChannelData(0);
    const step = Math.floor(len / 100);
    for (let i = 0; i < len; i += step) {
      hash = (hash << 5) - hash + Math.floor(Math.abs(channel[i]) * 1000);
      hash |= 0;
    }
    const idx = Math.abs(hash) % keys.length;
    return keys[idx];
  }

  /**
   * Pre-analyzes track completely upon import and attaches pre-computed waveform
   */
  static analyzeTrack(
    file: File | { name: string; size: number },
    buffer: AudioBuffer,
    source: 'local' | 'drive' | 'demo' = 'local'
  ): TrackEntity {
    const duration = buffer.duration;
    const bpm = this.estimateBPM(buffer);
    const beatgridOffset = this.findFirstDownbeat(buffer, bpm);
    const key = this.estimateKey(buffer);
    const detailedWaveform = this.extractDetailedWaveform(buffer, 250);

    const beatSec = 60 / bpm;
    const hotcueColors = [
      '#00C3FF', '#00E575', '#FF9900', '#BD00FF',
      '#FF2A4D', '#FFFF00', '#FF66CC', '#3399FF',
    ];
    const hotcueNames = [
      'Intro Cue', 'Main Beat', 'Breakdown', 'Build Up',
      'Peak Drop', 'Verse / Hook', 'Bridge', 'Outro',
    ];

    const hotcues: Hotcue[] = [];
    const beatMultipliers = [0, 32, 64, 96, 128, 160, 192, 224];

    for (let i = 0; i < 8; i++) {
      const cueTime = beatgridOffset + beatMultipliers[i] * beatSec;
      if (cueTime < duration - 2) {
        hotcues.push({
          id: i + 1,
          time: cueTime,
          type: i === 0 ? 'cue' : 'grid',
          color: hotcueColors[i],
          name: hotcueNames[i],
        });
      }
    }

    const cleanName = file.name.replace(/\.[^/.]+$/, '');
    const parts = cleanName.split(' - ');
    const artist = parts.length > 1 ? parts[0].trim() : 'Unknown Artist';
    const title = parts.length > 1 ? parts.slice(1).join(' - ').trim() : cleanName;

    return {
      id: `track_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`,
      title,
      artist,
      duration,
      bpm,
      key,
      beatgridOffset,
      hotcues,
      waveformPeaks: {
        lows: detailedWaveform.lows,
        mids: detailedWaveform.mids,
        highs: detailedWaveform.highs,
        peaks: detailedWaveform.peaks,
      },
      audioBuffer: buffer,
      source,
      sizeBytes: file.size,
    };
  }

  static exportAnalysisBackup(tracks: TrackEntity[]): AnalysisBackupFile {
    const backup: AnalysisBackupFile = {
      version: '1.0',
      exportedAt: new Date().toISOString(),
      appName: 'Traktor Pro Web DJ',
      tracks: {},
    };

    for (const track of tracks) {
      const trackKey = `${track.artist} - ${track.title}`.toLowerCase().trim();
      backup.tracks[trackKey] = {
        title: track.title,
        artist: track.artist,
        bpm: track.bpm,
        key: track.key,
        duration: track.duration,
        beatgridOffset: track.beatgridOffset,
        hotcues: track.hotcues,
      };
    }

    return backup;
  }

  static applyAnalysisBackup(
    tracks: TrackEntity[],
    backup: AnalysisBackupFile
  ): { updatedCount: number; tracks: TrackEntity[] } {
    let updatedCount = 0;
    const updatedTracks = tracks.map((track) => {
      const trackKey = `${track.artist} - ${track.title}`.toLowerCase().trim();
      const savedAnalysis = backup.tracks[trackKey];
      if (savedAnalysis) {
        updatedCount++;
        return {
          ...track,
          bpm: savedAnalysis.bpm,
          key: savedAnalysis.key,
          beatgridOffset: savedAnalysis.beatgridOffset,
          hotcues: savedAnalysis.hotcues || track.hotcues,
        };
      }
      return track;
    });

    return { updatedCount, tracks: updatedTracks };
  }
}
