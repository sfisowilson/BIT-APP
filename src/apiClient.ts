/**
 * Centralized API client for the BIT platform.
 * All HTTP calls to the .NET backend go through this module.
 * The Vite dev server proxies /api/* to the .NET backend,
 * so relative URLs work in both dev and production.
 */

import type { DetectionJob, JobsListResponse } from './types';

const TOKEN_KEY = 'bit_token';
const USER_KEY = 'bit_user';

// ─── Token & User helpers ──────────────────────────────────────────────

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

export function getSavedUser<T = unknown>(): T | null {
  const raw = localStorage.getItem(USER_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

export function setSavedUser<T = unknown>(user: T): void {
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

// ─── Core fetch wrappers ────────────────────────────────────────────────

const FRIENDLY_ERROR = 'Something went wrong. Please try again or contact support.';
const NETWORK_ERROR = 'Unable to connect to the server. Please check your connection and try again.';

/**
 * fetch() with JWT Bearer token attached from localStorage.
 * Intercepts at the network level:
 *   - Network failures → friendly "Unable to connect" message
 *   - 500-range responses → friendly generic error
 *   - 400-range responses → returned as-is so callers can extract data.error
 */
export async function fetchWithAuth(
  url: string,
  options: RequestInit = {},
): Promise<Response> {
  const token = getToken();
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...options.headers,
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
  try {
    const res = await fetch(url, { ...options, headers });
    if (res.status >= 500) {
      throw new Error(FRIENDLY_ERROR);
    }
    return res;
  } catch (err: any) {
    // Network error (TypeError: Failed to fetch) → friendly
    if (err.name === 'TypeError' || err.message?.includes('Failed to fetch') || err.message?.includes('NetworkError')) {
      throw new Error(NETWORK_ERROR);
    }
    // Already a friendly error from our 500 check — rethrow
    if (err.message === FRIENDLY_ERROR) throw err;
    // Unexpected — still don't leak raw details
    throw new Error(NETWORK_ERROR);
  }
}

/** Unauthenticated fetch (for login, forgot-password). Same interceptor behavior. */
export async function fetchPublic(
  url: string,
  options: RequestInit = {},
): Promise<Response> {
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...options.headers,
  };
  try {
    const res = await fetch(url, { ...options, headers });
    if (res.status >= 500) {
      throw new Error(FRIENDLY_ERROR);
    }
    return res;
  } catch (err: any) {
    if (err.name === 'TypeError' || err.message?.includes('Failed to fetch') || err.message?.includes('NetworkError')) {
      throw new Error(NETWORK_ERROR);
    }
    if (err.message === FRIENDLY_ERROR) throw err;
    throw new Error(NETWORK_ERROR);
  }
}

/** fetch + json() convenience helper (authenticated). */
export async function fetchJson<T = unknown>(url: string): Promise<T> {
  const r = await fetchWithAuth(url);
  return r.json() as Promise<T>;
}

// ─── Pagination & Filter Helpers ────────────────────────────────────────

/**
 * Build a URL query string from a params object, omitting undefined/null/empty values.
 */
export function buildQueryString(params: Record<string, unknown>): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  }
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/**
 * Authenticated fetch that returns a typed PaginatedResponse.
 * Pass filter params as a flat object; they are converted to query string.
 */
export async function fetchPaginated<T>(
  url: string,
  params: Record<string, unknown> = {},
): Promise<{ items: T[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }> {
  const qs = buildQueryString(params);
  const r = await fetchWithAuth(`${url}${qs}`);
  if (!r.ok) {
    const text = await r.text();
    throw new Error(`API error ${r.status} on ${url}: ${text.substring(0, 200)}`);
  }
  return r.json();
}

// ─── Auth API ───────────────────────────────────────────────────────────

export interface LoginRequest {
  email: string;
  password: string;
}

export interface UserSession {
  id: string;
  fullName: string;
  email: string;
  role: 'Admin' | 'Editor' | 'Advertiser';
  accountStatus: string;
}

export interface LoginResponse {
  token: string;
  user: UserSession;
}

export async function login(credentials: LoginRequest): Promise<LoginResponse> {
  let res: Response;
  try {
    res = await fetchPublic('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify(credentials),
    });
  } catch (err: any) {
    throw new Error('Unable to reach the identity service. Is the server running?');
  }

  // Handle non-JSON responses gracefully
  const contentType = res.headers.get('content-type');
  if (!contentType || !contentType.includes('application/json')) {
    const text = await res.text();
    throw new Error(`Unexpected server response (HTTP ${res.status}): ${text.substring(0, 100)}`);
  }

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error || 'Authentication failed — invalid credentials.');
  }
  if (!data.token || !data.user) {
    throw new Error('Authentication response is missing token or user data.');
  }
  // Persist session
  setToken(data.token);
  setSavedUser(data.user);
  return data as LoginResponse;
}

