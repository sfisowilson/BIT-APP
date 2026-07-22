import { useState, useRef, useCallback } from 'react';
import { getToken } from '../apiClient';

interface ChunkedUploadState {
  uploading: boolean;
  progress: number;       // 0-100
  chunkProgress: string;  // e.g. "12/48 chunks"
  uploadId: string | null;
  error: string | null;
}

interface UseChunkedUploadOptions {
  chunkSizeMB?: number;     // Default 25 MB per chunk
  maxConcurrent?: number;   // Default 3 parallel chunks
}

interface UseChunkedUploadReturn {
  state: ChunkedUploadState;
  startUpload: (file: File, metadata: Record<string, string>) => Promise<any>;
  cancelUpload: () => void;
  reset: () => void;
}

/**
 * Chunked, resumable upload hook for large broadcast video files.
 * 
 * Flow:
 *   1. POST /api/content/upload/init  → get uploadId
 *   2. POST /api/content/upload/chunk → upload each chunk (parallel, resumable)
 *   3. POST /api/content/upload/complete → assemble + register
 */
export function useChunkedUpload(
  options: UseChunkedUploadOptions = {}
): UseChunkedUploadReturn {
  const { chunkSizeMB = 25, maxConcurrent = 3 } = options;
  const chunkSizeBytes = chunkSizeMB * 1024 * 1024;

  const [state, setState] = useState<ChunkedUploadState>({
    uploading: false,
    progress: 0,
    chunkProgress: '',
    uploadId: null,
    error: null,
  });

  const abortRef = useRef<AbortController | null>(null);
  const stateRef = useRef(state);
  stateRef.current = state;

  const reset = useCallback(() => {
    setState({ uploading: false, progress: 0, chunkProgress: '', uploadId: null, error: null });
  }, []);

  const cancelUpload = useCallback(() => {
    abortRef.current?.abort();
    reset();
  }, [reset]);

  const authHeaders = (): HeadersInit => {
    const token = getToken();
    return token ? { 'Authorization': `Bearer ${token}` } : {};
  };

  const startUpload = useCallback(async (
    file: File,
    metadata: Record<string, string>
  ): Promise<any> => {
    abortRef.current = new AbortController();
    const signal = abortRef.current.signal;

    setState(prev => ({ ...prev, uploading: true, progress: 0, error: null }));

    try {
      const totalChunks = Math.ceil(file.size / chunkSizeBytes);

      // Step 1: Init session
      const initRes = await fetch('/api/content/upload/init', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({
          fileName: file.name,
          totalChunks,
          chunkSize: chunkSizeBytes,
          totalSize: file.size,
        }),
        signal,
      });

      if (!initRes.ok) {
        const err = await initRes.json();
        throw new Error(err.error || 'Failed to initialize upload.');
      }

      const { uploadId } = await initRes.json();
      setState(prev => ({ ...prev, uploadId, chunkProgress: `0/${totalChunks} chunks` }));

      // Step 2: Check which chunks are already uploaded (resume support)
      let uploadedChunks: number[] = [];
      try {
        const statusRes = await fetch(`/api/content/upload/status/${uploadId}`, {
          headers: authHeaders(),
          signal,
        });
        if (statusRes.ok) {
          const status = await statusRes.json();
          uploadedChunks = status.uploadedChunks || [];
        }
      } catch { /* ignore — start fresh */ }

      const pendingChunks: number[] = [];
      for (let i = 0; i < totalChunks; i++) {
        if (!uploadedChunks.includes(i)) {
          pendingChunks.push(i);
        }
      }

      // Update progress for already-uploaded chunks
      if (uploadedChunks.length > 0) {
        const completedPct = Math.round((uploadedChunks.length / totalChunks) * 100);
        setState(prev => ({ ...prev, progress: completedPct, chunkProgress: `${uploadedChunks.length}/${totalChunks} chunks` }));
      }

      // Step 3: Upload pending chunks with concurrency limit
      let completed = uploadedChunks.length;
      const queue = [...pendingChunks];

      const uploadChunk = async (chunkIndex: number): Promise<void> => {
        const start = chunkIndex * chunkSizeBytes;
        const end = Math.min(start + chunkSizeBytes, file.size);
        const blob = file.slice(start, end);

        const formData = new FormData();
        formData.append('uploadId', uploadId);
        formData.append('chunkIndex', String(chunkIndex));
        formData.append('totalChunks', String(totalChunks));
        formData.append('chunk', blob, `chunk_${chunkIndex}`);

        const res = await fetch('/api/content/upload/chunk', {
          method: 'POST',
          headers: authHeaders(),
          body: formData,
          signal,
        });

        if (!res.ok) {
          const err = await res.json();
          throw new Error(err.error || `Chunk ${chunkIndex} failed.`);
        }

        completed++;
        const pct = Math.round((completed / totalChunks) * 100);
        setState(prev => ({ ...prev, progress: pct, chunkProgress: `${completed}/${totalChunks} chunks` }));
      };

      // Process queue with concurrency control
      const workers: Promise<void>[] = [];
      for (let w = 0; w < Math.min(maxConcurrent, queue.length); w++) {
        workers.push((async () => {
          while (queue.length > 0) {
            const chunkIndex = queue.shift();
            if (chunkIndex === undefined) break;
            await uploadChunk(chunkIndex);
          }
        })());
      }
      await Promise.all(workers);

      if (signal.aborted) return;

      // Step 4: Complete & assemble
      const completeRes = await fetch('/api/content/upload/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({
          uploadId,
          title: metadata.title,
          sourceChannel: metadata.sourceChannel,
          campaignId: metadata.campaignId || undefined,
        }),
        signal,
      });

      if (!completeRes.ok) {
        const err = await completeRes.json();
        throw new Error(err.error || 'Assembly failed.');
      }

      const result = await completeRes.json();
      setState(prev => ({ ...prev, uploading: false, progress: 100, chunkProgress: `${totalChunks}/${totalChunks} chunks` }));
      return result;
    } catch (err: any) {
      if (err.name === 'AbortError') {
        setState(prev => ({ ...prev, uploading: false }));
        return;
      }
      setState(prev => ({ ...prev, uploading: false, error: err.message }));
      throw err;
    }
  }, [chunkSizeBytes, maxConcurrent]);

  return { state, startUpload, cancelUpload, reset };
}
