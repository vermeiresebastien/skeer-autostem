import { AudioEngine } from '../services/audioEngine';
import { BeatAnalyzer } from '../services/beatAnalysis';
import {
  createGoogleTask,
  deleteGoogleTask,
  deleteSetlistNote,
  downloadDriveAudioFile,
  getCurrentUser,
  getSetlistNotes,
  googleSignIn,
  listDriveAudioFiles,
  listGoogleTasks,
  logout,
  saveSetlistNote,
  SetlistNote,
  updateGoogleTaskStatus,
} from '../services/googleWorkspace';
import {
  MidiControllerService,
  MidiDeviceStatus,
} from '../services/midiController';
import { firebaseService, FirebaseStatus } from '../services/firebase';
import { CopilotChatDrawer } from './CopilotChatDrawer';
import {
  DeckId,
  DeckState,
  FXType,
  GoogleTaskItem,
  TrackEntity,
} from '../types';

const ZOOM_STEPS = [0.5, 1, 2, 4, 8, 16, 32];

export class Console2D {
  private container: HTMLElement;
  private engine: AudioEngine;
  private midiService: MidiControllerService;
  private midiStatus: MidiDeviceStatus;
  private firebaseStatus: FirebaseStatus = {
    initialized: false,
    user: null,
    cloudTrackCount: 0,
  };
  private tracks: TrackEntity[] = [];
  private currentFilter = '';
  private activeCrateTab: 'all' | 'demo' | 'local' | 'drive' | 'firebase' = 'all';
  private selectedTrackIndex = 0;
  private copilotDrawer: CopilotChatDrawer | null = null;

  // Waveform canvas animation references
  private animFrameId: number | null = null;
  private zoomedCanvases: Map<DeckId, HTMLCanvasElement> = new Map();
  private stripeCanvases: Map<DeckId, HTMLCanvasElement> = new Map();
  private deckZoomBeats: Map<DeckId, number> = new Map([
    ['A', 8],
    ['B', 8],
    ['C', 8],
    ['D', 8],
  ]);

  // UI state
  private isLibraryOpen = false; // Hidden by default as requested
  private isDdjModalOpen = false;
  private isDrawerOpen = false;
  private activeDrawerTab: 'tasks' | 'setlists' = 'tasks';
  private tasksList: GoogleTaskItem[] = [];
  private setlistsList: SetlistNote[] = [];

  // Google Drive Modal
  private isDriveModalOpen = false;
  private driveFiles: any[] = [];
  private isDriveLoading = false;

  // MIDI Monitor state
  private recentMidiLogs: string[] = [];
  private midiLedTimer: any = null;

  // Toast notifications
  private toastMessage: string | null = null;
  private toastTimer: any = null;

  private onSwitchTo3D?: () => void;

  constructor(container: HTMLElement, onSwitchTo3D?: () => void) {
    this.container = container;
    this.engine = AudioEngine.getInstance();
    this.midiService = MidiControllerService.getInstance();
    this.midiStatus = this.midiService.getStatus();
    this.onSwitchTo3D = onSwitchTo3D;

    // Listen for MIDI hardware state & activity
    this.midiService.onStatusChange((status) => {
      this.midiStatus = status;
      this.updateMidiPillUi();
    });

    this.midiService.onMidiActivity((detail) => {
      this.flashMidiLed();
      const logLine = `Ch${detail.channel} [${detail.type}] data1=${detail.data1} data2=${detail.data2} ${detail.name ? `(${detail.name})` : ''}`;
      this.recentMidiLogs.unshift(logLine);
      if (this.recentMidiLogs.length > 8) this.recentMidiLogs.pop();
      this.updateMidiLogUi();
      this.syncUiFromMidi(detail);
    });

    // Browser navigation from Pioneer DDJ-T1 Rotary Encoder & Push & Load Buttons
    this.midiService.onBrowserScroll((delta) => {
      this.handleMidiBrowserScroll(delta);
    });

    this.midiService.onBrowserLoad((deckId) => {
      this.handleMidiBrowserLoad(deckId);
    });

    this.midiService.onBrowserToggle(() => {
      this.toggleLibrary();
    });

    // Listen to Firebase Cloud Firestore status
    firebaseService.onStatus((status) => {
      this.firebaseStatus = status;
      const fbBadge = this.container.querySelector('#firebase-cloud-count');
      if (fbBadge) fbBadge.textContent = `${status.cloudTrackCount}`;
    });
  }

  public async init(): Promise<void> {
    const demoTracks = await this.engine.generateDemoTracks();
    this.tracks = [...demoTracks];

    if (this.tracks.length >= 2) {
      this.engine.loadTrack('A', this.tracks[0]);
      this.engine.loadTrack('B', this.tracks[1]);
    }

    this.tasksList = await listGoogleTasks();
    this.setlistsList = getSetlistNotes();

    this.render();
    this.startWaveformAnimation();
    this.setupGlobalShortcuts();
  }

  private render(): void {
    this.container.innerHTML = `
      <div class="traktor-console-root">
        <!-- Top Control Bar -->
        ${this.renderTopBar()}

        <!-- FX Units Panel (Top Rack) -->
        <div id="fx-units-container" class="fx-units-bar">
          ${this.renderFxUnits()}
        </div>

        <!-- Decks & Mixer Cockpit (Expanded full view) -->
        <div class="cockpit-grid ${this.engine.getMixerState().layoutMode}">
          <!-- DECK A -->
          <div class="deck-bay deck-a" id="deck-A">
            ${this.renderDeck('A')}
          </div>

          <!-- 4-CHANNEL MIXER -->
          <div class="mixer-bay" id="mixer-section">
            ${this.renderMixer()}
          </div>

          <!-- DECK B -->
          <div class="deck-bay deck-b" id="deck-B">
            ${this.renderDeck('B')}
          </div>
        </div>

        <!-- Secondary 4-Deck Row (if 4decks enabled) -->
        ${
          this.engine.getMixerState().layoutMode === '4decks'
            ? `
          <div class="secondary-decks-row">
            <div class="deck-bay deck-c" id="deck-C">
              ${this.renderDeck('C')}
            </div>
            <div class="deck-bay deck-d" id="deck-D">
              ${this.renderDeck('D')}
            </div>
          </div>
        `
            : ''
        }

        <!-- Floating Quick-Bar for Library (Hidden by default) -->
        <div class="floating-bottom-bar">
          <button id="btn-floating-library" class="btn-floating-library">
            <span class="material-symbols-outlined">library_music</span>
            <span class="lib-text">TRACK LIBRARY [SPACE]</span>
            <span class="track-count-badge font-mono">${this.tracks.length} Tracks</span>
          </button>
        </div>

        <!-- Slide-Up / Overlay Track Browser (Opened on click or Space) -->
        <div id="browser-drawer" class="browser-drawer ${this.isLibraryOpen ? 'open' : ''}">
          <div class="browser-drawer-header">
            <div class="drawer-title-group">
              <span class="material-symbols-outlined lib-icon">library_music</span>
              <h3>TRACK BROWSER & CRATES</h3>
              <span class="shortcut-tag font-mono">PRESS SPACE OR ESC TO CLOSE</span>
            </div>
            <button id="btn-close-browser" class="btn-close-drawer" title="Close Library (Space)">✕</button>
          </div>
          <div class="browser-drawer-content">
            ${this.renderBrowser()}
          </div>
        </div>

        <!-- Pioneer DDJ-T1 Hub Modal -->
        <div id="ddj-modal" class="modal-overlay ${this.isDdjModalOpen ? 'active' : ''}">
          ${this.renderDdjModal()}
        </div>

        <!-- Gig Companion Drawer (Google Tasks & Keep) -->
        <div id="gig-drawer" class="gig-drawer ${this.isDrawerOpen ? 'open' : ''}">
          ${this.renderGigDrawer()}
        </div>

        <!-- AI DJ Copilot Chatbot Drawer Container -->
        <div id="copilot-drawer-container"></div>

        <!-- Google Drive Picker Modal -->
        <div id="drive-modal" class="modal-overlay ${this.isDriveModalOpen ? 'active' : ''}">
          ${this.renderDriveModal()}
        </div>

        <!-- Toast Feedback -->
        <div id="toast-banner" class="toast-banner ${this.toastMessage ? 'visible' : ''}">
          <span class="material-symbols-outlined toast-ico">check_circle</span>
          <span class="toast-txt">${this.toastMessage || ''}</span>
        </div>
      </div>
    `;

    this.attachDomEvents();
  }

  // --- HTML Rendering Blocks ---

  private renderTopBar(): string {
    const mixer = this.engine.getMixerState();
    const user = getCurrentUser();

    const ddjConnected = this.midiStatus.connected;
    const isPioneer = this.midiStatus.isPioneerDDJT1;
    const ddjLabel = isPioneer
      ? 'PIONEER DDJ-T1'
      : ddjConnected
      ? this.midiStatus.deviceName.slice(0, 16)
      : 'PIONEER DDJ-T1';
    const ddjState = ddjConnected ? 'CONNECTED' : 'STANDBY';

    return `
      <header class="traktor-top-bar">
        <!-- Brand Zone -->
        <div class="brand-zone">
          <div class="traktor-logo-badge">
            <span class="logo-mark">NI</span>
            <span class="logo-text">TRAKTOR PRO</span>
            <span class="logo-sub">4.0 WEB</span>
          </div>

          <!-- Pioneer DDJ-T1 Controller Status Pill -->
          <button id="btn-ddj-t1" class="ddj-status-pill ${ddjConnected ? 'connected' : ''}" title="Pioneer DDJ-T1 Controller Hub & MIDI Monitor">
            <span class="ddj-led-dot ${ddjConnected ? 'online' : ''}" id="top-ddj-led"></span>
            <span class="ddj-title-txt font-mono">${ddjLabel}</span>
            <span class="ddj-badge font-mono">${ddjState}</span>
          </button>

          <button id="btn-toggle-3d" class="btn-spatial-toggle" title="Switch to 3D WebXR Spatial Rig">
            <span class="material-symbols-outlined icon">view_in_ar</span>
            <span class="label">3D Spatial Rig</span>
          </button>
        </div>

        <!-- Master Clock & Master Volume -->
        <div class="master-clock-zone">
          <div class="clock-display">
            <span class="lbl">MASTER CLOCK</span>
            <div class="bpm-stepper">
              <button class="btn-step" id="bpm-minus">-</button>
              <span class="bpm-val font-mono tabular-nums" id="master-bpm-readout">${mixer.masterBpm.toFixed(2)}</span>
              <button class="btn-step" id="bpm-plus">+</button>
            </div>
            <button class="btn-tap" id="btn-tap-tempo">TAP</button>
          </div>

          <!-- Master Volume Knob -->
          <div class="master-vol-group">
            <div class="traktor-knob-wrapper top-knob">
              <span class="knob-label">MASTER</span>
              <div class="traktor-knob" data-control="master_vol" data-min="0" data-max="1" data-default="0.85" data-value="${mixer.masterVolume}">
                <div class="knob-cap" style="transform: rotate(${(mixer.masterVolume - 0.5) * 270}deg)">
                  <div class="knob-notch"></div>
                </div>
              </div>
              <span class="knob-readout font-mono tabular-nums">${Math.round(mixer.masterVolume * 100)}%</span>
            </div>

            <!-- Master Stereo VU -->
            <div class="master-vu-stereo">
              <div class="vu-channel-wrap"><div class="vu-bar" id="master-vu-l"></div></div>
              <div class="vu-channel-wrap"><div class="vu-bar" id="master-vu-r"></div></div>
            </div>
          </div>
        </div>

        <!-- Actions & Workspace Zone -->
        <div class="top-actions-zone">
          <!-- Library Toggle Button (Primary Request) -->
          <button id="btn-toggle-library" class="btn-top-library ${this.isLibraryOpen ? 'active' : ''}" title="Open / Close Track Library [Space]">
            <span class="material-symbols-outlined">library_music</span>
            <span>LIBRARY (SPACE)</span>
          </button>

          <!-- AI DJ Copilot Button -->
          <button id="btn-toggle-copilot" class="btn-top-copilot ${this.copilotDrawer?.getIsOpen() ? 'active' : ''}" title="AI DJ Copilot - Set Analysis & Live Transition Coach (Key M)">
            <span class="material-symbols-outlined copilot-top-icon">smart_toy</span>
            <span class="copilot-btn-txt font-bold">DJ COPILOT</span>
            <span class="copilot-ai-sparkle">✨</span>
          </button>

          <!-- Layout Switcher -->
          <div class="layout-segmented-control">
            <button class="seg-btn ${mixer.layoutMode === '2decks' ? 'active' : ''}" data-layout="2decks">2 Decks</button>
            <button class="seg-btn ${mixer.layoutMode === '4decks' ? 'active' : ''}" data-layout="4decks">4 Decks</button>
          </div>

          <!-- Google Workspace & Cloud Sync -->
          <div class="auth-sync-group">
            ${
              user
                ? `
                <div class="user-chip" title="${user.email}">
                  <span class="material-symbols-outlined user-ico">account_circle</span>
                  <span class="user-name">${user.displayName || user.email?.split('@')[0]}</span>
                  <button id="btn-logout" class="btn-logout" title="Sign out">✕</button>
                </div>
              `
                : `
                <button id="btn-google-login" class="btn-google-auth">
                  <svg width="14" height="14" viewBox="0 0 24 24"><path fill="#fff" d="M12.545,10.239v3.821h5.445c-0.712,2.315-2.647,3.972-5.445,3.972c-3.332,0-6.033-2.701-6.033-6.032s2.701-6.032,6.033-6.032c1.498,0,2.866,0.549,3.921,1.453l2.814-2.814C17.503,2.988,15.139,2,12.545,2C7.021,2,2.543,6.477,2.543,12s4.478,10,10.002,10c8.396,0,10.249-7.85,9.426-11.748L12.545,10.239z"/></svg>
                  <span>Connect Google</span>
                </button>
              `
            }
          </div>

          <!-- Gig Companion Drawer Toggle -->
          <button id="btn-toggle-drawer" class="btn-gig-drawer ${this.isDrawerOpen ? 'active' : ''}" title="Gig Tasks & Setlist Notes">
            <span class="material-symbols-outlined">checklist</span>
            <span>Gig Prep</span>
          </button>
        </div>
      </header>
    `;
  }

