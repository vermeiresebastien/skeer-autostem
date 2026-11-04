/**
 * Backend bridge for Skeer autoSTEM.
 *
 * Replaces the in-browser BeatAnalyzer with calls to our back-end CLI.
 * The front-end calls these APIs; the server wraps the `autostem` CLI.
 *
 * Usage:
 *   const backend = new BackendBridge('http://localhost:3000');
 *   const analysis = await backend.analyzeTrack('/path/to/track.wav');
 *   const result = await backend.runQueue('/path/to/output');
 */

export interface BackendAnalysis {
  sourcePath: string;
  templatePath: string;
  analysis: {
    bpm: number;
    key: string;
    duration: number;
    beatgridOffset: number;
    waveformPeaks: {
      lows: number[];
      mids: number[];
      highs: number[];
      peaks: number[];
    };
  };
}

export interface BackendRunResult {
  exitCode: number;
  stdout: string;
  stderr: string;
}

export interface BackendQueueAddResult {
  added: number;
  output: string;
  stdout: string;
}

export class BackendBridge {
  private baseUrl: string;

  constructor(baseUrl: string = 'http://localhost:3000') {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  async analyzeTrack(sourcePath: string): Promise<BackendAnalysis> {
    const res = await fetch(`${this.baseUrl}/api/analyze`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sourcePath }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Analysis failed');
    }

    return res.json();
  }

  async addFilesToQueue(paths: string[], dest: string, stems = 'all', mode = 'speed'): Promise<BackendQueueAddResult> {
    const res = await fetch(`${this.baseUrl}/api/queue/add`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ paths, dest, stems, mode }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Queue add failed');
    }

    return res.json();
  }

  async runQueue(dest: string, simulate = false): Promise<BackendRunResult> {
    const res = await fetch(`${this.baseUrl}/api/run`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ dest, simulate }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Run failed');
    }

    return res.json();
  }

  async verifyFolder(folder: string): Promise<BackendRunResult> {
    const res = await fetch(`${this.baseUrl}/api/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ folder }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Verify failed');
    }

    return res.json();
  }

  async bench(shortPath: string, longerPath: string, dest: string, simulate = false): Promise<BackendRunResult> {
    const res = await fetch(`${this.baseUrl}/api/bench`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ shortPath, longerPath, dest, simulate }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Bench failed');
    }

    return res.json();
  }

  async geminiChat(messages: any[], systemInstruction?: string, model?: string): Promise<string> {
    const res = await fetch(`${this.baseUrl}/api/gemini/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages, systemInstruction, model }),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Gemini API call failed');
    }

    const data = await res.json();
    return data.text || '';
  }
}
