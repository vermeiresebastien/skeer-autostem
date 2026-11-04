import * as THREE from 'three';
import * as xb from 'xrblocks';
import { AudioEngine } from '../services/audioEngine';

export class SpatialXRBooth extends xb.Script {
  private engine: AudioEngine;
  private turntableA!: THREE.Group;
  private turntableB!: THREE.Group;
  private vinylPlatterA!: THREE.Mesh;
  private vinylPlatterB!: THREE.Mesh;
  private faderA!: THREE.Mesh;
  private faderB!: THREE.Mesh;
  private crossfaderMesh!: THREE.Mesh;
  private waveformMeshA!: THREE.Mesh;
  private waveformMeshB!: THREE.Mesh;
  private clubLight!: THREE.PointLight;
  private onReturnTo2D?: () => void;

  constructor(onReturnTo2D?: () => void) {
    super();
    this.engine = AudioEngine.getInstance();
    this.onReturnTo2D = onReturnTo2D;
  }

  public init(): void {
    // Stage Lighting
    this.add(new THREE.HemisphereLight(0x334466, 0x111122, 2.5));

    const spotLight = new THREE.SpotLight(0x00c3ff, 4, 10, Math.PI / 4, 0.4);
    spotLight.position.set(-1, 3, -1);
    this.add(spotLight);

    const spotLightB = new THREE.SpotLight(0xff9900, 4, 10, Math.PI / 4, 0.4);
    spotLightB.position.set(1, 3, -1);
    this.add(spotLightB);

    this.clubLight = new THREE.PointLight(0xbd00ff, 2, 8);
    this.clubLight.position.set(0, 2.2, -1.5);
    this.add(this.clubLight);

    // Build the 3D DJ Rig Table & Console
    const consoleGroup = new THREE.Group();
    consoleGroup.position.set(0, 0.95, -1.2);
    this.add(consoleGroup);

    // Table Stand
    const tableGeo = new THREE.BoxGeometry(1.6, 0.08, 0.8);
    const tableMat = new THREE.MeshStandardMaterial({
      color: 0x151821,
      metalness: 0.8,
      roughness: 0.25,
    });
    const tableMesh = new THREE.Mesh(tableGeo, tableMat);
    consoleGroup.add(tableMesh);

    // Illuminated Neon Trim
    const trimGeo = new THREE.BoxGeometry(1.62, 0.02, 0.82);
    const trimMat = new THREE.MeshBasicMaterial({ color: 0x00c3ff });
    const trimMesh = new THREE.Mesh(trimGeo, trimMat);
    trimMesh.position.y = -0.04;
    consoleGroup.add(trimMesh);

    // Build Turntable Deck A (Left)
    this.turntableA = this.createTurntable(0x00c3ff, 'A');
    this.turntableA.position.set(-0.48, 0.06, 0);
    consoleGroup.add(this.turntableA);

    // Build Turntable Deck B (Right)
    this.turntableB = this.createTurntable(0xff9900, 'B');
    this.turntableB.position.set(0.48, 0.06, 0);
    consoleGroup.add(this.turntableB);

    // Build Central 3D Mixer Section
    const mixerGroup = this.create3DMixer();
    mixerGroup.position.set(0, 0.06, 0);
    consoleGroup.add(mixerGroup);

    // Floating 3D Curved Waveform Panels
    this.waveformMeshA = this.createWaveformPanel(0x00c3ff);
    this.waveformMeshA.position.set(-0.48, 0.42, -0.2);
    consoleGroup.add(this.waveformMeshA);

    this.waveformMeshB = this.createWaveformPanel(0xff9900);
    this.waveformMeshB.position.set(0.48, 0.42, -0.2);
    consoleGroup.add(this.waveformMeshB);

    // Floating Spatial UI Card (XR Blocks)
    this.createSpatialHUD();
  }