  private renderFxUnits(): string {
    const fxUnits = this.engine.getMixerState().activeFxUnits;

    return fxUnits
      .map(
        (fx) => `
        <div class="fx-unit-rack ${fx.enabled ? 'active' : ''}" id="fx-unit-${fx.id}">
          <div class="fx-header">
            <button class="fx-on-btn ${fx.enabled ? 'active' : ''}" data-fx-id="${fx.id}">
              FX ${fx.id}
            </button>
            <select class="fx-type-select" data-fx-id="${fx.id}">
              <option value="delay" ${fx.type === 'delay' ? 'selected' : ''}>DELAY</option>
              <option value="reverb" ${fx.type === 'reverb' ? 'selected' : ''}>REVERB</option>
              <option value="flanger" ${fx.type === 'flanger' ? 'selected' : ''}>FLANGER</option>
              <option value="beatmasher" ${fx.type === 'beatmasher' ? 'selected' : ''}>BEATMASHER</option>
            </select>
          </div>
          <div class="fx-knobs-row">
            ${this.renderKnobHtml(`fx_${fx.id}_dw`, 'D/W', fx.dryWet, 0, 1, 0.5, '%', 'A', `fx_${fx.id}_dw`)}
            ${this.renderKnobHtml(`fx_${fx.id}_p1`, fx.type === 'delay' ? 'TIME' : fx.type === 'reverb' ? 'SIZE' : fx.type === 'flanger' ? 'SPEED' : 'FREQ', fx.param1, 0, 1, 0.5, '%', 'A', `fx_${fx.id}_p1`)}
            ${this.renderKnobHtml(`fx_${fx.id}_p2`, fx.type === 'delay' ? 'FEED' : fx.type === 'reverb' ? 'DAMP' : fx.type === 'flanger' ? 'FEED' : 'RESO', fx.param2, 0, 1, 0.5, '%', 'A', `fx_${fx.id}_p2`)}
            ${this.renderKnobHtml(`fx_${fx.id}_p3`, fx.type === 'delay' ? 'COLOR' : fx.type === 'reverb' ? 'COLOR' : fx.type === 'flanger' ? 'DEPTH' : 'STUTTER', fx.param3, 0, 1, 0.5, '%', 'A', `fx_${fx.id}_p3`)}
          </div>
        </div>
      `
      )
      .join('');
  }

  private renderKnobHtml(
    id: string,
    label: string,
    val: number,
    min: number,
    max: number,
    def: number,
    unit = '',
    deckId: DeckId = 'A',
    controlType = ''
  ): string {
    const ratio = (val - min) / (max - min);
    const deg = (ratio - 0.5) * 270;
    const displayVal =
      unit === 'dB'
        ? val >= 0
          ? `+${val.toFixed(1)}`
          : val.toFixed(1)
        : `${Math.round(ratio * 100)}`;

    return `
      <div class="traktor-knob-wrapper" data-control-id="${id}">
        <span class="knob-label">${label}</span>
        <div class="traktor-knob" data-deck="${deckId}" data-control="${controlType}" data-min="${min}" data-max="${max}" data-default="${def}" data-value="${val}" data-unit="${unit}">
          <div class="knob-cap" style="transform: rotate(${deg}deg)">
            <div class="knob-notch"></div>
          </div>
          <div class="knob-center-cap"></div>
        </div>
        <span class="knob-readout font-mono tabular-nums">${displayVal}${unit}</span>
      </div>
    `;
  }

