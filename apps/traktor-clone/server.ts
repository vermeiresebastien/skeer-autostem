import express from 'express';
import { fileURLToPath } from 'url';
import path from 'path';
import { execFile } from 'child_process';
import { GoogleGenAI } from '@google/genai';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const port = process.env.PORT || 3000;

app.use(express.json({ limit: '10mb' }));

// --- autostem CLI wrapper ---

const CLI_PATH = path.resolve(__dirname, '..', '..', 'artifacts', 'autostem-cli', 'autostem.exe');

function runAutostem(args: string[]): Promise<{ stdout: string; stderr: string; exitCode: number }> {
  return new Promise((resolve, reject) => {
    execFile(CLI_PATH, args, { maxBuffer: 10 * 1024 * 1024 }, (error, stdout, stderr) => {
      resolve({
        stdout: stdout || '',
        stderr: stderr || '',
        exitCode: error ? 1 : 0,
      });
    });
  });
}

// --- API: analyze a track (returns bpm, key, duration, waveform peaks) ---

app.post('/api/analyze', async (req, res) => {
  try {
    const { sourcePath } = req.body;
    if (!sourcePath) return res.status(400).json({ error: 'sourcePath required' });

    const workDir = path.join(__dirname, '..', 'work', 'analyze');
    const result = await runAutostem([
      'prepare',
      sourcePath,
      '--work', workDir,
    ]);

    if (result.exitCode !== 0) {
      return res.status(400).json({ error: result.stderr || result.stdout });
    }

    // Parse the prepare output to extract analysis
    // Output format: source: ...\ntemplate: ...\nproject: ...\nstems to: ...
    const lines = result.stdout.split('\n');
    const sourceLine = lines.find(l => l.startsWith('source:'));
    const templateLine = lines.find(l => l.startsWith('template:'));

    if (!sourceLine || !templateLine) {
      return res.status(500).json({ error: 'Unexpected prepare output' });
    }

    const sourcePathParsed = sourceLine.split(':')[1].trim();
    const templatePath = templateLine.split(':')[1].trim();

    // Read the generated .als file to extract clip info
    // For now, return basic info; full analysis would require reading the .als XML
    res.json({
      sourcePath: sourcePathParsed,
      templatePath,
      analysis: {
        // TODO: parse the .als to extract bpm, key, duration, waveform peaks
        // For now, return placeholder values that the front-end can override
        bpm: 120,
        key: '8A',
        duration: 0,
        beatgridOffset: 0,
        waveformPeaks: { lows: [], mids: [], highs: [], peaks: [] },
      },
    });
  } catch (err: any) {
    console.error('Analyze error:', err);
    res.status(500).json({ error: err?.message || 'Analysis failed' });
  }
});

// --- API: queue files (adds to saved queue without running) ---

app.post('/api/queue/add', async (req, res) => {
  try {
    const { paths, dest, stems, mode } = req.body;
    if (!paths || !Array.isArray(paths) || !dest) {
      return res.status(400).json({ error: 'paths (array) and dest required' });
    }

    const args = ['enqueue', ...paths, '--dest', dest];
    if (stems) args.push('--stems', stems);
    if (mode) args.push('--mode', mode);

    const result = await runAutostem(args);

    if (result.exitCode !== 0) {
      return res.status(400).json({ error: result.stderr || result.stdout });
    }

    res.json({
      added: result.stdout.includes('queued') ? parseInt(result.stdout.match(/queued (\d+)/)?.[1] || '0') : 0,
      output: dest,
      stdout: result.stdout,
    });
  } catch (err: any) {
    console.error('Queue add error:', err);
    res.status(500).json({ error: err?.message || 'Queue add failed' });
  }
});

// --- API: run queue (starts batch processing) ---

app.post('/api/run', async (req, res) => {
  try {
    const { paths, dest, stems, mode, simulate } = req.body;

    const args = ['run'];
    if (paths) args.push(...paths);
    args.push('--dest', dest);
    if (stems) args.push('--stems', stems);
    if (mode) args.push('--mode', mode);
    if (simulate) args.push('--simulate');

    const result = await runAutostem(args);

    res.json({
      exitCode: result.exitCode,
      stdout: result.stdout,
      stderr: result.stderr,
    });
  } catch (err: any) {
    console.error('Run error:', err);
    res.status(500).json({ error: err?.message || 'Run failed' });
  }
});

// --- API: verify a result folder ---

app.post('/api/verify', async (req, res) => {
  try {
    const { folder } = req.body;
    if (!folder) return res.status(400).json({ error: 'folder required' });

    const result = await runAutostem(['verify', folder]);

    res.json({
      exitCode: result.exitCode,
      stdout: result.stdout,
      stderr: result.stderr,
    });
  } catch (err: any) {
    console.error('Verify error:', err);
    res.status(500).json({ error: err?.message || 'Verify failed' });
  }
});

// --- API: bench (benchmark comparison) ---

app.post('/api/bench', async (req, res) => {
  try {
    const { shortPath, longerPath, dest, simulate } = req.body;
    if (!shortPath || !longerPath || !dest) {
      return res.status(400).json({ error: 'shortPath, longerPath, and dest required' });
    }

    const args = ['bench', shortPath, longerPath, '--dest', dest];
    if (simulate) args.push('--simulate');

    const result = await runAutostem(args);

    res.json({
      exitCode: result.exitCode,
      stdout: result.stdout,
      stderr: result.stderr,
    });
  } catch (err: any) {
    console.error('Bench error:', err);
    res.status(500).json({ error: err?.message || 'Bench failed' });
  }
});

// --- Gemini chat (existing) ---

app.post('/api/gemini/chat', async (req, res) => {
  try {
    const { messages, systemInstruction, model } = req.body || {};
    const apiKey = process.env.GEMINI_API_KEY;

    if (!apiKey) {
      return res.status(500).json({ error: 'GEMINI_API_KEY is not configured in process.env.' });
    }

    const ai = new GoogleGenAI({
      apiKey,
      httpOptions: {
        headers: {
          'User-Agent': 'aistudio-build',
        },
      },
    });

    const contents = (messages || []).map((m: any) => ({
      role: m.role === 'user' ? 'user' : 'model',
      parts: [{ text: m.content || m.text || '' }],
    }));

    const selectedModel = model || 'gemini-3.8-flash';

    const response = await ai.models.generateContent({
      model: selectedModel,
      contents,
      config: {
        systemInstruction: systemInstruction || undefined,
      },
    });

    res.json({ text: response.text || '' });
  } catch (err: any) {
    console.error('Gemini API Server Error:', err);
    res.status(500).json({ error: err?.message || 'Gemini API call failed.' });
  }
});

// Serve built static app in production
app.use(express.static(path.join(__dirname, 'dist')));
app.get('*', (_req, res) => {
  res.sendFile(path.join(__dirname, 'dist', 'index.html'));
});

app.listen(port, '0.0.0.0', () => {
  console.log(`Skeer autoSTEM server listening on http://0.0.0.0:${port}`);
  console.log(`autostem CLI at: ${CLI_PATH}`);
});