  private createTurntable(accentHex: number, deckId: 'A' | 'B'): THREE.Group {
    const group = new THREE.Group();

    // Chassis Box
    const baseGeo = new THREE.BoxGeometry(0.42, 0.04, 0.42);
    const baseMat = new THREE.MeshStandardMaterial({
      color: 0x1e222d,
      metalness: 0.7,
      roughness: 0.3,
    });
    const base = new THREE.Mesh(baseGeo, baseMat);
    group.add(base);

    // Circular Platter Well
    const platterWellGeo = new THREE.CylinderGeometry(0.18, 0.18, 0.015, 36);
    const platterWellMat = new THREE.MeshStandardMaterial({ color: 0x0d0e12 });
    const platterWell = new THREE.Mesh(platterWellGeo, platterWellMat);
    platterWell.position.y = 0.025;
    group.add(platterWell);

    // Vinyl Platter
    const vinylGeo = new THREE.CylinderGeometry(0.165, 0.165, 0.012, 36);
    const vinylMat = new THREE.MeshStandardMaterial({
      color: 0x050507,
      roughness: 0.2,
      metalness: 0.9,
    });
    const vinyl = new THREE.Mesh(vinylGeo, vinylMat);
    vinyl.position.y = 0.032;

    // Center Label
    const labelGeo = new THREE.CylinderGeometry(0.06, 0.06, 0.014, 32);
    const labelMat = new THREE.MeshBasicMaterial({ color: accentHex });
    const label = new THREE.Mesh(labelGeo, labelMat);
    vinyl.add(label);

    // Strobe Marker on Vinyl Edge
    const dotGeo = new THREE.BoxGeometry(0.012, 0.015, 0.02);
    const dotMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
    const dot = new THREE.Mesh(dotGeo, dotMat);
    dot.position.set(0.14, 0.002, 0);
    vinyl.add(dot);

    // Tonearm
    const armPivot = new THREE.Group();
    armPivot.position.set(0.16, 0.03, -0.15);
    const armGeo = new THREE.CylinderGeometry(0.004, 0.004, 0.22, 16);
    const armMat = new THREE.MeshStandardMaterial({ color: 0xaaaaaa, metalness: 0.9 });
    const arm = new THREE.Mesh(armGeo, armMat);
    arm.rotation.x = Math.PI / 2;
    arm.position.set(-0.06, 0, 0.08);
    armPivot.add(arm);
    group.add(armPivot);

    group.add(vinyl);

    if (deckId === 'A') {
      this.vinylPlatterA = vinyl;
    } else {
      this.vinylPlatterB = vinyl;
    }

    return group;
  }

  private create3DMixer(): THREE.Group {
    const group = new THREE.Group();

    // Mixer Faceplate
    const faceGeo = new THREE.BoxGeometry(0.38, 0.045, 0.48);
    const faceMat = new THREE.MeshStandardMaterial({
      color: 0x111318,
      metalness: 0.85,
      roughness: 0.2,
    });
    const face = new THREE.Mesh(faceGeo, faceMat);
    group.add(face);

    // Knobs (EQs & Filter for Ch A and Ch B)
    const knobPositions = [-0.1, 0.1];
    knobPositions.forEach((x, colIdx) => {
      const colColor = colIdx === 0 ? 0x00c3ff : 0xff9900;
      // 4 Knobs per column: High, Mid, Low, Filter
      for (let k = 0; k < 4; k++) {
        const knobGeo = new THREE.CylinderGeometry(0.016, 0.016, 0.022, 24);
        const knobMat = new THREE.MeshStandardMaterial({
          color: k === 3 ? colColor : 0x222630,
          metalness: 0.5,
          roughness: 0.3,
        });
        const knob = new THREE.Mesh(knobGeo, knobMat);
        knob.position.set(x, 0.035, -0.16 + k * 0.065);
        group.add(knob);
      }
    });

    // Channel Volume Faders
    const faderGeo = new THREE.BoxGeometry(0.022, 0.02, 0.035);
    const faderMat = new THREE.MeshStandardMaterial({ color: 0xeeeeee });

    this.faderA = new THREE.Mesh(faderGeo, faderMat);
    this.faderA.position.set(-0.1, 0.035, 0.12);
    group.add(this.faderA);

    this.faderB = new THREE.Mesh(faderGeo, faderMat);
    this.faderB.position.set(0.1, 0.035, 0.12);
    group.add(this.faderB);

    // Crossfader
    const xfadeGeo = new THREE.BoxGeometry(0.035, 0.02, 0.022);
    const xfadeMat = new THREE.MeshStandardMaterial({ color: 0xffffff });
    this.crossfaderMesh = new THREE.Mesh(xfadeGeo, xfadeMat);
    this.crossfaderMesh.position.set(0, 0.035, 0.2);
    group.add(this.crossfaderMesh);

    return group;
  }