  private renderDeck(deckId: DeckId): string {
    const deck = this.engine.getDeckState(deckId);
    const track = deck.track;

    const accentColor =
      deckId === 'A'
        ? '#00c3ff'
        : deckId === 'B'
        ? '#ff9900'
        : deckId === 'C'
        ? '#bd00ff'
        : '#00e575';
    const currentTime = this.formatTime(this.engine.getPlaybackTime(deckId));
    const totalTime = this.formatTime(track?.duration || 0);
    const zoomBeats = this.deckZoomBeats.get(deckId) || 8;

    return `
      <!-- Deck Top Strip -->
      <div class="deck-top-strip" style="--deck-accent: ${accentColor}">
        <div class="deck-badge">${deckId}</div>
        <div class="deck-track-meta">
          <div class="meta-row-1">
            <span class="track-title">${track ? track.title : 'Empty Deck'}</span>
            <span class="meta-sep">·</span>
            <span class="track-artist">${track ? track.artist : 'Load track from library (Press Space)'}</span>
          </div>
          <div class="meta-row-2">
            <span class="key-badge">${track ? track.key : '--'}</span>
            <span class="bpm-indicator font-mono tabular-nums">${deck.effectiveBpm.toFixed(2)} BPM</span>
            <span class="pitch-indicator font-mono tabular-nums">${deck.pitchPercent >= 0 ? '+' : ''}${deck.pitchPercent.toFixed(2)}%</span>
            <span class="time-readout font-mono tabular-nums">${currentTime} / ${totalTime}</span>
          </div>
        </div>
        <div class="deck-meta-actions">
          <button class="btn-meta-sync ${deck.sync ? 'active' : ''}" data-deck="${deckId}" data-action="sync">SYNC</button>
          <button class="btn-meta-master ${deck.master ? 'active' : ''}" data-deck="${deckId}" data-action="master">MASTER</button>
        </div>
      </div>

      <!-- Beat Phase Meter (Precision centered) -->
      <div class="beat-phase-meter-bar">
        <div class="phase-center-tick"></div>
        <div class="phase-offset-indicator" id="phase-meter-${deckId}"></div>
      </div>

      <!-- Waveform Header with Deep Zoom Controls -->
      <div class="waveform-header-bar">
        <span class="waveform-tag font-mono">SPECTRUM 3-BAND</span>
        <div class="waveform-zoom-bar">
          <button class="btn-zoom btn-zoom-out" data-deck="${deckId}" title="Zoom Out (Show more beats)">−</button>
          <span class="zoom-readout font-mono" id="zoom-readout-${deckId}">ZOOM: ${zoomBeats}B</span>
          <button class="btn-zoom btn-zoom-in" data-deck="${deckId}" title="Zoom In (Deep Transient Detail)">+</button>
        </div>
      </div>

      <!-- Zoomed Scrolling Waveform -->
      <div class="zoomed-waveform-container" data-deck="${deckId}">
        <canvas id="zoomed-waveform-${deckId}" class="zoomed-canvas"></canvas>
        <div class="center-playhead-line"></div>
      </div>

      <!-- Stripe Minimap Waveform -->
      <div class="stripe-waveform-container" data-deck="${deckId}">
        <canvas id="stripe-waveform-${deckId}" class="stripe-canvas"></canvas>
        <div class="stripe-playhead" id="stripe-playhead-${deckId}"></div>
      </div>

      <!-- Pioneer DDJ-T1 Needle Drop / Quick Search Strip -->
      <div class="needle-search-strip" data-deck="${deckId}" title="Pioneer DDJ-T1 Needle Drop (Touch / Drag to jump anywhere in track)">
        <div class="needle-strip-track">
          <div class="needle-strip-cursor" id="needle-cursor-${deckId}"></div>
        </div>
        <div class="needle-strip-label font-mono">PIONEER DDJ-T1 NEEDLE SEARCH</div>
      </div>

      <!-- Deck Lower Performance Controls -->
      <div class="deck-lower-controls">
        <!-- 8 Hotcue Pads (4 x 2 Grid) -->
        <div class="hotcues-grid">
          ${[1, 2, 3, 4, 5, 6, 7, 8]
            .map((num) => {
              const cue = track?.hotcues.find((h) => h.id === num);
              return `
              <button class="hotcue-pad ${cue ? 'set' : 'empty'}" data-deck="${deckId}" data-cue="${num}" style="--cue-color: ${
                cue?.color || '#334155'
              }" title="Hotcue ${num} (Hold Shift to Delete)">
                <span class="cue-num">${num}</span>
                <span class="cue-lbl">${cue ? cue.name : 'EMPTY'}</span>
              </button>
            `;
            })
            .join('')}
        </div>

        <!-- Loop and Beatjump Controls (Pioneer DDJ-T1 In/Out/Auto/Jump) -->
        <div class="loop-controls-strip">
          <div class="loop-size-picker">
            <button class="btn-loop-in" data-deck="${deckId}" data-action="loop_in" title="Pioneer DDJ Loop In / 4-Beat">IN</button>
            <button class="btn-loop-out" data-deck="${deckId}" data-action="loop_out" title="Pioneer DDJ Loop Out">OUT</button>
            <button class="btn-loop-half" data-deck="${deckId}" data-action="loop_half">/2</button>
            <button class="btn-loop-active ${deck.loopActive ? 'active' : ''}" data-deck="${deckId}" data-action="loop_toggle">
              LOOP ${deck.loopLength}
            </button>
            <button class="btn-loop-double" data-deck="${deckId}" data-action="loop_double">x2</button>
          </div>
          <div class="beatjump-picker">
            <button class="btn-jump" data-deck="${deckId}" data-jump="-4">◄ 4 BEATS</button>
            <button class="btn-jump" data-deck="${deckId}" data-jump="+4">4 BEATS ►</button>
          </div>
        </div>

        <!-- Pioneer DDJ-T1 Jog Platter & Transport Split -->
        <div class="transport-pitch-split">
          <div class="transport-buttons">
            <button class="btn-transport btn-play ${deck.isPlaying ? 'playing' : ''}" data-deck="${deckId}" data-action="play">
              <span class="material-symbols-outlined">${deck.isPlaying ? 'pause' : 'play_arrow'}</span>
              <span>PLAY</span>
            </button>
            <button class="btn-transport btn-cue" data-deck="${deckId}" data-action="cue">CUE</button>
            <button class="btn-transport btn-cup" data-deck="${deckId}" data-action="cup">CUP</button>
            <button class="btn-transport btn-flux ${deck.fluxMode ? 'active' : ''}" data-deck="${deckId}" data-action="flux">FLUX</button>
          </div>

          <!-- Interactive Pioneer DDJ-T1 Jog Platter (Center Vinyl Scratch + Outer Nudge) -->
          <div class="jog-platter-container">
            <div class="jog-wheel-outer" data-deck="${deckId}" title="Pioneer DDJ-T1 Outer Jog Rim (Drag left/right to Pitch Bend Nudge)">
              <div class="jog-platter-hub ${deck.isScratching ? 'scratching' : ''}" data-deck="${deckId}" title="Pioneer DDJ-T1 Top Platter (Click & drag to Vinyl Scratch)">
                <span class="jog-mode-lbl font-mono">${deck.isScratching ? 'SCRATCH' : 'VINYL JOG'}</span>
                <div class="jog-rotation-marker" id="jog-marker-${deckId}"></div>
              </div>
            </div>
          </div>

          <!-- Perfectly Centered Vertical Pitch Fader with Tempo Range Toggle -->
          <div class="pitch-fader-column">
            <div class="nudge-btns">
              <button class="btn-nudge" data-deck="${deckId}" data-nudge="up">+</button>
              <button class="btn-nudge" data-deck="${deckId}" data-nudge="down">-</button>
            </div>
            <button class="btn-tempo-range font-mono" data-deck="${deckId}" title="Cycle Tempo Range (±6%, ±10%, ±16%, ±100%)">±${deck.tempoRange ?? 8}%</button>
            <div class="custom-vertical-fader pitch-fader" data-control="pitch" data-deck="${deckId}">
              <div class="fader-rail"></div>
              <div class="fader-center-mark"></div>
              <div class="fader-cap" id="pitch-cap-${deckId}" style="top: ${50 - (deck.pitchPercent / (deck.tempoRange ?? 8 * 2)) * 50}%">
                <div class="cap-line"></div>
              </div>
            </div>
            <span class="pitch-val font-mono tabular-nums" id="pitch-val-${deckId}">${deck.pitchPercent >= 0 ? '+' : ''}${deck.pitchPercent.toFixed(2)}%</span>
          </div>
        </div>
      </div>
    `;
  }

  private renderMixer(): string {
    const mixer = this.engine.getMixerState();
    const deckA = this.engine.getDeckState('A');
    const deckB = this.engine.getDeckState('B');

    return `
      <div class="mixer-chassis">
        <div class="mixer-channels-row">
          <!-- Channel A Strip -->
          ${this.renderChannelStrip('A', deckA)}

          <!-- Master Center Split (Headphones & Curve) -->
          <div class="mixer-center-split">
            <span class="strip-header">MONITOR</span>
            <div class="center-knobs-col">
              ${this.renderKnobHtml('headphone_mix', 'CUE MIX', mixer.headphoneMix, 0, 1, 0.5, '%', 'A', 'hp_mix')}
              ${this.renderKnobHtml('headphone_vol', 'CUE VOL', mixer.headphoneVolume, 0, 1, 0.8, '%', 'A', 'hp_vol')}
            </div>
            <div class="xfade-curve-selector">
              <span class="knob-lbl">CURVE</span>
              <select id="xfade-curve-select" class="curve-select">
                <option value="smooth" ${mixer.crossfaderCurve === 'smooth' ? 'selected' : ''}>SMOOTH</option>
                <option value="linear" ${mixer.crossfaderCurve === 'linear' ? 'selected' : ''}>LINEAR</option>
                <option value="scratch" ${mixer.crossfaderCurve === 'scratch' ? 'selected' : ''}>SCRATCH</option>
              </select>
            </div>
          </div>

          <!-- Channel B Strip -->
          ${this.renderChannelStrip('B', deckB)}
        </div>

        <!-- Precision Centered Crossfader Bay -->
        <div class="crossfader-bay">
          <div class="xfade-assign-row">
            <span class="xfade-tag">A</span>
            <div class="custom-horizontal-fader" id="crossfader-track">
              <div class="fader-rail-h"></div>
              <div class="fader-center-mark-h"></div>
              <div class="fader-cap-h" id="crossfader-cap" style="left: ${(mixer.crossfader + 1) * 50}%">
                <div class="cap-line-h"></div>
              </div>
            </div>
            <span class="xfade-tag">B</span>
          </div>
        </div>
      </div>
    `;
  }

  private renderChannelStrip(deckId: DeckId, deck: DeckState): string {
    return `
      <div class="channel-strip channel-${deckId.toLowerCase()}" data-deck="${deckId}">
        <span class="strip-header">CH ${deckId}</span>

        <!-- FX Assign Buttons -->
        <div class="fx-assign-strip">
          <button class="btn-fx-assign ${deck.fxAssign[0] ? 'active' : ''}" data-deck="${deckId}" data-fx="1">FX 1</button>
          <button class="btn-fx-assign ${deck.fxAssign[1] ? 'active' : ''}" data-deck="${deckId}" data-fx="2">FX 2</button>
        </div>

        <!-- GAIN (TRIM) KNOB -->
        ${this.renderKnobHtml(`gain_${deckId}`, 'GAIN', deck.gain, 0, 2, 1.0, 'x', deckId, 'gain')}

        <!-- 3-BAND ISOLATOR EQ -->
        <div class="eq-knobs-stack">
          ${this.renderKnobHtml(`eq_hi_${deckId}`, 'HIGH', deck.eqHigh, -24, 12, 0, 'dB', deckId, 'eq_high')}
          ${this.renderKnobHtml(`eq_mid_${deckId}`, 'MID', deck.eqMid, -24, 12, 0, 'dB', deckId, 'eq_mid')}
          ${this.renderKnobHtml(`eq_low_${deckId}`, 'LOW', deck.eqLow, -24, 12, 0, 'dB', deckId, 'eq_low')}
        </div>

        <!-- DUAL RESONANT FILTER -->
        ${this.renderKnobHtml(`filter_${deckId}`, 'FILTER', deck.filter, -1, 1, 0, '%', deckId, 'filter')}

        <!-- CUE (PFL) HEADPHONE BUTTON -->
        <button class="btn-cue-pfl ${deck.cuePfl ? 'active' : ''}" data-deck="${deckId}">CUE</button>

        <!-- CHANNEL VOLUME FADER & PEAK VU METER -->
        <div class="channel-fader-vu-row">
          <div class="custom-vertical-fader volume-fader" data-control="volume" data-deck="${deckId}">
            <div class="fader-rail"></div>
            <div class="fader-cap" id="vol-cap-${deckId}" style="bottom: ${deck.volume * 100}%">
              <div class="cap-line"></div>
            </div>
          </div>
          <div class="channel-vu-meter">
            <div class="vu-bar" id="channel-vu-${deckId}" style="height: 0%"></div>
          </div>
        </div>

        <!-- CROSSFADER ASSIGN SWITCH -->
        <div class="xfade-assign-switch">
          <button class="btn-assign ${deck.xfadeAssign === 'A' ? 'active' : ''}" data-deck="${deckId}" data-assign="A">A</button>
          <button class="btn-assign ${deck.xfadeAssign === 'Thru' ? 'active' : ''}" data-deck="${deckId}" data-assign="Thru">▲</button>
          <button class="btn-assign ${deck.xfadeAssign === 'B' ? 'active' : ''}" data-deck="${deckId}" data-assign="B">B</button>
        </div>
      </div>
    `;
  }

  private renderBrowser(): string {
    return `
      <div class="browser-chassis">
        <!-- Browser Toolbar -->
        <div class="browser-toolbar">
          <div class="crate-tabs">
            <button class="tab-btn ${this.activeCrateTab === 'all' ? 'active' : ''}" data-crate="all">All Tracks (${this.tracks.length})</button>
            <button class="tab-btn ${this.activeCrateTab === 'demo' ? 'active' : ''}" data-crate="demo">Demo Stems</button>
            <button class="tab-btn ${this.activeCrateTab === 'local' ? 'active' : ''}" data-crate="local">Local Library</button>
            <button class="tab-btn ${this.activeCrateTab === 'drive' ? 'active' : ''}" data-crate="drive">Google Drive</button>
            <button class="tab-btn ${this.activeCrateTab === 'firebase' ? 'active' : ''}" data-crate="firebase">Firebase Cloud (<span id="firebase-cloud-count">${this.firebaseStatus.cloudTrackCount}</span>)</button>
          </div>

          <div class="search-box">
            <span class="material-symbols-outlined search-icon">search</span>
            <input type="text" id="browser-search" placeholder="Search track, artist, key, BPM..." value="${this.currentFilter}" />
          </div>

          <!-- Triple Option Actions: Local Upload + Analysis Backup / Google Drive / Firebase Cloud -->
          <div class="browser-actions">
            <!-- Option 1: Local Upload + Analysis File Backup -->
            <label class="btn-action btn-upload" title="Upload MP3, WAV, OGG, FLAC files">
              <span class="material-symbols-outlined">upload_file</span>
              <span>Upload Local</span>
              <input type="file" id="local-audio-upload" multiple accept="audio/*" style="display:none;" />
            </label>

            <button id="btn-export-backup" class="btn-action" title="Export .traktor.json analysis backup file">
              <span class="material-symbols-outlined">download</span>
              <span>Backup Analysis (.json)</span>
            </button>

            <label class="btn-action" title="Restore analyzed grids & cues from backup file">
              <span class="material-symbols-outlined">restore_page</span>
              <span>Restore Backup</span>
              <input type="file" id="restore-backup-input" accept=".json" style="display:none;" />
            </label>

            <!-- Option 2: Google Drive -->
            <button id="btn-open-drive" class="btn-action btn-drive-accent" title="Select audio from Google Drive">
              <span class="material-symbols-outlined">add_to_drive</span>
              <span>Google Drive</span>
            </button>

            <!-- Option 3: Firebase Cloud Storage / Firestore -->
            <button id="btn-sync-firebase" class="btn-action btn-firebase-accent" title="Upload analyzed track metadata to Firebase Cloud">
              <span class="material-symbols-outlined">cloud_upload</span>
              <span>Sync to Firebase</span>
            </button>

            <button id="btn-load-firebase" class="btn-action" title="Fetch tracks and hotcues from Firebase Cloud">
              <span class="material-symbols-outlined">cloud_download</span>
              <span>Load Firebase</span>
            </button>

            <button id="btn-backup-firebase" class="btn-action" title="Save full analysis backup file to Firebase Firestore">
              <span class="material-symbols-outlined">backup</span>
              <span>Cloud Backup</span>
            </button>
          </div>
        </div>

        <!-- Track Table -->
        <div class="track-table-container">
          ${this.renderTableOnly()}
        </div>
      </div>
    `;
  }

  private renderDdjModal(): string {
    const isPioneer = this.midiStatus.isPioneerDDJT1;
    const isConn = this.midiStatus.connected;
    const name = this.midiStatus.deviceName;

    return `
      <div class="modal-card ddj-modal-card">
        <div class="modal-header">
          <div class="modal-title-with-icon">
            <span class="material-symbols-outlined text-cyan-400">tune</span>
            <h3>Pioneer DDJ-T1 Controller Hub</h3>
          </div>
          <button id="btn-close-ddj-modal" class="modal-close-btn">✕</button>
        </div>

        <div class="ddj-modal-body">
          <!-- Connection Status Card -->
          <div class="ddj-status-card ${isConn ? 'connected' : 'offline'}">
            <div class="ddj-status-row">
              <div class="status-indicator-badge">
                <span class="status-pulse-dot"></span>
                <span class="status-text font-mono">${isConn ? (isPioneer ? 'PIONEER DDJ-T1 CONNECTED' : `DEVICE: ${name}`) : 'WAITING FOR MIDI CONTROLLER'}</span>
              </div>
              <button id="btn-rescan-midi" class="btn-rescan font-mono">RE-SCAN DEVICES</button>
            </div>
            <p class="ddj-desc-text">
              Plug in your <strong>Pioneer DDJ-T1</strong> via USB. Web MIDI automatically detects channel mapping:
              <strong>Ch 1</strong> (Deck A), <strong>Ch 2</strong> (Deck B), <strong>Ch 3</strong> (Deck C), <strong>Ch 4</strong> (Deck D).
            </p>
          </div>

          <!-- Comprehensive Pioneer DDJ-T1 TSI Mapping Specifications Grid -->
          <div class="ddj-mapping-grid">
            <div class="map-col">
              <h4>JOG WHEELS & NEEDLE SEARCH</h4>
              <ul>
                <li><strong>Platter Touch:</strong> Note 54/72 (Engages Vinyl Scratch)</li>
                <li><strong>Platter Rotation:</strong> CC 33/34 (2's comp relative scratch)</li>
                <li><strong>Outer Jog Rim:</strong> CC 33/34 (Pitch bend ±nudge)</li>
                <li><strong>Needle Drop Strip:</strong> CC 75 (Direct 0–100% position seek)</li>
                <li><strong>Needle Touch:</strong> Note 75 (Needle contact trigger)</li>
              </ul>
            </div>
            <div class="map-col">
              <h4>TRANSPORT & PERFORMANCE</h4>
              <ul>
                <li><strong>Play / Pause:</strong> Note 11 (0x0B)</li>
                <li><strong>Cue / CUP:</strong> Note 12 (0x0C) / Note 1</li>
                <li><strong>Sync / Master:</strong> Note 88 / Note 89</li>
                <li><strong>Key Lock / Range:</strong> Note 26 / Note 90 (±6..100%)</li>
                <li><strong>Loops:</strong> In (Note 16), Out (Note 17), Auto (Note 18)</li>
                <li><strong>Hotcues 1–8:</strong> Notes 24–31 (Shift = Delete)</li>
                <li><strong>Beatjump:</strong> Notes 21 / 22 (Move BWD / FWD)</li>
              </ul>
            </div>
            <div class="map-col">
              <h4>MIXER, FX & BROWSER</h4>
              <ul>
                <li><strong>Channel Faders & Trim:</strong> CC 19 & CC 4 (Ch 1–4)</li>
                <li><strong>3-Band EQ (Hi/Mid/Low):</strong> CC 16, 17, 18</li>
                <li><strong>Bipolar Filter:</strong> CC 23 (LPF < 64 < HPF)</li>
                <li><strong>Crossfader:</strong> CC 31 (Deck A < Center > Deck B)</li>
                <li><strong>Cue PFL & FX Assign:</strong> Note 84, Note 70/71</li>
                <li><strong>Browse Rotary / Push:</strong> CC 64 & Note 65</li>
                <li><strong>Load A / B / C / D:</strong> Notes 70, 71, 72, 73</li>
              </ul>
            </div>
          </div>

          <!-- Comprehensive Pioneer DDJ-T1 Live Hardware Simulator -->
          <div class="ddj-emulate-section">
            <span class="section-tag font-mono">LIVE HARDWARE SIMULATOR (CLICK TO TEST DDJ-T1 CONTROLS)</span>
            <div class="ddj-emulate-buttons">
              <!-- Jog Wheel & Scratch Controls -->
              <button class="btn-emu" data-emu="jog_touch_down">💿 Jog Platter Touch (Engage Scratch)</button>
              <button class="btn-emu" data-emu="jog_scratch_fwd">↷ Jog Scratch FWD (+8)</button>
              <button class="btn-emu" data-emu="jog_scratch_rev">↶ Jog Scratch REV (-8)</button>
              <button class="btn-emu" data-emu="jog_touch_up">✋ Platter Release (Resume Play)</button>
              <button class="btn-emu" data-emu="jog_nudge_up">➕ Outer Rim Pitch Bend (+)</button>
              <button class="btn-emu" data-emu="jog_nudge_down">➖ Outer Rim Pitch Bend (-)</button>

              <!-- Needle Drop Strip Controls -->
              <button class="btn-emu" data-emu="needle_25">📍 Needle Drop 25% (Drop to intro)</button>
              <button class="btn-emu" data-emu="needle_50">📍 Needle Drop 50% (Drop to breakdown)</button>
              <button class="btn-emu" data-emu="needle_75">📍 Needle Drop 75% (Drop to outro)</button>

              <!-- Transport Controls -->
              <button class="btn-emu" data-emu="play_a">▶ Play / Pause (Deck A)</button>
              <button class="btn-emu" data-emu="cue_a">⏹ Floating Cue (Deck A)</button>
              <button class="btn-emu" data-emu="sync_a">⚡ Sync BPM & Phase (Deck A)</button>
              <button class="btn-emu" data-emu="keylock_a">🔒 Key Lock Toggle (Deck A)</button>
              <button class="btn-emu" data-emu="tempo_range_a">📊 Cycle Tempo Range (±6..100%)</button>

              <!-- Loops & Move / Beatjump -->
              <button class="btn-emu" data-emu="loop_in_a">⭕ Loop In (Deck A)</button>
              <button class="btn-emu" data-emu="loop_out_a">🔴 Loop Out (Deck A)</button>
              <button class="btn-emu" data-emu="auto_loop_a">🔁 Auto Loop Toggle (Deck A)</button>
              <button class="btn-emu" data-emu="beatjump_bwd">◀ Beatjump -4 Beats</button>
              <button class="btn-emu" data-emu="beatjump_fwd">▶ Beatjump +4 Beats</button>

              <!-- Hotcues & Shift Delete -->
              <button class="btn-emu" data-emu="hotcue_1">🏷 Hotcue 1 Trigger</button>
              <button class="btn-emu" data-emu="hotcue_2">🏷 Hotcue 2 Trigger</button>
              <button class="btn-emu" data-emu="hotcue_del_1">❌ Shift + Hotcue 1 (Delete)</button>

              <!-- Browser & Track Loading -->
              <button class="btn-emu" data-emu="browse_scroll_down">📜 Browse Scroll Down</button>
              <button class="btn-emu" data-emu="browse_scroll_up">📜 Browse Scroll Up</button>
              <button class="btn-emu" data-emu="browse_push">📂 Browse Push (Toggle Library)</button>
              <button class="btn-emu" data-emu="load_deck_a">📥 Load Selected -> Deck A</button>
              <button class="btn-emu" data-emu="load_deck_b">📥 Load Selected -> Deck B</button>

              <!-- Mixer & Crossfader -->
              <button class="btn-emu" data-emu="xfade_left">◀ Crossfader Left (Deck A)</button>
              <button class="btn-emu" data-emu="xfade_center">↔ Crossfader Center</button>
              <button class="btn-emu" data-emu="xfade_right">▶ Crossfader Right (Deck B)</button>
              <button class="btn-emu" data-emu="filter_hp">🎛 Filter HighPass (+80%)</button>
              <button class="btn-emu" data-emu="filter_lp">🎛 Filter LowPass (-80%)</button>
              <button class="btn-emu" data-emu="filter_flat">🎛 Filter Flat (0%)</button>
            </div>
          </div>

          <!-- Live MIDI Activity Log -->
          <div class="midi-log-console">
            <span class="log-title font-mono">LIVE MIDI MESSAGE MONITOR:</span>
            <div class="midi-log-content font-mono" id="midi-log-output">
              ${
                this.recentMidiLogs.length === 0
                  ? '<span class="text-slate-500">No MIDI signals received yet. Plug in your Pioneer DDJ-T1 or click a test button above.</span>'
                  : this.recentMidiLogs.join('<br>')
              }
            </div>
          </div>
        </div>
      </div>
    `;
  }

  private renderGigDrawer(): string {
    return `
      <div class="drawer-header">
        <div class="drawer-tabs">
          <button class="drawer-tab ${this.activeDrawerTab === 'tasks' ? 'active' : ''}" data-drawer-tab="tasks">
            <span class="material-symbols-outlined">task_alt</span>
            <span>Google Tasks (Prep)</span>
          </button>
          <button class="drawer-tab ${this.activeDrawerTab === 'setlists' ? 'active' : ''}" data-drawer-tab="setlists">
            <span class="material-symbols-outlined">edit_note</span>
            <span>Google Keep (Setlists)</span>
          </button>
        </div>
        <button id="btn-close-drawer" class="btn-close-drawer">✕</button>
      </div>

      <div class="drawer-body">
        ${
          this.activeDrawerTab === 'tasks'
            ? `
          <div class="tasks-panel">
            <div class="task-input-row">
              <input type="text" id="new-task-input" placeholder="Add DJ gig preparation task..." />
              <button id="btn-add-task" class="btn-primary">Add Task</button>
            </div>
            <div class="tasks-list">
              ${
                this.tasksList.length === 0
                  ? '<div class="empty-list">No preparation tasks yet. Add soundcheck, cables, USB export items!</div>'
                  : this.tasksList
                      .map(
                        (t) => `
                <div class="task-item ${t.status === 'completed' ? 'completed' : ''}" data-task-id="${t.id}">
                  <input type="checkbox" class="task-checkbox" data-task-id="${t.id}" ${t.status === 'completed' ? 'checked' : ''} />
                  <span class="task-title">${t.title}</span>
                  <button class="btn-delete-task" data-task-id="${t.id}" title="Delete task">✕</button>
                </div>
              `
                      )
                      .join('')
              }
            </div>
          </div>
        `
            : `
          <div class="setlists-panel">
            <div class="setlist-create-card">
              <input type="text" id="new-setlist-title" placeholder="Setlist Name (e.g. Peak Time Festival Set)" />
              <textarea id="new-setlist-content" rows="3" placeholder="Notes, track sequence, BPM progression, key transitions..."></textarea>
              <button id="btn-save-setlist" class="btn-primary">Save Setlist Note</button>
            </div>
            <div class="setlists-list">
              ${
                this.setlistsList.length === 0
                  ? '<div class="empty-list">No setlist notes saved yet. Plan your transition keys and cues!</div>'
                  : this.setlistsList
                      .map(
                        (note) => `
                <div class="setlist-card" data-note-id="${note.id}">
                  <div class="setlist-header">
                    <h4>${note.title}</h4>
                    <span class="setlist-date">${note.updatedAt}</span>
                    <button class="btn-delete-note" data-note-id="${note.id}">✕</button>
                  </div>
                  <pre class="setlist-content">${note.content}</pre>
                </div>
              `
                      )
                      .join('')
              }
            </div>
          </div>
        `
        }
      </div>
    `;
  }

  private renderDriveModal(): string {
    return `
      <div class="modal-card">
        <div class="modal-header">
          <div class="modal-title-with-icon">
            <span class="material-symbols-outlined text-green-400">add_to_drive</span>
            <h3>Select Audio from Google Drive</h3>
          </div>
          <button id="btn-close-drive-modal" class="modal-close-btn">✕</button>
        </div>

        <div class="modal-body">
          <div class="drive-status-strip">
            <span>Browse and import audio files (.mp3, .wav, .flac) stored in your Google Drive.</span>
            <button id="btn-drive-refresh" class="btn-secondary">Refresh</button>
          </div>

          ${
            this.isDriveLoading
              ? `
            <div class="loading-state">
              <div class="spinner"></div>
              <p>Scanning Google Drive audio files...</p>
            </div>
          `
              : this.driveFiles.length === 0
              ? `
            <div class="empty-state">
              <span class="material-symbols-outlined empty-ico">audio_file</span>
              <p>No audio files found in Google Drive root or you haven't granted permission yet.</p>
              <button id="btn-drive-reauth" class="btn-primary">Grant Drive Permissions</button>
            </div>
          `
              : `
            <div class="drive-files-list">
              ${this.driveFiles
                .map(
                  (f) => `
                <div class="drive-file-row">
                  <div class="file-info">
                    <span class="material-symbols-outlined file-ico">music_note</span>
                    <span class="file-name">${f.name}</span>
                    <span class="file-size">${f.size ? Math.round(f.size / 1024 / 1024) + ' MB' : ''}</span>
                  </div>
                  <button class="btn-import-drive" data-file-id="${f.id}" data-file-name="${f.name}">
                    Import & Analyze
                  </button>
                </div>
              `
                )
                .join('')}
            </div>
          `
          }
        </div>
      </div>
    `;
  }

  // --- Waveform Canvas Animations (60 FPS) ---

  private startWaveformAnimation(): void {
    const loop = () => {
      this.updateWaveforms();
      this.updateVuMeters();
      this.animFrameId = requestAnimationFrame(loop);
    };
    this.animFrameId = requestAnimationFrame(loop);
  }

  private updateWaveforms(): void {
    const deckIds: DeckId[] = ['A', 'B'];
    if (this.engine.getMixerState().layoutMode === '4decks') {
      deckIds.push('C', 'D');
    }

    for (const deckId of deckIds) {
      const deck = this.engine.getDeckState(deckId);
      const currentTime = this.engine.getPlaybackTime(deckId);

      const timeElem = this.container.querySelector(`#deck-${deckId} .time-readout`);
      if (timeElem && deck.track) {
        timeElem.textContent = `${this.formatTime(currentTime)} / ${this.formatTime(deck.track.duration)}`;
      }

      // Always dynamically fetch canvas elements from the active DOM to ensure reliability
      const zoomedCanvas =
        (document.getElementById(`zoomed-waveform-${deckId}`) as HTMLCanvasElement) ||
        this.zoomedCanvases.get(deckId);
      if (zoomedCanvas) {
        this.zoomedCanvases.set(deckId, zoomedCanvas);
        if (deck.track?.waveformPeaks) {
          this.drawZoomedWaveform(zoomedCanvas, deck, currentTime, deckId);
        }
      }

      const stripeCanvas =
        (document.getElementById(`stripe-waveform-${deckId}`) as HTMLCanvasElement) ||
        this.stripeCanvases.get(deckId);
      if (stripeCanvas) {
        this.stripeCanvases.set(deckId, stripeCanvas);
        if (deck.track?.waveformPeaks) {
          this.drawStripeWaveform(stripeCanvas, deck, currentTime);
        }
      }

      const phaseElem = document.getElementById(`phase-meter-${deckId}`);
      if (phaseElem && deck.track) {
        const beatSec = 60 / deck.effectiveBpm;
        const phase = ((currentTime - deck.track.beatgridOffset) % beatSec) / beatSec;
        const offsetPercent = (phase - 0.5) * 100;
        phaseElem.style.transform = `translateX(${offsetPercent}%)`;
      }
    }
  }

  /**
   * Draws a highly detailed multi-resolution 3-band spectrum waveform
   * Deep zoom capability: can zoom from 32 beats down to 0.5 beats for extreme precision!
   */
  private drawZoomedWaveform(
    canvas: HTMLCanvasElement,
    deck: DeckState,
    currentTime: number,
    deckId: DeckId
  ): void {
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const targetWidth = canvas.clientWidth * window.devicePixelRatio || 500;
    const targetHeight = canvas.clientHeight * window.devicePixelRatio || 130;

    if (canvas.width !== targetWidth || canvas.height !== targetHeight) {
      canvas.width = targetWidth;
      canvas.height = targetHeight;
    }

    const width = canvas.width;
    const height = canvas.height;

    ctx.clearRect(0, 0, width, height);

    const track = deck.track!;
    const peaks = track.waveformPeaks!;
    const totalBuckets = peaks.peaks.length;
    const duration = track.duration;

    const beatSec = 60 / deck.effectiveBpm;
    const zoomBeats = this.deckZoomBeats.get(deckId) || 8;
    const windowSec = beatSec * zoomBeats;
    const pixelsPerSec = width / windowSec;
    const bucketsPerSec = totalBuckets / duration;

    const currentBucket = (currentTime / duration) * totalBuckets;
    const centerPx = width / 2;
    const centerY = height / 2;

    const startBucket = Math.floor(currentBucket - (width / 2 / pixelsPerSec) * bucketsPerSec);
    const endBucket = Math.ceil(currentBucket + (width / 2 / pixelsPerSec) * bucketsPerSec);

    // Draw background subtle center zero-line
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, centerY);
    ctx.lineTo(width, centerY);
    ctx.stroke();

    // 1. Draw Multi-Band Detailed Spectrum Bars with anti-aliasing
    const sliceWidth = Math.max(1.2, (pixelsPerSec / bucketsPerSec) * 1.15);

    for (let b = Math.max(0, startBucket); b < Math.min(totalBuckets, endBucket); b++) {
      const bucketTime = (b / totalBuckets) * duration;
      const x = centerPx + (bucketTime - currentTime) * pixelsPerSec;

      const peak = peaks.peaks[b] || 0;
      const low = peaks.lows[b] || 0;
      const mid = peaks.mids[b] || 0;
      const high = peaks.highs[b] || 0;

      const barHeight = peak * (height * 0.44);

      // Traktor signature Vector Spectrum color balance:
      // Low (Bass/Kick) = Crimson Red / Magenta
      // Mid (Vocals/Snare) = Punchy Emerald / Amber
      // High (Hi-hats/Air) = Electric Cyan / White
      const r = Math.min(255, Math.floor(low * 310 + 20));
      const g = Math.min(255, Math.floor(mid * 250 + high * 60));
      const bColor = Math.min(255, Math.floor(high * 300 + 40));

      ctx.fillStyle = `rgb(${r}, ${g}, ${bColor})`;
      ctx.fillRect(x, centerY - barHeight, sliceWidth, barHeight * 2);
    }

    // 2. Draw Precision Beatgrid Lines & Sub-beat Divisions
    const gridStart = Math.floor((currentTime - windowSec / 2 - track.beatgridOffset) / beatSec);
    const gridEnd = Math.ceil((currentTime + windowSec / 2 - track.beatgridOffset) / beatSec);

    // Sub-beat ticks for deep zoom (< 4 beats)
    if (zoomBeats <= 4) {
      const subDivisions = zoomBeats <= 1 ? 4 : 2;
      for (let beat = gridStart - 1; beat <= gridEnd + 1; beat++) {
        for (let sub = 1; sub < subDivisions; sub++) {
          const subTime = track.beatgridOffset + (beat + sub / subDivisions) * beatSec;
          const subX = centerPx + (subTime - currentTime) * pixelsPerSec;
          if (subX >= 0 && subX <= width) {
            ctx.strokeStyle = 'rgba(255, 255, 255, 0.16)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(subX, centerY - 14);
            ctx.lineTo(subX, centerY + 14);
            ctx.stroke();
          }
        }
      }
    }

    // Primary Beat & Downbeat Lines
    for (let beat = gridStart; beat <= gridEnd; beat++) {
      const beatTime = track.beatgridOffset + beat * beatSec;
      const x = centerPx + (beatTime - currentTime) * pixelsPerSec;

      const isDownbeat = ((beat % 4) + 4) % 4 === 0;
      ctx.strokeStyle = isDownbeat ? '#ff2a4d' : 'rgba(255, 255, 255, 0.42)';
      ctx.lineWidth = isDownbeat ? 2 : 1;
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, height);
      ctx.stroke();

      // Downbeat label
      if (isDownbeat && zoomBeats <= 16) {
        ctx.fillStyle = '#ff2a4d';
        ctx.font = 'bold 9px monospace';
        ctx.fillText(`${beat / 4 + 1}`, x + 3, 11);
      }
    }

    // 3. Draw Active Loop Overlay
    if (deck.loopActive && deck.loopEnd > deck.loopStart) {
      const loopStartX = centerPx + (deck.loopStart - currentTime) * pixelsPerSec;
      const loopEndX = centerPx + (deck.loopEnd - currentTime) * pixelsPerSec;
      if (loopEndX >= 0 && loopStartX <= width) {
        ctx.fillStyle = 'rgba(0, 230, 118, 0.18)';
        ctx.fillRect(Math.max(0, loopStartX), 0, Math.min(width, loopEndX) - Math.max(0, loopStartX), height);

        ctx.strokeStyle = '#00e676';
        ctx.lineWidth = 2;
        ctx.strokeRect(Math.max(0, loopStartX), 0, Math.min(width, loopEndX) - Math.max(0, loopStartX), height);
      }
    }

    // 4. Draw Hotcue Pins & Labels
    for (const cue of track.hotcues) {
      const x = centerPx + (cue.time - currentTime) * pixelsPerSec;
      if (x >= -20 && x <= width + 20) {
        ctx.fillStyle = cue.color;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x + 12, 0);
        ctx.lineTo(x + 12, 16);
        ctx.lineTo(x, 10);
        ctx.fill();

        ctx.fillStyle = '#000';
        ctx.font = 'bold 9px sans-serif';
        ctx.fillText(`${cue.id}`, x + 3, 10);
      }
    }
  }

  private drawStripeWaveform(canvas: HTMLCanvasElement, deck: DeckState, currentTime: number): void {
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const targetWidth = canvas.clientWidth * window.devicePixelRatio || 500;
    const targetHeight = canvas.clientHeight * window.devicePixelRatio || 32;

    if (canvas.width !== targetWidth || canvas.height !== targetHeight) {
      canvas.width = targetWidth;
      canvas.height = targetHeight;
    }

    const width = canvas.width;
    const height = canvas.height;

    ctx.clearRect(0, 0, width, height);

    const track = deck.track!;
    const peaks = track.waveformPeaks!;
    const count = peaks.peaks.length;
    const centerY = height / 2;

    for (let i = 0; i < width; i++) {
      const bucketIdx = Math.floor((i / width) * count);
      const peak = peaks.peaks[bucketIdx] || 0;
      const barH = peak * (height * 0.44);

      const isPlayed = (i / width) * track.duration <= currentTime;
      ctx.fillStyle = isPlayed ? '#00c3ff' : '#475569';
      ctx.fillRect(i, centerY - barH, 1, barH * 2);
    }

    // Playhead line
    const playheadX = (currentTime / track.duration) * width;
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(playheadX, 0);
    ctx.lineTo(playheadX, height);
    ctx.stroke();
  }

  private updateVuMeters(): void {
    ['A', 'B'].forEach((id) => {
      const level = this.engine.getChannelVuLevel(id as DeckId);
      const vuElem = document.getElementById(`channel-vu-${id}`);
      if (vuElem) {
        vuElem.style.height = `${Math.min(100, Math.round(level * 100))}%`;
      }
    });

    const masterVu = this.engine.getMasterVuLevels();
    const lElem = document.getElementById('master-vu-l');
    const rElem = document.getElementById('master-vu-r');
    if (lElem) lElem.style.height = `${Math.min(100, Math.round(masterVu.left * 100))}%`;
    if (rElem) rElem.style.height = `${Math.min(100, Math.round(masterVu.right * 100))}%`;
  }

  // --- Tactile Drag Controller for Knobs & Faders ---

  private setupKnobInteractions(): void {
    this.container.querySelectorAll('.traktor-knob').forEach((elem) => {
      const knob = elem as HTMLElement;
      let startY = 0;
      let startVal = 0;
      const min = Number(knob.dataset.min);
      const max = Number(knob.dataset.max);
      const def = Number(knob.dataset.default);
      const unit = knob.dataset.unit || '';
      const deckId = knob.dataset.deck as DeckId;
      const control = knob.dataset.control!;

      const updateKnobValue = (newVal: number) => {
        newVal = Math.max(min, Math.min(max, newVal));
        knob.dataset.value = newVal.toString();

        const ratio = (newVal - min) / (max - min);
        const deg = (ratio - 0.5) * 270;
        const cap = knob.querySelector('.knob-cap') as HTMLElement;
        if (cap) cap.style.transform = `rotate(${deg}deg)`;

        const wrapper = knob.closest('.traktor-knob-wrapper');
        const readout = wrapper?.querySelector('.knob-readout');
        if (readout) {
          readout.textContent =
            unit === 'dB'
              ? newVal >= 0
                ? `+${newVal.toFixed(1)}`
                : newVal.toFixed(1)
              : `${Math.round(ratio * 100)}${unit}`;
        }

        // Send to Audio Engine
        if (control === 'gain') {
          this.engine.setGain(deckId, newVal);
        } else if (control === 'eq_high') {
          this.engine.setEq(deckId, 'high', newVal);
        } else if (control === 'eq_mid') {
          this.engine.setEq(deckId, 'mid', newVal);
        } else if (control === 'eq_low') {
          this.engine.setEq(deckId, 'low', newVal);
        } else if (control === 'filter') {
          this.engine.setFilter(deckId, newVal);
        } else if (control === 'master_vol') {
          this.engine.setMasterVolume(newVal);
        } else if (control === 'hp_vol') {
          this.engine.setHeadphoneVolume(newVal);
        } else if (control === 'hp_mix') {
          this.engine.getMixerState().headphoneMix = newVal;
        }

        // FX Units modulation (Fixing the FX knobs issue)
        const fxMatch = control.match(/^fx_(\d+)_(dw|p1|p2|p3)$/);
        if (fxMatch) {
          const fxId = Number(fxMatch[1]);
          const paramType =
            fxMatch[2] === 'dw'
              ? 'dryWet'
              : fxMatch[2] === 'p1'
              ? 'param1'
              : fxMatch[2] === 'p2'
              ? 'param2'
              : 'param3';
          this.engine.setFxParam(fxId, paramType, newVal);
        }
      };

      knob.addEventListener('mousedown', (e) => {
        e.preventDefault();
        startY = e.clientY;
        startVal = Number(knob.dataset.value);

        const onMouseMove = (moveEvt: MouseEvent) => {
          const deltaY = startY - moveEvt.clientY;
          const range = max - min;
          const newVal = startVal + (deltaY / 120) * range;
          updateKnobValue(newVal);
        };

        const onMouseUp = () => {
          window.removeEventListener('mousemove', onMouseMove);
          window.removeEventListener('mouseup', onMouseUp);
        };

        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
      });

      // Double click to reset to default center
      knob.addEventListener('dblclick', () => {
        updateKnobValue(def);
      });
    });
  }

  private setupFaderInteractions(): void {
    // 1. Channel Volume Faders
    this.container.querySelectorAll('.volume-fader').forEach((elem) => {
      const fader = elem as HTMLElement;
      const deckId = fader.dataset.deck as DeckId;

      const updateFaderFromY = (clientY: number) => {
        const rect = fader.getBoundingClientRect();
        const ratio = Math.max(0, Math.min(1, (rect.bottom - clientY) / rect.height));
        this.engine.setVolume(deckId, ratio);
        const cap = fader.querySelector('.fader-cap') as HTMLElement;
        if (cap) cap.style.bottom = `${ratio * 100}%`;
      };

      fader.addEventListener('mousedown', (e) => {
        e.preventDefault();
        updateFaderFromY(e.clientY);

        const onMouseMove = (moveEvt: MouseEvent) => {
          updateFaderFromY(moveEvt.clientY);
        };
        const onMouseUp = () => {
          window.removeEventListener('mousemove', onMouseMove);
          window.removeEventListener('mouseup', onMouseUp);
        };
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
      });
    });

    // 2. Pitch Faders (Straight vertical, zero tilt)
    this.container.querySelectorAll('.pitch-fader').forEach((elem) => {
      const fader = elem as HTMLElement;
      const deckId = fader.dataset.deck as DeckId;

      const updatePitchFromY = (clientY: number) => {
        const rect = fader.getBoundingClientRect();
        const ratio = Math.max(0, Math.min(1, (clientY - rect.top) / rect.height));
        // ratio 0 = +16%, ratio 0.5 = 0%, ratio 1 = -16%
        const pitchPercent = (0.5 - ratio) * 32;
        this.engine.setPitch(deckId, pitchPercent);

        const cap = fader.querySelector('.fader-cap') as HTMLElement;
        if (cap) cap.style.top = `${ratio * 100}%`;
        const valElem = this.container.querySelector(`#pitch-val-${deckId}`);
        if (valElem) {
          valElem.textContent = `${pitchPercent >= 0 ? '+' : ''}${pitchPercent.toFixed(2)}%`;
        }
      };

      fader.addEventListener('mousedown', (e) => {
        e.preventDefault();
        updatePitchFromY(e.clientY);

        const onMouseMove = (moveEvt: MouseEvent) => {
          updatePitchFromY(moveEvt.clientY);
        };
        const onMouseUp = () => {
          window.removeEventListener('mousemove', onMouseMove);
          window.removeEventListener('mouseup', onMouseUp);
        };
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
      });

      fader.addEventListener('dblclick', () => {
        this.engine.setPitch(deckId, 0);
        const cap = fader.querySelector('.fader-cap') as HTMLElement;
        if (cap) cap.style.top = '50%';
        const valElem = this.container.querySelector(`#pitch-val-${deckId}`);
        if (valElem) valElem.textContent = '+0.00%';
      });
    });

    // 3. Precision Horizontal Crossfader
    const xfadeTrack = this.container.querySelector('#crossfader-track') as HTMLElement;
    if (xfadeTrack) {
      const updateCrossfaderFromX = (clientX: number) => {
        const rect = xfadeTrack.getBoundingClientRect();
        const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
        const xfadeVal = (ratio - 0.5) * 2; // -1 to +1
        this.engine.updateCrossfader(xfadeVal);

        const cap = xfadeTrack.querySelector('#crossfader-cap') as HTMLElement;
        if (cap) cap.style.left = `${ratio * 100}%`;
      };

      xfadeTrack.addEventListener('mousedown', (e) => {
        e.preventDefault();
        updateCrossfaderFromX(e.clientX);

        const onMouseMove = (moveEvt: MouseEvent) => {
          updateCrossfaderFromX(moveEvt.clientX);
        };
        const onMouseUp = () => {
          window.removeEventListener('mousemove', onMouseMove);
          window.removeEventListener('mouseup', onMouseUp);
        };
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
      });

      xfadeTrack.addEventListener('dblclick', () => {
        this.engine.updateCrossfader(0);
        const cap = xfadeTrack.querySelector('#crossfader-cap') as HTMLElement;
        if (cap) cap.style.left = '50%';
      });
    }
  }

  // --- DOM Event Bindings ---

  private attachDomEvents(): void {
    this.setupKnobInteractions();
    this.setupFaderInteractions();

    // 3D Spatial Rig Switcher
    this.container.querySelector('#btn-toggle-3d')?.addEventListener('click', () => {
      this.onSwitchTo3D?.();
    });

    // Master Clock BPM Stepper & Tap Tempo
    this.container.querySelector('#bpm-minus')?.addEventListener('click', () => {
      const current = this.engine.getMixerState().masterBpm;
      this.engine.setMasterBpm(Math.max(60, current - 0.5));
      this.updateMasterBpmDisplay();
    });
    this.container.querySelector('#bpm-plus')?.addEventListener('click', () => {
      const current = this.engine.getMixerState().masterBpm;
      this.engine.setMasterBpm(Math.min(220, current + 0.5));
      this.updateMasterBpmDisplay();
    });

    let tapTimes: number[] = [];
    this.container.querySelector('#btn-tap-tempo')?.addEventListener('click', () => {
      const now = performance.now();
      tapTimes.push(now);
      if (tapTimes.length > 5) tapTimes.shift();
      if (tapTimes.length >= 2) {
        const diffs = [];
        for (let i = 1; i < tapTimes.length; i++) {
          diffs.push(tapTimes[i] - tapTimes[i - 1]);
        }
        const avgDiff = diffs.reduce((a, b) => a + b, 0) / diffs.length;
        const bpm = 60000 / avgDiff;
        if (bpm >= 60 && bpm <= 220) {
          this.engine.setMasterBpm(bpm);
          this.updateMasterBpmDisplay();
        }
      }
    });

    // Layout Mode (2 decks / 4 decks)
    this.container.querySelectorAll('.layout-segmented-control .seg-btn').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const layout = (e.currentTarget as HTMLElement).dataset.layout as '2decks' | '4decks';
        this.engine.getMixerState().layoutMode = layout;
        this.render();
      });
    });

    // Pioneer DDJ-T1 Hub Modal
    this.container.querySelector('#btn-ddj-t1')?.addEventListener('click', () => {
      this.isDdjModalOpen = true;
      const modal = this.container.querySelector('#ddj-modal');
      if (modal) {
        modal.classList.add('active');
        modal.innerHTML = this.renderDdjModal();
        this.attachDdjModalEvents();
      }
    });

    // Library Drawer Toggles (Top bar & Floating bar)
    this.container.querySelector('#btn-toggle-library')?.addEventListener('click', () => {
      this.toggleLibrary();
    });
    this.container.querySelector('#btn-floating-library')?.addEventListener('click', () => {
      this.toggleLibrary(true);
    });
    this.container.querySelector('#btn-close-browser')?.addEventListener('click', () => {
      this.toggleLibrary(false);
    });

    // AI DJ Copilot Drawer initialization & toggle
    const copilotContainer = this.container.querySelector('#copilot-drawer-container') as HTMLElement;
    if (copilotContainer) {
      const wasOpen = this.copilotDrawer ? this.copilotDrawer.getIsOpen() : false;
      this.copilotDrawer = new CopilotChatDrawer(
        copilotContainer,
        () => this.tracks,
        wasOpen
      );
    }

    this.container.querySelector('#btn-toggle-copilot')?.addEventListener('click', () => {
      this.copilotDrawer?.toggle();
    });

    // Google Auth
    this.container.querySelector('#btn-google-login')?.addEventListener('click', async () => {
      try {
        await googleSignIn();
        this.tasksList = await listGoogleTasks();
        this.render();
      } catch (err) {
        console.warn('Login canceled or failed:', err);
      }
    });

    this.container.querySelector('#btn-logout')?.addEventListener('click', async () => {
      await logout();
      this.render();
    });

    // Gig Drawer Toggle
    this.container.querySelector('#btn-toggle-drawer')?.addEventListener('click', () => {
      this.isDrawerOpen = !this.isDrawerOpen;
      const drawer = this.container.querySelector('#gig-drawer');
      drawer?.classList.toggle('open', this.isDrawerOpen);
    });

    this.container.querySelector('#btn-close-drawer')?.addEventListener('click', () => {
      this.isDrawerOpen = false;
      this.container.querySelector('#gig-drawer')?.classList.remove('open');
    });

    this.container.querySelectorAll('.drawer-tab').forEach((tab) => {
      tab.addEventListener('click', (e) => {
        const target = e.currentTarget as HTMLElement;
        this.activeDrawerTab = target.dataset.drawerTab as 'tasks' | 'setlists';
        const body = this.container.querySelector('.drawer-body');
        if (body) {
          body.innerHTML = this.renderGigDrawer();
          this.attachDrawerEvents();
        }
        this.container.querySelectorAll('.drawer-tab').forEach((t) => t.classList.remove('active'));
        target.classList.add('active');
      });
    });

    this.attachDrawerEvents();

    // Crossfader Curve
    this.container.querySelector('#xfade-curve-select')?.addEventListener('change', (e) => {
      const val = (e.target as HTMLSelectElement).value as any;
      this.engine.setCrossfaderCurve(val);
    });

    // FX Units: ON Button toggle & Type select
    this.container.querySelectorAll('.fx-on-btn').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const fxId = Number((e.currentTarget as HTMLElement).dataset.fxId);
        const newState = this.engine.toggleFxUnit(fxId);
        (e.currentTarget as HTMLElement).classList.toggle('active', newState);
        const rack = this.container.querySelector(`#fx-unit-${fxId}`);
        rack?.classList.toggle('active', newState);
      });
    });

    this.container.querySelectorAll('.fx-type-select').forEach((sel) => {
      sel.addEventListener('change', (e) => {
        const select = e.target as HTMLSelectElement;
        const fxId = Number(select.dataset.fxId);
        const type = select.value as FXType;
        this.engine.setFxType(fxId, type);

        // Update knob labels for that FX unit
        const p1 = this.container.querySelector(`[data-control-id="fx_${fxId}_p1"] .knob-label`);
        const p2 = this.container.querySelector(`[data-control-id="fx_${fxId}_p2"] .knob-label`);
        const p3 = this.container.querySelector(`[data-control-id="fx_${fxId}_p3"] .knob-label`);
        if (p1) p1.textContent = type === 'delay' ? 'TIME' : type === 'reverb' ? 'SIZE' : type === 'flanger' ? 'SPEED' : 'FREQ';
        if (p2) p2.textContent = type === 'delay' ? 'FEED' : type === 'reverb' ? 'DAMP' : type === 'flanger' ? 'FEED' : 'RESO';
        if (p3) p3.textContent = type === 'delay' ? 'COLOR' : type === 'reverb' ? 'COLOR' : type === 'flanger' ? 'DEPTH' : 'STUTTER';
      });
    });

    // Attach Deck Events
    ['A', 'B', 'C', 'D'].forEach((d) => {
      const deckId = d as DeckId;
      this.attachDeckEvents(deckId);
    });

    // Local Audio Upload
    const localUpload = this.container.querySelector('#local-audio-upload') as HTMLInputElement;
    localUpload?.addEventListener('change', async (e) => {
      const files = (e.target as HTMLInputElement).files;
      if (!files || files.length === 0) return;
      await this.handleLocalFiles(Array.from(files));
    });

    // Export Analysis Backup (.traktor.json)
    this.container.querySelector('#btn-export-backup')?.addEventListener('click', () => {
      const backup = BeatAnalyzer.exportAnalysisBackup(this.tracks);
      const jsonStr = JSON.stringify(backup, null, 2);
      const blob = new Blob([jsonStr], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `traktor_library_${Date.now()}.traktor.json`;
      a.click();
      URL.revokeObjectURL(url);
      this.showToast('Analysis backup (.traktor.json) exported successfully!');
    });

    // Restore Analysis Backup
    const restoreInput = this.container.querySelector('#restore-backup-input') as HTMLInputElement;
    restoreInput?.addEventListener('change', async (e) => {
      const file = (e.target as HTMLInputElement).files?.[0];
      if (!file) return;
      const text = await file.text();
      try {
        const backup = JSON.parse(text);
        const { tracks } = BeatAnalyzer.applyAnalysisBackup(this.tracks, backup);
        this.tracks = tracks;
        this.showToast('Restored hotcues and beatgrids from backup!');
        this.render();
      } catch (err) {
        console.error('Invalid analysis backup file:', err);
      }
    });

    // Open Google Drive Picker
    this.container.querySelector('#btn-open-drive')?.addEventListener('click', async () => {
      this.openDriveModal();
    });

    // Browser Search Input
    const searchInput = this.container.querySelector('#browser-search') as HTMLInputElement;
    searchInput?.addEventListener('input', (e) => {
      this.currentFilter = (e.target as HTMLInputElement).value;
      const tableContainer = this.container.querySelector('.track-table-container');
      if (tableContainer) {
        tableContainer.innerHTML = this.renderTableOnly();
        this.attachTableEvents();
      }
    });

    // Crate Tabs
    this.container.querySelectorAll('.crate-tabs .tab-btn').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const target = e.currentTarget as HTMLElement;
        this.activeCrateTab = target.dataset.crate as any;
        this.container.querySelectorAll('.crate-tabs .tab-btn').forEach((b) => b.classList.remove('active'));
        target.classList.add('active');
        const tableContainer = this.container.querySelector('.track-table-container');
        if (tableContainer) {
          tableContainer.innerHTML = this.renderTableOnly();
          this.attachTableEvents();
        }
      });
    });

    this.attachTableEvents();
  }

  private toggleLibrary(open?: boolean): void {
    this.isLibraryOpen = open !== undefined ? open : !this.isLibraryOpen;
    const drawer = this.container.querySelector('#browser-drawer');
    const topBtn = this.container.querySelector('#btn-toggle-library');
    drawer?.classList.toggle('open', this.isLibraryOpen);
    topBtn?.classList.toggle('active', this.isLibraryOpen);
  }

  private attachDeckEvents(deckId: DeckId): void {
    const deckElem = this.container.querySelector(`#deck-${deckId}`);
    if (!deckElem) return;

    deckElem.querySelector('.btn-play')?.addEventListener('click', () => {
      this.engine.togglePlay(deckId);
      const isPlaying = this.engine.getDeckState(deckId).isPlaying;
      const playBtn = deckElem.querySelector('.btn-play');
      playBtn?.classList.toggle('playing', isPlaying);
      const icon = playBtn?.querySelector('.material-symbols-outlined');
      if (icon) icon.textContent = isPlaying ? 'pause' : 'play_arrow';
    });

    deckElem.querySelector('.btn-cue')?.addEventListener('click', () => {
      this.engine.cue(deckId);
    });

    deckElem.querySelector('.btn-cup')?.addEventListener('click', () => {
      this.engine.cup(deckId);
    });

    deckElem.querySelector('.btn-meta-sync')?.addEventListener('click', () => {
      this.engine.sync(deckId);
      const isSync = this.engine.getDeckState(deckId).sync;
      deckElem.querySelector('.btn-meta-sync')?.classList.toggle('active', isSync);
    });

    deckElem.querySelector('.btn-meta-master')?.addEventListener('click', () => {
      this.engine.getDeckState(deckId).master = !this.engine.getDeckState(deckId).master;
      deckElem.querySelector('.btn-meta-master')?.classList.toggle('active', this.engine.getDeckState(deckId).master);
    });

    deckElem.querySelector('.btn-flux')?.addEventListener('click', () => {
      this.engine.toggleFlux(deckId);
      const isFlux = this.engine.getDeckState(deckId).fluxMode;
      deckElem.querySelector('.btn-flux')?.classList.toggle('active', isFlux);
    });

    // Waveform Zoom Controls
    deckElem.querySelector('.btn-zoom-in')?.addEventListener('click', () => {
      this.stepZoom(deckId, -1);
    });
    deckElem.querySelector('.btn-zoom-out')?.addEventListener('click', () => {
      this.stepZoom(deckId, 1);
    });

    // Waveform container mouse wheel zoom
    const zoomContainer = deckElem.querySelector('.zoomed-waveform-container');
    zoomContainer?.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        const wheelEvt = e as WheelEvent;
        if (wheelEvt.deltaY < 0) {
          this.stepZoom(deckId, -1); // Zoom in
        } else {
          this.stepZoom(deckId, 1); // Zoom out
        }
      },
      { passive: false }
    );

    // Loop controls
    deckElem.querySelector('.btn-loop-active')?.addEventListener('click', () => {
      this.engine.toggleLoop(deckId);
      const active = this.engine.getDeckState(deckId).loopActive;
      deckElem.querySelector('.btn-loop-active')?.classList.toggle('active', active);
    });

    deckElem.querySelector('.btn-loop-double')?.addEventListener('click', () => {
      const len = this.engine.getDeckState(deckId).loopLength * 2;
      this.engine.setLoopLength(deckId, Math.min(32, len));
      const btn = deckElem.querySelector('.btn-loop-active');
      if (btn) btn.textContent = `LOOP ${this.engine.getDeckState(deckId).loopLength}`;
    });

    deckElem.querySelector('.btn-loop-half')?.addEventListener('click', () => {
      const len = this.engine.getDeckState(deckId).loopLength / 2;
      this.engine.setLoopLength(deckId, Math.max(0.0625, len));
      const btn = deckElem.querySelector('.btn-loop-active');
      if (btn) btn.textContent = `LOOP ${this.engine.getDeckState(deckId).loopLength}`;
    });

    deckElem.querySelectorAll('.btn-jump').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const jumpBeats = Number((e.currentTarget as HTMLElement).dataset.jump);
        this.engine.beatjump(deckId, jumpBeats);
      });
    });

    deckElem.querySelectorAll('.hotcue-pad').forEach((pad) => {
      pad.addEventListener('click', (e) => {
        const cueNum = Number((e.currentTarget as HTMLElement).dataset.cue);
        if ((e as MouseEvent).shiftKey) {
          this.engine.deleteHotcue(deckId, cueNum);
        } else {
          this.engine.triggerHotcue(deckId, cueNum);
        }
        const track = this.engine.getDeckState(deckId).track;
        const exists = track?.hotcues.some((h) => h.id === cueNum);
        (e.currentTarget as HTMLElement).classList.toggle('set', Boolean(exists));
      });
    });

    deckElem.querySelectorAll('.btn-nudge').forEach((btn) => {
      btn.addEventListener('mousedown', (e) => {
        const dir = (e.currentTarget as HTMLElement).dataset.nudge === 'up' ? 'nudge_up' : 'nudge_down';
        this.engine.pitchBend(deckId, dir);
      });
      btn.addEventListener('mouseup', () => {
        this.engine.pitchBend(deckId, 'release');
      });
      btn.addEventListener('mouseleave', () => {
        this.engine.pitchBend(deckId, 'release');
      });
    });

    // Mixer section Channel connections for this deck
    const mixerChassis = this.container.querySelector(`.channel-${deckId.toLowerCase()}`);
    if (mixerChassis) {
      mixerChassis.querySelectorAll('.btn-fx-assign').forEach((btn) => {
        btn.addEventListener('click', (e) => {
          const fxUnitId = Number((e.currentTarget as HTMLElement).dataset.fx);
          this.engine.toggleFxAssign(deckId, fxUnitId);
          const state = this.engine.getDeckState(deckId).fxAssign[fxUnitId - 1];
          (e.currentTarget as HTMLElement).classList.toggle('active', state);
        });
      });

      mixerChassis.querySelector('.btn-cue-pfl')?.addEventListener('click', (e) => {
        const current = this.engine.getDeckState(deckId).cuePfl;
        this.engine.setCuePfl(deckId, !current);
        (e.currentTarget as HTMLElement).classList.toggle('active', !current);
      });

      mixerChassis.querySelectorAll('.btn-assign').forEach((btn) => {
        btn.addEventListener('click', (e) => {
          const assign = (e.currentTarget as HTMLElement).dataset.assign as 'A' | 'B' | 'Thru';
          this.engine.setCrossfaderAssign(deckId, assign);
          mixerChassis.querySelectorAll('.btn-assign').forEach((b) => b.classList.remove('active'));
          (e.currentTarget as HTMLElement).classList.add('active');
        });
      });
    }

    // Stripe waveform seeking
    const stripeContainer = deckElem.querySelector('.stripe-waveform-container');
    stripeContainer?.addEventListener('click', (e) => {
      const rect = stripeContainer.getBoundingClientRect();
      const clickX = (e as MouseEvent).clientX - rect.left;
      const ratio = Math.max(0, Math.min(1, clickX / rect.width));
      const track = this.engine.getDeckState(deckId).track;
      if (track) {
        this.engine.seek(deckId, ratio * track.duration);
      }
    });
  }

  private stepZoom(deckId: DeckId, direction: -1 | 1): void {
    const current = this.deckZoomBeats.get(deckId) || 8;
    const currentIdx = ZOOM_STEPS.indexOf(current);
    const newIdx = Math.max(0, Math.min(ZOOM_STEPS.length - 1, (currentIdx >= 0 ? currentIdx : 4) + direction));
    const newZoom = ZOOM_STEPS[newIdx];
    this.deckZoomBeats.set(deckId, newZoom);

    const badge = this.container.querySelector(`#zoom-readout-${deckId}`);
    if (badge) {
      badge.textContent = `ZOOM: ${newZoom}B`;
    }
  }

  private attachTableEvents(): void {
    this.container.querySelectorAll('.btn-load-deck').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const target = e.currentTarget as HTMLElement;
        const deckId = target.dataset.deck as DeckId;
        const trackId = target.dataset.trackId;
        const track = this.tracks.find((t) => t.id === trackId);
        if (track) {
          this.engine.loadTrack(deckId, track);
          const deckBay = this.container.querySelector(`#deck-${deckId}`);
          if (deckBay) {
            deckBay.innerHTML = this.renderDeck(deckId);
            this.attachDeckEvents(deckId);
            this.setupKnobInteractions();
            this.setupFaderInteractions();

            // Explicitly re-acquire canvas references so waveform rendering is instantly alive
            const newZoomed = document.getElementById(`zoomed-waveform-${deckId}`) as HTMLCanvasElement;
            const newStripe = document.getElementById(`stripe-waveform-${deckId}`) as HTMLCanvasElement;
            if (newZoomed) this.zoomedCanvases.set(deckId, newZoomed);
            if (newStripe) this.stripeCanvases.set(deckId, newStripe);
          }
          this.showToast(`Loaded "${track.title}" into Deck ${deckId}`);
        }
      });
    });
  }

  private attachDdjModalEvents(): void {
    this.container.querySelector('#btn-close-ddj-modal')?.addEventListener('click', () => {
      this.isDdjModalOpen = false;
      this.container.querySelector('#ddj-modal')?.classList.remove('active');
    });

    this.container.querySelector('#btn-rescan-midi')?.addEventListener('click', async () => {
      await this.midiService.requestMidi();
      const modal = this.container.querySelector('#ddj-modal');
      if (modal) modal.innerHTML = this.renderDdjModal();
      this.attachDdjModalEvents();
    });

    // Hardware Simulator Buttons
    this.container.querySelectorAll('.btn-emu').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const action = (e.currentTarget as HTMLElement).dataset.emu;
        if (action === 'play_a') {
          this.midiService.simulateMidiMessage(1, 'NoteOn', 11, 127);
        } else if (action === 'cue_a') {
          this.midiService.simulateMidiMessage(1, 'NoteOn', 12, 127);
        } else if (action === 'jog_a') {
          this.midiService.simulateMidiMessage(1, 'CC', 33, 68); // nudge up
        } else if (action === 'play_b') {
          this.midiService.simulateMidiMessage(2, 'NoteOn', 11, 127);
        } else if (action === 'sync_b') {
          this.midiService.simulateMidiMessage(2, 'NoteOn', 88, 127);
        } else if (action === 'xfade_center') {
          this.midiService.simulateMidiMessage(1, 'CC', 31, 64);
        }
      });
    });
  }

  private updateMidiPillUi(): void {
    const pill = this.container.querySelector('#btn-ddj-t1');
    const dot = this.container.querySelector('#top-ddj-led');
    if (!pill || !dot) return;

    const isConn = this.midiStatus.connected;
    const isPioneer = this.midiStatus.isPioneerDDJT1;
    pill.classList.toggle('connected', isConn);
    dot.classList.toggle('online', isConn);

    const titleTxt = pill.querySelector('.ddj-title-txt');
    const badgeTxt = pill.querySelector('.ddj-badge');
    if (titleTxt) {
      titleTxt.textContent = isPioneer ? 'PIONEER DDJ-T1' : isConn ? this.midiStatus.deviceName.slice(0, 16) : 'PIONEER DDJ-T1';
    }
    if (badgeTxt) {
      badgeTxt.textContent = isConn ? 'CONNECTED' : 'STANDBY';
    }
  }

  private flashMidiLed(): void {
    const dot = this.container.querySelector('#top-ddj-led');
    if (!dot) return;
    dot.classList.add('flash');
    if (this.midiLedTimer) clearTimeout(this.midiLedTimer);
    this.midiLedTimer = setTimeout(() => {
      dot.classList.remove('flash');
    }, 90);
  }

  private updateMidiLogUi(): void {
    const out = this.container.querySelector('#midi-log-output');
    if (out) {
      out.innerHTML = this.recentMidiLogs.join('<br>');
    }
  }

  private syncUiFromMidi(detail: { channel: number; type: string; data1: number; data2: number }): void {
    // Crossfader: CC 31
    if (detail.type === 'CC' && (detail.data1 === 31 || detail.data1 === 10)) {
      const norm = detail.data2 / 127;
      const cap = this.container.querySelector('#crossfader-cap') as HTMLElement;
      if (cap) cap.style.left = `${norm * 100}%`;
    }

    // Channel Volume: CC 19 / CC 7
    if (detail.type === 'CC' && (detail.data1 === 19 || detail.data1 === 7)) {
      const deckId: DeckId = detail.channel === 1 ? 'A' : detail.channel === 2 ? 'B' : detail.channel === 3 ? 'C' : 'D';
      const norm = detail.data2 / 127;
      const cap = this.container.querySelector(`#vol-cap-${deckId}`) as HTMLElement;
      if (cap) cap.style.bottom = `${norm * 100}%`;
    }

    // Play button sync
    if (detail.type === 'NoteOn' && (detail.data1 === 11 || detail.data1 === 0)) {
      const deckId: DeckId = detail.channel === 1 ? 'A' : detail.channel === 2 ? 'B' : detail.channel === 3 ? 'C' : 'D';
      const deck = this.engine.getDeckState(deckId);
      const playBtn = this.container.querySelector(`#deck-${deckId} .btn-play`);
      playBtn?.classList.toggle('playing', deck.isPlaying);
      const icon = playBtn?.querySelector('.material-symbols-outlined');
      if (icon) icon.textContent = deck.isPlaying ? 'pause' : 'play_arrow';
    }
  }

  private attachDrawerEvents(): void {
    this.container.querySelector('#btn-add-task')?.addEventListener('click', async () => {
      const input = this.container.querySelector('#new-task-input') as HTMLInputElement;
      if (!input || !input.value.trim()) return;
      const task = await createGoogleTask(input.value.trim());
      this.tasksList.unshift(task);
      input.value = '';
      const list = this.container.querySelector('.tasks-list');
      if (list) {
        list.innerHTML = this.tasksList
          .map(
            (t) => `
          <div class="task-item ${t.status === 'completed' ? 'completed' : ''}" data-task-id="${t.id}">
            <input type="checkbox" class="task-checkbox" data-task-id="${t.id}" ${t.status === 'completed' ? 'checked' : ''} />
            <span class="task-title">${t.title}</span>
            <button class="btn-delete-task" data-task-id="${t.id}" title="Delete task">✕</button>
          </div>
        `
          )
          .join('');
        this.attachDrawerEvents();
      }
    });

    this.container.querySelectorAll('.task-checkbox').forEach((box) => {
      box.addEventListener('change', async (e) => {
        const target = e.target as HTMLInputElement;
        const taskId = target.dataset.taskId!;
        const status = target.checked ? 'completed' : 'needsAction';
        await updateGoogleTaskStatus(taskId, status);
        const item = this.container.querySelector(`.task-item[data-task-id="${taskId}"]`);
        item?.classList.toggle('completed', target.checked);
      });
    });

    this.container.querySelectorAll('.btn-delete-task').forEach((btn) => {
      btn.addEventListener('click', async (e) => {
        const taskId = (e.currentTarget as HTMLElement).dataset.taskId!;
        const confirmDelete = window.confirm('Delete this gig preparation task?');
        if (!confirmDelete) return;
        await deleteGoogleTask(taskId);
        this.tasksList = this.tasksList.filter((t) => t.id !== taskId);
        this.container.querySelector(`.task-item[data-task-id="${taskId}"]`)?.remove();
      });
    });

    this.container.querySelector('#btn-save-setlist')?.addEventListener('click', () => {
      const titleInput = this.container.querySelector('#new-setlist-title') as HTMLInputElement;
      const contentInput = this.container.querySelector('#new-setlist-content') as HTMLTextAreaElement;
      if (!titleInput || !contentInput || !titleInput.value.trim()) return;

      const newNote = saveSetlistNote(titleInput.value.trim(), contentInput.value.trim());
      this.setlistsList.unshift(newNote);
      titleInput.value = '';
      contentInput.value = '';

      const list = this.container.querySelector('.setlists-list');
      if (list) {
        list.innerHTML = this.setlistsList
          .map(
            (note) => `
          <div class="setlist-card" data-note-id="${note.id}">
            <div class="setlist-header">
              <h4>${note.title}</h4>
              <span class="setlist-date">${note.updatedAt}</span>
              <button class="btn-delete-note" data-note-id="${note.id}">✕</button>
            </div>
            <pre class="setlist-content">${note.content}</pre>
          </div>
        `
          )
          .join('');
        this.attachDrawerEvents();
      }
    });

    this.container.querySelectorAll('.btn-delete-note').forEach((btn) => {
      btn.addEventListener('click', (e) => {
        const noteId = (e.currentTarget as HTMLElement).dataset.noteId!;
        deleteSetlistNote(noteId);
        this.setlistsList = this.setlistsList.filter((n) => n.id !== noteId);
        this.container.querySelector(`.setlist-card[data-note-id="${noteId}"]`)?.remove();
      });
    });
  }

  private async openDriveModal(): Promise<void> {
    this.isDriveModalOpen = true;
    this.isDriveLoading = true;
    const modal = this.container.querySelector('#drive-modal');
    if (modal) {
      modal.classList.add('active');
      modal.innerHTML = this.renderDriveModal();
    }

    try {
      this.driveFiles = await listDriveAudioFiles();
    } catch (err) {
      console.warn('Drive list files error:', err);
      this.driveFiles = [];
    } finally {
      this.isDriveLoading = false;
      if (modal) {
        modal.innerHTML = this.renderDriveModal();
        this.attachDriveModalEvents();
      }
    }
  }

  private attachDriveModalEvents(): void {
    this.container.querySelector('#btn-close-drive-modal')?.addEventListener('click', () => {
      this.isDriveModalOpen = false;
      this.container.querySelector('#drive-modal')?.classList.remove('active');
    });

    this.container.querySelector('#btn-drive-refresh')?.addEventListener('click', () => {
      this.openDriveModal();
    });

    this.container.querySelectorAll('.btn-import-drive').forEach((btn) => {
      btn.addEventListener('click', async (e) => {
        const target = e.currentTarget as HTMLElement;
        const fileId = target.dataset.fileId!;
        const fileName = target.dataset.fileName!;
        target.textContent = 'Importing...';

        try {
          const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
          const ctx = new AudioCtx();
          const track = await downloadDriveAudioFile(fileId, fileName, ctx);
          this.tracks.unshift(track);
          this.isDriveModalOpen = false;
          this.container.querySelector('#drive-modal')?.classList.remove('active');
          this.showToast(`Imported "${track.title}" from Google Drive!`);
          this.render();
        } catch (err) {
          console.error('Import drive file failed:', err);
          target.textContent = 'Failed';
        }
      });
    });
  }

  private async handleLocalFiles(files: File[]): Promise<void> {
    const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
    const ctx = new AudioCtx();

    for (const file of files) {
      try {
        const arrayBuf = await file.arrayBuffer();
        const audioBuf = await ctx.decodeAudioData(arrayBuf);
        const track = BeatAnalyzer.analyzeTrack(file, audioBuf, 'local');
        this.tracks.unshift(track);
      } catch (err) {
        console.error('Failed to decode local audio:', file.name, err);
      }
    }

    this.showToast(`Imported & analyzed ${files.length} audio track(s)!`);
    this.render();
  }

  private renderTableOnly(): string {
    const filtered = this.tracks.filter((t) => {
      const matchFilter =
        this.currentFilter === '' ||
        t.title.toLowerCase().includes(this.currentFilter.toLowerCase()) ||
        t.artist.toLowerCase().includes(this.currentFilter.toLowerCase()) ||
        t.key.toLowerCase().includes(this.currentFilter.toLowerCase());

      if (this.activeCrateTab === 'demo') return matchFilter && t.source === 'demo';
      if (this.activeCrateTab === 'local') return matchFilter && t.source === 'local';
      if (this.activeCrateTab === 'drive') return matchFilter && t.source === 'drive';
      return matchFilter;
    });

    return `
      <table class="track-table">
        <thead>
          <tr>
            <th style="width: 140px;">Load Deck</th>
            <th>Title</th>
            <th>Artist</th>
            <th style="width: 90px; text-align: center;">BPM</th>
            <th style="width: 70px; text-align: center;">Key</th>
            <th style="width: 80px; text-align: center;">Time</th>
            <th style="width: 90px; text-align: center;">Hotcues</th>
            <th style="width: 80px; text-align: center;">Source</th>
          </tr>
        </thead>
        <tbody>
          ${
            filtered.length === 0
              ? `
            <tr>
              <td colspan="8" class="empty-table-cell">
                <div class="empty-state">
                  <p>No tracks match your query. Upload local MP3/WAV tracks or import from Google Drive!</p>
                </div>
              </td>
            </tr>
          `
              : filtered
                  .map(
                    (t) => `
            <tr class="track-row" data-track-id="${t.id}">
              <td class="load-actions-cell">
                <button class="btn-load-deck btn-load-a" data-deck="A" data-track-id="${t.id}">LOAD A</button>
                <button class="btn-load-deck btn-load-b" data-deck="B" data-track-id="${t.id}">LOAD B</button>
                ${
                  this.engine.getMixerState().layoutMode === '4decks'
                    ? `
                  <button class="btn-load-deck btn-load-c" data-deck="C" data-track-id="${t.id}">C</button>
                  <button class="btn-load-deck btn-load-d" data-deck="D" data-track-id="${t.id}">D</button>
                `
                    : ''
                }
              </td>
              <td class="font-bold text-white">${t.title}</td>
              <td class="text-slate-400">${t.artist}</td>
              <td class="font-mono tabular-nums text-cyan-400 text-center">${t.bpm.toFixed(2)}</td>
              <td class="text-center"><span class="camelot-pill">${t.key}</span></td>
              <td class="font-mono tabular-nums text-slate-300 text-center">${this.formatTime(t.duration)}</td>
              <td class="text-center"><span class="hotcue-count-badge">${t.hotcues.length} Cues</span></td>
              <td class="text-center"><span class="source-tag source-${t.source}">${t.source.toUpperCase()}</span></td>
            </tr>
          `
                  )
                  .join('')
          }
        </tbody>
      </table>
    `;
  }

  private updateMasterBpmDisplay(): void {
    const readout = this.container.querySelector('#master-bpm-readout');
    if (readout) {
      readout.textContent = this.engine.getMixerState().masterBpm.toFixed(2);
    }
  }

  private showToast(msg: string): void {
    this.toastMessage = msg;
    const banner = this.container.querySelector('#toast-banner');
    if (banner) {
      const txt = banner.querySelector('.toast-txt');
      if (txt) txt.textContent = msg;
      banner.classList.add('visible');
    }
    if (this.toastTimer) clearTimeout(this.toastTimer);
    this.toastTimer = setTimeout(() => {
      this.container.querySelector('#toast-banner')?.classList.remove('visible');
      this.toastMessage = null;
    }, 2800);
  }

  private setupGlobalShortcuts(): void {
    window.addEventListener('keydown', (e) => {
      if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement).tagName)) return;

      if (e.code === 'Space') {
        e.preventDefault();
        this.toggleLibrary();
      } else if (e.code === 'KeyM') {
        e.preventDefault();
        this.copilotDrawer?.toggle();
      } else if (e.code === 'Escape') {
        if (this.copilotDrawer?.getIsOpen()) {
          this.copilotDrawer.toggle(false);
        }
        if (this.isLibraryOpen) this.toggleLibrary(false);
        if (this.isDdjModalOpen) {
          this.isDdjModalOpen = false;
          this.container.querySelector('#ddj-modal')?.classList.remove('active');
        }
      } else if (e.code === 'KeyA') {
        this.engine.togglePlay('A');
      } else if (e.code === 'KeyB') {
        this.engine.togglePlay('B');
      } else if (e.code === 'KeyC') {
        this.engine.cue('A');
      } else if (e.code === 'KeyS') {
        this.engine.sync('A');
      } else if (e.code === 'Digit1') {
        this.engine.triggerHotcue('A', 1);
      } else if (e.code === 'Digit2') {
        this.engine.triggerHotcue('A', 2);
      } else if (e.code === 'Digit3') {
        this.engine.triggerHotcue('A', 3);
      } else if (e.code === 'Digit4') {
        this.engine.triggerHotcue('A', 4);
      }
    });
  }

  private formatTime(sec: number): string {
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    const ms = Math.floor((sec % 1) * 100);
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}.${ms.toString().padStart(2, '0')}`;
  }

  public dispose(): void {
    if (this.animFrameId) {
      cancelAnimationFrame(this.animFrameId);
    }
  }
}
