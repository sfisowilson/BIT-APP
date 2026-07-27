import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { motion, AnimatePresence } from 'motion/react';
import { 
  Download, 
  Sliders, 
  Video, 
  Tv, 
  Cpu, 
  Activity, 
  FileText, 
  AlertTriangle, 
  RefreshCw,
  Lock,
  Unlock,
  Shield,
  KeyRound,
  Users,
  Code2,
  LogOut,
  Package,
  Plus,
  Sun,
  Moon,
  BarChart3,
  Clock,
  UserPlus,
  HelpCircle
} from 'lucide-react';

import { 
  ContentItem, 
  SceneItem, 
  SurfaceItem, 
  SurfaceItemResponse,
  parseSurfaceItem,
  CampaignItem, 
  CreativeAsset, 
  RenderItem, 
  EventLog, 
  AlarmItem, 
  SurfaceAssetPair,
  TIMELINE_DATA 
} from './types';
import { DOCUMENT_CONTENT } from './document';
import { login as apiLogin, fetchWithAuth as apiFetchWithAuth, getToken, setToken, clearToken, getSavedUser, setSavedUser, type UserSession, retranscode, redetectScenes, detectScenesOnly, detectSurfacesForScene, resetPipeline, refreshToken, fetchStatsSummary, fetchSurfacesBatch, type StatsSummary } from './apiClient';
import { useChunkedUpload } from './hooks/useChunkedUpload';
import { useSignalR, type DetectionProgressEvent, type RenderProgressEvent, type ContentStatusEvent, type AlarmEvent, type NotificationEvent } from './hooks/useSignalR';

// Import our modular sub-components
import { CampaignsTab } from './components/CampaignsTab';
import { IngestionTab } from './components/IngestionTab';
import { EditorTab } from './components/EditorTab';
import { RendersTab } from './components/RendersTab';
import { TelemetryTab } from './components/TelemetryTab';
import { AdminConsoleTab } from './components/AdminConsoleTab';
import { CampaignSelector } from './components/CampaignSelector';
import { CampaignSidebar, type SidebarView } from './components/CampaignSidebar';
import { CampaignDashboard } from './components/CampaignDashboard';
import { AnalyticsTab } from './components/AnalyticsTab';
import { JobsTab } from './components/JobsTab';
import { BitLogo } from './components/BitLogo';
import { useIdleTimer } from './hooks/useIdleTimer';
import { NotificationPreferencesPanel } from './components/NotificationPreferencesPanel';
import { AttentionBell } from './components/AttentionBell';
import { NotFoundPage } from './components/NotFoundPage';

