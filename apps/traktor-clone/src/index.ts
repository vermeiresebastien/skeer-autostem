import * as xb from 'xrblocks';
import { Console2D } from './components/Console2D';
import { SpatialXRBooth } from './components/SpatialXRBooth';
import { AudioEngine } from './services/audioEngine';

class TraktorApp {
  private console2DContainer!: HTMLElement;
  private console2D!: Console2D;
  private booth!: SpatialXRBooth;
  private is3DActive = false;
  private xrOptions: xb.Options;

  constructor() {
    this.xrOptions = new xb.Options();
    this.xrOptions.setAppTitle('Traktor Pro Web DJ');
    this.xrOptions.setAppDescription(
      'Professional web DJ workstation with dual 2D hardware console and 3D WebXR spatial booth.'
    );
  }

  public async start(): Promise<void> {
    const root = document.getElementById('root');
    if (!root) return;

    // Create 2D Console container
    this.console2DContainer = document.createElement('div');
    this.console2DContainer.id = 'traktor-2d-container';
    this.console2DContainer.style.width = '100%';
    this.console2DContainer.style.height = '100%';
    root.appendChild(this.console2DContainer);

    // Initialize 2D Console
    this.console2D = new Console2D(this.console2DContainer, () => {
      this.switchTo3D();
    });
    await this.console2D.init();

    // Prepare 3D Spatial Booth
    this.booth = new SpatialXRBooth(() => {
      this.switchTo2D();
    });
    xb.add(this.booth);

    // Initialize XR Blocks
    await xb.init(this.xrOptions);

    // XR Blocks attaches a canvas to document.body or window.
    // By default, keep 2D console on top, 3D booth in background.
    this.setupDisplayLayering();
  }

  private setupDisplayLayering(): void {
    const canvas = document.querySelector('canvas') as HTMLCanvasElement;
    if (canvas) {
      canvas.style.position = 'fixed';
      canvas.style.top = '0';
      canvas.style.left = '0';
      canvas.style.width = '100%';
      canvas.style.height = '100%';
      canvas.style.zIndex = '1';
      canvas.style.display = 'none';
    }

    if (this.console2DContainer) {
      this.console2DContainer.style.position = 'relative';
      this.console2DContainer.style.zIndex = '10';
    }
  }

  public switchTo3D(): void {
    this.is3DActive = true;
    AudioEngine.getInstance().resume();

    // Show 3D Canvas
    const canvas = document.querySelector('canvas') as HTMLCanvasElement;
    if (canvas) {
      canvas.style.display = 'block';
      canvas.style.zIndex = '10';
    }

    // Hide 2D Console
    if (this.console2DContainer) {
      this.console2DContainer.style.display = 'none';
    }

    // Floating return button overlay
    let returnOverlay = document.getElementById('return-to-2d-overlay');
    if (!returnOverlay) {
      returnOverlay = document.createElement('div');
      returnOverlay.id = 'return-to-2d-overlay';
      returnOverlay.style.position = 'fixed';
      returnOverlay.style.top = '16px';
      returnOverlay.style.left = '16px';
      returnOverlay.style.zIndex = '100';
      returnOverlay.innerHTML = `
        <button id="btn-floating-return" style="
          display: flex;
          align-items: center;
          gap: 8px;
          background: #00c3ff;
          color: #000;
          font-weight: 800;
          font-size: 0.85rem;
          border: none;
          padding: 10px 18px;
          border-radius: 8px;
          cursor: pointer;
          box-shadow: 0 4px 18px rgba(0, 195, 255, 0.5);
        ">
          <span class="material-symbols-outlined" style="font-size: 1.1rem;">dashboard</span>
          <span>Return to 2D Pro Console</span>
        </button>
      `;
      document.body.appendChild(returnOverlay);

      document.getElementById('btn-floating-return')?.addEventListener('click', () => {
        this.switchTo2D();
      });
    } else {
      returnOverlay.style.display = 'block';
    }
  }

  public switchTo2D(): void {
    this.is3DActive = false;

    // Hide 3D Canvas
    const canvas = document.querySelector('canvas') as HTMLCanvasElement;
    if (canvas) {
      canvas.style.display = 'none';
      canvas.style.zIndex = '1';
    }

    // Show 2D Console
    if (this.console2DContainer) {
      this.console2DContainer.style.display = 'flex';
    }

    // Hide return overlay
    const returnOverlay = document.getElementById('return-to-2d-overlay');
    if (returnOverlay) {
      returnOverlay.style.display = 'none';
    }
  }
}

document.addEventListener('DOMContentLoaded', () => {
  const app = new TraktorApp();
  app.start().catch((err) => {
    console.error('Failed to start Traktor Pro Web DJ:', err);
  });
});