  private createWaveformPanel(colorHex: number): THREE.Mesh {
    const geo = new THREE.PlaneGeometry(0.42, 0.18, 32, 1);
    const mat = new THREE.MeshBasicMaterial({
      color: colorHex,
      wireframe: true,
      transparent: true,
      opacity: 0.85,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.rotation.x = -Math.PI / 8;
    return mesh;
  }

  private createSpatialHUD(): void {
    const card = new xb.UICard({
      size: { width: 0.72, height: 'auto' },
      manipulation: true,
      edge: true,
      style: {
        flexDirection: 'column',
        gap: 12,
        padding: 16,
        backgroundColor: '#0d1117cc',
      },
      children: [
        new xb.UIText({
          text: 'TRAKTOR 3D SPATIAL RIG',
          style: { fontSize: 24, fontWeight: 'bold', textAlign: 'center' },
        }),
        new xb.UIText({
          text: 'Turntables rotate with live track audio. Play, pause, or switch back to the full 2D console below.',
          style: { fontSize: 15, lineHeight: 1.35, textAlign: 'center', opacity: 0.8 },
        }),
        new xb.UIPanel({
          style: { width: '100%', flexDirection: 'row', gap: 10, justifyContent: 'center' },
          children: [
            new xb.UIButton({
              label: 'Deck A Play/Pause',
              icon: 'play_arrow',
              style: { flexGrow: 1 },
              onClick: () => {
                this.engine.togglePlay('A');
              },
            }),
            new xb.UIButton({
              label: 'Deck B Play/Pause',
              icon: 'play_arrow',
              style: { flexGrow: 1 },
              onClick: () => {
                this.engine.togglePlay('B');
              },
            }),
          ],
        }),
        new xb.UIButton({
          label: 'Return to 2D Pro Console',
          icon: 'dashboard',
          style: { width: '100%', backgroundColor: '#00c3ff' },
          onClick: () => {
            this.onReturnTo2D?.();
          },
        }),
      ],
    });

    card.position.set(0, 1.6, -1.35);
    this.add(card);
  }

  public update(): void {
    const deckA = this.engine.getDeckState('A');
    const deckB = this.engine.getDeckState('B');

    // Rotate platters when playing (33.3 RPM = ~3.49 rad/sec)
    if (deckA.isPlaying && this.vinylPlatterA) {
      this.vinylPlatterA.rotation.y += 0.05 * (1.0 + deckA.pitchPercent / 100);
    }
    if (deckB.isPlaying && this.vinylPlatterB) {
      this.vinylPlatterB.rotation.y += 0.05 * (1.0 + deckB.pitchPercent / 100);
    }

    // Dynamic Club Lighting pulsed to Master VU
    const masterVu = this.engine.getMasterVuLevels();
    const peak = Math.max(masterVu.left, masterVu.right);
    if (this.clubLight) {
      this.clubLight.intensity = 1.0 + peak * 4.0;
    }

    // Update 3D Crossfader Position (-0.08 to +0.08m)
    const mixer = this.engine.getMixerState();
    if (this.crossfaderMesh) {
      this.crossfaderMesh.position.x = mixer.crossfader * 0.08;
    }
  }
}