/** MReq 8: Silently refresh an expiring JWT token. */
export async function refreshToken(): Promise<LoginResponse | null> {
  const currentToken = getToken();
  if (!currentToken) return null;
  try {
    const res = await fetchPublic('/api/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ token: currentToken }),
    });
    if (!res.ok) return null;
    const data = await res.json();
    if (data.token) {
      setToken(data.token);
      setSavedUser(data.user);
    }
    return data as LoginResponse;
  } catch {
    return null;
  }
}

export function logout(): void {
  clearToken();
}

// ─── Pipeline Stage Management API ──────────────────────────────────────

/**
 * Transition a content item to a target pipeline stage with validation.
 */
export async function transitionStage(
  contentId: string,
  targetStage: string,
  errorMessage?: string,
): Promise<{ success: boolean; id: string; ingestionStatus: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/transition`, {
    method: 'POST',
    body: JSON.stringify({ targetStage, errorMessage }),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to transition pipeline stage.');
  }
  return r.json();
}

/** Re-run transcoding for a content item. */
export async function retranscode(
  contentId: string,
): Promise<{ success: boolean; id: string; ingestionStatus: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/retranscode`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to restart transcoding.');
  }
  return r.json();
}

/** Re-run scene detection for a content item. Returns jobId for polling. */
export async function redetectScenes(
  contentId: string,
): Promise<{ jobId: string; id: string; ingestionStatus: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/redetect-scenes`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to restart scene detection.');
  }
  return r.json();
}

/** Queue scenes-only detection (FFmpeg cuts + thumbnails, no surface detection). */
export async function detectScenesOnly(
  contentId: string,
  videoTitle: string,
): Promise<{ jobId: string; contentId: string; message: string }> {
  const r = await fetchWithAuth('/api/video/detect-scenes', {
    method: 'POST',
    body: JSON.stringify({ contentId, videoTitle }),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to enqueue scenes-only detection.');
  }
  return r.json();
}

/** Queue per-scene surface detection for a single scene. */
export async function detectSurfacesForScene(
  sceneId: string,
): Promise<{ jobId: string; sceneId: string; message: string }> {
  const r = await fetchWithAuth(`/api/scenes/${sceneId}/detect-surfaces`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to enqueue surface detection.');
  }
  return r.json();
}

/** Fetch surfaces for multiple scenes in a single batched request. */
export async function fetchSurfacesBatch(sceneIds: string[]): Promise<any[]> {
  if (sceneIds.length === 0) return [];
  const r = await fetchWithAuth(`/api/scenes/surfaces/batch?sceneIds=${encodeURIComponent(sceneIds.join(','))}`);
  if (!r.ok) throw new Error('Failed to fetch surfaces batch');
  return r.json();
}

/** AI-powered placement suggestions via Gemini. */
export interface AiPlacementSuggestion {
  placements: { surfaceId: string; assetId: string; reasoning: string }[];
  explanation: string;
  modelUsed: string;
}

export async function suggestPlacements(payload: {
  prompt: string;
  contentId: string;
  sceneId: string;
  surfaces: { id: string; surfaceType: string; confidenceScore: number }[];
  assets: { id: string; name: string; brandCategory: string }[];
}): Promise<AiPlacementSuggestion> {
  const r = await fetchWithAuth('/api/placements/suggest', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to get placement suggestions.');
  }
  return r.json();
}

/** Retry a failed render — resets to Queued and re-enqueues the compositing job. */
export async function retryRender(
  renderId: string,
): Promise<{ id: string; renderStatus: string; message?: string }> {
  const r = await fetchWithAuth(`/api/renders/${renderId}/retry`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to retry render.');
  }
  return r.json();
}

/** Poll for detection job progress. Returns 0-100 percentage and current status. */
export async function getDetectionStatus(
  contentId: string,
): Promise<{
  contentId: string;
  progress: number;
  ingestionStatus: string;
  jobId: string | null;
  errorMessage: string | null;
  completed: boolean;
  failed: boolean;
}> {
  const r = await fetchWithAuth(`/api/content/${contentId}/detection-status`, { method: 'GET' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to get detection status.');
  }
  return r.json();
}

/** Mark content as Failed with an error message. */
export async function markFailed(
  contentId: string,
  errorMessage?: string,
): Promise<{ success: boolean; id: string; ingestionStatus: string; lastErrorMessage?: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/mark-failed`, {
    method: 'POST',
    body: JSON.stringify({ targetStage: 'Failed', errorMessage }),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to mark as failed.');
  }
  return r.json();
}