export default function App() {
  const navigate = useNavigate();
  const location = useLocation();

  // ── URL-derived state (enables link sharing & back/forward navigation) ──
  // URL patterns:
  //   /                          → landing (no campaign)
  //   /c/:campaignId             → dashboard for campaign
  //   /c/:campaignId/:view       → specific view (assets|content|placements|renders|reports)
  //   /admin                     → admin console
  //   /telemetry                 → telemetry
  const { activeView, selectedCampaignId } = useMemo(() => {
    const parts = location.pathname.split('/').filter(Boolean);
    if (parts[0] === 'c' && parts[1]) {
      const view = (parts[2] as SidebarView) || 'dashboard';
      return { activeView: view, selectedCampaignId: parts[1] };
    }
    if (parts[0] === 'admin') return { activeView: 'admin' as SidebarView, selectedCampaignId: null };
    if (parts[0] === 'telemetry') return { activeView: 'telemetry' as SidebarView, selectedCampaignId: null };
    if (parts[0] === 'analytics') return { activeView: 'analytics' as SidebarView, selectedCampaignId: null };
    if (parts[0] === 'jobs') return { activeView: 'jobs' as SidebarView, selectedCampaignId: null };
    return { activeView: null, selectedCampaignId: null };
  }, [location.pathname]);

  /** Navigate to a specific view, optionally within a campaign context */
  const navigateTo = useCallback((view: SidebarView | null, campaignId?: string | null) => {
    if (!view) {
      navigate('/');
    } else if (campaignId) {
      navigate(`/c/${campaignId}${view === 'dashboard' ? '' : `/${view}`}`);
    } else if (view === 'admin') {
      navigate('/admin');
    } else if (view === 'telemetry') {
      navigate('/telemetry');
    } else {
      navigate('/');
    }
  }, [navigate]);

  const [selectedDay, setSelectedDay] = useState<number>(1);
  const [downloading, setDownloading] = useState<boolean>(false);

  // Theme state (MReq: Dark / Light Mode Switcher)
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    return (localStorage.getItem('bit_theme') as 'light' | 'dark') || 'light';
  });

  useEffect(() => {
    localStorage.setItem('bit_theme', theme);
    const root = document.documentElement;
    if (theme === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
  }, [theme]);

  // Authentication & RBAC States (MReq 8, 9)
  const [user, setUser] = useState<{ id: string; fullName: string; email: string; role: 'Admin' | 'Editor' | 'Advertiser' } | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loginEmail, setLoginEmail] = useState<string>('');
  const [loginPassword, setLoginPassword] = useState<string>('');
  const [authError, setAuthError] = useState<string | null>(null);

  // App States representing the operational UI
  const [contentList, setContentList] = useState<ContentItem[]>([]);
  const [aiAnalyzingVideoId, setAiAnalyzingVideoId] = useState<string | null>(null);
  const [isPipelineActionPending, setIsPipelineActionPending] = useState<string | null>(null);
  const [sceneRefreshKey, setSceneRefreshKey] = useState(0); // incremented to force scene refresh on detection complete
  const [statsSummary, setStatsSummary] = useState<StatsSummary | null>(null);
  const [statsLoading, setStatsLoading] = useState(false);
  const [showRoleRequest, setShowRoleRequest] = useState(false);
  const [requestedRole, setRequestedRole] = useState('Editor');
  const [roleRequestReason, setRoleRequestReason] = useState('');
  const [roleRequestMsg, setRoleRequestMsg] = useState<{type:'success'|'error',text:string}|null>(null);
  const [showForgotPassword, setShowForgotPassword] = useState(false);
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotSending, setForgotSending] = useState(false);
  const [forgotMsg, setForgotMsg] = useState<{type:'success'|'error',text:string}|null>(null);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [changingPassword, setChangingPassword] = useState(false);
  const [passwordChangeMsg, setPasswordChangeMsg] = useState<{type:'success'|'error',text:string}|null>(null);
  const [campaignList, setCampaignList] = useState<CampaignItem[]>([]);
  const [assetList, setAssetList] = useState<CreativeAsset[]>([]);
  const [renderList, setRenderList] = useState<RenderItem[]>([]);
  const [logList, setLogList] = useState<EventLog[]>([]);
  const [alarmList, setAlarmList] = useState<AlarmItem[]>([]);

  // Selection / Form states
  const [selectedVideo, setSelectedVideo] = useState<string>('');
  const [scenesForVideo, setScenesForVideo] = useState<SceneItem[]>([]);
  const [selectedSceneId, setSelectedSceneId] = useState<string>('');
  const [surfacesForScene, setSurfacesForScene] = useState<SurfaceItem[]>([]);
  const [selectedSurfaceId, setSelectedSurfaceId] = useState<string>('');
  const [rejectionReason, setRejectionReason] = useState<string>('');
  const [surfacesByScene, setSurfacesByScene] = useState<Record<string, SurfaceItem[]>>({});

  // Phase 2: Asset placement tracking (surfaceId -> assetId)
  const [surfaceAssetPairs, setSurfaceAssetPairs] = useState<Record<string, string>>({});

  // Phase 3: AI asset suggestion state
  const [isSuggestingAssets, setIsSuggestingAssets] = useState<Record<string, boolean>>({});
  const [aiSuggestions, setAiSuggestions] = useState<Record<string, { assetId: string; reason: string }[]>>({});

  // Form Submissions
  const [newCampaignName, setNewCampaignName] = useState<string>('');
  const [newCampaignCode, setNewCampaignCode] = useState<string>('');
  const [newCampaignBudget, setNewCampaignBudget] = useState<string>('');
  const [newCampaignRegion, setNewCampaignRegion] = useState<string>('SADC Region');
  const [campaignError, setCampaignError] = useState<string | null>(null);

  const [newAssetName, setNewAssetName] = useState<string>('');
  const [newAssetType, setNewAssetType] = useState<"Image" | "Logo" | "Video">('Image');
  const [newAssetCategory, setNewAssetCategory] = useState<string>('Beverages (Non-Alcoholic)');
  const [newAssetFile, setNewAssetFile] = useState<File | null>(null);

  const [newVideoTitle, setNewVideoTitle] = useState<string>('');
  const [newVideoRes, setNewVideoRes] = useState<string>('1920x1080 (1080p)');
  const [newVideoFps, setNewVideoFps] = useState<number>(50);
  const [newVideoDuration, setNewVideoDuration] = useState<string>('00:05:00');
  const [newVideoChannel, setNewVideoChannel] = useState<string>('SuperSport Variety');
  const [newVideoFile, setNewVideoFile] = useState<File | null>(null);
  const [ingestError, setIngestError] = useState<string | null>(null);
  const [ingesting, setIngesting] = useState<boolean>(false);
  const [uploadProgress, setUploadProgress] = useState<number>(0); // 0-100
  const [chunkProgress, setChunkProgress] = useState<string>(''); // e.g. "12/48 chunks"
  const chunkedUpload = useChunkedUpload({ chunkSizeMB: 25, maxConcurrent: 3 });

  // Dispatch Composite Renders
  const [composerCampaignId, setComposerCampaignId] = useState<string>('');
  const [composerAssetId, setComposerAssetId] = useState<string>('');
  const [composerPreset, setComposerPreset] = useState<string>('Broadcast-ProRes');

  // Alarm simulation
  const [alarmSimSeverity, setAlarmSimSeverity] = useState<"Minor" | "Major" | "Critical">('Major');
  const [alarmSimSource, setAlarmSimSource] = useState<string>('S3 Storage Engine');
  const [alarmSimDesc, setAlarmSimDesc] = useState<string>('S3 bucket staging path connection timed out after 5 consecutive retry attempts.');

  // Handle identity verification (MReq 8, 9)
  const handleLogin = async (e?: React.FormEvent, bypassCredentials?: { email: string; pass: string }) => {
    if (e) e.preventDefault();
    setAuthError(null);
    const email = bypassCredentials ? bypassCredentials.email : loginEmail;
    const password = bypassCredentials ? bypassCredentials.pass : loginPassword;

    try {
      const data = await apiLogin({ email, password });
      // apiLogin already persists token + user to localStorage
      // Sync React state so the login gate lifts
      setToken(data.token);
      setUser(data.user);
      // Clear form fields after successful login
      setLoginEmail('');
      setLoginPassword('');
    } catch (err: any) {
      console.error(err);
      setAuthError(err.message || "Authentication failed.");
    }
  };

  const handleLogout = () => {
    // Clear React auth state
    setToken(null);
    setUser(null);
    // Clear persisted session
    clearToken();
    // Reset all operational data so next user starts fresh
    setContentList([]);
    setCampaignList([]);
    setAssetList([]);
    setRenderList([]);
    setLogList([]);
    setAlarmList([]);
    setScenesForVideo([]);
    setSurfacesForScene([]);
    setSelectedVideo('');
    setSelectedSceneId('');
    setSelectedSurfaceId('');
    setSurfaceAssetPairs({});
    setAiSuggestions({});
    setIsSuggestingAssets({});
    // Navigate to landing page (clears URL)
    navigate('/');
  };

  // MReq 9: Submit a role elevation request
  const handleRequestRole = async () => {
    setRoleRequestMsg(null);
    try {
      const res = await fetchWithAuth('/api/user/request-role', {
        method: 'POST',
        body: JSON.stringify({ requestedRole, reason: roleRequestReason }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Request failed.');
      setRoleRequestMsg({ type: 'success', text: data.message });
      setTimeout(() => { setShowRoleRequest(false); setRoleRequestMsg(null); }, 2000);
    } catch (err: any) {
      setRoleRequestMsg({ type: 'error', text: err.message });
    }
  };

  // Forgot password handler
  const handleForgotPassword = async () => {
    if (!forgotEmail.trim()) return;
    setForgotSending(true);
    setForgotMsg(null);
    try {
      const res = await fetchWithAuth('/api/auth/forgot-password', {
        method: 'POST',
        body: JSON.stringify({ email: forgotEmail }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Failed.');
      setForgotMsg({ type: 'success', text: data.message });
    } catch (err: any) {
      setForgotMsg({ type: 'error', text: err.message });
    } finally {
      setForgotSending(false);
    }
  };

  // Change password handler
  const handleChangePassword = async () => {
    if (!currentPassword || !newPassword) return;
    setChangingPassword(true);
    setPasswordChangeMsg(null);
    try {
      const res = await fetchWithAuth('/api/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Failed.');
      setPasswordChangeMsg({ type: 'success', text: data.message });
      setCurrentPassword(''); setNewPassword('');
    } catch (err: any) {
      setPasswordChangeMsg({ type: 'error', text: err.message });
    } finally {
      setChangingPassword(false);
    }
  };

  // MReq 8: Idle timeout — auto-logout after 28 min inactivity + 60s countdown
  const { showCountdown, secondsRemaining, resetTimer } = useIdleTimer({
    idleMinutes: 28,
    countdownSeconds: 60,
    onTimeout: handleLogout,
  });

  // MReq 8: Attempt silent token refresh on activity
  useEffect(() => {
    if (!user) return;
    const refreshInterval = setInterval(async () => {
      const refreshed = await refreshToken();
      if (refreshed?.token) {
        setToken(refreshed.token);
        setUser(refreshed.user as typeof user);
      }
    }, 30 * 60 * 1000); // every 30 minutes
    return () => clearInterval(refreshInterval);
  }, [user]);

  // MReq 19: Fetch stats when analytics tab is active
  useEffect(() => {
    if (activeView !== 'analytics' || !user) return;
    setStatsLoading(true);
    fetchStatsSummary()
      .then(setStatsSummary)
      .catch(console.error)
      .finally(() => setStatsLoading(false));
  }, [activeView, user]);

  // Secure request broker — 500/network errors intercepted by apiClient
  const fetchWithAuth = async (url: string, options: RequestInit = {}) => {
    const res = await apiFetchWithAuth(url, options);
    if (res.status === 401) {
      handleLogout();
      throw new Error('Session expired. Please sign in again.');
    }
    return res;
  };

  // Restore saved session from localStorage on mount (MReq 8)
  // Validates the stored token against the API — stale/fake tokens are discarded.
  // If the API is unreachable, we optimistically restore (don't punish for cold starts).
  useEffect(() => {
    const restore = async () => {
      const savedToken = getToken();
      const savedUser = getSavedUser<UserSession>();
      if (!savedToken || !savedUser) return;

      try {
        const res = await fetchPublic('/api/auth/validate', {
          method: 'POST',
          body: JSON.stringify({ token: savedToken }),
        });
        if (res.ok) {
          setToken(savedToken);
          setUser(savedUser);
        } else if (res.status === 401) {
          // Token explicitly rejected — clear stale session
          clearToken();
        } else {
          // Unexpected response — restore optimistically
          setToken(savedToken);
          setUser(savedUser);
        }
      } catch {
        // API unreachable (cold start, network) — restore optimistically
        setToken(savedToken);
        setUser(savedUser);
      }
    };
    restore();
  }, []);

  // Fetch exactly the data needed for the current view — no over-fetching.
  const fetchViewData = async () => {
    if (!token) return;
    try {
      const fetchJson = async (url: string) => {
        const r = await fetchWithAuth(url);
        return r.json();
      };

      const campaignParam = selectedCampaignId ? `?campaignId=${selectedCampaignId}` : '';

      // Campaigns are always needed (sidebar navigation)
      const campaignsPromise = fetchJson('/api/campaigns').then((data: any) => {
        setCampaignList(data.items || data);
      });

      // Logs + alarms are lightweight and cross-cutting
      const logsPromise = fetchJson('/api/logs').then((data: any) => {
        setLogList(data.items || data);
      });
      const alarmsPromise = fetchJson('/api/alarms').then((data: any) => {
        setAlarmList(data.items || data);
      });

      // Content / assets / renders only needed in campaign-scoped views
      const isCampaignView = activeView && !['admin', 'telemetry', 'analytics', 'jobs'].includes(activeView) && selectedCampaignId;

      if (isCampaignView) {
        const [contentRes, assetsRes, rendersRes] = await Promise.allSettled([
          fetchJson(`/api/content${campaignParam}`),
          fetchJson(`/api/assets${campaignParam}`),
          fetchJson(`/api/renders${campaignParam}`),
        ]);
        if (contentRes.status === 'fulfilled') { const data = contentRes.value as any; setContentList(data.items || data); }
        if (assetsRes.status === 'fulfilled') {
          const data = assetsRes.value as any;
          const assets = (data.items || data) as CreativeAsset[];
          setAssetList(assets.map(a => ({ ...a, thumbnailUrl: a.storageKey?.startsWith('/api/') ? a.storageKey : a.thumbnailUrl })));
        }
        if (rendersRes.status === 'fulfilled') { const data = rendersRes.value as any; setRenderList(data.items || data); }
      } else {
        // Non-campaign views: still need campaigns, logs, alarms
      }

      await Promise.allSettled([campaignsPromise, logsPromise, alarmsPromise]);
    } catch (err) {
      console.error("API Fetch Error:", err);
    }
  };

  // Full data refresh — only used after mutations (render, detect, approve, etc.)
  const fetchAllData = async () => {
    if (!token) return;
    try {
      const fetchJson = async (url: string) => {
        const r = await fetchWithAuth(url);
        return r.json();
      };
      const campaignParam = selectedCampaignId ? `?campaignId=${selectedCampaignId}` : '';
      const [contentRes, campaignsRes, assetsRes, rendersRes, logsRes, alarmsRes] = await Promise.allSettled([
        fetchJson(`/api/content${campaignParam}`),
        fetchJson('/api/campaigns'),
        fetchJson(`/api/assets${campaignParam}`),
        fetchJson(`/api/renders${campaignParam}`),
        fetchJson('/api/logs'),
        fetchJson('/api/alarms'),
      ]);
      if (contentRes.status === 'fulfilled') { const data = contentRes.value as any; setContentList(data.items || data); }
      if (campaignsRes.status === 'fulfilled') { const data = campaignsRes.value as any; setCampaignList(data.items || data); }
      if (assetsRes.status === 'fulfilled') {
        const data = assetsRes.value as any;
        const assets = (data.items || data) as CreativeAsset[];
        setAssetList(assets.map(a => ({ ...a, thumbnailUrl: a.storageKey?.startsWith('/api/') ? a.storageKey : a.thumbnailUrl })));
      }
      if (rendersRes.status === 'fulfilled') { const data = rendersRes.value as any; setRenderList(data.items || data); }
      if (logsRes.status === 'fulfilled') { const data = logsRes.value as any; setLogList(data.items || data); }
      if (alarmsRes.status === 'fulfilled') { const data = alarmsRes.value as any; setAlarmList(data.items || data); }

      const failures = [
        { name: 'content', res: contentRes }, { name: 'campaigns', res: campaignsRes },
        { name: 'assets', res: assetsRes }, { name: 'renders', res: rendersRes },
        { name: 'logs', res: logsRes }, { name: 'alarms', res: alarmsRes },
      ].filter(f => f.res.status === 'rejected');
      if (failures.length > 0) console.warn('Some API endpoints failed:', failures.map(f => `${f.name}: ${(f.res as PromiseRejectedResult).reason}`));
    } catch (err) {
      console.error("API Fetch Error:", err);
    }
  };

  // Lightweight poll: only refresh operational data (renders, alarms, logs).
  // Never touches contentList/campaignList/assetList — avoids disrupting the video player
  // and placement workbench which depend on stable content/scene/surface state.
  const fetchOperationalData = async () => {
    if (!token) return;
    try {
      const fetchJson = async (url: string) => {
        const r = await fetchWithAuth(url);
        return r.json();
      };
      const campaignParam = selectedCampaignId ? `?campaignId=${selectedCampaignId}` : '';
      const [rendersRes, logsRes, alarmsRes] = await Promise.allSettled([
        fetchJson(`/api/renders${campaignParam}`),
        fetchJson('/api/logs'),
        fetchJson('/api/alarms')
      ]);
      if (rendersRes.status === 'fulfilled') {
        const data = rendersRes.value as any;
        setRenderList(data.items || data);
      }
      if (logsRes.status === 'fulfilled') {
        const data = logsRes.value as any;
        setLogList(data.items || data);
      }
      if (alarmsRes.status === 'fulfilled') {
        const data = alarmsRes.value as any;
        setAlarmList(data.items || data);
      }
    } catch { /* silent — polling should never break the UI */ }
  };

  // Load only what the current view needs. Re-fetches when route changes.
  useEffect(() => {
    if (token) {
      fetchViewData();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, activeView, selectedCampaignId]);

  // ── SignalR — real-time push for detection/render progress, content status, alarms & logs ──
  // All live updates flow through here; no polling needed.
  const { connectionState: _signalRState } = useSignalR({
    onDetectionProgress: (e: DetectionProgressEvent) => {
      // Update detection progress for the content item in local state
      setContentList(prev => prev.map(c =>
        c.id === e.contentId ? { ...c, detectionProgress: e.percent } : c
      ));
      // Clear AI analyzing flag when complete or failed
      if (e.percent >= 100 || e.status === 'Failed') {
        setAiAnalyzingVideoId(prev => prev === e.contentId ? null : prev);
        setIsPipelineActionPending(prev => prev === e.contentId ? null : prev);
        // Force scene refresh for the currently selected video
        setSceneRefreshKey(k => k + 1);
        // Refresh content data on completion
        fetchAllData();
      }
    },
    onRenderProgress: (e: RenderProgressEvent) => {
      setRenderList(prev => prev.map(r =>
        r.id === e.renderId ? { ...r, progress: e.percent, renderStatus: e.percent >= 100 ? 'Finished' : r.renderStatus } : r
      ));
      if (e.percent >= 100) {
        fetchOperationalData();
      }
    },
    onContentStatusChanged: (e: ContentStatusEvent) => {
      setContentList(prev => prev.map(c =>
        c.id === e.contentId ? { ...c, ingestionStatus: e.newStatus } : c
      ));
      if (e.newStatus === 'Completed' || e.newStatus === 'Ready' || e.newStatus === 'Failed' || e.newStatus === 'SurfacesReady') {
        setAiAnalyzingVideoId(prev => prev === e.contentId ? null : prev);
        setIsPipelineActionPending(prev => prev === e.contentId ? null : prev);
        setSceneRefreshKey(k => k + 1);
        fetchAllData();
      }
    },
    onAlarmEvent: (_alarm: AlarmEvent) => {
      fetchOperationalData();
    },
    onNotification: (_n: NotificationEvent) => {
      fetchOperationalData();
    },
  });

  // Redirect to landing if the campaign ID in the URL doesn't exist (once campaigns are loaded)
  useEffect(() => {
    if (selectedCampaignId && campaignList.length > 0) {
      const exists = campaignList.some(c => c.id === selectedCampaignId);
      if (!exists) {
        navigate('/', { replace: true });
      }
    }
  }, [selectedCampaignId, campaignList, navigate]);

  // Validate that the URL view is a known SidebarView — redirect to dashboard if not
  const VALID_VIEWS: string[] = ['dashboard', 'assets', 'content', 'placements', 'renders', 'reports', 'admin', 'telemetry', 'analytics', 'jobs'];
  useEffect(() => {
    if (activeView && !VALID_VIEWS.includes(activeView)) {
      if (selectedCampaignId) {
        navigate(`/c/${selectedCampaignId}`, { replace: true });
      } else {
        navigate('/', { replace: true });
      }
    }
  }, [activeView, selectedCampaignId, navigate]);

  // Update document title based on current route (for link sharing / bookmarks)
  useEffect(() => {
    const campaignName = selectedCampaignId
      ? campaignList.find(c => c.id === selectedCampaignId)?.name
      : null;
    const viewLabel = activeView
      ? activeView.charAt(0).toUpperCase() + activeView.slice(1)
      : '';
    if (campaignName && viewLabel) {
      document.title = `${campaignName} · ${viewLabel} — BIT Platform`;
    } else if (campaignName) {
      document.title = `${campaignName} — BIT Platform`;
    } else if (activeView) {
      document.title = `${viewLabel} — BIT Platform`;
    } else {
      document.title = 'Brand Inserts Technology (BIT)';
    }
  }, [activeView, selectedCampaignId, campaignList]);

  // Auto-select first completed video when contentList loads and no valid video is selected
  useEffect(() => {
    if (!token || contentList.length === 0) return;
    const completed = contentList.filter(v => v.ingestionStatus === 'Completed');
    if (completed.length === 0) return;
    const exists = completed.some(v => v.id === selectedVideo);
    if (!selectedVideo || !exists) {
      setSelectedVideo(completed[0].id);
    }
  }, [contentList, token]);

  // Fetch scenes when selected video changes — also batch-fetch all surfaces
  useEffect(() => {
    if (!selectedVideo || !token) return;
    fetchWithAuth(`/api/content/${selectedVideo}/scenes`)
      .then(r => r.json())
      .then(async (data: SceneItem[]) => {
        setScenesForVideo(data);
        if (data.length > 0) {
          setSelectedSceneId(data[0].id);
          // Batch-fetch surfaces for ALL scenes in one request
          const sceneIds = data.map((s: SceneItem) => s.id);
          try {
            const allSurfaces = await fetchSurfacesBatch(sceneIds);
            const byScene: Record<string, SurfaceItem[]> = {};
            for (const sf of allSurfaces) {
              const parsed = parseSurfaceItem(sf);
              if (!byScene[sf.sceneId]) byScene[sf.sceneId] = [];
              byScene[sf.sceneId].push(parsed);
            }
            setSurfacesByScene(byScene);
            setSurfacesForScene(byScene[data[0].id] || []);
          } catch {
            setSurfacesByScene({});
            setSurfacesForScene([]);
          }
        } else {
          setSelectedSceneId('');
          setSurfacesForScene([]);
          setSurfacesByScene({});
          setSelectedSurfaceId('');
        }
      })
      .catch(() => {
        setScenesForVideo([]);
        setSelectedSceneId('');
        setSurfacesForScene([]);
        setSurfacesByScene({});
        setSelectedSurfaceId('');
      });
  }, [selectedVideo, token, sceneRefreshKey]);

  // Use cached surfaces from batch fetch when selected scene changes
  useEffect(() => {
    if (!selectedSceneId) return;
    const cached = surfacesByScene[selectedSceneId];
    if (cached) {
      setSurfacesForScene(cached);
      if (cached.length > 0) {
        setSelectedSurfaceId(cached[0].id);
      } else {
        setSelectedSurfaceId('');
      }
    } else {
      setSurfacesForScene([]);
      setSelectedSurfaceId('');
    }
  }, [selectedSceneId, surfacesByScene]);

  // Sync composer campaign with selected campaign context (MReq 10)
  useEffect(() => {
    if (selectedCampaignId && !composerCampaignId) {
      setComposerCampaignId(selectedCampaignId);
    }
  }, [selectedCampaignId]);

  // Sync chunked upload progress to App state for UI display
  useEffect(() => {
    if (chunkedUpload.state.uploading) {
      setUploadProgress(chunkedUpload.state.progress);
      setChunkProgress(chunkedUpload.state.chunkProgress);
    }
  }, [chunkedUpload.state.progress, chunkedUpload.state.chunkProgress, chunkedUpload.state.uploading]);

  // Handle Campaign Creation
  const handleCreateCampaign = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    setCampaignError(null);
    try {
      const res = await fetchWithAuth('/api/campaigns', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newCampaignName,
          namingStructureCode: newCampaignCode,
          totalBudget: Number(newCampaignBudget),
          targetRegion: newCampaignRegion
        })
      });

      const data = await res.json();
      if (!res.ok) {
        setCampaignError(data.error);
        return;
      }

      setNewCampaignName('');
      setNewCampaignCode('');
      setNewCampaignBudget('');
      fetchAllData();
    } catch (err) {
      console.error(err);
      setCampaignError("API route communication failure.");
    }
  };

  // Handle Asset library upload (MReq 10: with real file upload support)
  const handleUploadAsset = async (e: React.FormEvent, campaignId?: string) => {
    e.preventDefault();
    if (!newAssetName) return;
    try {
      if (newAssetFile) {
        // File upload via multipart form
        const formData = new FormData();
        formData.append('name', newAssetName);
        formData.append('type', newAssetType);
        formData.append('brandCategory', newAssetCategory);
        if (campaignId) formData.append('campaignId', campaignId);
        formData.append('file', newAssetFile);

        const token = getToken();
        await fetch('/api/assets/upload', {
          method: 'POST',
          headers: token ? { 'Authorization': `Bearer ${token}` } : {},
          body: formData,
        });
      } else {
        // JSON-only creation
        await fetchWithAuth('/api/assets', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            name: newAssetName,
            type: newAssetType,
            brandCategory: newAssetCategory,
            campaignId: campaignId || null
          })
        });
      }
      setNewAssetName('');
      setNewAssetFile(null);
      fetchAllData();
    } catch (err) {
      console.error(err);
    }
  };

  // Handle updating an existing asset
  const handleUpdateAsset = async (assetId: string, data: { name?: string; type?: string; brandCategory?: string; file?: File }) => {
    try {
      if (data.file) {
        const formData = new FormData();
        if (data.name) formData.append('name', data.name);
        if (data.type) formData.append('type', data.type);
        if (data.brandCategory) formData.append('brandCategory', data.brandCategory);
        formData.append('file', data.file);

        const token = getToken();
        await fetch(`/api/assets/${assetId}/upload`, {
          method: 'PUT',
          headers: token ? { 'Authorization': `Bearer ${token}` } : {},
          body: formData,
        });
      } else {
        await fetchWithAuth(`/api/assets/${assetId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(data)
        });
      }
      fetchAllData();
    } catch (err) {
      console.error('Failed to update asset:', err);
    }
  };

  // Handle associating an existing asset with a campaign
  const handleAssociateAsset = async (assetId: string, campaignId: string) => {
    try {
      await fetchWithAuth(`/api/assets/${assetId}/campaign/${campaignId}`, {
        method: 'PUT'
      });
      fetchAllData();
    } catch (err) {
      console.error('Failed to associate asset:', err);
    }
  };

  // Handle removing an asset from its campaign
  const handleUnassociateAsset = async (assetId: string) => {
    try {
      await fetchWithAuth(`/api/assets/${assetId}/unassociate`, {
        method: 'PUT'
      });
      fetchAllData();
    } catch (err) {
      console.error('Failed to unassociate asset:', err);
    }
  };

  // Handle deleting a creative asset
  const handleDeleteAsset = async (id: string) => {
    try {
      await fetchWithAuth(`/api/assets/${id}`, {
        method: 'DELETE'
      });
      fetchAllData();
    } catch (err) {
      console.error("Failed to delete asset:", err);
    }
  };

  // Handle deleting a campaign
  const handleDeleteCampaign = async (id: string) => {
    try {
      await fetchWithAuth(`/api/campaigns/${id}`, {
        method: 'DELETE'
      });
      fetchAllData();
    } catch (err) {
      console.error("Failed to delete campaign:", err);
    }
  };

  // Handle deleting ingested content (video)
  const handleDeleteContent = async (id: string) => {
    try {
      await fetchWithAuth(`/api/content/${id}`, {
        method: 'DELETE'
      });
      if (selectedVideo === id) {
        setSelectedVideo('');
        setSelectedSceneId('');
        setSelectedSurfaceId('');
      }
      fetchAllData();
    } catch (err) {
      console.error("Failed to delete video:", err);
    }
  };

  // Handle Content Upload (MReq 1: chunked upload for files > 100 MB, direct XHR for smaller)
  const handleIngestVideo = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newVideoTitle || ingesting) return;
    setIngestError(null);
    setIngesting(true);
    setUploadProgress(0);
    setChunkProgress('');

    // Use chunked upload for files larger than 100 MB
    const CHUNKED_THRESHOLD = 100 * 1024 * 1024; // 100 MB
    if (newVideoFile && newVideoFile.size > CHUNKED_THRESHOLD) {
      try {
        const result = await chunkedUpload.startUpload(newVideoFile, {
          title: newVideoTitle,
          sourceChannel: newVideoChannel,
          campaignId: selectedCampaignId || '',
        });

        // Sync progress from chunked upload
        setUploadProgress(chunkedUpload.state.progress);
        setChunkProgress(chunkedUpload.state.chunkProgress);

        setNewVideoTitle('');
        setNewVideoDuration('00:05:00');
        setNewVideoFile(null);
        setNewVideoRes('1920x1080 (1080p)');
        setNewVideoFps(50);
        setNewVideoChannel('SuperSport Variety');
        setIngesting(false);
        setUploadProgress(0);
        setChunkProgress('');
        fetchAllData();
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setIngestError(err.message);
        }
        setIngesting(false);
        setUploadProgress(0);
        setChunkProgress('');
      }
      return;
    }

    const formData = new FormData();
    formData.append('title', newVideoTitle);
    formData.append('resolution', newVideoRes);
    formData.append('frameRate', String(newVideoFps));
    formData.append('duration', newVideoDuration);
    formData.append('sourceChannel', newVideoChannel);
    if (selectedCampaignId) {
      formData.append('campaignId', selectedCampaignId);
    }
    if (newVideoFile) {
      formData.append('file', newVideoFile);
    }

    // Use XMLHttpRequest for upload progress tracking
    await new Promise<void>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', '/api/content/upload');

      const token = getToken();
      if (token) {
        xhr.setRequestHeader('Authorization', `Bearer ${token}`);
      }

      xhr.upload.addEventListener('progress', (evt) => {
        if (evt.lengthComputable) {
          const pct = Math.round((evt.loaded / evt.total) * 100);
          setUploadProgress(pct);
        }
      });

      xhr.addEventListener('load', () => {
        try {
          const data = JSON.parse(xhr.responseText);
          if (xhr.status >= 200 && xhr.status < 300) {
            setNewVideoTitle('');
            setNewVideoDuration('00:05:00');
            setNewVideoFile(null);
            setNewVideoRes('1920x1080 (1080p)');
            setNewVideoFps(50);
            setNewVideoChannel('SuperSport Variety');
            setIngesting(false);
            setUploadProgress(0);
            fetchAllData();
            resolve();
          } else {
            setIngestError(data.error || `Upload failed (HTTP ${xhr.status}).`);
            setIngesting(false);
            setUploadProgress(0);
            resolve(); // resolve anyway so UI updates
          }
        } catch {
          setIngestError('Failed to parse server response.');
          setIngesting(false);
          setUploadProgress(0);
          resolve();
        }
      });

      xhr.addEventListener('error', () => {
        setIngestError('Network error during upload. Check your connection and try again.');
        setIngesting(false);
        setUploadProgress(0);
        resolve();
      });

      xhr.addEventListener('abort', () => {
        setIngesting(false);
        setUploadProgress(0);
        resolve();
      });

      xhr.send(formData);
    });
  };

  // Handle Surface Approval Decision (MReq 11: real campaign context, audit trail)
  const handleSurfaceDecision = async (decision: "Approved" | "Rejected") => {
    console.log('[approve] called', { decision, selectedSurfaceId, hasUser: !!user, selectedSceneId });
    if (!selectedSurfaceId) { alert('No surface selected. Click a surface on the video first.'); return; }
    if (!user) { alert('User session not found. Please log in again.'); return; }
    try {
      const r = await fetchWithAuth(`/api/surfaces/${selectedSurfaceId}/approve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          decision,
          rejectionReason: decision === "Rejected" ? rejectionReason : "",
          campaignId: selectedCampaignId || undefined,
          userId: user.id
        })
      });
      if (!r.ok) {
        const data = await r.json();
        alert(data.error || 'Approval failed.');
        return;
      }
      setRejectionReason('');
      const rawUpdated = await fetchWithAuth(`/api/scenes/${selectedSceneId}/surfaces`).then(r => r.json()) as SurfaceItemResponse[];
      const parsed = rawUpdated.map(parseSurfaceItem);
      setSurfacesForScene(parsed);
      setSurfacesByScene(prev => ({ ...prev, [selectedSceneId]: parsed }));
      fetchOperationalData();
      alert(`Surface ${decision === 'Approved' ? 'approved' : 'rejected'} successfully.`);
    } catch (err: any) {
      alert(err.message || 'Approval failed. Check console.');
      console.error(err);
    }
  };

  // Handle Render Job Compositing (MReq 7, 14, 23)
  const handleQueueRender = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!composerCampaignId || !composerAssetId || !selectedSurfaceId) return;

    try {
      await fetchWithAuth('/api/renders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          contentId: selectedVideo,
          surfaceId: selectedSurfaceId,
          campaignId: composerCampaignId,
          assetId: composerAssetId,
          exportPreset: composerPreset
        })
      });
      fetchAllData();
    } catch (err) {
      console.error(err);
    }
  };

  // Handle compositing preview (MReq 6: generate composite frame for QA)
  const [compositePreview, setCompositePreview] = useState<string | null>(null);
  const [compositingPreview, setCompositingPreview] = useState(false);

  const handlePreviewComposite = async (surfaceId: string, assetId: string) => {
    setCompositingPreview(true);
    try {
      const surface = surfacesForScene.find(s => s.id === surfaceId);
      if (!surface) return;

      const res = await fetchWithAuth('/api/compositing/preview', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          surfaceId,
          assetId,
          contentId: selectedVideo,
          frameNumber: surface.detectedAtFrame ?? 0,
          boundaryCoordinatesJson: JSON.stringify(surface.boundaryCoordinates)
        })
      });

      const data = await res.json();
      setCompositePreview(data.imageBase64);
    } catch (err) {
      console.error('Compositing preview failed:', err);
    } finally {
      setCompositingPreview(false);
    }
  };

  // Handle scene-level approval (approve scene with its placed assets for rendering)
  const handleSceneApprove = async (sceneId: string) => {
    console.log('[approve] handleSceneApprove called', { sceneId, selectedVideo });
    try {
      const r = await fetchWithAuth(`/api/scenes/update`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: sceneId, qaStatus: 'Approved' })
      });
      if (!r.ok) {
        const data = await r.json();
        alert(data.error || 'Scene approval failed.');
        return;
      }
      if (selectedVideo) {
        const refreshed = await fetchWithAuth(`/api/content/${selectedVideo}/scenes`).then(r => r.json());
        setScenesForVideo(refreshed);
      }
      fetchAllData();
      alert('Scene approved successfully! You can now submit for rendering.');
    } catch (err: any) {
      alert(err.message || 'Scene approval failed.');
      console.error('Failed to approve scene:', err);
    }
  };

  // Handle AI video splitting (scenes only — no surfaces). User triggers surfaces per-scene afterwards.
  const handleAiSplitAnalyze = async (contentId: string, videoTitle: string) => {
    if (!contentId || !videoTitle) return;
    setAiAnalyzingVideoId(contentId);
    setSelectedVideo(contentId);

    // Safety timeout: clear analyzing flag after 10 min if SignalR never delivers completion
    const safetyTimer = setTimeout(() => {
      setAiAnalyzingVideoId(prev => prev === contentId ? null : prev);
      console.warn('[SceneDetection] Safety timeout cleared analyzing flag for', contentId);
    }, 10 * 60 * 1000);

    try {
      // Queue scenes-only detection — SignalR DetectionProgress pushes live updates.
      // onDetectionProgress callback handles completion + scene refresh.
      await detectScenesOnly(contentId, videoTitle);
    } catch (err: any) {
      console.error("AI Split/Analyze Error:", err);
      alert("Scene detection failed. Please try again or contact support.");
      setAiAnalyzingVideoId(null);
      clearTimeout(safetyTimer);
    }
  };

  // ── Pipeline Re-Run Handlers ──────────────────────────────────────────

  /** Re-run scene detection (from Completed or SceneDetecting stage). */
  const handleRedetectScenes = async (contentId: string, videoTitle: string) => {
    if (!contentId) return;
    setIsPipelineActionPending(contentId);
    setAiAnalyzingVideoId(contentId);
    setSelectedVideo(contentId);
    try {
      // Queue re-detect — SignalR DetectionProgress pushes live updates.
      await redetectScenes(contentId);
    } catch (err: any) {
      console.error('Re-detect scenes error:', err);
      alert("Failed to re-detect scenes. Please try again or contact support.");
      setAiAnalyzingVideoId(null);
      setIsPipelineActionPending(null);
    }
  };

  /** Re-run transcoding for a content item. */
  const handleRetranscode = async (contentId: string) => {
    if (!contentId) return;
    setIsPipelineActionPending(contentId);
    try {
      await retranscode(contentId);
      await fetchAllData();
    } catch (err: any) {
      console.error('Retranscode error:', err);
      alert("Failed to restart transcoding. Please try again or contact support.");
    } finally {
      setIsPipelineActionPending(null);
    }
  };

  /** Trigger surface detection for a single scene (Gemini + SAM3). */
  const handleDetectSurfacesForScene = async (sceneId: string, contentId: string) => {
    if (!sceneId) return;
    try {
      await detectSurfacesForScene(sceneId);

      // Optimistically update the scene's status in local state
      setScenesForVideo(prev => prev.map(s =>
        s.id === sceneId ? { ...s, surfaceStatus: 'Detecting' as const } : s
      ));

      // Safety timeout: refresh scenes after 5 min to clear detecting state if SignalR doesn't deliver
      setTimeout(async () => {
        try {
          const refreshed = await fetchWithAuth(`/api/content/${contentId}/scenes`).then(r => r.json());
          setScenesForVideo(refreshed);
        } catch { /* silent */ }
      }, 5 * 60 * 1000);

      // SignalR DetectionProgress + ContentStatusChanged will trigger fetchAllData()
      // and scene refresh when detection completes — no polling needed.
    } catch (err: any) {
      console.error('Surface detection error:', err);
      alert(err.message || 'Failed to start surface detection.');
    }
  };

  /** Full pipeline reset — clear all progress back to Staging. */
  const handleResetPipeline = async (contentId: string) => {
    if (!contentId) return;
    setIsPipelineActionPending(contentId);
    try {
      await resetPipeline(contentId);
      await fetchAllData();
      // Also refresh scenes
      try {
        const refreshedScenes = await fetchWithAuth(`/api/content/${contentId}/scenes`).then(r => r.json());
        setScenesForVideo(refreshedScenes);
      } catch { /* scenes may not exist after reset */ }
    } catch (err: any) {
      console.error('Reset pipeline error:', err);
      alert("Failed to reset pipeline. Please try again or contact support.");
    } finally {
      setIsPipelineActionPending(null);
    }
  };

  // Handle Alarm Clearing (MReq 21)
  const handleClearAlarm = async (id: string) => {
    try {
      await fetchWithAuth(`/api/alarms/${id}/clear`, { method: 'POST' });
      fetchAllData();
    } catch (err) {
      console.error(err);
    }
  };

  // Simulate throwing an Alarm
  const handleSimulateAlarm = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await fetchWithAuth('/api/alarms/trigger', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          severity: alarmSimSeverity,
          source: alarmSimSource,
          description: alarmSimDesc
        })
      });
      fetchAllData();
    } catch (err) {
      console.error(err);
    }
  };

  const handleTriggerLog = async (code: string, severity: 'Info' | 'Warning' | 'Major' | 'Critical', module: string, user: string, desc: string) => {
    try {
      await fetchWithAuth('/api/logs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ eventCode: code, severity, module, user, description: desc })
      });
      fetchAllData();
    } catch (err) {
      console.error("Failed to post log:", err);
    }
  };

  // Phase 2: Place an asset on a surface
  const handlePlaceAsset = (surfaceId: string, assetId: string) => {
    setSurfaceAssetPairs(prev => ({ ...prev, [surfaceId]: assetId }));
  };

  // Phase 2: Remove an asset from a surface
  const handleRemoveAsset = (surfaceId: string) => {
    setSurfaceAssetPairs(prev => {
      const next = { ...prev };
      delete next[surfaceId];
      return next;
    });
  };

  // Phase 2: Submit a surface+asset placement for rendering
  const handleSubmitPlacement = async (surfaceId: string, assetId: string, campaignId: string) => {
    if (!selectedVideo) return;
    try {
      const r = await fetchWithAuth('/api/renders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          contentId: selectedVideo,
          surfaceId,
          campaignId: campaignId || composerCampaignId,
          assetId,
          exportPreset: composerPreset
        })
      });
      if (!r.ok) {
        const data = await r.json();
        alert(data.error || 'Failed to submit render.');
        return false;
      }
      fetchAllData();
      return true;
    } catch (err: any) {
      alert(err.message || 'Failed to submit render. Check the console for details.');
      console.error('Failed to submit placement:', err);
      return false;
    }
  };

  // Phase 3: AI-powered asset suggestion with smart category matching
  const handleAiSuggestAssets = async (surfaceId: string) => {
    const surface = surfacesForScene.find(s => s.id === surfaceId);
    if (!surface) return;

    setIsSuggestingAssets(prev => ({ ...prev, [surfaceId]: true }));

    // Smart matching: surface type keywords -> recommended brand categories
    const surfaceLower = surface.surfaceType.toLowerCase();
    const categoryScores: Record<string, number> = {};

    // Score each brand category based on surface type relevance
    const isOutdoor = /billboard|hoarding|wall|building|facade|outdoor|street/i.test(surfaceLower);
    const isScreen = /screen|tv|monitor|display|led|lcd|digital/i.test(surfaceLower);
    const isField = /field|pitch|grass|stadium|ground|court/i.test(surfaceLower);
    const isProduct = /product|table|shelf|counter|bar|desk/i.test(surfaceLower);
    const isVehicle = /vehicle|car|bus|taxi|truck|van/i.test(surfaceLower);
    const isSignage = /sign|banner|poster|flag/i.test(surfaceLower);

    for (const asset of assetList) {
      let score = surface.viabilityScore * 100; // base score from viability
      const cat = asset.brandCategory;

      if (isOutdoor && /Apparel|Automotive|Beverage|Telecom|Retail|Insurance/i.test(cat)) score += 30;
      if (isScreen && /Electronics|Gaming|Streaming|Software|Telecom/i.test(cat)) score += 35;
      if (isField && /Sports|Beverage|Apparel|Automotive|Energy/i.test(cat)) score += 30;
      if (isProduct && /FMCG|Beverage|Beauty|Luxury|Electronics/i.test(cat)) score += 30;
      if (isVehicle && /Automotive|Motoring|Logistics|Energy|Insurance/i.test(cat)) score += 35;
      if (isSignage && /Retail|Entertainment|Streaming|Gaming|Real Estate/i.test(cat)) score += 25;

      categoryScores[asset.id] = score;
    }

    // Try server first, fall back to scored local matching
    try {
      const res = await fetchWithAuth('/api/scenes/ai-suggest-assets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          surfaceType: surface.surfaceType,
          confidenceScore: surface.confidenceScore,
          viabilityScore: surface.viabilityScore,
          campaignId: selectedCampaignId
        })
      });

      if (res.ok) {
        const data = await res.json();
        if (data.suggestions && data.suggestions.length > 0) {
          setAiSuggestions(prev => ({ ...prev, [surfaceId]: data.suggestions }));
          setIsSuggestingAssets(prev => ({ ...prev, [surfaceId]: false }));
          return;
        }
      }
    } catch { /* fall through to local scoring */ }

    // Intelligent local scoring fallback
    const campaignAssets = selectedCampaignId
      ? assetList.filter(a => a.campaignId === selectedCampaignId)
      : assetList;

    const suggestions = campaignAssets
      .map(a => ({
        assetId: a.id,
        score: categoryScores[a.id] || surface.viabilityScore * 100,
        reason: generateMatchReason(a, surface)
      }))
      .sort((a, b) => b.score - a.score)
      .slice(0, 3)
      .map(({ assetId, reason }) => ({ assetId, reason }));

    setAiSuggestions(prev => ({ ...prev, [surfaceId]: suggestions }));
    setIsSuggestingAssets(prev => ({ ...prev, [surfaceId]: false }));
  };

  /** Generate a human-readable reason why this asset matches this surface */
  function generateMatchReason(asset: CreativeAsset, surface: SurfaceItem): string {
    const surfaceLower = surface.surfaceType.toLowerCase();
    const reasons: string[] = [];

    if (/billboard|hoarding|wall|building/i.test(surfaceLower))
      reasons.push(`Outdoor ${surface.surfaceType.toLowerCase()} — high visibility for ${asset.brandCategory} brands`);
    else if (/screen|tv|monitor|display/i.test(surfaceLower))
      reasons.push(`Digital ${surface.surfaceType.toLowerCase()} — ideal for ${asset.brandCategory} content`);
    else if (/field|pitch|stadium/i.test(surfaceLower))
      reasons.push(`Sports ${surface.surfaceType.toLowerCase()} — strong ${asset.brandCategory} audience fit`);
    else
      reasons.push(`${surface.surfaceType} surface — compatible with ${asset.brandCategory}`);

    reasons.push(`${Math.round(surface.confidenceScore * 100)}% detection confidence`);
    reasons.push(`${Math.round(surface.viabilityScore * 100)}% viability score`);
    reasons.push(`${asset.type} · ${asset.dimensions}`);

    return reasons.join(' · ');
  }

  const handleDownloadDoc = () => {
    setDownloading(true);
    setTimeout(() => {
      try {
        const blob = new Blob([DOCUMENT_CONTENT], { type: 'application/msword' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'Brand_Inserts_Technology_Implementation_Plan.doc';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      } catch (error) {
        console.error('Download failed:', error);
      } finally {
        setDownloading(false);
      }
    }, 1000);
  };

  const currentDayDetails = TIMELINE_DATA.find(d => d.day === selectedDay) || TIMELINE_DATA[0];
  const currentSurface = surfacesForScene.find(sf => sf.id === selectedSurfaceId);

  if (!token || !user) {
    return (
      <div className="min-h-screen bg-slate-950 flex flex-col items-center justify-center p-6 text-slate-100 font-sans" id="login_gate">
        <div className="w-full max-w-4xl grid grid-cols-1 md:grid-cols-12 gap-8 items-stretch">
          
          {/* Brand Info Panel */}
          <div className="md:col-span-5 flex flex-col justify-between p-8 bg-gradient-to-br from-slate-900 via-indigo-950 to-slate-900 rounded-2xl shadow-2xl text-white border border-slate-800">
            <div>
              <div className="mb-6 flex items-center">
                <BitLogo variant="dark" height={52} />
              </div>
              <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-white/10 text-[10px] font-bold uppercase tracking-wider mb-6">
                <span className="h-2 w-2 rounded-full bg-emerald-400 animate-pulse"></span>
                IDENTITY SERVICES GATEWAY
              </div>
              <h2 className="text-2xl font-black tracking-tight leading-tight">
                Brand Inserts Technology
              </h2>
              <p className="text-xs text-slate-300 mt-3 leading-relaxed">
                Unlock enterprise access to South Africa's premier video stream dynamic advertising platform. Ingest video files, schedule visual overlay insertions, customize with generative AI models, and dispatch GPU compositing processes.
              </p>
            </div>
            
            <div className="mt-8 pt-6 border-t border-white/10 text-[10px] text-blue-200/80 font-mono">
              <div>Secure SSO Environment</div>
              <div>System Version: BIT-v1.2.0-Prod</div>
            </div>
          </div>

          {/* Form & Switcher Panel */}
          <div className="md:col-span-7 bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl p-8 flex flex-col justify-between">
            <div>
              <h3 className="text-lg font-bold text-white mb-1 flex items-center gap-2">
                <Lock className="h-5 w-5 text-blue-500" /> Secure Sign In
              </h3>
              <p className="text-xs text-slate-400 mb-6">Enter your authorized administrative or operational credentials.</p>

              {authError && (
                <div className="mb-4 p-3 bg-red-950/50 border border-red-500/50 rounded-lg text-xs text-red-200 flex items-center gap-2">
                  <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
                  <span>{authError}</span>
                </div>
              )}

              <form onSubmit={handleLogin} className="space-y-4">
                <div>
                  <label className="block text-[10px] font-mono font-bold uppercase text-slate-400 mb-1.5">Email Address</label>
                  <input 
                    type="email"
                    value={loginEmail}
                    onChange={(e) => setLoginEmail(e.target.value)}
                    placeholder="e.g. admin@afrobotics.co.za"
                    className="w-full px-3.5 py-2.5 bg-slate-950/80 border border-slate-800 rounded-lg text-xs font-mono text-white placeholder-slate-600 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 transition-all"
                    required
                  />
                </div>
                <div>
                  <label className="block text-[10px] font-mono font-bold uppercase text-slate-400 mb-1.5">Password</label>
                  <input 
                    type="password"
                    value={loginPassword}
                    onChange={(e) => setLoginPassword(e.target.value)}
                    placeholder="••••••••••••"
                    className="w-full px-3.5 py-2.5 bg-slate-950/80 border border-slate-800 rounded-lg text-xs font-mono text-white placeholder-slate-600 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 transition-all"
                    required
                  />
                </div>
                <button
                  type="submit"
                  className="w-full py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-bold text-xs rounded-lg transition-all cursor-pointer shadow-md hover:shadow-blue-500/10"
                >
                  Sign In to Console
                </button>
              </form>

              <p className="text-xs text-slate-500 text-center mt-4">
                <span onClick={() => setShowForgotPassword(!showForgotPassword)} className="cursor-pointer hover:text-blue-400 underline">
                  Forgot password?
                </span>
              </p>

              {showForgotPassword && (
                <div className="mt-4 p-4 bg-slate-800/50 border border-slate-700 rounded-xl space-y-3">
                  <p className="text-xs text-slate-400">Enter your email to receive a reset link.</p>
                  <div className="flex gap-2">
                    <input type="email" value={forgotEmail} onChange={e => setForgotEmail(e.target.value)}
                      placeholder="your@email.com" className="flex-1 px-3 py-2 bg-slate-950/80 border border-slate-800 rounded-lg text-xs font-mono text-white placeholder-slate-600 focus:outline-none focus:border-blue-500" />
                    <button onClick={handleForgotPassword} disabled={forgotSending}
                      className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-bold text-xs rounded-lg cursor-pointer transition-colors disabled:opacity-50 shrink-0">
                      {forgotSending ? 'Sending...' : 'Send Link'}
                    </button>
                  </div>
                  {forgotMsg && (
                    <div className={`text-xs font-bold ${forgotMsg.type === 'success' ? 'text-emerald-400' : 'text-red-400'}`}>{forgotMsg.text}</div>
                  )}
                </div>
              )}

            </div>

            <div className="mt-8 pt-6 border-t border-slate-800">
              <span className="text-[10px] font-mono font-bold uppercase text-slate-400 block mb-3">
                Pre-configured Roles &amp; Credentials
              </span>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                {[
                  { 
                    role: 'Admin', 
                    name: 'Sabelo Nkosi', 
                    email: 'admin@afrobotics.co.za', 
                    pass: 'admin123',
                    badge: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
                  },
                  { 
                    role: 'Editor', 
                    name: 'Sfiso Dlamini', 
                    email: 'loverboy.sfiso@gmail.com', 
                    pass: 'editor123',
                    badge: 'bg-amber-500/10 text-amber-400 border-amber-500/20'
                  },
                  { 
                    role: 'Advertiser', 
                    name: 'Thabo Ndlovu', 
                    email: 'advertiser@afrobotics.co.za', 
                    pass: 'advertiser123',
                    badge: 'bg-purple-500/10 text-purple-400 border-purple-500/20'
                  }
                ].map((cred) => (
                  <button
                    key={cred.role}
                    type="button"
                    onClick={() => handleLogin(undefined, { email: cred.email, pass: cred.pass })}
                    className="p-2.5 bg-slate-950/40 border border-slate-800 hover:bg-slate-850 hover:border-blue-500 rounded-xl text-left cursor-pointer transition-all flex flex-col justify-between"
                  >
                    <div>
                      <div className="flex items-center justify-between gap-1 mb-1">
                        <span className="text-[10px] font-bold text-white truncate">{cred.name}</span>
                        <span className={`text-[8px] font-mono font-bold px-1 rounded border uppercase ${cred.badge}`}>{cred.role}</span>
                      </div>
                      <div className="text-[9px] font-mono text-slate-400 truncate">{cred.email}</div>
                    </div>
                    <div className="text-[9px] font-mono text-slate-500 mt-1">Pass: <code className="text-slate-300">{cred.pass}</code></div>
                  </button>
                ))}
              </div>
            </div>

          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50/70 text-slate-700 font-sans antialiased pb-12" id="app_root">
      {/* HEADER BANNER */}
      <header className="border-b border-slate-200 bg-white px-6 py-4 sticky top-0 z-50 shadow-xs" id="portal_header">
        <div className="max-w-full mx-auto flex items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-3">
              <BitLogo variant="light" height={42} />
              <div className="hidden sm:block pl-3 border-l border-slate-200">
                <div className="flex items-center gap-1.5 mb-0.5">
                  <span className="inline-flex h-2 w-2 rounded-full bg-emerald-500 animate-pulse"></span>
                  <span className="text-[10px] font-bold uppercase tracking-widest text-emerald-600 font-mono">BIT PLATFORM</span>
                </div>
                <div className="text-[11px] font-semibold text-slate-500">Dynamic In-Content Overlay</div>
              </div>
            </div>
            {/* Campaign Selector — always visible */}
            <div className="pl-4 border-l border-slate-200">
              <CampaignSelector
                campaigns={campaignList}
                selectedId={selectedCampaignId}
                onSelect={(id) => navigateTo('dashboard', id)}
                onCreateNew={() => navigate('/')}
                assetCounts={Object.fromEntries(assetList.reduce((acc, a) => {
                  if (a.campaignId) acc.set(a.campaignId, (acc.get(a.campaignId) || 0) + 1);
                  return acc;
                }, new Map<string, number>()))}
              />
            </div>
          </div>
          
          <div className="flex flex-wrap items-center gap-4">
            {alarmList.some(a => a.isActive) && (
              <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-red-50 border border-red-200 text-red-600 text-xs font-semibold animate-pulse">
                <AlertTriangle className="h-4 w-4 text-red-500" />
                {alarmList.filter(a => a.isActive).length}
              </span>
            )}

            {user && (
              <div className="flex items-center gap-3 pl-4 border-l border-slate-200">
                <div className="text-right">
                  <div className="flex items-center gap-1.5 justify-end">
                    <span className="text-xs font-bold text-slate-900">{user.fullName}</span>
                    <span className={`text-[8px] font-mono font-bold px-1 rounded uppercase border ${
                      user.role === 'Admin' ? 'bg-emerald-50 text-emerald-600 border-emerald-200' :
                      user.role === 'Editor' ? 'bg-amber-50 text-amber-600 border-amber-200' :
                      'bg-purple-50 text-purple-600 border-purple-200'
                    }`}>
                      {user.role}
                    </span>
                  </div>
                  <div className="text-[10px] text-slate-400 font-mono">{user.email}</div>
                </div>
                
                {/* Theme Switcher Button */}
                <button 
                  onClick={() => setTheme(theme === 'light' ? 'dark' : 'light')} 
                  className="p-2 rounded-lg text-slate-400 hover:text-blue-500 hover:bg-slate-100 dark:hover:bg-slate-800 cursor-pointer transition-all border border-slate-200 dark:border-slate-700" 
                  title={theme === 'light' ? "Switch to Dark Mode" : "Switch to Light Mode"}
                >
                  {theme === 'light' ? <Moon className="h-4 w-4" /> : <Sun className="h-4 w-4" />}
                </button>

                {/* Attention Bell */}
                <AttentionBell />

                {/* MReq 9: Role Request */}
                <button
                  onClick={() => { setShowRoleRequest(!showRoleRequest); setRoleRequestMsg(null); }}
                  className="p-2 rounded-lg text-slate-400 hover:text-amber-500 hover:bg-amber-50 cursor-pointer transition-all border border-slate-200"
                  title="Request a role elevation"
                >
                  <UserPlus className="h-4 w-4" />
                </button>

                <button onClick={handleLogout} className="p-2 rounded-lg text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-all border border-slate-200 hover:border-red-100" title="Logout">
                  <LogOut className="h-4 w-4" />
                </button>

                <a href="mailto:support@brandinserts.tech" className="p-2 rounded-lg text-slate-400 hover:text-blue-500 hover:bg-blue-50 cursor-pointer transition-all border border-slate-200" title="Contact Support">
                  <HelpCircle className="h-4 w-4" />
                </a>
              </div>
            )}
          </div>
        </div>
      </header>

      {/* MReq 9: Role Request Popover */}
      <AnimatePresence>
        {showRoleRequest && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="fixed top-[73px] right-4 z-40 bg-white border border-slate-200 rounded-xl shadow-xl p-5 w-80"
          >
            <h4 className="text-sm font-bold text-slate-800 mb-3">Request Role Elevation</h4>
            <p className="text-xs text-slate-500 mb-3">Your current role: <strong>{user?.role}</strong></p>
            <select
              value={requestedRole}
              onChange={e => setRequestedRole(e.target.value)}
              className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs mb-3 focus:outline-none focus:border-blue-500"
            >
              {['Admin', 'Editor', 'Advertiser'].filter(r => r !== user?.role).map(r => (
                <option key={r} value={r}>{r}</option>
              ))}
            </select>
            <textarea
              value={roleRequestReason}
              onChange={e => setRoleRequestReason(e.target.value)}
              placeholder="Reason for request (optional)..."
              rows={2}
              className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs mb-3 resize-none focus:outline-none focus:border-blue-500"
            />
            {roleRequestMsg && (
              <div className={`text-[10px] font-bold mb-3 px-3 py-1.5 rounded-lg ${roleRequestMsg.type === 'success' ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700'}`}>
                {roleRequestMsg.text}
              </div>
            )}
            <button
              onClick={handleRequestRole}
              className="w-full py-2 bg-blue-600 hover:bg-blue-500 text-white font-bold rounded-lg text-xs cursor-pointer transition-colors"
            >
              Submit Request
            </button>
            <div className="mt-4 pt-3 border-t border-slate-100">
              <h4 className="text-[10px] font-bold text-slate-500 uppercase tracking-wider font-mono mb-2">Change Password</h4>
              <input type="password" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)}
                placeholder="Current password" className="w-full px-2 py-1.5 bg-slate-50 border rounded text-xs mb-2 focus:outline-none focus:border-blue-500" />
              <input type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)}
                placeholder="New password" className="w-full px-2 py-1.5 bg-slate-50 border rounded text-xs mb-2 focus:outline-none focus:border-blue-500" />
              <button onClick={handleChangePassword} disabled={changingPassword}
                className="w-full py-1.5 bg-blue-600 hover:bg-blue-500 text-white font-bold rounded text-xs cursor-pointer transition-colors disabled:opacity-50">
                {changingPassword ? 'Changing...' : 'Change Password'}
              </button>
              {passwordChangeMsg && (
                <div className={`text-[10px] font-bold mt-1.5 ${passwordChangeMsg.type === 'success' ? 'text-emerald-600' : 'text-red-600'}`}>
                  {passwordChangeMsg.text}
                </div>
              )}
            </div>
            <div className="mt-4 pt-3 border-t border-slate-100">
              <NotificationPreferencesPanel />
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="flex gap-0 max-w-full mx-auto" id="app_body">
        {/* MReq 8: Idle timeout countdown modal */}
        <AnimatePresence>
          {showCountdown && (
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center backdrop-blur-sm"
            >
              <motion.div
                initial={{ scale: 0.9, opacity: 0 }}
                animate={{ scale: 1, opacity: 1 }}
                exit={{ scale: 0.9, opacity: 0 }}
                className="bg-white rounded-2xl p-8 shadow-2xl max-w-sm w-full mx-4 text-center"
              >
                <Clock className="h-12 w-12 text-amber-500 mx-auto mb-4" />
                <h3 className="text-lg font-bold text-slate-800 mb-2">Session Expiring</h3>
                <p className="text-sm text-slate-500 mb-4">
                  You've been inactive. Your session will end in <strong className="text-red-600 text-xl">{secondsRemaining}s</strong>.
                </p>
                <p className="text-xs text-slate-400 mb-6">Move your mouse or press any key to stay signed in.</p>
                <button
                  onClick={() => { resetTimer(); }}
                  className="w-full px-4 py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-sm rounded-xl cursor-pointer transition-colors"
                >
                  I'm Still Here
                </button>
              </motion.div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* LEFT SIDEBAR */}
        <div className="border-r border-slate-200 bg-white px-4 py-6 min-h-[calc(100vh-140px)] sticky top-[73px] self-start" id="sidebar_wrapper">
          <CampaignSidebar
            selectedCampaignId={selectedCampaignId}
            userRole={user?.role || 'Editor'}
            campaignAssetCount={selectedCampaignId ? assetList.filter(a => a.campaignId === selectedCampaignId).length : 0}
            contentCount={selectedCampaignId ? contentList.filter(v => v.ingestionStatus === 'Completed' && v.campaignId === selectedCampaignId).length : 0}
            renderCount={selectedCampaignId ? renderList.filter(r => r.campaignId === selectedCampaignId).length : 0}
          />
        </div>

        {/* MAIN CONTENT */}
        <main className="flex-1 px-6 py-6 overflow-auto" id="main_content">
          <AnimatePresence mode="wait">
            {/* No campaign selected — landing page, admin, or telemetry */}
            {!selectedCampaignId ? (
              <>
                {activeView === 'admin' && user?.role === 'Admin' && (
                  <AdminConsoleTab onTriggerLog={handleTriggerLog} currentUser={user} />
                )}
                {activeView === 'admin' && user?.role !== 'Admin' && (
                  <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="max-w-4xl mx-auto py-12 text-center" key="admin_unauthorized">
                    <div className="p-12 bg-white border border-slate-200 rounded-2xl shadow-sm">
                      <Shield className="h-12 w-12 text-rose-400 mx-auto mb-4" />
                      <h2 className="text-xl font-bold text-slate-800 mb-2">Access Denied</h2>
                      <p className="text-sm text-slate-500">You do not have administrator privileges. Only users with the Admin role can access this section.</p>
                    </div>
                  </motion.div>
                )}
                {activeView === 'telemetry' && (
                  <TelemetryTab
                    logList={logList}
                    alarmList={alarmList}
                    handleClearAlarm={handleClearAlarm}
                    handleSimulateAlarm={handleSimulateAlarm}
                    alarmSimSeverity={alarmSimSeverity}
                    setAlarmSimSeverity={setAlarmSimSeverity}
                    alarmSimSource={alarmSimSource}
                    setAlarmSimSource={setAlarmSimSource}
                    alarmSimDesc={alarmSimDesc}
                    setAlarmSimDesc={setAlarmSimDesc}
                  />
                )}
                {activeView === 'analytics' && (
                  <AnalyticsTab summary={statsSummary} loading={statsLoading} />
                )}
                {activeView === 'jobs' && (
                  <JobsTab onJobChanged={fetchAllData} />
                )}
                {!activeView && (location.pathname === '/' || location.pathname === '') && (
                  <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="max-w-4xl mx-auto space-y-8 py-12" key="no_campaign">
                    <div className="text-center">
                      <Package className="h-16 w-16 text-slate-300 mx-auto mb-4" />
                      <h2 className="text-2xl font-extrabold text-slate-800 font-display">Select or Create a Campaign</h2>
                      <p className="text-sm text-slate-500 mt-2 max-w-md mx-auto">
                        All platform features are organized around campaigns. Choose an existing campaign or create a new one to begin.
                      </p>
                    </div>

                    {/* Campaign Creation Form */}
                    <div className="bg-white border-2 border-blue-300 rounded-2xl p-6 shadow-sm max-w-2xl mx-auto">
                      <h3 className="text-sm font-bold text-slate-800 font-display mb-1 flex items-center gap-2">
                        <Plus className="h-4 w-4 text-blue-600" />
                        Create New Campaign
                      </h3>
                      <p className="text-xs text-slate-500 mb-5">Define campaign schedules, regions and budgets.</p>
                      <form onSubmit={(e) => { e.preventDefault(); handleCreateCampaign(e); }} className="space-y-4">
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Campaign Name</label>
                            <input type="text" value={newCampaignName} onChange={(e) => setNewCampaignName(e.target.value)}
                              placeholder="e.g., Coke Zero Summer"
                              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors" required />
                          </div>
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Naming Code</label>
                            <input type="text" value={newCampaignCode} onChange={(e) => setNewCampaignCode(e.target.value)}
                              placeholder="e.g., UZ01EP12_COKE"
                              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors" required />
                          </div>
                        </div>
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Budget (USD)</label>
                            <input type="number" value={newCampaignBudget} onChange={(e) => setNewCampaignBudget(e.target.value)}
                              placeholder="e.g., 15000"
                              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors" required />
                          </div>
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Target Region</label>
                            <select value={newCampaignRegion} onChange={(e) => setNewCampaignRegion(e.target.value)}
                              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-2 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors">
                              <option value="SADC Region">SADC Region (Southern Africa)</option>
                              <option value="East Africa proxy">East Africa Broadcast proxy</option>
                              <option value="Global Streaming stream">Global Streaming streams</option>
                            </select>
                          </div>
                        </div>
                        {campaignError && (
                          <p className="text-2xs text-red-600 font-semibold font-mono bg-red-50 p-2.5 rounded-lg border border-red-100">{campaignError}</p>
                        )}
                        <button type="submit"
                          className="w-full inline-flex items-center justify-center gap-2 px-3 py-2.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-sm rounded-lg transition-all cursor-pointer">
                          <Plus className="h-4 w-4" />
                          Register Brand Campaign
                        </button>
                      </form>
                    </div>

                    {/* Existing Campaigns */}
                    {campaignList.length > 0 && (
                      <>
                        <div className="text-center">
                          <p className="text-xs text-slate-400 font-mono uppercase tracking-wider">Or select an existing campaign</p>
                        </div>
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 max-w-2xl mx-auto">
                          {campaignList.map(c => {
                            const assetCount = assetList.filter(a => a.campaignId === c.id).length;
                            return (
                              <button key={c.id} onClick={() => navigateTo('dashboard', c.id)}
                                className="bg-white border border-slate-200 rounded-xl p-5 text-left hover:border-blue-300 hover:shadow-md transition-all cursor-pointer">
                                <span className={`inline-block h-2.5 w-2.5 rounded-full mb-2 ${
                                  c.status === 'Active' ? 'bg-emerald-500' : c.status === 'Draft' ? 'bg-blue-500' : 'bg-slate-400'
                                }`} />
                                <h3 className="text-sm font-bold text-slate-800">{c.name}</h3>
                                <p className="text-[10px] text-slate-400 font-mono mt-1">{c.namingStructureCode}</p>
                                <div className="flex items-center gap-3 mt-3 text-[10px] text-slate-500">
                                  <span>{c.targetRegion}</span>
                                  <span>{assetCount} assets</span>
                                  <span className="font-bold">${c.totalBudget.toLocaleString()}</span>
                                </div>
                              </button>
                            );
                          })}
                        </div>
                      </>
                    )}
                  </motion.div>
                )}
              </>
            ) : (
              /* Campaign selected — render the active view */
              <>
                {activeView === 'dashboard' && (
                  <CampaignDashboard
                    campaign={campaignList.find(c => c.id === selectedCampaignId)!}
                    assets={assetList.filter(a => a.campaignId === selectedCampaignId)}
                    contentList={contentList.filter(v => selectedCampaignId && v.campaignId === selectedCampaignId)}
                    renders={renderList.filter(r => r.campaignId === selectedCampaignId)}
                    onNavigate={(view) => navigateTo(view, selectedCampaignId)}
                  />
                )}

                {activeView === 'assets' && (
                  <CampaignsTab
                    campaignList={campaignList}
                    assetList={assetList}
                    selectedCampaignId={selectedCampaignId}
                    setSelectedCampaignId={(id) => id ? navigateTo('assets', id) : navigate('/')}
                    newAssetName={newAssetName}
                    setNewAssetName={setNewAssetName}
                    newAssetType={newAssetType}
                    setNewAssetType={setNewAssetType}
                    newAssetCategory={newAssetCategory}
                    setNewAssetCategory={setNewAssetCategory}
                    handleCreateAsset={handleUploadAsset}
                    handleUpdateAsset={handleUpdateAsset}
                    handleAssociateAsset={handleAssociateAsset}
                    handleUnassociateAsset={handleUnassociateAsset}
                    handleDeleteCampaign={handleDeleteCampaign}
                    handleDeleteAsset={handleDeleteAsset}
                    newAssetFile={newAssetFile}
                    setNewAssetFile={setNewAssetFile}
                  />
                )}

                {activeView === 'content' && (
                  <IngestionTab
                    selectedVideo={selectedVideo}
                    setSelectedVideo={setSelectedVideo}
                    scenesForVideo={scenesForVideo}
                    selectedSceneId={selectedSceneId}
                    setSelectedSceneId={setSelectedSceneId}
                    onNavigateToPlacements={() => navigateTo('placements', selectedCampaignId)}
                    newVideoTitle={newVideoTitle}
                    setNewVideoTitle={setNewVideoTitle}
                    newVideoRes={newVideoRes}
                    setNewVideoRes={setNewVideoRes}
                    newVideoFps={newVideoFps}
                    setNewVideoFps={setNewVideoFps}
                    newVideoDuration={newVideoDuration}
                    setNewVideoDuration={setNewVideoDuration}
                    newVideoChannel={newVideoChannel}
                    setNewVideoChannel={setNewVideoChannel}
                    newVideoFile={newVideoFile}
                    setNewVideoFile={setNewVideoFile}
                    handleIngestVideo={handleIngestVideo}
                    ingestError={ingestError}
                    ingesting={ingesting}
                    uploadProgress={uploadProgress}
                    chunkProgress={chunkProgress}
                    handleDeleteContent={handleDeleteContent}
                    handleAiSplitAnalyze={handleAiSplitAnalyze}
                    aiAnalyzingVideoId={aiAnalyzingVideoId}
                    selectedCampaignId={selectedCampaignId}
                    campaignList={campaignList.map(c => ({ id: c.id, name: c.name }))}
                    onDataChanged={fetchAllData}
                    onRetranscode={handleRetranscode}
                    onRedetectScenes={handleRedetectScenes}
                    onResetPipeline={handleResetPipeline}
                    isPipelineActionPending={isPipelineActionPending}
                    onDetectSurfacesForScene={handleDetectSurfacesForScene}
                  />
                )}

                {activeView === 'placements' && (
                  <EditorTab
                    contentList={contentList}
                    selectedVideo={selectedVideo}
                    setSelectedVideo={setSelectedVideo}
                    selectedSceneId={selectedSceneId}
                    setSelectedSceneId={setSelectedSceneId}
                    scenesForVideo={scenesForVideo}
                    surfacesForScene={surfacesForScene}
                    selectedSurfaceId={selectedSurfaceId}
                    setSelectedSurfaceId={setSelectedSurfaceId}
                    rejectionReason={rejectionReason}
                    setRejectionReason={setRejectionReason}
                    handleSurfaceDecision={handleSurfaceDecision}
                    currentSurface={currentSurface}
                    handleSceneApprove={handleSceneApprove}
                    onPreviewComposite={handlePreviewComposite}
                    onClearCompositePreview={() => setCompositePreview(null)}
                    compositingPreview={compositingPreview}
                    compositePreviewImage={compositePreview}
                    assetList={assetList}
                    campaignList={campaignList}
                    // Phase 1
                    handleAiSplitAnalyze={handleAiSplitAnalyze}
                    aiAnalyzingVideoId={aiAnalyzingVideoId}
                    onDetectSurfacesForScene={handleDetectSurfacesForScene}
                    // Phase 2
                    selectedCampaignId={selectedCampaignId ?? undefined}
                    surfaceAssetPairs={surfaceAssetPairs}
                    onPlaceAsset={handlePlaceAsset}
                    onRemoveAsset={handleRemoveAsset}
                    onSubmitPlacement={handleSubmitPlacement}
                    // Phase 3
                    onAiSuggestAssets={handleAiSuggestAssets}
                    isSuggestingAssets={isSuggestingAssets}
                    aiSuggestions={aiSuggestions}
                    // Phase 4
                    onNavigateToRenders={() => navigateTo('renders', selectedCampaignId)}
                    onNavigateToContent={() => navigateTo('content', selectedCampaignId)}
                    hasContentIngested={contentList.some(v => v.ingestionStatus === 'Completed')}
                    hasSurfacesDetected={scenesForVideo.length > 0 && surfacesForScene.length > 0}
                    hasPlacedAssets={Object.keys(surfaceAssetPairs).length > 0}
                    hasRenders={renderList.filter(r => selectedCampaignId && r.campaignId === selectedCampaignId).length > 0}
                  />
                )}

                {activeView === 'renders' && (
                  <RendersTab
                    renderList={renderList.filter(r => selectedCampaignId && r.campaignId === selectedCampaignId)}
                    campaignName={campaignList.find(c => c.id === selectedCampaignId)?.name}
                  />
                )}

                {activeView === 'reports' && (
                  <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="max-w-2xl mx-auto py-8 text-center" key="reports">
                    <FileText className="h-12 w-12 text-slate-300 mx-auto mb-3" />
                    <h3 className="text-lg font-bold text-slate-800 font-display">Campaign Reports</h3>
                    <p className="text-sm text-slate-500 mt-2">Billing, exposure analytics, and audit logs for this campaign.</p>
                    <div className="mt-6 p-6 bg-white border border-slate-200 rounded-xl shadow-sm text-left space-y-2">
                      <div className="text-xs font-mono text-slate-600 flex justify-between">
                        <span>Campaign Budget:</span>
                        <span className="font-bold">${campaignList.find(c => c.id === selectedCampaignId)?.totalBudget.toLocaleString()}</span>
                      </div>
                      <div className="text-xs font-mono text-slate-600 flex justify-between">
                        <span>Assets Staged:</span>
                        <span className="font-bold">{assetList.filter(a => a.campaignId === selectedCampaignId).length}</span>
                      </div>
                      <div className="text-xs font-mono text-slate-600 flex justify-between">
                        <span>Renders Completed:</span>
                        <span className="font-bold">{renderList.filter(r => r.renderStatus === 'Finished' && r.campaignId === selectedCampaignId).length}</span>
                      </div>
                      <div className="text-xs font-mono text-slate-600 flex justify-between">
                        <span>Total Processing Time:</span>
                        <span className="font-bold">{(renderList.filter(r => r.campaignId === selectedCampaignId).reduce((sum, r) => sum + r.processingDurationMs, 0) / 1000).toFixed(1)}s</span>
                      </div>
                    </div>
                  </motion.div>
                )}
              </>
            )}
          </AnimatePresence>

          {/* 404 — no matching route for authenticated users */}
          {user && !activeView && location.pathname !== '/' && <NotFoundPage />}
        </main>
      </div>

      {/* FOOTER */}
      <footer className="max-w-7xl mx-auto px-6 mt-12 pt-6 border-t border-slate-200 text-center text-xs text-slate-400 font-mono" id="portal_footer">
        <div>Brand Inserts Technology (BIT) • Release 1 Operational Portal • Confidential Document</div>
        <div className="mt-1">Copyright © 2026 Brand Inserts Technology. All rights reserved. Registered under secure cloud-compliance guidelines.</div>
      </footer>
    </div>
  );
}
