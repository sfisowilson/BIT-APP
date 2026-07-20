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
  Moon
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
import { login as apiLogin, fetchWithAuth as apiFetchWithAuth, getToken, setToken, clearToken, getSavedUser, setSavedUser, type UserSession } from './apiClient';

// Import our modular sub-components
import { CampaignsTab } from './components/CampaignsTab';
import { IngestionTab } from './components/IngestionTab';
import { EditorTab } from './components/EditorTab';
import { ComposerTab } from './components/ComposerTab';
import { TelemetryTab } from './components/TelemetryTab';
import { AdminConsoleTab } from './components/AdminConsoleTab';
import { CampaignSelector } from './components/CampaignSelector';
import { CampaignSidebar, type SidebarView } from './components/CampaignSidebar';
import { CampaignDashboard } from './components/CampaignDashboard';

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
      setAuthError(err.message || "Identity Service connection error.");
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

  // Secure request broker (MReq 8 over secure JWT authorization)
  const fetchWithAuth = async (url: string, options: RequestInit = {}) => {
    const res = await apiFetchWithAuth(url, options);
    // Auto-logout if token is invalid or expired
    if (res.status === 401) {
      handleLogout();
      throw new Error('Session expired. Please sign in again.');
    }
    if (!res.ok) {
      const clone = res.clone();
      const text = await clone.text();
      throw new Error(`HTTP ${res.status} on ${url}: ${text.substring(0, 100)}`);
    }
    const contentType = res.headers.get('content-type');
    if (!contentType || !contentType.includes('application/json')) {
      const clone = res.clone();
      const text = await clone.text();
      throw new Error(`Expected JSON from ${url} but got content-type "${contentType}" and body: ${text.substring(0, 100)}`);
    }
    return res;
  };

  // Restore saved session from localStorage on mount (MReq 8)
  // No auto-login — user must explicitly authenticate via the login form
  useEffect(() => {
    const savedToken = getToken();
    const savedUser = getSavedUser<UserSession>();
    if (savedToken && savedUser) {
      setToken(savedToken);
      setUser(savedUser);
    }
  }, []);

  // Fetch all core operational datasets from backend APIs
  const fetchAllData = async () => {
    if (!token) return;
    try {
      const fetchJson = async (url: string) => {
        const r = await fetchWithAuth(url);
        return r.json();
      };

      const campaignParam = selectedCampaignId ? `?campaignId=${selectedCampaignId}` : '';
      const [
        contentRes,
        campaignsRes,
        assetsRes,
        rendersRes,
        logsRes,
        alarmsRes
      ] = await Promise.allSettled([
        fetchJson(`/api/content${campaignParam}`),
        fetchJson('/api/campaigns'),
        fetchJson(`/api/assets${campaignParam}`),
        fetchJson(`/api/renders${campaignParam}`),
        fetchJson('/api/logs'),
        fetchJson('/api/alarms')
      ]);

      if (contentRes.status === 'fulfilled') setContentList(contentRes.value);
      if (campaignsRes.status === 'fulfilled') setCampaignList(campaignsRes.value);
      if (assetsRes.status === 'fulfilled') setAssetList(assetsRes.value);
      if (rendersRes.status === 'fulfilled') setRenderList(rendersRes.value);
      if (logsRes.status === 'fulfilled') setLogList(logsRes.value);
      if (alarmsRes.status === 'fulfilled') setAlarmList(alarmsRes.value);

      const failures = [
        { name: 'content', res: contentRes },
        { name: 'campaigns', res: campaignsRes },
        { name: 'assets', res: assetsRes },
        { name: 'renders', res: rendersRes },
        { name: 'logs', res: logsRes },
        { name: 'alarms', res: alarmsRes }
      ].filter(f => f.res.status === 'rejected');

      if (failures.length > 0) {
        console.warn('Some API endpoints failed to load:', failures.map(f => `${f.name}: ${(f.res as PromiseRejectedResult).reason}`));
      }
    } catch (err) {
      console.error("API Fetch Error:", err);
    }
  };

  // Run initial load
  useEffect(() => {
    if (token) {
      fetchAllData();

      // Poll for fresh data every 5 seconds (renders, alarms, logs)
      const interval = setInterval(() => {
        fetchAllData();
      }, 5000);
      return () => {
        clearInterval(interval);
      };
    }
  }, [token]);

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
  const VALID_VIEWS: string[] = ['dashboard', 'assets', 'content', 'placements', 'renders', 'reports', 'admin', 'telemetry'];
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
      document.title = `${campaignName} · ${viewLabel} — Afrobotics BIT`;
    } else if (campaignName) {
      document.title = `${campaignName} — Afrobotics BIT`;
    } else if (activeView) {
      document.title = `${viewLabel} — Afrobotics BIT`;
    } else {
      document.title = 'Afrobotics BIT — Brand Insertion Technology';
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

  // Fetch scenes when selected video changes
  useEffect(() => {
    if (!selectedVideo || !token) return;
    fetchWithAuth(`/api/content/${selectedVideo}/scenes`)
      .then(r => r.json())
      .then(data => {
        setScenesForVideo(data);
        if (data.length > 0) {
          setSelectedSceneId(data[0].id);
        } else {
          setSelectedSceneId('');
          setSurfacesForScene([]);
          setSelectedSurfaceId('');
        }
      })
      .catch(() => {
        setScenesForVideo([]);
        setSelectedSceneId('');
        setSurfacesForScene([]);
        setSelectedSurfaceId('');
      });
  }, [selectedVideo, token]);

  // Fetch surfaces when selected scene changes
  useEffect(() => {
    if (!selectedSceneId || !token) return;
    fetchWithAuth(`/api/scenes/${selectedSceneId}/surfaces`)
      .then(r => r.json())
      .then((rawData: SurfaceItemResponse[]) => {
        const parsed = rawData.map(parseSurfaceItem);
        setSurfacesForScene(parsed);
        if (parsed.length > 0) {
          setSelectedSurfaceId(parsed[0].id);
        } else {
          setSelectedSurfaceId('');
        }
      });
  }, [selectedSceneId]);

  // Sync composer campaign with selected campaign context (MReq 10)
  useEffect(() => {
    if (selectedCampaignId && !composerCampaignId) {
      setComposerCampaignId(selectedCampaignId);
    }
  }, [selectedCampaignId]);

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

  // Handle Content Upload (MReq 1: real file upload with metadata)
  const handleIngestVideo = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newVideoTitle || ingesting) return;
    setIngestError(null);
    setIngesting(true);
    try {
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

      const token = getToken();
      const res = await fetch('/api/content/upload', {
        method: 'POST',
        headers: token ? { 'Authorization': `Bearer ${token}` } : {},
        body: formData,
      });

      const data = await res.json();
      if (!res.ok) {
        setIngestError(data.error || 'Ingestion failed.');
        setIngesting(false);
        return;
      }
      setNewVideoTitle('');
      setNewVideoDuration('00:05:00');
      setNewVideoFile(null);
      setNewVideoRes('1920x1080 (1080p)');
      setNewVideoFps(50);
      setNewVideoChannel('SuperSport Variety');
      setIngesting(false);
      fetchAllData();
    } catch (err) {
      console.error(err);
      setIngestError('API communication failure.');
      setIngesting(false);
    }
  };

  // Handle Surface Approval Decision (MReq 11: real campaign context, audit trail)
  const handleSurfaceDecision = async (decision: "Approved" | "Rejected") => {
    if (!selectedSurfaceId || !user) return;
    try {
      await fetchWithAuth(`/api/surfaces/${selectedSurfaceId}/approve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          decision,
          rejectionReason: decision === "Rejected" ? rejectionReason : "",
          campaignId: selectedCampaignId || undefined,
          userId: user.id
        })
      });
      setRejectionReason('');
      // Force refresh of surfaces list
      const rawUpdated = await fetchWithAuth(`/api/scenes/${selectedSceneId}/surfaces`).then(r => r.json()) as SurfaceItemResponse[];
      setSurfacesForScene(rawUpdated.map(parseSurfaceItem));
      fetchAllData();
    } catch (err) {
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
          frameNumber: 0,
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
    try {
      await fetchWithAuth(`/api/scenes/update`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: sceneId, qaStatus: 'Approved' })
      });
      if (selectedVideo) {
        const refreshed = await fetchWithAuth(`/api/content/${selectedVideo}/scenes`).then(r => r.json());
        setScenesForVideo(refreshed);
      }
      fetchAllData();
    } catch (err) {
      console.error('Failed to approve scene:', err);
    }
  };

  // Handle AI video splitting and opportunity detection using Gemini (Engine core)
  const handleAiSplitAnalyze = async (contentId: string, videoTitle: string) => {
    if (!contentId || !videoTitle) return;
    setAiAnalyzingVideoId(contentId);

    try {
      // 1. Call our real Express server with Gemini logic to analyze and split
      const res = await fetchWithAuth('/api/video/ai-split-analyze', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ videoTitle, contentId })
      });

      const responseData = await res.json();
      if (!res.ok) {
        throw new Error(responseData.error || "AI Video Split and Spatial analysis failed.");
      }

      // 2. Save the custom generated scenes & surfaces into our mock DB
      const saveRes = await fetchWithAuth('/api/video/ai-split-save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          contentId,
          scenes: responseData.data.scenes
        })
      });

      if (!saveRes.ok) {
        throw new Error("Failed to save AI-generated scenes.");
      }

      // 3. Reload everything
      await fetchAllData();
      
      // Trigger a reload of scenes for the active video
      const refreshedScenes = await fetchWithAuth(`/api/content/${contentId}/scenes`).then(r => r.json());
      setScenesForVideo(refreshedScenes);
      if (refreshedScenes.length > 0) {
        setSelectedSceneId(refreshedScenes[0].id);
      }
    } catch (err: any) {
      console.error("AI Split/Analyze Error:", err);
      alert(err instanceof Error ? err.message : "Error executing AI Spatial Video Splitting.");
    } finally {
      setAiAnalyzingVideoId(null);
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
      await fetchWithAuth('/api/renders', {
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
      fetchAllData();
    } catch (err) {
      console.error('Failed to submit placement:', err);
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
        a.download = 'Afrobotics_BIT_Implementation_Plan.doc';
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
          <div className="md:col-span-5 flex flex-col justify-between p-8 bg-gradient-to-br from-blue-700 to-indigo-900 rounded-2xl shadow-2xl text-white">
            <div>
              <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-white/10 text-[10px] font-bold uppercase tracking-wider mb-6">
                <span className="h-2 w-2 rounded-full bg-emerald-400 animate-pulse"></span>
                IDENTITY SERVICES GATEWAY
              </div>
              <h2 className="text-2xl font-black tracking-tight leading-tight">
                Afrobotics Brand Insertion Technology
              </h2>
              <p className="text-xs text-blue-100 mt-3 leading-relaxed">
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
              <img 
                src="https://static.wixstatic.com/media/b8640c_265e3e68123947c9a20bcbc636f9d98e~mv2.png" 
                alt="Afrobotics Logo" 
                className="h-10 w-auto object-contain shrink-0"
                referrerPolicy="no-referrer"
              />
              <div>
                <div className="flex items-center gap-2 mb-0.5">
                  <span className="inline-flex h-2 w-2 rounded-full bg-emerald-500 animate-pulse"></span>
                  <span className="text-[10px] font-bold uppercase tracking-widest text-emerald-600 font-mono">Afrobotics BIT</span>
                </div>
                <h1 className="text-base font-extrabold text-slate-900 tracking-tight font-display">
                  Brand Insertion Technology
                </h1>
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

                <button onClick={handleLogout} className="p-2 rounded-lg text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-all border border-slate-200 hover:border-red-100" title="Logout">
                  <LogOut className="h-4 w-4" />
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <div className="flex gap-0 max-w-full mx-auto" id="app_body">
        {/* LEFT SIDEBAR */}
        <div className="border-r border-slate-200 bg-white px-4 py-6 min-h-[calc(100vh-140px)] sticky top-[73px] self-start" id="sidebar_wrapper">
          <CampaignSidebar
            selectedCampaignId={selectedCampaignId}
            userRole={user?.role || 'Editor'}
            campaignAssetCount={selectedCampaignId ? assetList.filter(a => a.campaignId === selectedCampaignId).length : 0}
            contentCount={contentList.filter(v => v.ingestionStatus === 'Completed' && (!selectedCampaignId || v.campaignId === selectedCampaignId)).length}
            renderCount={renderList.filter(r => !selectedCampaignId || r.campaignId === selectedCampaignId).length}
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
                {!activeView && (
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
                      <p className="text-xs text-slate-500 mb-5">Define campaign schedules, regions and budgets (<strong>MReq 10</strong>).</p>
                      <form onSubmit={(e) => { e.preventDefault(); handleCreateCampaign(e); }} className="space-y-4">
                        <div className="grid grid-cols-2 gap-4">
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Campaign Name</label>
                            <input type="text" value={newCampaignName} onChange={(e) => setNewCampaignName(e.target.value)}
                              placeholder="e.g., Coke Zero Summer"
                              className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors" required />
                          </div>
                          <div>
                            <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Naming Code (MReq 10)</label>
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
                    contentList={contentList.filter(v => !selectedCampaignId || v.campaignId === selectedCampaignId)}
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
                    contentList={contentList}
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
                    handleDeleteContent={handleDeleteContent}
                    handleAiSplitAnalyze={handleAiSplitAnalyze}
                    aiAnalyzingVideoId={aiAnalyzingVideoId}
                    selectedCampaignId={selectedCampaignId}
                    campaignList={campaignList.map(c => ({ id: c.id, name: c.name }))}
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
                    hasRenders={renderList.filter(r => !selectedCampaignId || r.campaignId === selectedCampaignId).length > 0}
                  />
                )}

                {activeView === 'renders' && (
                  <ComposerTab
                    selectedSurfaceId={selectedSurfaceId}
                    selectedVideo={selectedVideo}
                    campaignList={campaignList}
                    composerCampaignId={composerCampaignId}
                    setComposerCampaignId={setComposerCampaignId}
                    assetList={assetList}
                    composerAssetId={composerAssetId}
                    setComposerAssetId={setComposerAssetId}
                    composerPreset={composerPreset}
                    setComposerPreset={setComposerPreset}
                    handleQueueRender={handleQueueRender}
                    renderList={renderList.filter(r => !selectedCampaignId || r.campaignId === selectedCampaignId)}
                    scenesForVideo={scenesForVideo}
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
        </main>
      </div>

      {/* FOOTER */}
      <footer className="max-w-7xl mx-auto px-6 mt-12 pt-6 border-t border-slate-200 text-center text-xs text-slate-400 font-mono" id="portal_footer">
        <div>Afrobotics BIT Brand Insertion Technology • Release 1 Operational Portal • Confidential Document</div>
        <div className="mt-1">Copyright © 2026 Afrobotics. All rights reserved. Registered under secure cloud-compliance guidelines.</div>
      </footer>
    </div>
  );
}
