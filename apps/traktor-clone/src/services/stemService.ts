/**
 * Stem separation service — calls our back-end's queue/run/verify pipeline.
 *
 * After a track is loaded into a deck, the user can trigger stem separation.
 * The service:
 *   1. Adds the track to our back-end's queue (via /api/queue/add)
 *   2. Runs the batch (via /api/run)
 *   3. Verifies the output (via /api/verify)
 *   4. Returns the verified stem paths
 *
 * The front-end then loads each stem as a separate deck (A=vocals, B=drums, C=bass, D=others).
 */

import { BackendBridge } from './backendBridge';

export interface StemSeparationResult {
  success: boolean;
  stems: {
    vocals: string | null;
    drums: string | null;
    bass: string | null;
    others: string | null;
  };
  resultFolder: string | null;
  error?: string;
}

export class StemService {
  private backend: BackendBridge;

  constructor(baseUrl = 'http://localhost:3000') {
    this.backend = new BackendBridge(baseUrl);
  }

  /**
   * Separate a track into stems using our back-end.
   *
   * @param sourcePath Path to the source audio file on disk
   * @param destPath Destination folder for the stems
   * @param stems Stem selection: 'all' | 'drums' | 'vocals,drums' | etc.
   * @param mode 'speed' (High Speed) or 'quality' (High Quality)
   * @param simulate If true, skip the actual Live separation (for testing)
   */
  async separate(
    sourcePath: string,
    destPath: string,
    stems = 'all',
    mode = 'speed',
    simulate = false
  ): Promise<StemSeparationResult> {
    try {
      // Step 1: Add to queue
      const queueResult = await this.backend.addFilesToQueue(
        [sourcePath],
        destPath,
        stems,
        mode
      );

      if (queueResult.exitCode !== 0) {
        return {
          success: false,
          stems: { vocals: null, drums: null, bass: null, others: null },
          resultFolder: null,
          error: queueResult.stderr || queueResult.stdout,
        };
      }

      // Step 2: Run the queue
      const runResult = await this.backend.runQueue(destPath, simulate);

      if (runResult.exitCode !== 0) {
        return {
          success: false,
          stems: { vocals: null, drums: null, bass: null, others: null },
          resultFolder: null,
          error: runResult.stderr || runResult.stdout,
        };
      }

      // Step 3: Verify the output
      const resultFolder = `${destPath}/${this.getTrackName(sourcePath)}`;
      const verifyResult = await this.backend.verifyFolder(resultFolder);

      if (verifyResult.exitCode !== 0) {
        // Verification failed, but stems may still be on disk
        return {
          success: false,
          stems: this.guessStemPaths(resultFolder),
          resultFolder,
          error: verifyResult.stderr || verifyResult.stdout,
        };
      }

      // Step 4: Parse verification output to get stem paths
      const stems = this.parseStemPaths(resultFolder);

      return {
        success: true,
        stems,
        resultFolder,
      };
    } catch (err: any) {
      return {
        success: false,
        stems: { vocals: null, drums: null, bass: null, others: null },
        resultFolder: null,
        error: err.message || 'Stem separation failed',
      };
    }
  }

  private getTrackName(sourcePath: string): string {
    const parts = sourcePath.split(/[\\/]/);
    return parts[parts.length - 1].replace(/\.[^/.]+$/, '');
  }

  private guessStemPaths(folder: string): StemSeparationResult['stems'] {
    // Guess stem paths based on naming convention: <track>-vocals.wav, etc.
    const base = folder.replace(/\\/g, '/');
    return {
      vocals: `${base}/vocals.wav`,
      drums: `${base}/drums.wav`,
      bass: `${base}/bass.wav`,
      others: `${base}/other.wav`,
    };
  }

  private parseStemPaths(folder: string): StemSeparationResult['stems'] {
    // In a real implementation, parse the autostem.manifest.json to get exact paths
    // For now, guess based on naming convention
    return this.guessStemPaths(folder);
  }
}
