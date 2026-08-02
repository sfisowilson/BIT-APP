/**
 * Centralized API client for the BIT platform.
 * All HTTP calls to the .NET backend go through this module.
 * The Vite dev server proxies /api/* to the .NET backend,
 * so relative URLs work in both dev and production.
 */

import type { DetectionJob, JobsListResponse, ShotItem, InvoiceSummary } from './types';

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

/**
 * Queue the full shot-aware detection pipeline: FFmpeg shot-cut detection → SAM3 keyframe
 * embedding → clustering shots into scenes (a scene may span multiple cuts) → surface
 * detection per clustered scene. This is the pipeline that makes "AI Split Analyze" actually
 * produce meaningful scenes with surfaces, as opposed to detectScenesOnly's raw 1:1 FFmpeg
 * cuts with no surfaces.
 */
export async function aiSplitAnalyze(
  contentId: string,
  videoTitle: string,
): Promise<{ jobId: string; contentId: string; message: string }> {
  const r = await fetchWithAuth('/api/video/ai-split-analyze', {
    method: 'POST',
    body: JSON.stringify({ contentId, videoTitle }),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to enqueue AI split/analyze.');
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

/** List the shots (camera cuts) making up a scene, ordered by shotIndex. A scene can span multiple shots. */
export async function fetchShotsForScene(sceneId: string): Promise<ShotItem[]> {
  const r = await fetchWithAuth(`/api/scenes/${sceneId}/shots`);
  if (!r.ok) throw new Error('Failed to fetch shots for scene.');
  return r.json();
}

/** Delete a single scene and its child surfaces/ad-slots/approvals. Fails if any surface is Approved. */
export async function deleteScene(sceneId: string): Promise<{ success: boolean; id: string; message: string }> {
  const r = await fetchWithAuth(`/api/scenes/${sceneId}`, { method: 'DELETE' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to delete scene.');
  }
  return r.json();
}

/** Delete all scenes (and their child surfaces/ad-slots/approvals) for a content item. Fails if any surface is Approved. */
export async function deleteAllScenes(contentId: string): Promise<{ success: boolean; contentId: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/scenes`, { method: 'DELETE' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to delete all scenes.');
  }
  return r.json();
}

/** Delete a single surface and its child ad-slots/approvals. Fails if the surface is Approved. */
export async function deleteSurface(surfaceId: string): Promise<{ success: boolean; id: string; message: string }> {
  const r = await fetchWithAuth(`/api/surfaces/${surfaceId}`, { method: 'DELETE' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to delete surface.');
  }
  return r.json();
}

/** Delete all surfaces for a scene. Fails if any surface is Approved. */
export async function deleteAllSurfaces(sceneId: string): Promise<{ success: boolean; deletedCount: number; message: string }> {
  const r = await fetchWithAuth(`/api/scenes/${sceneId}/surfaces`, { method: 'DELETE' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to delete all surfaces.');
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

/** Mark (or unmark) a render as the chosen one for its scene in the content's final assembled video. */
export async function setRenderQueuedForFinal(renderId: string, queued: boolean): Promise<RenderItem> {
  const r = await fetchWithAuth(`/api/renders/${renderId}/queue-for-final`, {
    method: 'PUT',
    body: JSON.stringify({ queued }),
  });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to update render queue status.');
  }
  return r.json();
}

/** Delete a render and its output files. */
export async function deleteRender(renderId: string): Promise<{ success: boolean }> {
  const r = await fetchWithAuth(`/api/renders/${renderId}`, { method: 'DELETE' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to delete render.');
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

/** Assemble one final video combining every scene: each scene's queued render if it has one, original footage otherwise. */
export async function startFinalAssembly(
  contentId: string,
): Promise<{ id: string; finalAssemblyStatus: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/final-assembly`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to start final assembly.');
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

export interface AttentionCounts {
  totalAttention: number;
  pendingRoleRequests: number;
  pendingSurfaces: number;
  failedRenders: number;
  failedContent: number;
  activeAlarms: number;
}

/** Dismisses the current backlog for one AttentionBell category ("pendingSurfaces" |
 * "failedRenders" | "failedContent") — items created after this call still count going
 * forward, so genuinely new items surface again. See AttentionController.cs. */
export async function dismissAttentionCategory(category: string): Promise<AttentionCounts> {
  const r = await fetchWithAuth('/api/notifications/attention/dismiss', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ category }),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to dismiss.' }));
    throw new Error(err.error || 'Failed to dismiss.');
  }
  return r.json();
}

export interface CampaignSummary {
  hasApprovedPlacements: boolean;
}

export async function fetchCampaignSummary(campaignId: string): Promise<CampaignSummary> {
  const r = await fetchWithAuth(`/api/campaigns/${campaignId}/summary`);
  return r.json();
}

/** Real, backend-calculated campaign invoice (exposure seconds × viability multiplier + render
 * processing costs + VAT) — see dotnet-api/Services/InvoiceService.cs. */
export async function fetchCampaignInvoice(campaignId: string): Promise<InvoiceSummary> {
  const r = await fetchWithAuth(`/api/campaigns/${campaignId}/invoice`);
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to load invoice.' }));
    throw new Error(err.error || 'Failed to load invoice.');
  }
  return r.json();
}

// ─── Interactive Placement API ─────────────────────────────────────────

import type {
  SegmentPreviewRequest,
  SegmentPreviewResponse,
  InteractiveRenderRequest,
  CreateSurfaceFromClickRequest,
  CreateSurfaceFromQuadRequest,
  CreateSurfaceResponse,
  RenderItem,
  CreatePromptRenderRequest,
} from './types';

export type {
  SegmentPreviewRequest,
  SegmentPreviewResponse,
  InteractiveRenderRequest,
  CreateSurfaceFromClickRequest,
  CreateSurfaceFromQuadRequest,
  CreateSurfaceResponse,
};

/**
 * Preview-segment a clicked point on a video frame using SAM3 video-rle.
 * Returns a mask polygon for SVG overlay in the placement editor.
 */
export async function previewSegment(dto: SegmentPreviewRequest): Promise<SegmentPreviewResponse> {
  const r = await fetchWithAuth('/api/surfaces/preview-segment', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Preview segmentation failed.' }));
    throw new Error(err.error || 'Preview segmentation failed.');
  }
  return r.json();
}

/**
 * Dispatch an interactive placement render.
 * Routes to generative (pikaswaps) or planar (homography warp) based on assetType.
 */
export async function confirmInteractivePlacement(dto: InteractiveRenderRequest): Promise<{ renderId: string }> {
  const r = await fetchWithAuth('/api/renders/interactive', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to dispatch render.' }));
    throw new Error(err.error || 'Failed to dispatch render.');
  }
  return r.json();
}

/**
 * Dispatch a prompt-based AI placement preview (the "AI Placement Assistant → Generate New"
 * flow). No surfaceId required — the AI model infers placement purely from promptText plus the
 * asset image. Returns the render immediately in "Queued" status; poll renderList for
 * renderStatus "PreviewReady" before offering approvePromptSplice/rejectPromptPlacement.
 */
export async function submitPromptPlacement(dto: CreatePromptRenderRequest): Promise<RenderItem> {
  const r = await fetchWithAuth('/api/renders/prompt-preview', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to dispatch prompt placement.' }));
    throw new Error(err.error || 'Failed to dispatch prompt placement.');
  }
  return r.json();
}

/** Approve a PreviewReady prompt-placement render — splices it into the full source video. */
export async function approvePromptSplice(renderId: string): Promise<RenderItem> {
  const r = await fetchWithAuth(`/api/renders/${renderId}/approve-splice`, {
    method: 'POST',
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to approve placement.' }));
    throw new Error(err.error || 'Failed to approve placement.');
  }
  return r.json();
}

/** Reject a PreviewReady prompt-placement render — no splice, no final video produced. */
export async function rejectPromptPlacement(renderId: string, reason?: string): Promise<{ success: boolean }> {
  const r = await fetchWithAuth(`/api/renders/${renderId}/reject-prompt`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to reject placement.' }));
    throw new Error(err.error || 'Failed to reject placement.');
  }
  return r.json();
}

/**
 * Persist a SurfaceItem from an interactive "Insert Product" click (SAM3 mask).
 * Must be called before confirmInteractivePlacement — the render dispatch requires a real surfaceId.
 */
export async function createSurfaceFromClick(dto: CreateSurfaceFromClickRequest): Promise<CreateSurfaceResponse> {
  const r = await fetchWithAuth('/api/surfaces/from-click', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to create surface.' }));
    throw new Error(err.error || 'Failed to create surface.');
  }
  return r.json();
}

/**
 * Persist a SurfaceItem from an interactive "Place Signage" 4-corner quad.
 * Must be called before confirmInteractivePlacement — the render dispatch requires a real surfaceId.
 */
export async function createSurfaceFromQuad(dto: CreateSurfaceFromQuadRequest): Promise<CreateSurfaceResponse> {
  const r = await fetchWithAuth('/api/surfaces/from-quad', {
    method: 'POST',
    body: JSON.stringify(dto),
  });
  if (!r.ok) {
    const err = await r.json().catch(() => ({ error: 'Failed to create surface.' }));
    throw new Error(err.error || 'Failed to create surface.');
  }
  return r.json();
}
