/**
 * Centralized API client for the BIT platform.
 * All HTTP calls to the .NET backend go through this module.
 * The Vite dev server proxies /api/* to the .NET backend,
 * so relative URLs work in both dev and production.
 */

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

/**
 * fetch() with JWT Bearer token attached from localStorage.
 * Relative URLs like '/api/content' work because of the Vite proxy.
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
  return fetch(url, { ...options, headers });
}

/** Unauthenticated fetch (for login). */
export async function fetchPublic(
  url: string,
  options: RequestInit = {},
): Promise<Response> {
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...options.headers,
  };
  return fetch(url, { ...options, headers });
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

/** Re-run scene detection for a content item. */
export async function redetectScenes(
  contentId: string,
): Promise<{ success: boolean; id: string; ingestionStatus: string; message: string }> {
  const r = await fetchWithAuth(`/api/content/${contentId}/redetect-scenes`, { method: 'POST' });
  if (!r.ok) {
    const data = await r.json();
    throw new Error(data.error || 'Failed to restart scene detection.');
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
