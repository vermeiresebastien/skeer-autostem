import { initializeApp, getApps } from 'firebase/app';
import {
  getAuth,
  signInWithPopup,
  GoogleAuthProvider,
  onAuthStateChanged,
  User,
  signOut,
} from 'firebase/auth';
import firebaseConfig from '../../firebase-applet-config.json';
import { GoogleTaskItem, TrackEntity } from '../types';
import { BeatAnalyzer } from './beatAnalysis';

// Initialize Firebase App
const app = getApps().length === 0 ? initializeApp(firebaseConfig) : getApps()[0];
const auth = getAuth(app);

const provider = new GoogleAuthProvider();
// Workspace Scopes
provider.addScope('https://www.googleapis.com/auth/drive.readonly');
provider.addScope('https://www.googleapis.com/auth/drive.file');
provider.addScope('https://www.googleapis.com/auth/tasks');

let isSigningIn = false;
let cachedAccessToken: string | null = null;

export const initAuth = (
  onAuthSuccess?: (user: User, token: string) => void,
  onAuthFailure?: () => void
) => {
  return onAuthStateChanged(auth, async (user: User | null) => {
    if (user) {
      if (cachedAccessToken) {
        if (onAuthSuccess) onAuthSuccess(user, cachedAccessToken);
      } else if (!isSigningIn) {
        cachedAccessToken = null;
        if (onAuthFailure) onAuthFailure();
      }
    } else {
      cachedAccessToken = null;
      if (onAuthFailure) onAuthFailure();
    }
  });
};

export const googleSignIn = async (): Promise<{ user: User; accessToken: string } | null> => {
  try {
    isSigningIn = true;
    const result = await signInWithPopup(auth, provider);
    const credential = GoogleAuthProvider.credentialFromResult(result);
    if (!credential?.accessToken) {
      throw new Error('Failed to get access token from Google Auth');
    }
    cachedAccessToken = credential.accessToken;
    return { user: result.user, accessToken: cachedAccessToken };
  } catch (error: unknown) {
    console.error('Google Sign In error:', error);
    throw error;
  } finally {
    isSigningIn = false;
  }
};

export const getAccessToken = (): string | null => {
  return cachedAccessToken;
};

export const logout = async (): Promise<void> => {
  await signOut(auth);
  cachedAccessToken = null;
};

export const getCurrentUser = (): User | null => {
  return auth.currentUser;
};

// --- Google Drive API Client ---

export interface DriveAudioFile {
  id: string;
  name: string;
  mimeType: string;
  size?: string;
  modifiedTime?: string;
}

export const listDriveAudioFiles = async (): Promise<DriveAudioFile[]> => {
  const token = getAccessToken();
  if (!token) throw new Error('Not authenticated with Google Workspace');

  const query = "mimeType contains 'audio/' or name contains '.mp3' or name contains '.wav' or name contains '.flac' or name contains '.ogg'";
  const url = `https://www.googleapis.com/drive/v3/files?q=${encodeURIComponent(
    query
  )}&fields=files(id,name,mimeType,size,modifiedTime)&pageSize=50`;

  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!res.ok) {
    throw new Error(`Google Drive API error: ${res.statusText}`);
  }

  const data = await res.json();
  return (data.files || []) as DriveAudioFile[];
};

export const downloadDriveAudioFile = async (
  fileId: string,
  fileName: string,
  audioContext: AudioContext
): Promise<TrackEntity> => {
  const token = getAccessToken();
  if (!token) throw new Error('Not authenticated with Google Workspace');

  const url = `https://www.googleapis.com/drive/v3/files/${fileId}?alt=media`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!res.ok) {
    throw new Error(`Failed to download audio file from Google Drive: ${res.statusText}`);
  }

  const arrayBuffer = await res.arrayBuffer();
  const audioBuffer = await audioContext.decodeAudioData(arrayBuffer);

  return BeatAnalyzer.analyzeTrack(
    { name: fileName, size: arrayBuffer.byteLength },
    audioBuffer,
    'drive'
  );
};

export const saveAnalysisBackupToDrive = async (
  backupJson: string,
  fileName = 'traktor_library_backup.traktor.json'
): Promise<string> => {
  const token = getAccessToken();
  if (!token) throw new Error('Not authenticated with Google Workspace');

  const metadata = {
    name: fileName,
    mimeType: 'application/json',
  };

  const form = new FormData();
  form.append(
    'metadata',
    new Blob([JSON.stringify(metadata)], { type: 'application/json' })
  );
  form.append('file', new Blob([backupJson], { type: 'application/json' }));

  const res = await fetch(
    'https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart',
    {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
      body: form,
    }
  );

  if (!res.ok) {
    throw new Error(`Failed to save backup to Google Drive: ${res.statusText}`);
  }

  const fileData = await res.json();
  return fileData.id;
};

// --- Google Tasks API Client ---

