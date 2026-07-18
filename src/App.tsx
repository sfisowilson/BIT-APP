import React, { useState, useEffect } from 'react';
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
  LogOut
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

export default function App() {
  const [activeTab, setActiveTab] = useState<'campaigns' | 'ingestion' | 'editor' | 'composer' | 'telemetry' | 'admin'>('campaigns');
  const [selectedDay, setSelectedDay] = useState<number>(1);
  const [downloading, setDownloading] = useState<boolean>(false);

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
  const [selectedCampaignId, setSelectedCampaignId] = useState<string | null>(null);
  const [selectedVideo, setSelectedVideo] = useState<string>('v-01');
  const [scenesForVideo, setScenesForVideo] = useState<SceneItem[]>([]);
  const [selectedSceneId, setSelectedSceneId] = useState<string>('');
  const [surfacesForScene, setSurfacesForScene] = useState<SurfaceItem[]>([]);
  const [selectedSurfaceId, setSelectedSurfaceId] = useState<string>('');
  const [rejectionReason, setRejectionReason] = useState<string>('');

  // Form Submissions
  const [newCampaignName, setNewCampaignName] = useState<string>('');
  const [newCampaignCode, setNewCampaignCode] = useState<string>('');
  const [newCampaignBudget, setNewCampaignBudget] = useState<string>('');
  const [newCampaignRegion, setNewCampaignRegion] = useState<string>('SADC Region');
  const [campaignError, setCampaignError] = useState<string | null>(null);

  const [newAssetName, setNewAssetName] = useState<string>('');
  const [newAssetType, setNewAssetType] = useState<"Image" | "Logo" | "Video">('Image');
  const [newAssetCategory, setNewAssetCategory] = useState<string>('Beverages (Non-Alcoholic)');

  const [newVideoTitle, setNewVideoTitle] = useState<string>('');
  const [newVideoRes, setNewVideoRes] = useState<string>('1920x1080 (1080p)');
  const [newVideoFps, setNewVideoFps] = useState<number>(50);
  const [newVideoDuration, setNewVideoDuration] = useState<string>('00:05:00');
  const [newVideoChannel, setNewVideoChannel] = useState<string>('SuperSport Variety');
  const [newVideoFile, setNewVideoFile] = useState<File | null>(null);
  const [ingestError, setIngestError] = useState<string | null>(null);

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
      setToken(data.token);
      setUser(data.user);
    } catch (err: any) {
      console.error(err);
      setAuthError(err.message || "Identity Service connection error.");
    }
  };

  const handleLogout = () => {
    setToken(null);
    setUser(null);
    clearToken();
  };

  // Secure request broker (MReq 8 over secure JWT authorization)
  const fetchWithAuth = async (url: string, options: RequestInit = {}) => {
    const res = await apiFetchWithAuth(url, options);
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

  // Load user session from local storage on load or auto-login (MReq 8)
  useEffect(() => {
    const savedToken = getToken();
    const savedUser = getSavedUser<UserSession>();
    if (savedToken && savedUser) {
      setToken(savedToken);
      setUser(savedUser);
    } else {
      // Auto-login as default editor to ensure out-of-the-box operation (Sfiso Dlamini, Editor)
      handleLogin(undefined, { email: 'loverboy.sfiso@gmail.com', pass: 'editor123' });
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

      const [
        contentRes,
        campaignsRes,
        assetsRes,
        rendersRes,
        logsRes,
        alarmsRes
      ] = await Promise.allSettled([
        fetchJson('/api/content'),
        fetchJson('/api/campaigns'),
        fetchJson('/api/assets'),
        fetchJson('/api/renders'),
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
      });
  }, [selectedVideo, contentList, token]);

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

  // Handle Campaign Creation
  const handleCreateCampaign = async (e: React.FormEvent) => {
    e.preventDefault();
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

  // Handle Asset library upload (MReq 10: optional campaign association)
  const handleUploadAsset = async (e: React.FormEvent, campaignId?: string) => {
    e.preventDefault();
    if (!newAssetName) return;
    try {
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
      setNewAssetName('');
      fetchAllData();
    } catch (err) {
      console.error(err);
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
    if (!newVideoTitle) return;
    setIngestError(null);
    try {
      const formData = new FormData();
      formData.append('title', newVideoTitle);
      formData.append('resolution', newVideoRes);
      formData.append('frameRate', String(newVideoFps));
      formData.append('duration', newVideoDuration);
      formData.append('sourceChannel', newVideoChannel);
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
        return;
      }
      setNewVideoTitle('');
      setNewVideoDuration('00:05:00');
      setNewVideoFile(null);
      fetchAllData();
    } catch (err) {
      console.error(err);
      setIngestError('API communication failure.');
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

  // Handle AI Scene Customization (Pre-Stitch model prompts)
  const handleAiCustomizeScene = async (sceneId: string, prompt: string) => {
    if (!sceneId || !prompt) return;

    // Immediately set state to processing for instant visual feedback
    setScenesForVideo(prev => prev.map(s => s.id === sceneId ? { ...s, aiPrompt: prompt, aiStatus: 'processing' } : s));

    try {
      const selectedScene = scenesForVideo.find(s => s.id === sceneId);
      const selectedVideoTitle = contentList.find(v => v.id === selectedVideo)?.title || "";

      // Call our real Express server with the prompt & metadata
      const res = await fetchWithAuth('/api/scenes/ai-modify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sceneId,
          prompt,
          videoTitle: selectedVideoTitle,
          sceneIndex: selectedScene?.sceneIndex
        })
      });

      if (!res.ok) {
        throw new Error("AI Scene customization request failed.");
      }

      const responseData = await res.json();
      const data = responseData.data;

      // Save properties locally to our mock persistence DB
      await fetchWithAuth('/api/scenes/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: sceneId,
          aiPrompt: prompt,
          aiStatus: 'completed',
          aiOutputDescription: data.description,
          aiModelUsed: 'gemini-3.5-flash'
        })
      });

      // Refresh scene list
      if (selectedVideo) {
        const refreshed = await fetchWithAuth(`/api/content/${selectedVideo}/scenes`).then(r => r.json());
        setScenesForVideo(refreshed);
      }

      // Record in system event log
      handleTriggerLog(
        "SCENE_AI_PROCESSED",
        "Info",
        "CompositingAI",
        user?.email || "loverboy.sfiso@gmail.com",
        `AI pre-stitch update applied to Scene #${selectedScene?.sceneIndex}: "${data.description.slice(0, 80)}..."`
      );
    } catch (err: any) {
      console.error("AI customization error:", err);

      setScenesForVideo(prev => prev.map(s => s.id === sceneId ? { ...s, aiStatus: 'failed' } : s));

      await fetchWithAuth('/api/scenes/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: sceneId,
          aiStatus: 'failed'
        })
      });
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
                    pass: 'adv123',
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
      <header className="border-b border-slate-200 bg-white px-6 py-5 sticky top-0 z-50 shadow-xs" id="portal_header">
        <div className="max-w-7xl mx-auto flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <span className="inline-flex h-2 w-2 rounded-full bg-blue-600 animate-pulse"></span>
              <span className="text-[10px] font-bold uppercase tracking-widest text-blue-600 font-mono">Afrobotics BIT Production Console</span>
            </div>
            <h1 className="text-xl lg:text-2xl font-extrabold text-slate-900 tracking-tight font-display">
              Brand Insertion Technology (BIT) Platform
            </h1>
            <p className="text-xs text-slate-500 mt-0.5">
              Operational Workspace — Live REST APIs, Campaign Planners, Video Ingest &amp; GPU render queues
            </p>
          </div>
          
          <div className="flex flex-wrap items-center gap-4">
            {/* Active alarms indicator */}
            {alarmList.some(a => a.isActive) && (
              <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-red-50 border border-red-200 text-red-600 text-xs font-semibold animate-pulse">
                <AlertTriangle className="h-4 w-4 text-red-500" />
                {alarmList.filter(a => a.isActive).length} CRITICAL ALARMS
              </span>
            )}

            {/* Current logged-in user info */}
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

                <button
                  type="button"
                  onClick={handleLogout}
                  className="p-2 rounded-lg text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-all border border-slate-200 hover:border-red-100"
                  title="Logout / Switch Account"
                >
                  <LogOut className="h-4 w-4" />
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 lg:px-6 mt-8" id="main_content">
        
        {/* INTERACTIVE WORKFLOW PIPELINE PROGRESS GUIDE */}
        <div className="bg-white border border-slate-200/80 rounded-2xl p-5 mb-8 shadow-sm" id="pipeline_wizard">
          <div className="flex items-center gap-2 mb-4">
            <span className="flex h-5 w-5 items-center justify-center rounded-full bg-blue-600 text-white text-[10px] font-bold">✓</span>
            <h3 className="text-xs font-extrabold text-slate-800 uppercase tracking-widest font-display">Interactive Platform Flow Pipeline</h3>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-5 lg:grid-cols-5 gap-3">
            {[
              { id: 'campaigns', step: '1', title: '1. Campaign Planner', desc: 'Define campaigns & assets', count: `${campaignList.length} Active` },
              { id: 'ingestion', step: '2', title: '2. Video Ingest', desc: 'Ingest feeds & index cuts', count: `${contentList.length} Ingested` },
              { id: 'editor', step: '3', title: '3. QA Workbench', desc: 'Approve or exclude slots', count: `Interactive Player` },
              { id: 'composer', step: '4', title: '4. Compositor', desc: 'Warp overlays & render', count: `${renderList.length} Dispatch jobs` },
              { id: 'telemetry', step: '5', title: '5. Alarms & Logs', desc: 'CSV audits & telemetry', count: `${logList.length} Events` }
            ].map((stepItem) => {
              const isActive = activeTab === stepItem.id;
              return (
                <button
                  key={stepItem.id}
                  onClick={() => setActiveTab(stepItem.id as any)}
                  className={`p-3.5 rounded-xl border text-left cursor-pointer transition-all ${
                    isActive 
                      ? 'bg-blue-50/70 border-blue-500 shadow-xs ring-1 ring-blue-500/10' 
                      : 'bg-slate-50/60 hover:bg-slate-100/50 border-slate-200/70'
                  }`}
                  id={`pipeline_step_${stepItem.id}`}
                >
                  <div className="flex items-center justify-between">
                    <span className={`text-[9px] font-mono font-bold px-1 py-0.5 rounded ${
                      isActive ? 'bg-blue-600 text-white' : 'bg-slate-200 text-slate-600'
                    }`}>
                      STEP 0{stepItem.step}
                    </span>
                    <span className="text-[9px] text-slate-400 font-mono font-medium">{stepItem.count}</span>
                  </div>
                  <h4 className={`text-xs font-bold mt-2 ${isActive ? 'text-blue-700' : 'text-slate-800'}`}>
                    {stepItem.title}
                  </h4>
                  <p className="text-[10px] text-slate-500 mt-1 leading-normal">{stepItem.desc}</p>
                </button>
              );
            })}
          </div>
        </div>

        {/* TABS SELECTOR */}
        <div className="flex border-b border-slate-200 overflow-x-auto gap-1 mb-8" id="tab_navigation">
          {[
            { id: 'campaigns', label: 'Campaigns & Assets', icon: Sliders },
            { id: 'ingestion', label: 'Video Ingestion', icon: Video },
            { id: 'editor', label: 'Approvals Workbench', icon: Tv },
            { id: 'composer', label: 'GPU Composite', icon: Cpu },
            { id: 'telemetry', label: 'O&M & Telemetry', icon: Activity },
            { id: 'admin', label: 'User Admin (RBAC)', icon: Users },
          ].map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id as any)}
                className={`flex items-center gap-2 px-4 py-3 text-xs lg:text-sm font-semibold tracking-tight whitespace-nowrap transition-all border-b-2 cursor-pointer ${
                  isActive 
                    ? 'border-blue-600 text-blue-600 bg-blue-50/30' 
                    : 'border-transparent text-slate-500 hover:text-slate-800 hover:bg-slate-100/40'
                }`}
                id={`tab_button_${tab.id}`}
              >
                <Icon className={`h-4 w-4 ${isActive ? 'text-blue-600' : 'text-slate-400'}`} />
                {tab.label}
              </button>
            );
          })}
        </div>

        {/* TAB CONTENTS RENDER BLOCK WITH TRANSITIONS */}
        <AnimatePresence mode="wait">
          {activeTab === 'campaigns' && (
            <CampaignsTab
              campaignList={campaignList}
              assetList={assetList}
              selectedCampaignId={selectedCampaignId}
              setSelectedCampaignId={setSelectedCampaignId}
              newCampaignName={newCampaignName}
              setNewCampaignName={setNewCampaignName}
              newCampaignCode={newCampaignCode}
              setNewCampaignCode={setNewCampaignCode}
              newCampaignBudget={newCampaignBudget}
              setNewCampaignBudget={setNewCampaignBudget}
              newCampaignRegion={newCampaignRegion}
              setNewCampaignRegion={setNewCampaignRegion}
              handleCreateCampaign={handleCreateCampaign}
              campaignError={campaignError}
              newAssetName={newAssetName}
              setNewAssetName={setNewAssetName}
              newAssetType={newAssetType}
              setNewAssetType={setNewAssetType}
              newAssetCategory={newAssetCategory}
              setNewAssetCategory={setNewAssetCategory}
              handleCreateAsset={handleUploadAsset}
              handleAssociateAsset={handleAssociateAsset}
              handleUnassociateAsset={handleUnassociateAsset}
              handleDeleteCampaign={handleDeleteCampaign}
              handleDeleteAsset={handleDeleteAsset}
            />
          )}

          {activeTab === 'ingestion' && (
            <IngestionTab
              contentList={contentList}
              selectedVideo={selectedVideo}
              setSelectedVideo={setSelectedVideo}
              scenesForVideo={scenesForVideo}
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
              handleDeleteContent={handleDeleteContent}
              handleAiSplitAnalyze={handleAiSplitAnalyze}
              aiAnalyzingVideoId={aiAnalyzingVideoId}
            />
          )}

          {activeTab === 'editor' && (
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
              handleAiCustomizeScene={handleAiCustomizeScene}
              assetList={assetList}
              campaignList={campaignList}
            />
          )}

          {activeTab === 'composer' && (
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
              renderList={renderList}
              scenesForVideo={scenesForVideo}
            />
          )}

          {activeTab === 'telemetry' && (
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

          {activeTab === 'admin' && (
            <AdminConsoleTab
              onTriggerLog={handleTriggerLog}
              currentUser={user}
            />
          )}
        </AnimatePresence>
      </main>

      {/* FOOTER */}
      <footer className="max-w-7xl mx-auto px-6 mt-12 pt-6 border-t border-slate-200 text-center text-xs text-slate-400 font-mono" id="portal_footer">
        <div>Afrobotics BIT Brand Insertion Technology • Release 1 Operational Portal • Confidential Document</div>
        <div className="mt-1">Copyright © 2026 Afrobotics. All rights reserved. Registered under secure cloud-compliance guidelines.</div>
      </footer>
    </div>
  );
}
