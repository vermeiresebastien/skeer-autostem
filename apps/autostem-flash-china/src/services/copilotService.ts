import { ChatMessage, DeckState, TrackEntity } from '../types';

const STORAGE_KEY = 'traktor_copilot_chat_history_v1';

const SYSTEM_INSTRUCTION = `You are the Traktor DJ Copilot & Master DJ Mentor, an elite club DJ, musicologist, and audio engineer specializing in Native Instruments Traktor Pro, Pioneer DJ hardware, and professional mixing.

Your mission is to help DJs of all levels with:
1. PRACTICAL APP TOOLS:
   - Decks & Transport: Play, Cue, CUP, Sync, Master, Flux mode, and Pitch nudging.
   - Deep Zoom Waveforms: Explaining the multi-band spectrum (Crimson = sub/kicks, Green = mids/vocals, Cyan = highs/cymbals) and how to zoom down to 0.5 beats for extreme grid precision.
   - Hotcues & Loops: 8 performance pads, beatjump (◄4 / 4►), active loops, loop halving/doubling.
   - 4 FX Units: Delay (time, feedback, high-damp), Reverb (room size, lowpass damp), Flanger (LFO speed, depth), Beatmasher (stutter, resonance).
   - Pioneer DDJ-T1 Hardware: Channel mapping (Ch 1 = Deck A, Ch 2 = Deck B, Ch 3 = Deck C, Ch 4 = Deck D), jog wheel touch/scratch, faders, 3-band isolator EQs, filter knobs, crossfader curves.
   - Library & Cloud: Local audio upload with downloadable .traktor.json analysis backup, Google Drive import, and Google Tasks gig prep checklist.

2. DJ FUNDAMENTALS & TECHNIQUES:
   - Harmonic Mixing: Master-level guidance on the Camelot Wheel (e.g., 8A Minor to 8B Major, 8A to 9A energy rise, diagonal transitions, and energy-boost shifts).
   - Phrasing & Timing: 32-beat phrases, 16-bar intro/outro transitions, breakdown drops, beatgrid downbeat alignment.
   - EQ Blending & Gain Staging: Isolator cuts (never overlap two low-end kicks/808s), high-pass / low-pass filter sweeps, smooth vs. scratch crossfader cut-in curves.

3. PLAYLIST ANALYSIS & TRACKLIST SUGGESTIONS:
   - When provided with the user's active library tracks or loaded decks, perform a deep playlist analysis:
     a) Analyze BPM range and tempo progression.
     b) Evaluate Camelot key compatibility across the tracks.
     c) Recommend the optimal track order for smooth energy progression.
     d) Provide specific transition recipes between tracks (e.g., "At 1:16 during Track 1's breakdown, engage a 4-beat loop, pitch up +1.2% to match Track 2's 128 BPM, swap Low EQs at the drop").

4. TRACK CURATION & SELECTION ADVICE:
   - Guide DJs on how to find tracks that complement each other, explain the psychoacoustic reasons why specific rhythms and melodies blend well, and suggest tracklist structures for different gigs (clubs, festival peak-time, sunset lounge, radio sets).

FORMATTING GUIDELINES:
- Use clean, structured Markdown with bold highlights, bulleted steps, and concise Camelot key pills (e.g. \`[8A - A Minor]\`).
- Be knowledgeable, encouraging, practical, and direct.`;

export class CopilotService {
  private static instance: CopilotService;
  private history: ChatMessage[] = [];

  private constructor() {
    this.loadHistory();
    if (this.history.length === 0) {
      this.history.push({
        id: `msg_welcome_${Date.now()}`,
        role: 'assistant',
        content: `👋 **Welcome to the Traktor DJ Copilot!**\n\nI'm your AI DJ mentor and playlist architect. I have live access to your active decks and loaded track library.\n\nHere is how I can help:\n- **Analyze your current playlist** for harmonic Camelot keys, tempo flow, and transition points.\n- **Guide your live transitions** between Deck A and Deck B.\n- **Master Traktor Pro tools** (Deep Zoom waveforms, FX units, hotcues, Pioneer DDJ-T1 mapping).\n- **Curate tracklists & recommend tracks** that fit your set's energy arc.\n\nClick any quick prompt below or ask me anything!`,
        timestamp: Date.now(),
      });
      this.saveHistory();
    }
  }

  public static getInstance(): CopilotService {
    if (!CopilotService.instance) {
      CopilotService.instance = new CopilotService();
    }
    return CopilotService.instance;
  }

  public getHistory(): ChatMessage[] {
    return [...this.history];
  }

