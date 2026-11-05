import { initializeApp, getApps, getApp, FirebaseApp } from 'firebase/app';
import {
  getFirestore,
  Firestore,
  doc,
  setDoc,
  getDoc,
  collection,
  getDocs,
  deleteDoc,
  onSnapshot,
} from 'firebase/firestore';
import {
  getAuth,
  Auth,
  signInAnonymously,
  onAuthStateChanged,
  User,
} from 'firebase/auth';
import { TrackEntity, AnalysisBackupFile, Hotcue } from '../types';

let firebaseApp: FirebaseApp | null = null;
let db: Firestore | null = null;
let auth: Auth | null = null;

export interface FirebaseStatus {
  initialized: boolean;
  user: User | null;
  error?: string;
  cloudTrackCount: number;
}

class FirebaseService {
  private static instance: FirebaseService;
  private statusListeners: Array<(status: FirebaseStatus) => void> = [];
  private currentStatus: FirebaseStatus = {
    initialized: false,
    user: null,
    cloudTrackCount: 0,
  };

  private constructor() {
    this.initFirebase();
  }

  public static getInstance(): FirebaseService {
    if (!FirebaseService.instance) {
      FirebaseService.instance = new FirebaseService();
    }
    return FirebaseService.instance;
  }

  public async initFirebase(): Promise<void> {
    try {
      // Load config from firebase-applet-config.json
      const response = await fetch('/firebase-applet-config.json');
      if (!response.ok) {
        console.warn('Could not load firebase-applet-config.json');
        return;
      }
      const config = await response.json();

      if (!getApps().length) {
        firebaseApp = initializeApp(config);
      } else {
        firebaseApp = getApp();
      }

      db = getFirestore(firebaseApp);
      auth = getAuth(firebaseApp);

      // Sign in anonymously if not already signed in to enable access
      onAuthStateChanged(auth, async (user) => {
        this.currentStatus.user = user;
        if (user) {
          this.currentStatus.initialized = true;
          this.listenToCloudLibrary();
        }
        this.notifyStatus();
      });

      if (!auth.currentUser) {
        await signInAnonymously(auth).catch((err) => {
          console.warn('Anonymous auth note (rules permit public writes):', err);
          this.currentStatus.initialized = true;
          this.listenToCloudLibrary();
          this.notifyStatus();
        });
      }
    } catch (err: any) {
      console.warn('Firebase init notice:', err);
      this.currentStatus.error = err?.message || String(err);
      this.notifyStatus();
    }
  }

  private listenToCloudLibrary(): void {
    if (!db) return;
    try {
      const col = collection(db, 'cloud_library');
      onSnapshot(col, (snapshot) => {
        this.currentStatus.cloudTrackCount = snapshot.size;
        this.notifyStatus();
      });
    } catch (e) {
      console.warn('Cloud library snapshot listener:', e);
    }
  }

  public onStatus(callback: (status: FirebaseStatus) => void): () => void {
    this.statusListeners.push(callback);
    callback(this.currentStatus);
    return () => {
      this.statusListeners = this.statusListeners.filter((cb) => cb !== callback);
    };
  }

  private notifyStatus(): void {
    for (const listener of this.statusListeners) {
      listener({ ...this.currentStatus });
    }
  }

  /**
   * Saves a track's metadata, analysis, hotcues, and waveform peaks to Firebase Cloud Storage/Firestore
   */
  public async saveTrackToCloud(track: TrackEntity): Promise<boolean> {
    if (!db) return false;
    try {
      const trackDoc = doc(db, 'cloud_library', track.id);
      const payload: Record<string, any> = {
        id: track.id,
        title: track.title,
        artist: track.artist,
        album: track.album || '',
        bpm: track.bpm,
        key: track.key,
        duration: track.duration,
        beatgridOffset: track.beatgridOffset,
        hotcues: track.hotcues || [],
        source: track.source,
        updatedAt: Date.now(),
      };
      if (track.waveformPeaks) {
        payload.waveformPeaks = {
          lows: track.waveformPeaks.lows.slice(0, 1000),
          mids: track.waveformPeaks.mids.slice(0, 1000),
          highs: track.waveformPeaks.highs.slice(0, 1000),
          peaks: track.waveformPeaks.peaks.slice(0, 1000),
        };
      }
      await setDoc(trackDoc, payload, { merge: true });
      return true;
    } catch (err) {
      console.error('Failed to save track to Firebase:', err);
      return false;
    }
  }

  /**
   * Fetches all cloud-saved tracks from Firestore
   */
  public async loadTracksFromCloud(): Promise<TrackEntity[]> {
    if (!db) return [];
    try {
      const col = collection(db, 'cloud_library');
      const snap = await getDocs(col);
      const tracks: TrackEntity[] = [];
      snap.forEach((d) => {
        const data = d.data() as any;
        tracks.push({
          id: data.id || d.id,
          title: data.title || 'Untitled',
          artist: data.artist || 'Unknown Artist',
          album: data.album,
          bpm: data.bpm || 120,
          key: data.key || '8A',
          duration: data.duration || 180,
          beatgridOffset: data.beatgridOffset || 0,
          hotcues: (data.hotcues as Hotcue[]) || [],
          waveformPeaks: data.waveformPeaks,
          source: 'firebase',
        });
      });
      return tracks;
    } catch (err) {
      console.error('Failed to load tracks from Firebase:', err);
      return [];
    }
  }

  /**
   * Backup entire library analysis file into Firestore
   */
  public async saveLibraryBackup(backup: AnalysisBackupFile): Promise<boolean> {
    if (!db) return false;
    try {
      const backupDoc = doc(db, 'backups', 'latest_library_backup');
      await setDoc(backupDoc, {
        ...backup,
        storedAt: new Date().toISOString(),
      });
      return true;
    } catch (err) {
      console.error('Failed to save library backup to Firebase:', err);
      return false;
    }
  }

  /**
   * Load the latest library analysis backup from Firestore
   */
  public async loadLibraryBackup(): Promise<AnalysisBackupFile | null> {
    if (!db) return null;
    try {
      const backupDoc = doc(db, 'backups', 'latest_library_backup');
      const snap = await getDoc(backupDoc);
      if (snap.exists()) {
        return snap.data() as AnalysisBackupFile;
      }
      return null;
    } catch (err) {
      console.error('Failed to load library backup from Firebase:', err);
      return null;
    }
  }

  /**
   * Delete track from cloud
   */
  public async deleteTrackFromCloud(trackId: string): Promise<boolean> {
    if (!db) return false;
    try {
      await deleteDoc(doc(db, 'cloud_library', trackId));
      return true;
    } catch (e) {
      console.error('Failed to delete cloud track:', e);
      return false;
    }
  }
}

export const firebaseService = FirebaseService.getInstance();
