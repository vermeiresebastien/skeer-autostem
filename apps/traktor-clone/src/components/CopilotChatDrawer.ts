import { AudioEngine } from '../services/audioEngine';
import { CopilotService } from '../services/copilotService';
import { TrackEntity } from '../types';

export class CopilotChatDrawer {
  private container: HTMLElement;
  private engine: AudioEngine;
  private copilot: CopilotService;
  private isOpen = false;
  private isThinking = false;
  private getTracks: () => TrackEntity[];
  private currentModel = 'gemini-3.8-flash';

  constructor(
    container: HTMLElement,
    getTracks: () => TrackEntity[],
    initialOpen = false
  ) {
    this.container = container;
    this.getTracks = getTracks;
    this.engine = AudioEngine.getInstance();
    this.copilot = CopilotService.getInstance();
    this.isOpen = initialOpen;
    this.render();
  }

  public toggle(open?: boolean): void {
    this.isOpen = open !== undefined ? open : !this.isOpen;
    const drawer = this.container.querySelector('#copilot-drawer');
    const topBtn = document.querySelector('#btn-toggle-copilot');

    drawer?.classList.toggle('open', this.isOpen);
    topBtn?.classList.toggle('active', this.isOpen);

    if (this.isOpen) {
      this.scrollToBottom();
      const input = this.container.querySelector('#copilot-chat-input') as HTMLInputElement;
      setTimeout(() => input?.focus(), 150);
    }
  }

  public getIsOpen(): boolean {
    return this.isOpen;
  }

  public render(): void {
    const history = this.copilot.getHistory();

    this.container.innerHTML = `
      <div id="copilot-drawer" class="copilot-drawer ${this.isOpen ? 'open' : ''}">
        <!-- Drawer Header -->
        <div class="copilot-header">
          <div class="copilot-title-group">
            <div class="copilot-icon-badge">
              <span class="material-symbols-outlined copilot-ico">smart_toy</span>
              <span class="copilot-pulse-dot"></span>
            </div>
            <div class="copilot-title-text">
              <div class="title-row">
                <span class="main-title font-bold">DJ COPILOT</span>
                <span class="model-badge font-mono">${this.currentModel}</span>
              </div>
              <span class="sub-title">AI Playlist Architect & Traktor Mentor</span>
            </div>
          </div>

          <div class="copilot-header-actions">
            <!-- Analyze Current Set Button -->
            <button id="btn-header-analyze-set" class="btn-copilot-pill" title="Analyze currently loaded decks & library tracks">
              <span class="material-symbols-outlined">analytics</span>
              <span>Analyze Set</span>
            </button>

            <!-- Clear History -->
            <button id="btn-copilot-clear" class="btn-copilot-icon" title="Clear chat history">
              <span class="material-symbols-outlined">delete_sweep</span>
            </button>

            <!-- Close Drawer -->
            <button id="btn-copilot-close" class="btn-copilot-icon" title="Close Copilot (Esc)">
              <span class="material-symbols-outlined">close</span>
            </button>
          </div>
        </div>

        <!-- Quick Action Prompt Chips -->
        <div class="copilot-chips-bar">
          <button class="copilot-chip" data-prompt="Analyze my current library and recommend an optimal tracklist order with Camelot harmonic keys and BPM transitions.">
            📊 Analyze My Playlist
          </button>
          <button class="copilot-chip" data-prompt="How should I transition between Deck A and Deck B right now? Give me an exact EQ, loop, and timing recipe.">
            🎚️ Transition Deck A ➔ B
          </button>
          <button class="copilot-chip" data-prompt="Explain the Camelot Wheel for harmonic mixing and give me 3 creative key transition techniques.">
            🔑 Camelot Harmonic Guide
          </button>
          <button class="copilot-chip" data-prompt="How do I use my Pioneer DDJ-T1 controller with this app? What are the default channel and jog mappings?">
            🎛️ Pioneer DDJ-T1 Mapping
          </button>
          <button class="copilot-chip" data-prompt="How does the Deep Zoom waveform work? How can I zoom into 0.5 beats and what do the 3-band colors mean?">
            🔍 Deep Zoom Waveforms
          </button>
          <button class="copilot-chip" data-prompt="Suggest 5 tracks that would fit well with my current set for peak-time energy. Explain why they work.">
            🎵 Suggest New Tracks
          </button>
        </div>

        <!-- Scrollable Chat Message Thread -->
        <div class="copilot-messages-thread" id="copilot-messages-thread">
          ${history
            .map((msg) => this.renderMessage(msg.role, msg.content, msg.timestamp))
            .join('')}

          ${
            this.isThinking
              ? `
            <div class="copilot-msg msg-assistant thinking">
              <div class="msg-avatar">
                <span class="material-symbols-outlined">smart_toy</span>
              </div>
              <div class="msg-body">
                <div class="typing-indicator">
                  <span></span><span></span><span></span>
                </div>
                <span class="thinking-text font-mono">Analyzing DJ booth harmony & crafting advice...</span>
              </div>
            </div>
          `
              : ''
          }
        </div>

        <!-- Chat Input Footer -->
        <div class="copilot-input-footer">
          <div class="copilot-input-box">
            <textarea
              id="copilot-chat-input"
              rows="1"
              placeholder="Ask about mixing, track harmony, Pioneer DDJ-T1, or set analysis..."
            ></textarea>
            <button id="btn-copilot-send" class="btn-copilot-send" title="Send message (Enter)">
              <span class="material-symbols-outlined">send</span>
            </button>
          </div>
          <div class="copilot-footer-meta">
            <span class="hint-txt">Press Enter to send, Shift+Enter for new line</span>
            <div class="model-select-group">
              <span class="model-lbl">Model:</span>
              <select id="copilot-model-select" class="copilot-model-dropdown">
                <option value="gemini-3.8-flash" ${this.currentModel === 'gemini-3.8-flash' ? 'selected' : ''}>gemini-3.8-flash (Recommended)</option>
                <option value="gemini-3.1-flash-lite" ${this.currentModel === 'gemini-3.1-flash-lite' ? 'selected' : ''}>gemini-3.1-flash-lite (Fast)</option>
                <option value="gemini-4-argon" ${this.currentModel === 'gemini-4-argon' ? 'selected' : ''}>gemini-4-argon (Complex)</option>
              </select>
            </div>
          </div>
        </div>
      </div>
    `;

    this.attachEvents();
  }