export const listGoogleTasks = async (): Promise<GoogleTaskItem[]> => {
  const token = getAccessToken();
  if (!token) return getLocalFallbackTasks();

  try {
    // 1. Get primary tasklist
    const listsRes = await fetch('https://tasks.googleapis.com/tasks/v1/users/@me/lists', {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!listsRes.ok) throw new Error(`Tasks API error: ${listsRes.statusText}`);
    const listsData = await listsRes.json();
    const primaryListId = listsData.items?.[0]?.id || '@default';

    // 2. Get tasks
    const tasksRes = await fetch(
      `https://tasks.googleapis.com/tasks/v1/lists/${primaryListId}/tasks?showCompleted=true&showHidden=true`,
      {
        headers: { Authorization: `Bearer ${token}` },
      }
    );
    if (!tasksRes.ok) throw new Error(`Tasks fetch error: ${tasksRes.statusText}`);
    const tasksData = await tasksRes.json();

    const items: GoogleTaskItem[] = (tasksData.items || []).map((t: any) => ({
      id: t.id,
      title: t.title || 'Untitled Task',
      notes: t.notes || '',
      status: t.status === 'completed' ? 'completed' : 'needsAction',
      due: t.due,
    }));

    return items.length > 0 ? items : getLocalFallbackTasks();
  } catch (err) {
    console.warn('Google Tasks fetch failed, using local gig checklist:', err);
    return getLocalFallbackTasks();
  }
};

export const createGoogleTask = async (
  title: string,
  notes = ''
): Promise<GoogleTaskItem> => {
  const token = getAccessToken();
  if (!token) {
    // Return local task
    const task: GoogleTaskItem = {
      id: `task_${Date.now()}`,
      title,
      notes,
      status: 'needsAction',
    };
    saveLocalTask(task);
    return task;
  }

  const res = await fetch('https://tasks.googleapis.com/tasks/v1/lists/@default/tasks', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ title, notes }),
  });

  if (!res.ok) throw new Error(`Failed to create Google Task: ${res.statusText}`);
  const data = await res.json();
  return {
    id: data.id,
    title: data.title,
    notes: data.notes || '',
    status: data.status === 'completed' ? 'completed' : 'needsAction',
  };
};

export const updateGoogleTaskStatus = async (
  taskId: string,
  status: 'needsAction' | 'completed'
): Promise<void> => {
  const token = getAccessToken();
  if (!token) {
    updateLocalTask(taskId, status);
    return;
  }

  await fetch(`https://tasks.googleapis.com/tasks/v1/lists/@default/tasks/${taskId}`, {
    method: 'PATCH',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ status }),
  });
};

export const deleteGoogleTask = async (taskId: string): Promise<void> => {
  const token = getAccessToken();
  if (!token) {
    deleteLocalTask(taskId);
    return;
  }

  await fetch(`https://tasks.googleapis.com/tasks/v1/lists/@default/tasks/${taskId}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${token}` },
  });
};

// Local storage fallbacks for gig checklist and setlists
const TASKS_STORAGE_KEY = 'traktor_gig_tasks';
const SETLIST_STORAGE_KEY = 'traktor_setlists';

export const getLocalFallbackTasks = (): GoogleTaskItem[] => {
  try {
    const raw = localStorage.getItem(TASKS_STORAGE_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    // ignore
  }
  return [
    { id: '1', title: 'Verify audio interface & headphone cue monitor', status: 'completed' },
    { id: '2', title: 'Soundcheck master output & booth levels', status: 'needsAction' },
    { id: '3', title: 'Export backup analysis file (.traktor.json) to USB/Drive', status: 'completed' },
    { id: '4', title: 'Check key compatibility (Camelot wheel 8A -> 9A transition)', status: 'needsAction' },
    { id: '5', title: 'Test peak drops & 32-beat build loop cues on Deck B', status: 'needsAction' },
  ];
};

const saveLocalTask = (task: GoogleTaskItem) => {
  const tasks = getLocalFallbackTasks();
  tasks.unshift(task);
  localStorage.setItem(TASKS_STORAGE_KEY, JSON.stringify(tasks));
};

const updateLocalTask = (taskId: string, status: 'needsAction' | 'completed') => {
  const tasks = getLocalFallbackTasks().map((t) => (t.id === taskId ? { ...t, status } : t));
  localStorage.setItem(TASKS_STORAGE_KEY, JSON.stringify(tasks));
};

const deleteLocalTask = (taskId: string) => {
  const tasks = getLocalFallbackTasks().filter((t) => t.id !== taskId);
  localStorage.setItem(TASKS_STORAGE_KEY, JSON.stringify(tasks));
};

export interface SetlistNote {
  id: string;
  title: string;
  content: string;
  updatedAt: string;
}

export const getSetlistNotes = (): SetlistNote[] => {
  try {
    const raw = localStorage.getItem(SETLIST_STORAGE_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    // ignore
  }
  return [
    {
      id: 'setlist_1',
      title: 'Main Room Peak Setlist (126-132 BPM)',
      content:
        '1. Traktor Prime - Neon Horizon [Intro Cue 1 -> Drop at Cue 2]\n2. Traktor Apex - Acid Velocity [Harmonic mix 8A to 9A, 16-beat loop on Deck A while introducing Deck B Bass]\n3. VIP Dub Edit - Cue 5 breakdown with HighPass Filter sweep\n4. Outro transition into 130 BPM melodic techno',
      updatedAt: new Date().toLocaleDateString(),
    },
    {
      id: 'setlist_2',
      title: 'Warm-up Deep Grooves (122-125 BPM)',
      content:
        'Focus on smooth crossfader blends, keeping Bass Kill active on incoming track until bar 32. Use Reverb Unit 2 on high hats during breakdown.',
      updatedAt: new Date().toLocaleDateString(),
    },
  ];
};

export const saveSetlistNote = (title: string, content: string): SetlistNote => {
  const notes = getSetlistNotes();
  const newNote: SetlistNote = {
    id: `setlist_${Date.now()}`,
    title,
    content,
    updatedAt: new Date().toLocaleDateString(),
  };
  notes.unshift(newNote);
  localStorage.setItem(SETLIST_STORAGE_KEY, JSON.stringify(notes));
  return newNote;
};

export const deleteSetlistNote = (id: string): void => {
  const notes = getSetlistNotes().filter((n) => n.id !== id);
  localStorage.setItem(SETLIST_STORAGE_KEY, JSON.stringify(notes));
};