/** Full pipeline reset — clear all progress back to Staging. */
export async function resetPipeline(
  contentId: string,
): Promise<{ success: boolean; id: string; ingestionStatus: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/reset`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to reset pipeline.');
  }
  return r.json();
}

// ─── Job Management API ─────────────────────────────────────────────────

/** Fetch all background detection jobs with their current state. */
export async function getJobs(): Promise<JobsListResponse> {
  const r = await fetchWithAuth('/api/jobs');
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to fetch jobs.');
  }
  return r.json();
}

/** Stop/Cancel a background detection job by jobId or contentId. */
export async function stopJob(jobId: string): Promise<{ success: boolean; jobId: string; contentId: string; message: string }> {
  const r = await fetchWithAuth(`/api/jobs/${jobId}/stop`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to stop job.');
  }
  return r.json();
}

/** Pause a background detection job by jobId or contentId. */
export async function pauseJob(jobId: string): Promise<{ success: boolean; jobId: string; contentId: string; message: string }> {
  const r = await fetchWithAuth(`/api/jobs/${jobId}/pause`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to pause job.');
  }
  return r.json();
}

/** Resume a paused background detection job by jobId or contentId. */
export async function resumeJob(jobId: string): Promise<{ success: boolean; jobId: string; contentId: string; message: string }> {
  const r = await fetchWithAuth(`/api/jobs/${jobId}/resume`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to resume job.');
  }
  return r.json();
}

// ─── Surface Tracking API ────────────────────────────────────────────────

/** Trigger per-frame surface tracking for an existing surface. Enqueues a Hangfire job. */
export async function trackSurface(surfaceId: string): Promise<{ jobId: string; surfaceId: string; message: string }> {
  const r = await fetchWithAuth(`/api/surfaces/${surfaceId}/track`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to enqueue tracking job.');
  }
  return r.json();
}

// ─── BI / Statistics API (MReq 19) ──────────────────────────────────────

export interface StatsSummary {
  totalContent: number;
  totalScenes: number;
  totalSurfaces: number;
  totalRenders: number;
  totalCampaigns: number;
  activeAlarms: number;
  rendersLast7Days: number;
  contentLast7Days: number;
  avgRenderTimeMs: number;
}

export async function fetchStatsSummary(): Promise<StatsSummary> {
  const r = await fetchWithAuth('/api/stats/summary');
  return r.json();
}
