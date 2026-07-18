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
  const res = await fetchPublic('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify(credentials),
  });
  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error || 'Authentication failed');
  }
  // Persist session
  setToken(data.token);
  setSavedUser(data.user);
  return data as LoginResponse;
}

export function logout(): void {
  clearToken();
}