  public clearHistory(): void {
    this.history = [
      {
        id: `msg_welcome_${Date.now()}`,
        role: 'assistant',
        content: `🧹 **Chat history cleared.** Ready for your next DJ question, playlist analysis, or transition advice!`,
        timestamp: Date.now(),
      },
    ];
    this.saveHistory();
  }

  public async sendMessage(
    userText: string,
    context?: {
      tracks?: TrackEntity[];
      deckA?: DeckState;
      deckB?: DeckState;
      masterBpm?: number;
    },
    modelName: string = 'gemini-3.8-flash'
  ): Promise<string> {
    const userMsgId = `msg_user_${Date.now()}`;
    this.history.push({
      id: userMsgId,
      role: 'user',
      content: userText,
      timestamp: Date.now(),
    });
    this.saveHistory();

    // Prepare system instruction augmented with live DJ booth context
    let augmentedSystemInstruction = SYSTEM_INSTRUCTION;
    if (context) {
      const boothSummary = this.formatBoothContext(context);
      augmentedSystemInstruction += `\n\n--- CURRENT LIVE DJ BOOTH CONTEXT ---\n${boothSummary}\n-------------------------------------`;
    }

    // Format message history for Gemini API
    const messagesPayload = this.history
      .filter((m) => m.role === 'user' || m.role === 'assistant')
      .map((m) => ({
        role: m.role === 'user' ? 'user' : 'model',
        content: m.content,
      }));

    try {
      const response = await fetch('/api/gemini/chat', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          messages: messagesPayload,
          systemInstruction: augmentedSystemInstruction,
          model: modelName,
        }),
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || `Server responded with status ${response.status}`);
      }

      const data = await response.json();
      const assistantText = data.text || 'I analyzed your request, but received an empty response. Please try again.';

      this.history.push({
        id: `msg_asst_${Date.now()}`,
        role: 'assistant',
        content: assistantText,
        timestamp: Date.now(),
        modelUsed: modelName,
      });
      this.saveHistory();

      return assistantText;
    } catch (err: any) {
      console.error('Copilot send message error:', err);
      const fallbackError = `⚠️ **DJ Copilot Connection Notice:**\n${err.message || 'Could not communicate with the Gemini service.'}\n\nPlease check your internet connection or verify the Gemini API key.`;
      this.history.push({
        id: `msg_err_${Date.now()}`,
        role: 'assistant',
        content: fallbackError,
        timestamp: Date.now(),
      });
      this.saveHistory();
      return fallbackError;
    }
  }

  private formatBoothContext(context: {
    tracks?: TrackEntity[];
    deckA?: DeckState;
    deckB?: DeckState;
    masterBpm?: number;
  }): string {
    const lines: string[] = [];

    if (context.masterBpm) {
      lines.push(`Master Clock BPM: ${context.masterBpm.toFixed(2)}`);
    }

    if (context.deckA?.track) {
      const t = context.deckA.track;
      lines.push(
        `[DECK A]: "${t.title}" by ${t.artist} | Key: ${t.key} | BPM: ${context.deckA.effectiveBpm.toFixed(2)} (Base: ${t.bpm}) | Pitch: ${context.deckA.pitchPercent >= 0 ? '+' : ''}${context.deckA.pitchPercent.toFixed(2)}% | Playing: ${context.deckA.isPlaying}`
      );
    } else {
      lines.push(`[DECK A]: Empty`);
    }

    if (context.deckB?.track) {
      const t = context.deckB.track;
      lines.push(
        `[DECK B]: "${t.title}" by ${t.artist} | Key: ${t.key} | BPM: ${context.deckB.effectiveBpm.toFixed(2)} (Base: ${t.bpm}) | Pitch: ${context.deckB.pitchPercent >= 0 ? '+' : ''}${context.deckB.pitchPercent.toFixed(2)}% | Playing: ${context.deckB.isPlaying}`
      );
    } else {
      lines.push(`[DECK B]: Empty`);
    }

    if (context.tracks && context.tracks.length > 0) {
      lines.push(`\nUSER'S LOADED LIBRARY (${context.tracks.length} tracks):`);
      context.tracks.forEach((t, i) => {
        lines.push(
          `${i + 1}. "${t.title}" - ${t.artist} | Key: ${t.key} | ${t.bpm.toFixed(1)} BPM | ${Math.floor(t.duration / 60)}m${Math.floor(t.duration % 60)}s | ${t.hotcues.length} Hotcues | Source: ${t.source}`
        );
      });
    }

    return lines.join('\n');
  }

  private loadHistory(): void {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        this.history = JSON.parse(raw);
      }
    } catch {
      this.history = [];
    }
  }

  private saveHistory(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.history.slice(-30))); // Keep last 30 turns
    } catch {
      // Ignored
    }
  }
}