  private renderMessage(role: string, content: string, timestamp: number): string {
    const isUser = role === 'user';
    const timeStr = new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

    // Format simple markdown: bold, code, bullet lists, newlines
    const formattedText = this.formatMarkdown(content);

    return `
      <div class="copilot-msg ${isUser ? 'msg-user' : 'msg-assistant'}">
        <div class="msg-avatar">
          <span class="material-symbols-outlined">${isUser ? 'person' : 'smart_toy'}</span>
        </div>
        <div class="msg-bubble-wrap">
          <div class="msg-meta">
            <span class="sender-name font-bold">${isUser ? 'You' : 'DJ Copilot'}</span>
            <span class="msg-time font-mono">${timeStr}</span>
          </div>
          <div class="msg-content">${formattedText}</div>
        </div>
      </div>
    `;
  }

  private formatMarkdown(text: string): string {
    // Escape standard HTML
    let out = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Code blocks ```...```
    out = out.replace(/```([a-z]*)\n([\s\S]*?)```/g, (_match, _lang, code) => {
      return `<pre class="copilot-code-block font-mono"><code>${code.trim()}</code></pre>`;
    });

    // Inline code `...`
    out = out.replace(/`([^`]+)`/g, '<code class="copilot-inline-code font-mono">$1</code>');

    // Bold **...**
    out = out.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

    // Italic *...*
    out = out.replace(/\*([^*]+)\*/g, '<em>$1</em>');

    // Bullet points
    out = out.replace(/^\s*[-•]\s+(.+)$/gm, '<li class="copilot-li">$1</li>');
    out = out.replace(/(<li class="copilot-li">.*<\/li>)/gs, '<ul class="copilot-ul">$1</ul>');

    // Numbered lists
    out = out.replace(/^\s*(\d+)\.\s+(.+)$/gm, '<li class="copilot-oli" value="$1">$2</li>');
    out = out.replace(/(<li class="copilot-oli">.*<\/li>)/gs, '<ol class="copilot-ol">$1</ol>');

    // Paragraph breaks
    out = out.replace(/\n\n+/g, '<br><br>');
    out = out.replace(/\n/g, '<br>');

    return out;
  }

  private attachEvents(): void {
    // Close button
    this.container.querySelector('#btn-copilot-close')?.addEventListener('click', () => {
      this.toggle(false);
    });

    // Clear history button
    this.container.querySelector('#btn-copilot-clear')?.addEventListener('click', () => {
      if (confirm('Clear DJ Copilot chat history?')) {
        this.copilot.clearHistory();
        this.render();
      }
    });

    // Analyze Set Header Button
    this.container.querySelector('#btn-header-analyze-set')?.addEventListener('click', () => {
      this.handleSend(
        'Please analyze my current library and active decks: evaluate our BPM range, Camelot harmonic key compatibility, and give me a recommended tracklist order with transition tips.'
      );
    });

    // Model Selector
    const modelSelect = this.container.querySelector('#copilot-model-select') as HTMLSelectElement;
    modelSelect?.addEventListener('change', () => {
      this.currentModel = modelSelect.value;
      const badge = this.container.querySelector('.model-badge');
      if (badge) badge.textContent = this.currentModel;
    });

    // Prompt Chips
    this.container.querySelectorAll('.copilot-chip').forEach((chip) => {
      chip.addEventListener('click', (e) => {
        const prompt = (e.currentTarget as HTMLElement).dataset.prompt;
        if (prompt) {
          this.handleSend(prompt);
        }
      });
    });

    // Input Box & Send
    const input = this.container.querySelector('#copilot-chat-input') as HTMLTextAreaElement;
    const sendBtn = this.container.querySelector('#btn-copilot-send');

    const triggerSend = () => {
      if (!input || !input.value.trim() || this.isThinking) return;
      const text = input.value.trim();
      input.value = '';
      this.handleSend(text);
    };

    sendBtn?.addEventListener('click', triggerSend);

    input?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        triggerSend();
      }
    });

    // Auto-expand textarea height
    input?.addEventListener('input', () => {
      input.style.height = 'auto';
      input.style.height = `${Math.min(120, input.scrollHeight)}px`;
    });
  }

  private async handleSend(text: string): Promise<void> {
    if (this.isThinking) return;

    this.isThinking = true;
    this.render();
    this.scrollToBottom();

    // Collect live booth context
    const tracks = this.getTracks();
    const deckA = this.engine.getDeckState('A');
    const deckB = this.engine.getDeckState('B');
    const masterBpm = this.engine.getMixerState().masterBpm;

    try {
      await this.copilot.sendMessage(
        text,
        {
          tracks,
          deckA,
          deckB,
          masterBpm,
        },
        this.currentModel
      );
    } finally {
      this.isThinking = false;
      this.render();
      this.scrollToBottom();
    }
  }

  private scrollToBottom(): void {
    const thread = this.container.querySelector('#copilot-messages-thread');
    if (thread) {
      thread.scrollTop = thread.scrollHeight;
    }
  }
}
