import React from 'react';
import { motion } from 'motion/react';
import {
  Tv, Play, Shield, CheckCircle, AlertTriangle, Sparkles, Wand2,
  Loader2, Eye, Layout, Image, Package, ArrowRight, Search, Cpu,
  MapPin, ChevronRight, X, Upload, RefreshCw, Download, Clock, Trash2
} from 'lucide-react';
import { ContentItem, SceneItem, SurfaceItem, CreativeAsset, CampaignItem, SurfaceAssetPair, RenderItem, CreatePromptRenderRequest, MIN_PROMPT_EDIT_DURATION_SECONDS, MAX_PROMPT_EDIT_DURATION_SECONDS } from '../types';
import { SurfaceClickOverlay } from './SurfaceClickOverlay';
import { PromptGeneratePanel } from './PromptGeneratePanel';
import { confirmInteractivePlacement, createSurfaceFromClick, createSurfaceFromQuad, fetchShotsForScene } from '../apiClient';
import type { MaskPolygon, ShotItem } from '../types';

interface EditorTabProps {
  // Core video/scene/surface selection
  contentList: ContentItem[];
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  scenesForVideo: SceneItem[];
  surfacesForScene: SurfaceItem[];
  selectedSurfaceId: string;
  setSelectedSurfaceId: (v: string) => void;

  // Surface QA
  rejectionReason: string;
  setRejectionReason: (v: string) => void;
  handleSurfaceDecision: (decision: "Approved" | "Rejected") => Promise<void>;
  currentSurface: SurfaceItem | undefined;

  // Asset inventory
  assetList: CreativeAsset[];
  campaignList: CampaignItem[];

  // Phase 1: AI analysis trigger from placements screen
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
  onDetectSurfacesForScene?: (sceneId: string, contentId: string) => Promise<void>;
  onDeleteSurface?: (surfaceId: string) => Promise<void>;
  onDeleteAllSurfaces?: (sceneId: string) => Promise<void>;

  // Phase 2: Asset placement on surfaces
  selectedCampaignId?: string;
  surfaceAssetPairs: Record<string, string>; // surfaceId -> assetId
  onPlaceAsset: (surfaceId: string, assetId: string) => void;
  onRemoveAsset: (surfaceId: string) => void;
  onSubmitPlacement: (surfaceId: string, assetId: string, campaignId: string) => Promise<boolean>;

  // Phase 3: AI asset suggestion
  onAiSuggestAssets?: (surfaceId: string) => Promise<{ assetId: string; reason: string }[]>;
  isSuggestingAssets?: Record<string, boolean>;
  aiSuggestions?: Record<string, { assetId: string; reason: string }[]>;

  // Scene approval workflow
  handleSceneApprove?: (sceneId: string) => Promise<void>;

  // Compositing preview
  onPreviewComposite?: (surfaceId: string, assetId: string) => void;
  onClearCompositePreview?: () => void;
  compositingPreview?: boolean;
  compositePreviewImage?: string | null;

  // Phase 4: Cross-tab navigation
  onNavigateToRenders?: () => void;
  onNavigateToContent?: () => void;
  hasContentIngested?: boolean;
  hasSurfacesDetected?: boolean;
  hasPlacedAssets?: boolean;
  hasRenders?: boolean;

  // Render tracking — show render status inline on the placement screen
  renderList?: RenderItem[];
  onRetryRender?: (renderId: string) => Promise<void>;
  onSetRenderQueuedForFinal?: (renderId: string, queued: boolean) => Promise<void>;
  onDeleteRender?: (renderId: string) => Promise<void>;
  userRole?: 'Admin' | 'Editor' | 'Advertiser';

  // AI Placement Assistant — "Generate New" mode (prompt-based AI video placement, no surface required)
  onSubmitPromptPlacement?: (dto: CreatePromptRenderRequest) => Promise<void>;
  onApprovePromptSplice?: (renderId: string) => Promise<void>;
  onRejectPromptPlacement?: (renderId: string) => Promise<void>;
  activePromptRender?: RenderItem | null;

  // Final assembly — combine every scene's queued render + original footage into one video
  selectedContent?: ContentItem | null;
  onStartFinalAssembly?: (contentId: string) => Promise<void>;
}

export const EditorTab: React.FC<EditorTabProps> = ({
  contentList,
  selectedVideo,
  setSelectedVideo,
  selectedSceneId,
  setSelectedSceneId,
  scenesForVideo = [],
  surfacesForScene = [],
  selectedSurfaceId = '',
  setSelectedSurfaceId,
  rejectionReason,
  setRejectionReason,
  handleSurfaceDecision,
  currentSurface,
  assetList = [],
  campaignList = [],
  // Phase 1
  handleAiSplitAnalyze,
  aiAnalyzingVideoId,
  onDetectSurfacesForScene,
  onDeleteSurface,
  onDeleteAllSurfaces,
  // Phase 2
  selectedCampaignId,
  surfaceAssetPairs = {},
  onPlaceAsset,
  onRemoveAsset,
  onSubmitPlacement,
  // Phase 3
  onAiSuggestAssets,
  isSuggestingAssets = {},
  aiSuggestions = {},
  // Scene approval
  handleSceneApprove,
  // Compositing
  onPreviewComposite,
  onClearCompositePreview,
  compositingPreview = false,
  compositePreviewImage,
  // Phase 4
  onNavigateToRenders,
  onNavigateToContent,
  hasContentIngested = false,
  hasSurfacesDetected = false,
  hasPlacedAssets = false,
  hasRenders = false,
  // Render tracking
  renderList = [],
  onRetryRender,
  onSetRenderQueuedForFinal,
  onDeleteRender,
  userRole,
  // AI Placement Assistant — Generate New mode
  onSubmitPromptPlacement,
  onApprovePromptSplice,
  onRejectPromptPlacement,
  activePromptRender,
  // Final assembly
  selectedContent,
  onStartFinalAssembly,
}) => {
  // Controls whether the click-to-place overlay (SurfaceClickOverlay) intercepts clicks on the
  // video. It fully covers the player, including the native play/pause/seek controls, so it must
  // be off by default — otherwise every click is swallowed as a placement click and the video is
  // unplayable. Turned on automatically when the user picks a placement mode (explicit intent to
  // click on the video next); the toggle button lets them drop back to normal playback at any time.
  const [placementOverlayActive, setPlacementOverlayActive] = React.useState(false);

  // Shared inline feedback for this tab's action handlers (approve/reject, delete, detect,
  // submit placement, etc.) — these used to surface failures via a raw browser alert(); the
  // handlers now let errors propagate and this banner displays them instead, consistent with
  // the rest of the app's UI.
  const [actionError, setActionError] = React.useState('');
  const [actionSuccess, setActionSuccess] = React.useState('');
  const runAction = async <T,>(fn: () => Promise<T>, successMessage?: string): Promise<T | undefined> => {
    setActionError('');
    setActionSuccess('');
    try {
      const result = await fn();
      if (successMessage) setActionSuccess(successMessage);
      return result;
    } catch (err: any) {
      setActionError(err.message || 'Action failed.');
      return undefined;
    }
  };

  const [aiPromptText, setAiPromptText] = React.useState('');
  const [aiPlacing, setAiPlacing] = React.useState(false);
  const [aiExplanation, setAiExplanation] = React.useState('');
  const [assistantMode, setAssistantMode] = React.useState<'match' | 'generate'>('match');
  const [previewAssetId, setPreviewAssetId] = React.useState<string>('');
  const [selectedBlendMode, setSelectedBlendMode] = React.useState<'multiply' | 'overlay' | 'normal'>('multiply');
  const [ambientIntensity, setAmbientIntensity] = React.useState<number>(0.85);
  const [showingPlacementPanel, setShowingPlacementPanel] = React.useState<boolean>(true);
  const videoRef = React.useRef<HTMLVideoElement>(null);
  const [submitConfirming, setSubmitConfirming] = React.useState<string>('');
  const [redetectConfirmOpen, setRedetectConfirmOpen] = React.useState(false);
  const [deleteSurfaceConfirmId, setDeleteSurfaceConfirmId] = React.useState<string | null>(null);
  const [deleteAllSurfacesConfirmOpen, setDeleteAllSurfacesConfirmOpen] = React.useState(false);
  const [deletingSurfaceId, setDeletingSurfaceId] = React.useState<string | null>(null);
  const [deletingAllSurfaces, setDeletingAllSurfaces] = React.useState(false);
  const [retryingId, setRetryingId] = React.useState<string | null>(null);
  const [queuingId, setQueuingId] = React.useState<string | null>(null);
  const [deletingRenderId, setDeletingRenderId] = React.useState<string | null>(null);
  const [deleteRenderConfirmId, setDeleteRenderConfirmId] = React.useState<string | null>(null);
  const [assemblingFinal, setAssemblingFinal] = React.useState(false);

  // ── Interactive placement state ──
  const [interactionMode, setInteractionMode] = React.useState<'product' | 'signage'>('product');
  const [interactiveMask, setInteractiveMask] = React.useState<import('../types').MaskPolygon | null>(null);
  const [interactiveQuad, setInteractiveQuad] = React.useState<[import('../components/SurfaceClickOverlay').QuadPoint, import('../components/SurfaceClickOverlay').QuadPoint, import('../components/SurfaceClickOverlay').QuadPoint, import('../components/SurfaceClickOverlay').QuadPoint] | null>(null);
  const [interactiveAssetId, setInteractiveAssetId] = React.useState<string>('');
  const [interactivePlacing, setInteractivePlacing] = React.useState(false);

  // ── Derived data ──────────────────────────────────────────────────
  const currentScene = scenesForVideo.find(s => s.id === selectedSceneId);
  const activeVideo = contentList.find(v => v.id === selectedVideo);
  const isLocalVideo = activeVideo?.storageKey?.startsWith('/api/content/file/');

  // Shots (camera cuts) making up the current scene — a scene can span multiple shots,
  // and a placement must stay consistent across all of them.
  const [shotsForScene, setShotsForScene] = React.useState<ShotItem[]>([]);
  React.useEffect(() => {
    if (!selectedSceneId) { setShotsForScene([]); return; }
    let cancelled = false;
    fetchShotsForScene(selectedSceneId)
      .then(shots => { if (!cancelled) setShotsForScene(shots); })
      .catch(() => { if (!cancelled) setShotsForScene([]); });
    return () => { cancelled = true; };
  }, [selectedSceneId]);

  // Track video playback position as frame number. Also enforces the selected scene's
  // boundary during playback — without this, native <video controls> playback runs straight
  // past the scene into whatever comes next, with no indication the scene itself has ended.
  // Reaching the end loops back to the scene's start and keeps playing, rather than just
  // pausing dead at the last frame — repeated review of a scene shouldn't require hunting
  // down the separate "Replay Scene" button every time.
  const [currentVideoFrame, setCurrentVideoFrame] = React.useState<number>(0);
  React.useEffect(() => {
    const vid = videoRef.current;
    if (!vid || !activeVideo?.frameRate) return;
    const fps = activeVideo.frameRate;
    const onTimeUpdate = () => {
      const frame = Math.round(vid.currentTime * fps);
      setCurrentVideoFrame(frame);
      if (currentScene && !vid.paused && frame >= currentScene.endFrame) {
        vid.currentTime = currentScene.startFrame / fps;
        vid.play();
      }
    };
    vid.addEventListener('timeupdate', onTimeUpdate);
    return () => { vid.removeEventListener('timeupdate', onTimeUpdate); };
  }, [activeVideo?.frameRate, selectedSceneId, currentScene?.startFrame, currentScene?.endFrame]);

  // Filter surfaces: only show those detected near the current video frame (±30 frames)
  const visibleSurfaces = React.useMemo(() => {
    const frameWindow = 30;
    return surfacesForScene.filter(sf => {
      if (sf.id === selectedSurfaceId) return true;
      if (sf.detectedAtFrame == null || sf.detectedAtFrame === 0) return true;
      return Math.abs(sf.detectedAtFrame - currentVideoFrame) <= frameWindow;
    });
  }, [surfacesForScene, currentVideoFrame, selectedSurfaceId]);

  // Seek the video to a specific frame number, waiting for metadata if the video isn't ready yet.
  const seekToFrame = (seekFrame: number) => {
    const vid = videoRef.current;
    if (!vid || !activeVideo?.frameRate) return;
    const fps = activeVideo.frameRate;

    const rawSeekTime = seekFrame / fps;
    if (!isFinite(rawSeekTime) || rawSeekTime < 0) return;

    const doSeek = () => {
      if (!vid || vid.readyState < 1) return;
      // Clamp to the video's real duration — computed here, inside doSeek, rather than
      // before the readyState check. vid.duration is NaN until metadata has loaded, and
      // `NaN || 10` silently falls back to a bogus 10s default, which clamped every seek on
      // a freshly-mounted video down to ~9.9s regardless of the actual target (the exact bug
      // behind scenes never seeking to the right frame when navigating in from Content tab).
      const maxSafeTime = Math.max(0.1, (vid.duration || rawSeekTime + 1) - 0.1);
      const seekTime = Math.min(rawSeekTime, maxSafeTime);
      vid.currentTime = seekTime;
      vid.pause();
    };

    if (vid.readyState >= 1) {
      doSeek();
    } else {
      const onLoaded = () => {
        vid.removeEventListener('loadedmetadata', onLoaded);
        doSeek();
      };
      vid.addEventListener('loadedmetadata', onLoaded);
    }
  };

  // Seek video to the exact frame where this surface was detected
  const seekToSurface = (surfaceId: string) => {
    setSelectedSurfaceId(surfaceId);
    if (!activeVideo) return;
    const surface = surfacesForScene.find(s => s.id === surfaceId);
    if (!surface) return;

    let seekFrame: number;
    if (surface.detectedAtFrame != null && surface.detectedAtFrame >= 0) {
      seekFrame = surface.detectedAtFrame;
    } else {
      const scene = scenesForVideo.find(s => s.id === surface.sceneId);
      if (!scene) return;
      seekFrame = scene.startFrame + (scene.endFrame - scene.startFrame) / 2;
    }
    seekToFrame(seekFrame);
  };

  // Jump the video preview to the newly-selected scene's midpoint so the image actually
  // reflects what's selected, instead of staying wherever it happened to be (e.g. frame 0).
  React.useEffect(() => {
    if (!currentScene) return;
    const midpoint = currentScene.startFrame + (currentScene.endFrame - currentScene.startFrame) / 2;
    seekToFrame(midpoint);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedSceneId]);
  const hasCompletedVideos = contentList.some(v => v.ingestionStatus === 'Completed' && selectedCampaignId && v.campaignId === selectedCampaignId);
  const hasScenes = scenesForVideo.length > 0;

  const campaignAssets = selectedCampaignId
    ? assetList.filter(a => a.campaignId === selectedCampaignId)
    : assetList;

  const activePreviewAsset = assetList.find(a => a.id === previewAssetId);

  const getPlacedAsset = (surfaceId: string): CreativeAsset | undefined => {
    const assetId = surfaceAssetPairs[surfaceId];
    return assetId ? assetList.find(a => a.id === assetId) : undefined;
  };

  const steps = [
    { label: 'Content', done: hasContentIngested, icon: Upload },
    { label: 'Placements', done: hasSurfacesDetected, icon: MapPin, active: true },
    { label: 'Renders', done: hasRenders, icon: Cpu },
  ];

  const placedSurfaceCount = Object.keys(surfaceAssetPairs).length;

  // Dynamic viewBox from video resolution (fallback 1280x720)
  const videoWidth = videoRef.current?.videoWidth || activeVideo?.width || 1920;
  const videoHeight = videoRef.current?.videoHeight || activeVideo?.height || 1080;
  const viewBoxValue = `0 0 ${videoWidth} ${videoHeight}`;

  // Calculate letterbox offset so the SVG overlay aligns exactly with the rendered video content
  const [videoRect, setVideoRect] = React.useState<{ x: number; y: number; w: number; h: number }>({ x: 0, y: 0, w: 1, h: 1 });
  React.useEffect(() => {
    const vid = videoRef.current;
    if (!vid || !activeVideo?.resolution) return;
    const updateRect = () => {
      if (!vid || vid.videoWidth === 0) return;
      const elW = vid.clientWidth;
      const elH = vid.clientHeight;
      const vidW = vid.videoWidth;
      const vidH = vid.videoHeight;
      if (vidW === 0 || vidH === 0) return;
      const scale = Math.min(elW / vidW, elH / vidH);
      const dispW = vidW * scale;
      const dispH = vidH * scale;
      const offsetX = (elW - dispW) / 2;
      const offsetY = (elH - dispH) / 2;
      setVideoRect({ x: offsetX, y: offsetY, w: dispW, h: dispH });
    };
    updateRect();
    const observer = new ResizeObserver(updateRect);
    observer.observe(vid);
    vid.addEventListener('loadedmetadata', updateRect);
    return () => {
      observer.disconnect();
      vid.removeEventListener('loadedmetadata', updateRect);
    };
  }, [activeVideo?.resolution]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="space-y-6"
      key="editor_tab"
    >
      {/* ═══ Diagnostic pipeline status ═══ */}
      <div className="bg-white border border-slate-200/95 rounded-2xl p-4 shadow-sm">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-center">
          <div className={`rounded-lg p-2.5 ${hasCompletedVideos ? 'bg-emerald-50 border border-emerald-200' : 'bg-red-50 border border-red-200'}`}>
            <div className={`text-lg font-bold ${hasCompletedVideos ? 'text-emerald-600' : 'text-red-500'}`}>
              {contentList.filter(v => v.ingestionStatus === 'Completed' && selectedCampaignId && v.campaignId === selectedCampaignId).length}
            </div>
            <div className="text-[10px] font-mono font-bold uppercase text-slate-500">Videos Ready</div>
          </div>
          <div className={`rounded-lg p-2.5 ${hasScenes ? 'bg-emerald-50 border border-emerald-200' : 'bg-amber-50 border border-amber-200'}`}>
            <div className={`text-lg font-bold ${hasScenes ? 'text-emerald-600' : 'text-amber-500'}`}>
              {scenesForVideo.length}
            </div>
            <div className="text-[10px] font-mono font-bold uppercase text-slate-500">Scenes</div>
          </div>
          <div className={`rounded-lg p-2.5 ${surfacesForScene.length > 0 ? 'bg-emerald-50 border border-emerald-200' : 'bg-amber-50 border border-amber-200'}`}>
            <div className={`text-lg font-bold ${surfacesForScene.length > 0 ? 'text-emerald-600' : 'text-amber-500'}`}>
              {surfacesForScene.length}
            </div>
            <div className="text-[10px] font-mono font-bold uppercase text-slate-500">Surfaces</div>
          </div>
          <div className={`rounded-lg p-2.5 ${placedSurfaceCount > 0 ? 'bg-emerald-50 border border-emerald-200' : 'bg-slate-50 border border-slate-200'}`}>
            <div className={`text-lg font-bold ${placedSurfaceCount > 0 ? 'text-emerald-600' : 'text-slate-400'}`}>
              {placedSurfaceCount}
            </div>
            <div className="text-[10px] font-mono font-bold uppercase text-slate-500">Placed</div>
          </div>
        </div>
        <div className="mt-3 pt-3 border-t border-slate-100 flex items-center gap-2 text-[10px] font-mono text-slate-400">
          <span className="font-bold text-slate-500">Active:</span>
          <span className="text-slate-600">{activeVideo?.title || '—'}</span>
          <span className="text-slate-300">|</span>
          <span className="text-slate-600">Scene #{currentScene?.sceneIndex ?? '—'}</span>
          <span className="text-slate-300">|</span>
          <span className="text-slate-600">{activeVideo?.resolution || '—'}</span>
          <span className="text-slate-300">|</span>
          <span className={`font-bold ${selectedSurfaceId ? 'text-blue-600' : 'text-slate-400'}`}>
            {selectedSurfaceId ? `Surface: ${currentSurface?.surfaceType || selectedSurfaceId.slice(0,12)}` : 'No surface selected'}
          </span>
        </div>
      </div>

      {/* ═══ Step indicator bar ═══ */}
      <div className="bg-white border border-slate-200/95 rounded-2xl p-4 shadow-sm">
        <div className="flex items-center gap-2">
          {steps.map((step, i) => {
            const Icon = step.icon;
            const isLast = i === steps.length - 1;
            return (
              <React.Fragment key={step.label}>
                <div className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold transition-all ${
                  step.active
                    ? 'bg-blue-50 text-blue-700 border border-blue-200'
                    : step.done
                      ? 'bg-emerald-50 text-emerald-600 border border-emerald-200'
                      : 'bg-slate-50 text-slate-400 border border-slate-200'
                }`}>
                  {step.done ? (
                    <CheckCircle className="h-3.5 w-3.5 text-emerald-500" />
                  ) : (
                    <Icon className="h-3.5 w-3.5" />
                  )}
                  <span>{step.label}</span>
                </div>
                {!isLast && <ChevronRight className="h-3.5 w-3.5 text-slate-300" />}
              </React.Fragment>
            );
          })}
        </div>
      </div>

      {/* ═══ No surfaces state ═══ */}
      {hasScenes && surfacesForScene.length === 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-2xl p-6 shadow-sm">
          <div className="flex items-start gap-4">
            <div className="h-10 w-10 rounded-xl bg-amber-100 flex items-center justify-center shrink-0">
              <Search className="h-5 w-5 text-amber-600" />
            </div>
            <div className="flex-1">
              <h3 className="text-sm font-bold text-amber-800 font-display">No Advertising Surfaces Detected</h3>
              <p className="text-xs text-amber-600 mt-1 leading-relaxed">
                This scene hasn't been analyzed for placement opportunities yet. Run AI surface detection to find
                billboards, screens, walls, and other surfaces where brand assets can be placed.
              </p>
              <div className="flex items-center gap-3 mt-4">
                {onDetectSurfacesForScene && activeVideo && selectedSceneId && (() => {
                  const currentScene = scenesForVideo.find(s => s.id === selectedSceneId);
                  const isDetecting = currentScene?.surfaceStatus === 'Detecting';
                  return (
                  <button
                    onClick={() => runAction(() => onDetectSurfacesForScene(selectedSceneId, selectedVideo))}
                    className={`inline-flex items-center gap-2 px-4 py-2 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm ${
                      isDetecting ? 'bg-amber-500 hover:bg-amber-400' : 'bg-amber-600 hover:bg-amber-500'
                    }`}
                    title={isDetecting ? 'Detection may be stuck. Click to retry.' : 'Run AI surface detection'}
                  >
                    {isDetecting ? (
                      <>
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        Detecting Surfaces...
                      </>
                    ) : (
                      <>
                        <Sparkles className="h-3.5 w-3.5" />
                        Run AI Surface Detection
                      </>
                    )}
                  </button>
                  );
                })()}
                {onNavigateToContent && (
                  <button
                    onClick={onNavigateToContent}
                    className="inline-flex items-center gap-1.5 text-xs text-amber-700 hover:text-amber-900 font-medium cursor-pointer"
                  >
                    <Upload className="h-3 w-3" />
                    Go to Content tab
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ═══ No scenes at all ═══ */}
      {!hasScenes && hasCompletedVideos && (
        <div className="bg-blue-50 border border-blue-200 rounded-2xl p-6 shadow-sm">
          <div className="flex items-start gap-4">
            <div className="h-10 w-10 rounded-xl bg-blue-100 flex items-center justify-center shrink-0">
              <Tv className="h-5 w-5 text-blue-600" />
            </div>
            <div className="flex-1">
              <h3 className="text-sm font-bold text-blue-800 font-display">Scenes Not Yet Detected</h3>
              <p className="text-xs text-blue-600 mt-1 leading-relaxed">
                Your video has been ingested but scene detection hasn't run yet. Run the AI analysis from the Content tab
                to generate scenes and detect advertising surfaces.
              </p>
              {onNavigateToContent && (
                <button
                  onClick={onNavigateToContent}
                  className="mt-3 inline-flex items-center gap-1.5 px-3 py-1.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
                >
                  <ArrowRight className="h-3.5 w-3.5" />
                  Go to Content Tab
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* ═══ No videos at all ═══ */}
      {!hasCompletedVideos && (
        <div className="bg-slate-50 border border-slate-200 rounded-2xl p-8 shadow-sm text-center">
          <Upload className="h-12 w-12 text-slate-300 mx-auto mb-3" />
          <h3 className="text-sm font-bold text-slate-700 font-display">No Videos Ready for Placement</h3>
          <p className="text-xs text-slate-500 mt-1 max-w-md mx-auto">
            Upload and ingest a video in the Content tab first. Once ingested, run AI Scene Analysis to automatically
            detect advertising surfaces.
          </p>
          {onNavigateToContent && (
            <button
              onClick={onNavigateToContent}
              className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <ArrowRight className="h-3.5 w-3.5" />
              Go to Content Tab
            </button>
          )}
        </div>
      )}

      {/* ═══ Main layout ═══ */}
        {(actionError || actionSuccess) && (
          <div className={`mb-4 flex items-center justify-between gap-3 rounded-xl border px-4 py-2.5 text-xs font-semibold ${
            actionError ? 'bg-red-50 border-red-200 text-red-700' : 'bg-emerald-50 border-emerald-200 text-emerald-700'
          }`}>
            <span>{actionError ? `⚠️ ${actionError}` : `✅ ${actionSuccess}`}</span>
            <button
              onClick={() => { setActionError(''); setActionSuccess(''); }}
              className="shrink-0 text-current opacity-60 hover:opacity-100 cursor-pointer"
            >
              ✕
            </button>
          </div>
        )}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* ── LEFT (2/3): Video player + blend + AI enhance ── */}
          <div className="lg:col-span-2 space-y-6">

            {/* Video Player Card */}
            <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
              <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between border-b border-slate-100 pb-4 mb-5 gap-3">
                <div>
                  <h2 className="text-lg font-bold text-slate-800 font-display">Placement Workbench</h2>
                  <p className="text-xs text-slate-400 mt-0.5">
                    Review detected surfaces and place brand assets on them.
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  {/* Interactive mode toggle */}
                  <div className="flex items-center rounded-lg border border-indigo-200 overflow-hidden">
                    <button
                      onClick={() => { setInteractionMode('product'); setPlacementOverlayActive(true); }}
                      className={`px-2.5 py-1 text-[10px] font-bold cursor-pointer transition-colors ${
                        interactionMode === 'product'
                          ? 'bg-indigo-600 text-white'
                          : 'bg-white text-slate-600 hover:bg-indigo-50'
                      }`}
                    >
                      🎯 Insert Product
                    </button>
                    <button
                      onClick={() => { setInteractionMode('signage'); setPlacementOverlayActive(true); }}
                      className={`px-2.5 py-1 text-[10px] font-bold cursor-pointer transition-colors ${
                        interactionMode === 'signage'
                          ? 'bg-emerald-600 text-white'
                          : 'bg-white text-slate-600 hover:bg-emerald-50'
                      }`}
                    >
                      📐 Place Signage
                    </button>
                  </div>
                  {/* Escape hatch — the click-to-place overlay covers the whole video including
                      native play/pause/seek controls, so this is the only way to play the video
                      while a placement mode is selected. */}
                  <button
                    onClick={() => setPlacementOverlayActive(!placementOverlayActive)}
                    title={placementOverlayActive ? 'Click-to-place is capturing clicks on the video — turn off to use play/pause/seek' : 'Video controls are active — turn on to click a point/corner on the video'}
                    className={`px-2.5 py-1 text-[10px] font-bold rounded-lg border cursor-pointer transition-colors ${
                      placementOverlayActive
                        ? 'bg-amber-50 border-amber-300 text-amber-700 hover:bg-amber-100'
                        : 'bg-white border-slate-200 text-slate-600 hover:bg-slate-50'
                    }`}
                  >
                    {placementOverlayActive ? '🎯 Click-to-Place ON — click to play video instead' : '▶ Video Controls Active — click to place'}
                  </button>
                  {currentScene && (
                    <button
                      onClick={() => {
                        // seekToFrame always pauses as part of the seek itself — play() called
                        // immediately after races the still-in-progress seek and gets silently
                        // dropped, so wait for the real 'seeked' event before resuming playback.
                        const vid = videoRef.current;
                        seekToFrame(currentScene.startFrame);
                        if (vid) {
                          const onSeeked = () => { vid.play(); vid.removeEventListener('seeked', onSeeked); };
                          vid.addEventListener('seeked', onSeeked);
                        }
                      }}
                      title="Jump back to the start of this scene and play"
                      className="inline-flex items-center gap-1.5 px-2.5 py-1 text-[10px] font-bold rounded-lg border bg-white border-slate-200 text-slate-600 hover:bg-slate-50 cursor-pointer transition-colors"
                    >
                      <RefreshCw className="h-3 w-3" /> Replay Scene
                    </button>
                  )}
                  {/* Approve interactive placement */}
                  {(interactiveMask || interactiveQuad) && selectedCampaignId && (
                    <button
                      onClick={async () => {
                        if (!interactiveAssetId) return;
                        setInteractivePlacing(true);
                        try {
                          const assetType = interactionMode === 'signage' ? 'Planar' : 'Generative';

                          // Persist the click/quad as a real SurfaceItem first — the render
                          // dispatch below requires a surfaceId that already exists in the DB.
                          let surfaceId: string;
                          if (assetType === 'Planar' && interactiveQuad) {
                            const created = await createSurfaceFromQuad({
                              contentId: selectedVideo,
                              frameIndex: currentVideoFrame,
                              quadCornersJson: JSON.stringify(interactiveQuad),
                            });
                            surfaceId = created.surfaceId;
                          } else if (interactiveMask) {
                            const created = await createSurfaceFromClick({
                              contentId: selectedVideo,
                              frameIndex: currentVideoFrame,
                              maskPolygonJson: JSON.stringify(interactiveMask.points),
                            });
                            surfaceId = created.surfaceId;
                          } else {
                            return;
                          }

                          await confirmInteractivePlacement({
                            contentId: selectedVideo,
                            surfaceId,
                            campaignId: selectedCampaignId,
                            assetId: interactiveAssetId,
                            assetType,
                          });
                          setInteractiveMask(null);
                          setInteractiveQuad(null);
                        } catch (err: any) {
                          console.error('Interactive placement failed:', err);
                          setActionError(err.message || 'Failed to submit placement.');
                        } finally {
                          setInteractivePlacing(false);
                        }
                      }}
                      disabled={interactivePlacing || !interactiveAssetId}
                      className="px-3 py-1.5 text-[10px] font-bold rounded-lg bg-emerald-600 hover:bg-emerald-500 disabled:bg-slate-300 text-white cursor-pointer transition-all shadow-sm whitespace-nowrap"
                    >
                      {interactivePlacing ? 'Dispatching…' : '✅ Approve & Render'}
                    </button>
                  )}
                  {/* Asset selector for interactive placement */}
                  {(interactiveMask || interactiveQuad) && (
                    <select
                      value={interactiveAssetId}
                      onChange={(e) => setInteractiveAssetId(e.target.value)}
                      className="bg-white border border-slate-200 rounded-lg px-2 py-1.5 text-[10px] text-slate-800 focus:outline-none"
                    >
                      <option value="">Select asset…</option>
                      {assetList.map(a => (
                        <option key={a.id} value={a.id}>{a.name}</option>
                      ))}
                    </select>
                  )}
                  <div className="flex items-center gap-1 bg-slate-100 border border-slate-200 rounded-lg px-2 py-1 text-slate-600 font-mono text-[10px]">
                    <Eye className="h-3 w-3 text-fuchsia-600 animate-pulse" />
                    <span className="font-bold text-slate-700">Preview:</span>
                    <select
                      value={previewAssetId}
                      onChange={(e) => setPreviewAssetId(e.target.value)}
                      className="bg-transparent text-[10px] text-fuchsia-700 font-bold focus:outline-none border-none cursor-pointer"
                    >
                      <option value="">None</option>
                      {assetList.map(a => (
                        <option key={a.id} value={a.id}>{a.name}</option>
                      ))}
                    </select>
                  </div>
                  <select
                    value={selectedVideo}
                    onChange={(e) => setSelectedVideo(e.target.value)}
                    className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:outline-none"
                  >
                    {contentList.filter(v => v.ingestionStatus === 'Completed' && selectedCampaignId && v.campaignId === selectedCampaignId).map(v => (
                      <option key={v.id} value={v.id}>{v.title}</option>
                    ))}
                  </select>
                  {hasScenes && (
                    <select
                      value={selectedSceneId}
                      onChange={(e) => setSelectedSceneId(e.target.value)}
                      className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 font-mono focus:outline-none"
                    >
                      {scenesForVideo.map(s => (
                        <option key={s.id} value={s.id}>
                          Scene #{s.sceneIndex}{s.qaStatus === 'Approved' ? ' ✓' : s.qaStatus === 'Flagged' ? ' ⚠' : ''}
                        </option>
                      ))}
                    </select>
                  )}
                </div>
              </div>

              {/* Final assembly — combine every scene's queued render + original footage into one video */}
              {scenesForVideo.length > 0 && (onStartFinalAssembly || selectedContent) && (() => {
                const queuedSceneIds = new Set(renderList.filter(r => r.isQueuedForFinal && r.sceneId).map(r => r.sceneId));
                const queuedCount = scenesForVideo.filter(s => queuedSceneIds.has(s.id)).length;
                const status = selectedContent?.finalAssemblyStatus || 'NotStarted';
                const isAssembling = status === 'Processing' || assemblingFinal;

                return (
                  <div className="mt-2 p-3 rounded-xl border border-indigo-200 bg-indigo-50/50">
                    <div className="flex items-center justify-between gap-3 flex-wrap">
                      <span className="text-xs font-semibold text-indigo-900">
                        {queuedCount}/{scenesForVideo.length} scene{scenesForVideo.length !== 1 ? 's' : ''} queued for the final video
                      </span>
                      <div className="flex items-center gap-2">
                        {status === 'Finished' && selectedContent?.finalVideoStorageKey && (
                          <a href={selectedContent.finalVideoStorageKey} download className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm">
                            <Download className="h-3 w-3" /> Download Final Video
                          </a>
                        )}
                        {onStartFinalAssembly && selectedContent && (
                          <button
                            onClick={async () => {
                              setAssemblingFinal(true);
                              try { await onStartFinalAssembly(selectedContent.id); }
                              catch (err: any) { setActionError(err.message || 'Failed to start final assembly.'); }
                              finally { setAssemblingFinal(false); }
                            }}
                            disabled={isAssembling || queuedCount === 0}
                            title={queuedCount === 0 ? 'Queue at least one scene\'s render first' : undefined}
                            className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 disabled:bg-indigo-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm"
                          >
                            {isAssembling ? <><Loader2 className="h-3 w-3 animate-spin" />Assembling...</> : <><Sparkles className="h-3 w-3" />Render Final Video</>}
                          </button>
                        )}
                      </div>
                    </div>
                    {status === 'Processing' && (
                      <div className="w-full bg-indigo-200 rounded-full h-1.5 mt-2 overflow-hidden">
                        <div className="bg-indigo-500 h-full rounded-full transition-all duration-500" style={{ width: `${selectedContent?.finalAssemblyProgress || 0}%` }} />
                      </div>
                    )}
                    {status === 'Finished' && selectedContent?.finalVideoStorageKey && (
                      <video key={selectedContent.finalVideoStorageKey} src={selectedContent.finalVideoStorageKey} controls className="w-full rounded-lg mt-2 bg-black" />
                    )}
                    {status === 'Failed' && selectedContent?.finalAssemblyErrorMessage && (
                      <div className="text-[10px] text-red-600 mt-1.5">{selectedContent.finalAssemblyErrorMessage}</div>
                    )}
                  </div>
                );
              })()}

              {/* Scene Approval Bar */}
              {currentScene && placedSurfaceCount > 0 && (
                <div className={`mt-2 p-3 rounded-xl border flex items-center justify-between ${
                  currentScene.qaStatus === 'Approved'
                    ? 'bg-emerald-50 border-emerald-200'
                    : 'bg-amber-50 border-amber-200'
                }`}>
                  <div className="flex items-center gap-2 text-xs">
                    {currentScene.qaStatus === 'Approved' ? (
                      <><CheckCircle className="h-4 w-4 text-emerald-600" /><span className="font-bold text-emerald-800">Scene Approved — Ready for Render</span></>
                    ) : (
                      <><Eye className="h-4 w-4 text-amber-600" /><span className="font-bold text-amber-800">{placedSurfaceCount} asset{placedSurfaceCount > 1 ? 's' : ''} placed — Review and approve this scene</span></>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    {onPreviewComposite && selectedSurfaceId && surfaceAssetPairs[selectedSurfaceId] && (
                      <button
                        onClick={() => onPreviewComposite(selectedSurfaceId, surfaceAssetPairs[selectedSurfaceId])}
                        disabled={compositingPreview}
                        className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-lg bg-fuchsia-600 hover:bg-fuchsia-500 disabled:bg-fuchsia-300 text-white cursor-pointer transition-all shadow-sm"
                      >
                        {compositingPreview ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Generating Preview...</> : <>🎬 Preview Composite</>}
                      </button>
                    )}
                    {currentScene.qaStatus !== 'Approved' && handleSceneApprove && (
                      <button onClick={() => runAction(() => handleSceneApprove(currentScene.id), 'Scene approved successfully! You can now submit for rendering.')}
                        className="px-2.5 py-1 text-[10px] font-bold rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white cursor-pointer transition-colors">
                        ✓ Approve Scene
                      </button>
                    )}
                  </div>
                </div>
              )}

              {/* Compositing preview result */}
              {compositePreviewImage && (
                <div className="mt-2 p-2 bg-slate-900 rounded-xl border border-fuchsia-500/50">
                  <div className="flex items-center justify-between mb-1">
                    <span className="text-[9px] font-mono text-fuchsia-400 uppercase font-bold">Compositing Preview</span>
                    <button onClick={() => onClearCompositePreview?.()} className="text-[9px] text-slate-400 hover:text-white cursor-pointer">✕ Close</button>
                  </div>
                  <img src={`data:image/png;base64,${compositePreviewImage}`}
                    className="w-full rounded-lg border border-slate-700"
                    alt="Composited preview" />
                </div>
              )}

              {/* Video player */}
              <div className={`relative aspect-video bg-black border rounded-xl overflow-hidden group shadow-2xl transition-all duration-500 ${
                currentScene?.aiStatus === 'completed' ? 'border-fuchsia-500 shadow-fuchsia-500/10' : 'border-slate-700'
              }`}>
                {isLocalVideo && activeVideo ? (
                  <video
                    ref={videoRef}
                    src={activeVideo.storageKey}
                    className="absolute inset-0 w-full h-full object-contain"
                    controls
                    preload="metadata"
                    id="qa_video_player"
                  />
                ) : (
                  <div className="absolute inset-0 bg-gradient-to-br from-slate-800 via-slate-900 to-slate-950 flex items-center justify-center">
                    <div className="text-center">
                      <Play className="h-12 w-12 text-white/20 mx-auto mb-2" />
                      <p className="text-white/30 text-xs font-mono">No video file — upload in Content tab</p>
                    </div>
                  </div>
                )}

                {/* Interactive placement overlay — click-to-segment + draw-to-place */}
                {isLocalVideo && activeVideo && (
                  <SurfaceClickOverlay
                    videoRef={videoRef}
                    contentId={selectedVideo}
                    currentFrame={currentVideoFrame}
                    frameRate={activeVideo.frameRate || 30}
                    mode={interactionMode}
                    active={placementOverlayActive}
                    shots={shotsForScene}
                    assetUrl={
                      interactiveAssetId
                        ? assetList.find(a => a.id === interactiveAssetId)?.storageKey
                        : undefined
                    }
                    onMaskReceived={(polygon) => setInteractiveMask(polygon)}
                    onQuadConfirmed={(corners) => setInteractiveQuad(corners)}
                    onCancel={() => {
                      setInteractiveMask(null);
                      setInteractiveQuad(null);
                    }}
                  />
                )}

                {currentScene?.aiStatus === 'completed' && (
                  <div className="absolute top-4 left-4 z-10 bg-fuchsia-600/90 border border-fuchsia-400 text-white font-mono text-[9px] font-bold uppercase px-2.5 py-1 rounded-full flex items-center gap-1.5 shadow-md shadow-fuchsia-500/20 animate-pulse">
                    <Sparkles className="h-3 w-3" />
                    <span>AI Scene Enhanced</span>
                  </div>
                )}

                {/* Click helper when surfaces exist but none selected */}
                {surfacesForScene.length > 0 && !selectedSurfaceId && (
                  <div className="absolute inset-0 z-20 flex items-center justify-center pointer-events-none">
                    <div className="bg-slate-900/80 border border-blue-400/50 text-white rounded-xl px-5 py-3 text-center animate-pulse shadow-2xl">
                      <MapPin className="h-6 w-6 text-blue-400 mx-auto mb-1" />
                      <p className="text-xs font-bold">Click a highlighted region to inspect it</p>
                      <p className="text-[10px] text-slate-400 mt-0.5">{surfacesForScene.length} surface{surfacesForScene.length > 1 ? 's' : ''} detected</p>
                    </div>
                  </div>
                )}

                {/* SVG surface overlay */}
                <svg
                  className="pointer-events-none z-10"
                  style={{
                    position: 'absolute',
                    left: `${videoRect.x}px`,
                    top: `${videoRect.y}px`,
                    width: `${videoRect.w}px`,
                    height: `${videoRect.h}px`,
                  }}
                  viewBox={viewBoxValue}
                  id="player_overlay_svg"
                  preserveAspectRatio="xMidYMid meet"
                >
                  <defs>
                    <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                      <feGaussianBlur stdDeviation="3" result="blur" />
                      <feMerge>
                        <feMergeNode in="blur" />
                        <feMergeNode in="SourceGraphic" />
                      </feMerge>
                    </filter>
                    <pattern id="grid" width="20" height="20" patternUnits="userSpaceOnUse">
                      <path d="M 20 0 L 0 0 0 20" fill="none" stroke="rgba(255,255,255,0.15)" strokeWidth="0.5" />
                    </pattern>
                  </defs>
                  {visibleSurfaces.map(sf => {
                    const isSelected = selectedSurfaceId === sf.id;
                    const isExcluded = sf.status === "Excluded";
                    const isApproved = sf.status === "Approved";
                    const placedAsset = getPlacedAsset(sf.id);
                    const pointsString = sf.boundaryCoordinates.map(p => `${p.x},${p.y}`).join(" ");
                    const xs = sf.boundaryCoordinates.map(p => p.x);
                    const ys = sf.boundaryCoordinates.map(p => p.y);
                    const centerX = xs.reduce((a, b) => a + b, 0) / xs.length;
                    const centerY = ys.reduce((a, b) => a + b, 0) / ys.length;
                    const rotAngle = sf.orientationVector?.roll || 0;
                    const fillColor = isExcluded ? "#ef4444" : placedAsset ? "#10b981" : isApproved ? "#14b8a6" : "#3b82f6";
                    // Validate we have actual coordinates before rendering
                    if (!sf.boundaryCoordinates || sf.boundaryCoordinates.length < 3) return null;

                    // Moving tracking pin — the most recent tracked centroid at or before the
                    // current playback frame. Only present once a render has actually run for
                    // this surface (tracking happens during rendering, not detection).
                    let trackingPoint: { frame: number; x: number; y: number } | null = null;
                    for (const p of sf.trackingPoints) {
                      if (p.frame <= currentVideoFrame && (!trackingPoint || p.frame > trackingPoint.frame)) {
                        trackingPoint = p;
                      }
                    }

                    return (
                      <g key={sf.id} className="cursor-pointer pointer-events-auto" onClick={() => seekToSurface(sf.id)} id={`svg_surface_${sf.id}`}>
                        {/* Outer glow ring for visibility */}
                        <polygon
                          points={pointsString}
                          fill="none"
                          stroke={fillColor}
                          strokeWidth={isSelected ? 8 : 5}
                          strokeOpacity={0.25}
                          className="transition-all duration-200"
                        />
                        {/* Main polygon */}
                        <polygon
                          points={pointsString}
                          fill={fillColor}
                          fillOpacity={isSelected ? 0.55 : 0.4}
                          stroke={fillColor}
                          strokeWidth={isSelected ? 3.5 : 2.5}
                          strokeDasharray="none"
                          className={`transition-all duration-200 ${!isSelected && !isExcluded ? 'animate-pulse' : ''}`}
                          filter={isSelected ? 'url(#glow)' : undefined}
                        />
                        {/* Placed asset overlay — actual image or colored fallback */}
                        {placedAsset && (
                          <g style={{ mixBlendMode: selectedBlendMode, opacity: ambientIntensity }}>
                            {placedAsset.storageKey && placedAsset.storageKey.startsWith('/api/assets/file/') ? (
                              <image href={placedAsset.storageKey}
                                x={Math.min(...xs)} y={Math.min(...ys)}
                                width={Math.max(...xs) - Math.min(...xs)}
                                height={Math.max(...ys) - Math.min(...ys)}
                                preserveAspectRatio="xMidYMid slice"
                                transform={`rotate(${rotAngle}, ${centerX}, ${centerY})`}
                              />
                            ) : (
                              <>
                                <polygon points={pointsString} fill={
                                  placedAsset.brandCategory.includes('Beverage') ? '#1e3a8a' :
                                  placedAsset.brandCategory.includes('Automotive') || placedAsset.brandCategory.includes('Motoring') ? '#1e293b' :
                                  placedAsset.brandCategory.includes('Telecom') || placedAsset.brandCategory.includes('Mobile') ? '#701a75' :
                                  placedAsset.brandCategory.includes('Apparel') ? '#b45309' :
                                  placedAsset.brandCategory.includes('Electronics') || placedAsset.brandCategory.includes('Technology') ? '#065f46' : '#475569'
                                } fillOpacity={0.85} />
                                <text x={centerX} y={centerY + 4} fill="#ffffff" fontSize="13" fontWeight="bold" fontFamily="sans-serif" letterSpacing="1.5" textAnchor="middle" transform={`rotate(${rotAngle}, ${centerX}, ${centerY})`}>
                                  {placedAsset.name.toUpperCase()}
                                </text>
                              </>
                            )}
                            <polygon points={pointsString} fill="url(#grid)" fillOpacity={0.15} />
                            <circle cx={centerX + 30} cy={centerY - 20} r="10" fill="#10b981" stroke="white" strokeWidth="2" />
                            <text x={centerX + 30} y={centerY - 16} fill="white" fontSize="12" fontWeight="bold" textAnchor="middle">✓</text>
                          </g>
                        )}
                        {/* Preview asset overlay */}
                        {activePreviewAsset && isSelected && !placedAsset && (
                          <g style={{ mixBlendMode: selectedBlendMode, opacity: ambientIntensity }}>
                            <polygon points={pointsString} fill={
                              activePreviewAsset.brandCategory.includes('Beverage') ? '#1e3a8a' :
                              activePreviewAsset.brandCategory.includes('Automotive') || activePreviewAsset.brandCategory.includes('Motoring') ? '#1e293b' :
                              activePreviewAsset.brandCategory.includes('Telecom') || activePreviewAsset.brandCategory.includes('Mobile') ? '#701a75' :
                              activePreviewAsset.brandCategory.includes('Apparel') ? '#b45309' :
                              activePreviewAsset.brandCategory.includes('Electronics') || activePreviewAsset.brandCategory.includes('Technology') ? '#065f46' : '#475569'
                            } fillOpacity={0.8} />
                            <text x={centerX} y={centerY + 4} fill="#ffffff" fontSize="12" fontWeight="bold" fontFamily="sans-serif" letterSpacing="1.5" textAnchor="middle" transform={`rotate(${rotAngle}, ${centerX}, ${centerY})`}>
                              {activePreviewAsset.name.toUpperCase()}
                            </text>
                            <polygon points={pointsString} fill="url(#grid)" fillOpacity={0.2} />
                          </g>
                        )}
                        <text x={sf.boundaryCoordinates[0].x} y={sf.boundaryCoordinates[0].y - 8} fill="white" fontSize="10" fontWeight="bold" className="font-mono">
                          {sf.surfaceType.slice(0, 20)} ({Math.round(sf.confidenceScore * 100)}%)
                        </text>
                        {/* Moving tracking pin — follows the surface frame-by-frame during playback */}
                        {trackingPoint && (
                          <circle
                            cx={trackingPoint.x}
                            cy={trackingPoint.y}
                            r="7"
                            fill="#ef4444"
                            stroke="white"
                            strokeWidth="2"
                            className="pointer-events-none"
                          />
                        )}
                      </g>
                    );
                  })}
                </svg>

              </div>

              {/* Scene info bar — kept out of the video's own box so it never overlaps the native <video controls> bar */}
              <div className="mt-2 bg-slate-900/90 border border-slate-700 rounded-lg px-4 py-2 flex items-center justify-between text-[11px] font-mono text-slate-400">
                <div className="flex items-center gap-2">
                  <Eye className="h-3 w-3 text-blue-400" />
                  <span>
                    Scene #{currentScene?.sceneIndex ?? '—'}
                    {currentScene && ` · ${currentScene.durationSeconds.toFixed(1)}s`}
                    {' · '}{surfacesForScene.length} surface{surfacesForScene.length !== 1 ? 's' : ''} · {placedSurfaceCount} placed
                  </span>
                </div>
                <div><span>{activeVideo?.resolution || '—'} · {activeVideo?.frameRate || '—'} FPS</span></div>
              </div>

              {/* ── Surface Detection Summary — quick glance at all detected surfaces ── */}
              {surfacesForScene.length > 0 && (
                <div className="mt-4 bg-white border border-slate-200/90 rounded-xl p-4">
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="text-xs font-bold text-slate-500 uppercase tracking-wider font-display">
                      🎯 Detected Surfaces ({surfacesForScene.length})
                    </h4>
                    <div className="flex items-center gap-2">
                    {onDeleteAllSurfaces && (
                      <button
                        type="button"
                        onClick={() => setDeleteAllSurfacesConfirmOpen(true)}
                        className="px-2 py-1.5 rounded-lg text-[10px] font-mono font-bold text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors border border-transparent hover:border-red-200"
                        title="Delete all surfaces in this scene"
                      >
                        Delete All Surfaces
                      </button>
                    )}
                    {onDetectSurfacesForScene && activeVideo && selectedSceneId && (() => {
                      const currentScene = scenesForVideo.find(s => s.id === selectedSceneId);
                      const isDetecting = currentScene?.surfaceStatus === 'Detecting';
                      const approvedCount = surfacesForScene.filter(sf => sf.status === 'Approved').length;

                      const handleRedetectClick = () => {
                        if (approvedCount > 0) {
                          setRedetectConfirmOpen(true);
                        } else {
                          runAction(() => onDetectSurfacesForScene(selectedSceneId, selectedVideo));
                        }
                      };

                      return (
                        <button
                          onClick={handleRedetectClick}
                          className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-white font-semibold text-[10px] rounded-lg transition-all cursor-pointer shadow-sm ${
                            isDetecting ? 'bg-amber-500 hover:bg-amber-400' : 'bg-blue-600 hover:bg-blue-500'
                          }`}
                          title={isDetecting ? 'Detection in progress...' : 'Re-run surface detection to find new or changed surfaces'}
                        >
                          {isDetecting ? (
                            <><Loader2 className="h-3 w-3 animate-spin" /> Detecting...</>
                          ) : (
                            <><RefreshCw className="h-3 w-3" /> Re-run Detection</>
                          )}
                        </button>
                      );
                    })()}
                    </div>
                  </div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
                    {surfacesForScene.map(sf => {
                      const isSelected = selectedSurfaceId === sf.id;
                      const statusColor =
                        sf.status === 'Approved' ? 'border-emerald-300 bg-emerald-50' :
                        sf.status === 'Excluded' ? 'border-red-300 bg-red-50' :
                        'border-blue-300 bg-blue-50';
                      return (
                        <div
                          key={sf.id}
                          role="button"
                          tabIndex={0}
                          onClick={() => seekToSurface(sf.id)}
                          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') seekToSurface(sf.id); }}
                          className={`relative text-left p-3 rounded-lg border cursor-pointer transition-all ${statusColor} ${isSelected ? 'ring-2 ring-blue-500 shadow-md' : 'hover:shadow-sm'}`}
                        >
                          {onDeleteSurface && (
                            <button
                              type="button"
                              onClick={(e) => { e.stopPropagation(); setDeleteSurfaceConfirmId(sf.id); }}
                              className="absolute top-1 right-1 p-1 rounded bg-white/70 text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors z-10"
                              title="Delete this surface"
                            >
                              <Trash2 className="h-3 w-3" />
                            </button>
                          )}
                          <div className="flex items-start gap-2.5">
                            {/* Surface thumbnail */}
                            {sf.placementImageUrl ? (
                              <img
                                src={sf.placementImageUrl}
                                alt={sf.surfaceType}
                                className="h-12 w-16 rounded-md object-cover border border-slate-200 shrink-0 bg-slate-100"
                                loading="lazy"
                                onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
                              />
                            ) : (
                              <div className="h-12 w-16 rounded-md bg-slate-100 border border-slate-200 shrink-0 flex items-center justify-center">
                                <MapPin className="h-4 w-4 text-slate-300" />
                              </div>
                            )}
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center justify-between mb-1">
                                <span className="text-xs font-bold text-slate-800 truncate" title={sf.surfaceType}>{sf.surfaceType}</span>
                                <span className={`text-[10px] font-mono font-bold px-1.5 py-0.5 rounded ${
                                  sf.confidenceScore > 0.7 ? 'bg-emerald-100 text-emerald-700' :
                                  sf.confidenceScore > 0.4 ? 'bg-amber-100 text-amber-700' :
                                  'bg-red-100 text-red-700'
                                }`}>
                                  {Math.round(sf.confidenceScore * 100)}%
                                </span>
                              </div>
                              <div className="flex items-center gap-2 text-[10px] text-slate-500 font-mono">
                                <span>Viability: {Math.round(sf.viabilityScore * 100)}%</span>
                                <span>·</span>
                                <span>{sf.estimatedDepth}m</span>
                              </div>
                              {sf.exclusionReason && (
                                <div className="mt-1 text-[10px] text-red-500 italic truncate" title={sf.exclusionReason}>{sf.exclusionReason}</div>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Blend controls */}
              <div className="mt-4 p-4 bg-slate-50 rounded-xl border border-slate-200/60 grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <div className="text-[10px] uppercase font-mono font-bold text-slate-500 mb-1.5 flex items-center gap-1.5">
                    <Layout className="h-3.5 w-3.5 text-blue-500" />
                    <span>Blend Mode</span>
                  </div>
                  <div className="flex gap-1.5">
                    {[{ mode: 'normal', label: 'Flat Matte' }, { mode: 'multiply', label: 'Multiply' }, { mode: 'overlay', label: 'Overlay' }].map(b => (
                      <button key={b.mode} onClick={() => setSelectedBlendMode(b.mode as any)}
                        className={`px-2.5 py-1.5 text-[10px] font-semibold rounded-lg border transition-all cursor-pointer ${
                          selectedBlendMode === b.mode ? 'bg-blue-600 border-blue-700 text-white shadow-xs' : 'bg-white border-slate-200 text-slate-600 hover:bg-slate-50'
                        }`}>{b.label}</button>
                    ))}
                  </div>
                </div>
                <div>
                  <div className="text-[10px] uppercase font-mono font-bold text-slate-500 mb-1.5 flex justify-between">
                    <span>Ambient Light: {Math.round(ambientIntensity * 100)}%</span>
                  </div>
                  <input type="range" min="0.2" max="1.0" step="0.05" value={ambientIntensity}
                    onChange={(e) => setAmbientIntensity(parseFloat(e.target.value))}
                    className="w-full h-1 bg-slate-200 rounded-lg appearance-none cursor-pointer accent-blue-600" />
                </div>
              </div>

              <div className="mt-4 text-xs text-slate-500 flex items-center gap-1.5 font-mono">
                <span className="h-2 w-2 rounded-full bg-blue-500 animate-pulse"></span>
                <span>Click colored polygons above to select surfaces. Place assets from the right panel.</span>
              </div>
            </div>

            {/* Placed surfaces summary strip */}
            {placedSurfaceCount > 0 && (
              <div className="bg-white border border-slate-200/95 rounded-2xl p-5 shadow-sm">
                <h3 className="text-sm font-bold text-slate-800 font-display mb-3 flex items-center gap-2">
                  <CheckCircle className="h-4 w-4 text-emerald-500" />
                  Placed Surfaces ({placedSurfaceCount})
                </h3>
                <div className="space-y-2">
                  {surfacesForScene.filter(sf => surfaceAssetPairs[sf.id]).map(sf => {
                    const asset = getPlacedAsset(sf.id);
                    if (!asset) return null;
                    const renderForSurface = renderList.find(r => r.surfaceId === sf.id);
                    const isRenderProcessing = renderForSurface && (renderForSurface.renderStatus === 'Queued' || renderForSurface.renderStatus === 'Processing');
                    const isRenderFinished = renderForSurface?.renderStatus === 'Finished';
                    const isRenderNeedsReview = renderForSurface?.renderStatus === 'NeedsReview';
                    const isRenderFailed = renderForSurface?.renderStatus === 'Failed';
                    return (
                      <div key={sf.id} className={`border rounded-lg px-3 py-2 ${
                        isRenderFinished ? 'bg-emerald-50/50 border-emerald-200/60' :
                        isRenderNeedsReview ? 'bg-amber-50/50 border-amber-200/60' :
                        isRenderFailed ? 'bg-red-50/50 border-red-200/60' :
                        isRenderProcessing ? 'bg-amber-50/50 border-amber-200/60' :
                        'bg-emerald-50/50 border-emerald-200/60'
                      }`}>
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-3">
                            <div className={`h-8 w-8 rounded-lg flex items-center justify-center overflow-hidden border ${
                              isRenderFinished ? 'bg-emerald-100 border-emerald-200' :
                              isRenderNeedsReview ? 'bg-amber-100 border-amber-200' :
                              isRenderFailed ? 'bg-red-100 border-red-200' :
                              isRenderProcessing ? 'bg-amber-100 border-amber-200' :
                              'bg-emerald-100 border-emerald-200'
                            }`}>
                              {asset.thumbnailUrl ? (
                                <img src={asset.thumbnailUrl} alt={asset.name} className="h-full w-full object-cover" />
                              ) : (
                                <Image className={`h-4 w-4 ${isRenderFailed ? 'text-red-600' : (isRenderProcessing || isRenderNeedsReview) ? 'text-amber-600' : 'text-emerald-600'}`} />
                              )}
                            </div>
                            <div>
                              <div className="text-xs font-bold text-slate-800">{sf.surfaceType} ← {asset.name}</div>
                              <div className="text-[10px] text-slate-400 font-mono">{asset.type} · {asset.brandCategory} · {Math.round(sf.confidenceScore * 100)}% conf.</div>
                            </div>
                          </div>
                          <div className="flex items-center gap-2">
                            {!renderForSurface && (
                              <>
                                <button onClick={() => onRemoveAsset(sf.id)} className="text-[10px] text-red-500 hover:text-red-700 font-medium cursor-pointer">Remove</button>
                                <button
                                  onClick={() => { setSubmitConfirming(sf.id); runAction(() => onSubmitPlacement(sf.id, asset.id, selectedCampaignId || '')); setTimeout(() => setSubmitConfirming(''), 2000); }}
                                  disabled={submitConfirming === sf.id}
                                  className="inline-flex items-center gap-1 px-2.5 py-1 bg-emerald-600 hover:bg-emerald-500 disabled:bg-emerald-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all"
                                >
                                  {submitConfirming === sf.id ? <><Loader2 className="h-3 w-3 animate-spin" />Submitting...</> : <><Cpu className="h-3 w-3" />Submit for Render</>}
                                </button>
                              </>
                            )}
                            {isRenderProcessing && (
                              <div className="flex items-center gap-2">
                                <span className="inline-flex items-center gap-1 px-2 py-1 bg-amber-100 text-amber-700 font-semibold text-[10px] rounded-lg border border-amber-200">
                                  <Loader2 className="h-3 w-3 animate-spin" />
                                  {renderForSurface.renderStatus} {renderForSurface.progress > 0 ? `${renderForSurface.progress}%` : ''}
                                </span>
                                <button onClick={() => onNavigateToRenders?.()} className="text-[10px] text-blue-500 hover:text-blue-700 font-medium cursor-pointer">View in Renders</button>
                              </div>
                            )}
                            {isRenderFinished && (
                              <div className="flex items-center gap-2">
                                <span className="inline-flex items-center gap-1 px-2 py-1 bg-emerald-100 text-emerald-700 font-semibold text-[10px] rounded-lg border border-emerald-200">
                                  <CheckCircle className="h-3 w-3" /> Finished
                                </span>
                                {renderForSurface.storageKey && (
                                  <a href={renderForSurface.storageKey} download className="inline-flex items-center gap-1 px-2 py-1 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all">
                                    <Download className="h-3 w-3" />
                                  </a>
                                )}
                              </div>
                            )}
                            {isRenderNeedsReview && (
                              <div className="flex items-center gap-2">
                                <span className="inline-flex items-center gap-1 px-2 py-1 bg-amber-100 text-amber-700 font-semibold text-[10px] rounded-lg border border-amber-200">
                                  <AlertTriangle className="h-3 w-3" /> Needs Review
                                </span>
                                {renderForSurface.storageKey && (
                                  <a href={renderForSurface.storageKey} download className="inline-flex items-center gap-1 px-2 py-1 bg-amber-600 hover:bg-amber-500 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all">
                                    <Download className="h-3 w-3" />
                                  </a>
                                )}
                              </div>
                            )}
                            {isRenderFailed && (
                              <div className="flex items-center gap-2">
                                <span className="inline-flex items-center gap-1 px-2 py-1 bg-red-100 text-red-700 font-semibold text-[10px] rounded-lg border border-red-200">
                                  <X className="h-3 w-3" /> Failed
                                </span>
                                {onRetryRender && (
                                  <button
                                    onClick={async () => {
                                      setRetryingId(renderForSurface!.id);
                                      try { await onRetryRender(renderForSurface!.id); }
                                      catch (err: any) { setActionError(err.message || 'Failed to retry render.'); }
                                      finally { setRetryingId(null); }
                                    }}
                                    disabled={retryingId === renderForSurface.id}
                                    className="inline-flex items-center gap-1 px-2 py-1 bg-red-600 hover:bg-red-500 disabled:bg-red-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all"
                                  >
                                    {retryingId === renderForSurface.id ? <><Loader2 className="h-3 w-3 animate-spin" />Retrying...</> : <><RefreshCw className="h-3 w-3" />Retry</>}
                                  </button>
                                )}
                              </div>
                            )}
                          </div>
                        </div>
                        {/* NeedsReview reasons (e.g. compositing failed on every shot) are shown to
                            everyone — that's exactly the kind of silent gap this is meant to close.
                            Failure reasons stay admin-only, matching the rest of the app. */}
                        {isRenderNeedsReview && renderForSurface.lastErrorMessage && (
                          <div className="mt-2 p-2 bg-amber-100 border border-amber-200 rounded-lg">
                            <div className="text-[9px] font-mono font-bold uppercase mb-0.5 text-amber-700">Why this needs review</div>
                            <div className="text-[10px] leading-relaxed text-amber-800">{renderForSurface.lastErrorMessage}</div>
                          </div>
                        )}
                        {isRenderFailed && renderForSurface.lastErrorMessage && userRole === 'Admin' && (
                          <div className="mt-2 p-2 bg-red-100 border border-red-200 rounded-lg">
                            <div className="text-[9px] font-mono font-bold uppercase mb-0.5 text-red-600">Failure Reason (admin)</div>
                            <div className="text-[10px] font-mono leading-relaxed break-all text-red-700">{renderForSurface.lastErrorMessage}</div>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* AI Placement Assistant — auto-places assets on surfaces, or generates a brand-new placement via AI video edit */}
            <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
              <div className="flex items-center justify-between gap-2.5 border-b border-slate-100 pb-4 mb-4">
                <div className="flex items-center gap-2.5">
                  <div className={`h-8 w-8 rounded-lg flex items-center justify-center ${assistantMode === 'match' ? 'bg-emerald-50 text-emerald-600' : 'bg-fuchsia-50 text-fuchsia-600'}`}>
                    <Sparkles className="h-4 w-4" />
                  </div>
                  <div>
                    <h3 className="text-sm font-bold text-slate-800 font-display flex items-center gap-1.5">
                      AI Placement Assistant
                    </h3>
                    <p className="text-[11px] text-slate-400">
                      {assistantMode === 'match'
                        ? <>Describe which assets to place on which surfaces. <strong>Never modifies the original scene.</strong></>
                        : <>Describe a brand-new placement — AI generates the video clip directly.</>}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-1 bg-slate-100 rounded-lg p-1 shrink-0">
                  <button
                    type="button"
                    onClick={() => setAssistantMode('match')}
                    className={`px-2.5 py-1 rounded-md text-[10px] font-semibold transition-all cursor-pointer whitespace-nowrap ${assistantMode === 'match' ? 'bg-white text-emerald-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'}`}
                  >
                    🔗 Match to Surface
                  </button>
                  <button
                    type="button"
                    onClick={() => setAssistantMode('generate')}
                    className={`px-2.5 py-1 rounded-md text-[10px] font-semibold transition-all cursor-pointer whitespace-nowrap ${assistantMode === 'generate' ? 'bg-white text-fuchsia-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'}`}
                  >
                    ✨ Generate New
                  </button>
                </div>
              </div>

              {assistantMode === 'generate' ? (
                <PromptGeneratePanel
                  currentScene={currentScene}
                  campaignAssets={campaignAssets}
                  contentId={selectedVideo || ''}
                  campaignId={selectedCampaignId}
                  activePromptRender={activePromptRender}
                  onSubmit={async (dto) => { await onSubmitPromptPlacement?.(dto); }}
                  onApprove={async (renderId) => { await onApprovePromptSplice?.(renderId); }}
                  onReject={async (renderId) => { await onRejectPromptPlacement?.(renderId); }}
                />
              ) : currentScene ? (
                <div className="space-y-4">
                  <div className="text-2xs font-mono bg-emerald-50/50 text-emerald-700 border border-emerald-100/50 p-2.5 rounded-lg">
                    <span>Scene #{currentScene.sceneIndex} · {surfacesForScene.length} surfaces · {campaignAssets.length} campaign assets available</span>
                  </div>
                  <div>
                    <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">
                      Placement Instructions
                    </label>
                    <textarea
                      value={aiPromptText}
                      onChange={(e) => setAiPromptText(e.target.value)}
                      placeholder={'Examples:\n"Place the Nike logo on the billboard"\n"Put Coke Zero on the wall banner and Adidas on the field board"\n"Place all beverage assets on available surfaces"'}
                      rows={3}
                      className="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 text-xs text-slate-800 focus:outline-none focus:border-emerald-500/50 resize-none font-sans"
                    />
                  </div>

                  {/* Show available assets for context */}
                  <div className="flex flex-wrap gap-1 text-[9px] font-mono text-slate-400">
                    <span className="font-bold text-slate-500">Assets:</span>
                    {campaignAssets.slice(0, 6).map(a => (
                      <span key={a.id} className="bg-slate-100 px-1.5 py-0.5 rounded">{a.name}</span>
                    ))}
                    {campaignAssets.length > 6 && <span className="text-slate-300">+{campaignAssets.length - 6} more</span>}
                  </div>
                  <div className="flex flex-wrap gap-1 text-[9px] font-mono text-slate-400">
                    <span className="font-bold text-slate-500">Surfaces:</span>
                    {surfacesForScene.slice(0, 6).map(sf => (
                      <span key={sf.id} className="bg-slate-100 px-1.5 py-0.5 rounded">{sf.surfaceType}</span>
                    ))}
                    {surfacesForScene.length > 6 && <span className="text-slate-300">+{surfacesForScene.length - 6} more</span>}
                  </div>

                  <button
                    type="button"
                    onClick={async () => {
                      if (!aiPromptText.trim()) return;
                      setAiPlacing(true);
                      try {
                        const { suggestPlacements } = await import('../apiClient');
                        const result = await suggestPlacements({
                          prompt: aiPromptText,
                          contentId: selectedVideo || '',
                          sceneId: currentScene?.id || '',
                          surfaces: surfacesForScene.map(sf => ({ id: sf.id, surfaceType: sf.surfaceType, confidenceScore: sf.confidenceScore })),
                          assets: campaignAssets.map(a => ({ id: a.id, name: a.name, brandCategory: a.brandCategory })),
                        });
                        let placed = 0;
                        for (const pair of result.placements) {
                          if (surfaceAssetPairs[pair.surfaceId]) continue;
                          if (Object.values(surfaceAssetPairs).includes(pair.assetId)) continue;
                          onPlaceAsset(pair.surfaceId, pair.assetId);
                          placed++;
                        }
                        setAiExplanation(result.explanation);
                        setAiPromptText('');
                      } catch (err: any) {
                        console.error('AI placement failed:', err);
                        setAiExplanation(err.message || 'Placement failed. Check Gemini API key.');
                      } finally {
                        setAiPlacing(false);
                      }
                    }}
                    disabled={!aiPromptText.trim() || campaignAssets.length === 0 || surfacesForScene.length === 0 || aiPlacing}
                    className="w-full inline-flex items-center justify-center gap-2 px-3.5 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:bg-slate-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm"
                  >
                    {aiPlacing ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Asking Gemini...</> : <><Wand2 className="h-3.5 w-3.5" />Auto-Place Assets with AI</>}
                  </button>

                  {aiExplanation && (
                    <div className="text-[10px] text-emerald-700 bg-emerald-50 p-2.5 rounded-lg border border-emerald-200 font-medium">
                      💡 {aiExplanation}
                    </div>
                  )}

                  {campaignAssets.length === 0 && (
                    <div className="text-[10px] text-amber-600 bg-amber-50 p-2 rounded-lg border border-amber-200">
                      No campaign assets available. Add assets in the Assets tab first.
                    </div>
                  )}
                </div>
              ) : (
                <div className="text-xs text-slate-400 italic text-center py-6">Select a scene above to use the AI Placement Assistant.</div>
              )}
            </div>
          </div>

          {/* ── RIGHT (1/3): Surface details + Asset Placement + Navigate ── */}
          <div className="col-span-1 space-y-6">

            {/* Surface Metadata & QA */}
            <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
              <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">Surface Details</h3>
              {currentSurface ? (
                <div className="space-y-4">
                  {/* Surface thumbnail preview */}
                  {currentSurface.placementImageUrl && (
                    <div className="bg-slate-900 rounded-lg overflow-hidden border border-slate-300">
                      <img
                        src={currentSurface.placementImageUrl}
                        alt={`${currentSurface.surfaceType} thumbnail`}
                        className="w-full h-32 object-cover"
                        onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
                      />
                    </div>
                  )}
                  <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80">
                    <div className="text-[10px] uppercase tracking-wider text-slate-400 font-mono font-bold">Surface Type</div>
                    <div className="text-sm font-bold text-slate-800 mt-0.5">{currentSurface.surfaceType}</div>
                  </div>
                  <div className="grid grid-cols-2 gap-2 text-xs font-mono">
                    <div className="bg-slate-50 p-2.5 rounded-lg border border-slate-200/80"><span className="text-slate-400">Confidence:</span><div className="text-slate-800 font-bold mt-0.5">{(currentSurface.confidenceScore * 100).toFixed(0)}%</div></div>
                    <div className="bg-slate-50 p-2.5 rounded-lg border border-slate-200/80"><span className="text-slate-400">Depth:</span><div className="text-slate-800 font-bold mt-0.5">{currentSurface.estimatedDepth}m</div></div>
                  </div>
                  <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80 font-mono text-[10px] text-slate-500">
                    <div className="border-b border-slate-200/50 pb-1 mb-1.5 font-bold uppercase tracking-wide text-slate-400">3D Orientation</div>
                    <div>Yaw: {currentSurface.orientationVector.yaw}°</div><div>Pitch: {currentSurface.orientationVector.pitch}°</div><div>Roll: {currentSurface.orientationVector.roll}°</div>
                  </div>
                  <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80">
                    <div className="flex justify-between items-center text-xs">
                      <span className="text-slate-500 font-medium">Status:</span>
                      <span className={`px-2 py-0.5 rounded text-[10px] font-bold uppercase ${
                        currentSurface.status === 'Approved' ? 'bg-emerald-50 text-emerald-700 border border-emerald-100' :
                        currentSurface.status === 'Excluded' ? 'bg-red-50 text-red-700 border border-red-100' :
                        'bg-blue-50 text-blue-700 border border-blue-100'
                      }`}>{currentSurface.status}</span>
                    </div>
                    {currentSurface.exclusionReason && <p className="text-2xs text-red-600 leading-normal mt-2 italic bg-red-50 p-2 rounded border border-red-100">Exclusion: {currentSurface.exclusionReason}</p>}
                  </div>
                  {currentSurface.status === "Excluded" && currentSurface.exclusionReason?.includes("MReq 4") ? (
                    <div className="p-3.5 bg-red-50 border border-red-200/60 rounded-xl text-red-700 text-xs"><Shield className="h-4 w-4 inline mr-1 text-red-600" /><strong>Security Blocklist:</strong> Face classification overrides cannot be bypassed.</div>
                  ) : (
                    <div className="space-y-3 pt-2">
                      <button onClick={() => runAction(() => handleSurfaceDecision("Approved"), 'Surface approved successfully.')} className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs rounded-lg cursor-pointer transition-all shadow-xs"><CheckCircle className="h-3.5 w-3.5" />Approve Surface</button>
                      <div className="pt-3 border-t border-slate-100">
                        <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Exclusion Reason</label>
                        <input type="text" value={rejectionReason} onChange={(e) => setRejectionReason(e.target.value)} placeholder="e.g., Low contrast lighting" className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800 focus:outline-none focus:border-red-500/50 mb-2" />
                        <button onClick={() => runAction(() => handleSurfaceDecision("Rejected"), 'Surface rejected successfully.')} disabled={!rejectionReason} className="w-full inline-flex items-center justify-center gap-2 px-3 py-1.5 bg-red-50 hover:bg-red-100 border border-red-200 text-red-600 font-semibold text-xs rounded-lg cursor-pointer transition-all disabled:opacity-40"><AlertTriangle className="h-3.5 w-3.5" />Exclude Surface</button>
                      </div>
                    </div>
                  )}
                </div>
              ) : (
                <div className="text-xs text-slate-400 italic bg-slate-50 p-4 rounded-xl border border-slate-200/50 text-center">
                  {surfacesForScene.length > 0 ? '👆 Click a highlighted region on the video player to select a surface.' : 'No surfaces to review. Run AI Scene Analysis first.'}
                </div>
              )}
            </div>

            {/* PHASE 2: Asset Placement Panel */}
            {currentSurface && currentSurface.status !== 'Excluded' && (
              <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
                <div className="flex items-center justify-between mb-4">
                  <h3 className="text-sm font-bold text-slate-800 font-display flex items-center gap-2"><Package className="h-4 w-4 text-blue-500" />Asset Placement</h3>
                  <button onClick={() => setShowingPlacementPanel(!showingPlacementPanel)} className="text-[10px] text-slate-400 hover:text-slate-600 cursor-pointer">{showingPlacementPanel ? 'Hide' : 'Show'}</button>
                </div>
                {showingPlacementPanel && (
                  <div className="space-y-4">
                    {(() => {
                      const placed = getPlacedAsset(currentSurface.id);
                      if (placed) return (
                        <div className="bg-emerald-50 border border-emerald-200 rounded-xl p-3">
                          <div className="flex items-center gap-2 mb-2"><CheckCircle className="h-4 w-4 text-emerald-600" /><span className="text-xs font-bold text-emerald-800">Asset Placed</span></div>
                          <div className="flex items-center gap-3">
                            <div className="h-10 w-10 rounded-lg bg-emerald-100 flex items-center justify-center"><Image className="h-5 w-5 text-emerald-600" /></div>
                            <div className="flex-1 min-w-0"><div className="text-xs font-bold text-slate-800">{placed.name}</div><div className="text-[10px] text-slate-500 font-mono">{placed.type} · {placed.brandCategory}</div></div>
                            <button onClick={() => onRemoveAsset(currentSurface.id)} className="text-[10px] text-red-500 hover:text-red-700 font-medium cursor-pointer flex items-center gap-1"><X className="h-3 w-3" />Remove</button>
                          </div>
                        </div>
                      );
                      return null;
                    })()}

                    {/* PHASE 3: AI Suggest button */}
                    {onAiSuggestAssets && !getPlacedAsset(currentSurface.id) && (
                      <button onClick={() => onAiSuggestAssets(currentSurface.id)} disabled={isSuggestingAssets[currentSurface.id]}
                        className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-fuchsia-50 hover:bg-fuchsia-100 border border-fuchsia-200 text-fuchsia-700 font-semibold text-xs rounded-lg cursor-pointer transition-all disabled:opacity-50">
                        {isSuggestingAssets[currentSurface.id] ? <><Loader2 className="h-3.5 w-3.5 animate-spin" />AI analyzing...</> : <><Sparkles className="h-3.5 w-3.5" />✨ Suggest Best Assets</>}
                      </button>
                    )}

                    {/* AI suggestions display */}
                    {aiSuggestions[currentSurface.id] && aiSuggestions[currentSurface.id].length > 0 && !getPlacedAsset(currentSurface.id) && (
                      <div className="bg-fuchsia-50/30 border border-fuchsia-100 rounded-xl p-3 space-y-2">
                        <div className="text-[10px] font-bold text-fuchsia-700 font-mono uppercase">AI Recommendations</div>
                        {aiSuggestions[currentSurface.id].map(sugg => {
                          const suggAsset = assetList.find(a => a.id === sugg.assetId);
                          if (!suggAsset) return null;
                          return (
                            <div key={sugg.assetId} className="flex items-center gap-2 bg-white rounded-lg p-2 border border-fuchsia-100">
                              <div className="flex-1 min-w-0"><div className="text-[10px] font-bold text-slate-800">{suggAsset.name}</div><div className="text-[9px] text-slate-400 truncate" title={sugg.reason}>{sugg.reason}</div></div>
                              <button onClick={() => onPlaceAsset(currentSurface.id, suggAsset.id)} className="text-[9px] bg-fuchsia-600 hover:bg-fuchsia-500 text-white font-bold px-2 py-1 rounded cursor-pointer shrink-0">Place</button>
                            </div>
                          );
                        })}
                      </div>
                    )}

                    {/* Manual asset selection */}
                    {!getPlacedAsset(currentSurface.id) && (
                      <>
                        <div className="text-[10px] font-mono font-bold text-slate-400 uppercase">Available Assets {selectedCampaignId && <span className="text-blue-500">(campaign-filtered)</span>}</div>
                        {campaignAssets.length === 0 ? (
                          <div className="text-xs text-amber-600 bg-amber-50 p-3 rounded-lg border border-amber-200">
                            {selectedCampaignId ? 'No assets assigned to this campaign. Go to Assets tab to add assets.' : 'No assets available. Create assets in the Assets tab first.'}
                          </div>
                        ) : (
                          <div className="space-y-1.5 max-h-48 overflow-y-auto pr-1">
                            {campaignAssets.map(asset => (
                              <button key={asset.id} onClick={() => onPlaceAsset(currentSurface.id, asset.id)}
                                className="w-full flex items-center gap-2.5 p-2 rounded-lg border border-slate-200 hover:border-blue-400 hover:bg-blue-50/30 transition-all cursor-pointer text-left">
                                <div className="h-8 w-8 rounded-lg bg-slate-100 flex items-center justify-center shrink-0 overflow-hidden border border-slate-200">
                                  {asset.thumbnailUrl ? (
                                    <img src={asset.thumbnailUrl} alt={asset.name} className="h-full w-full object-cover" />
                                  ) : (
                                    <Image className="h-4 w-4 text-slate-400" />
                                  )}
                                </div>
                                <div className="flex-1 min-w-0"><div className="text-xs font-bold text-slate-800 truncate" title={asset.name}>{asset.name}</div><div className="text-[10px] text-slate-400 font-mono">{asset.type} · {asset.brandCategory}</div></div>
                                <div className="text-[10px] text-slate-300 font-mono">{asset.dimensions}</div>
                              </button>
                            ))}
                          </div>
                        )}
                      </>
                    )}

                    {/* Submit for render — only after scene is approved */}
                    {getPlacedAsset(currentSurface.id) && currentScene && (() => {
                      const asset = getPlacedAsset(currentSurface.id)!;
                      const currentRender = renderList.find(r => r.surfaceId === currentSurface.id);
                      const isRenderProcessing = currentRender && (currentRender.renderStatus === 'Queued' || currentRender.renderStatus === 'Processing');
                      // NeedsReview is a completed, playable/downloadable output too (partial shot
                      // coverage or a drift-check below threshold) — not a failure.
                      const isRenderFinished = currentRender?.renderStatus === 'Finished' || currentRender?.renderStatus === 'NeedsReview';
                      const isRenderNeedsReview = currentRender?.renderStatus === 'NeedsReview';
                      const isRenderFailed = currentRender?.renderStatus === 'Failed';
                      const playableUrl = currentRender && (currentRender.sceneClipStorageKey || currentRender.storageKey);

                      if (!currentRender) {
                        return currentScene.qaStatus === 'Approved' ? (
                          <button
                            onClick={async () => { setSubmitConfirming(currentSurface.id); const ok = await runAction(() => onSubmitPlacement(currentSurface.id, asset.id, selectedCampaignId || '')); setTimeout(() => setSubmitConfirming(''), 2000); if (ok) onNavigateToRenders?.(); }}
                            disabled={submitConfirming === currentSurface.id}
                            className="w-full inline-flex items-center justify-center gap-2 px-3 py-2.5 bg-emerald-600 hover:bg-emerald-500 disabled:bg-emerald-300 text-white font-bold text-xs rounded-lg cursor-pointer transition-all shadow-sm"
                          >
                            {submitConfirming === currentSurface.id ? <><Loader2 className="h-3.5 w-3.5 animate-spin" />Submitting to Render Queue...</> : <><Cpu className="h-3.5 w-3.5" />Submit & View Renders</>}
                          </button>
                        ) : (
                          <div className="text-[10px] text-amber-600 bg-amber-50 p-2.5 rounded-lg border border-amber-200 text-center">
                            ⚠️ Approve this scene before rendering. Use "✓ Approve Scene" above the video player.
                          </div>
                        );
                      }

                      return (
                        <div className="space-y-2">
                          {/* Render status card */}
                          <div className={`p-3 rounded-lg border ${
                            isRenderNeedsReview ? 'bg-amber-50 border-amber-200' :
                            isRenderFinished ? 'bg-emerald-50 border-emerald-200' :
                            isRenderFailed ? 'bg-red-50 border-red-200' :
                            'bg-amber-50 border-amber-200'
                          }`}>
                            <div className="flex items-center justify-between mb-1">
                              <span className="text-xs font-bold text-slate-800">Render Status</span>
                              <span className={`text-[10px] font-mono font-bold px-2 py-0.5 rounded ${
                                isRenderNeedsReview ? 'bg-amber-100 text-amber-700' :
                                isRenderFinished ? 'bg-emerald-100 text-emerald-700' :
                                isRenderFailed ? 'bg-red-100 text-red-700' :
                                'bg-amber-100 text-amber-700'
                              }`}>
                                {isRenderProcessing && <Loader2 className="h-3 w-3 inline animate-spin mr-1" />}
                                {currentRender.renderStatus}
                              </span>
                            </div>
                            {isRenderProcessing && currentRender.progress > 0 && (
                              <div className="w-full bg-slate-200 rounded-full h-1.5 mt-1.5 overflow-hidden">
                                <div className="bg-amber-500 h-full rounded-full transition-all duration-500" style={{ width: `${currentRender.progress}%` }} />
                              </div>
                            )}

                            {/* Play the composited output inline */}
                            {isRenderFinished && playableUrl && (
                              <video
                                key={playableUrl}
                                src={playableUrl}
                                controls
                                className="w-full rounded-lg mt-2 bg-black"
                              />
                            )}

                            {/* Actions */}
                            <div className="flex items-center gap-2 mt-2 flex-wrap">
                              {isRenderFinished && currentRender.storageKey && (
                                <a href={currentRender.storageKey} download className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm">
                                  <Download className="h-3 w-3" /> Download
                                </a>
                              )}
                              {isRenderFinished && onSetRenderQueuedForFinal && (
                                <button
                                  onClick={async () => {
                                    setQueuingId(currentRender.id);
                                    try { await onSetRenderQueuedForFinal(currentRender.id, !currentRender.isQueuedForFinal); }
                                    catch (err: any) { setActionError(err.message || 'Failed to update queue status.'); }
                                    finally { setQueuingId(null); }
                                  }}
                                  disabled={queuingId === currentRender.id}
                                  className={`inline-flex items-center gap-1.5 px-3 py-1.5 font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm ${
                                    currentRender.isQueuedForFinal
                                      ? 'bg-blue-600 hover:bg-blue-500 text-white'
                                      : 'bg-white border border-blue-300 text-blue-600 hover:bg-blue-50'
                                  } disabled:opacity-50`}
                                  title="Use this render for this scene in the final combined video"
                                >
                                  {queuingId === currentRender.id ? <Loader2 className="h-3 w-3 animate-spin" /> : <CheckCircle className="h-3 w-3" />}
                                  {currentRender.isQueuedForFinal ? 'Queued for Final Video' : 'Queue for Final Video'}
                                </button>
                              )}
                              {isRenderFailed && onRetryRender && (
                                <button
                                  onClick={async () => {
                                    setRetryingId(currentRender.id);
                                    try { await onRetryRender(currentRender.id); }
                                    catch (err: any) { setActionError(err.message || 'Failed to retry render.'); }
                                    finally { setRetryingId(null); }
                                  }}
                                  disabled={retryingId === currentRender.id}
                                  className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-red-600 hover:bg-red-500 disabled:bg-red-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm"
                                >
                                  {retryingId === currentRender.id ? <><Loader2 className="h-3 w-3 animate-spin" />Retrying...</> : <><RefreshCw className="h-3 w-3" />Retry Render</>}
                                </button>
                              )}
                              {onDeleteRender && (
                                <button
                                  onClick={() => setDeleteRenderConfirmId(currentRender.id)}
                                  className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-white border border-red-200 hover:bg-red-50 text-red-600 font-semibold text-[10px] rounded-lg cursor-pointer transition-all"
                                >
                                  <Trash2 className="h-3 w-3" /> Delete
                                </button>
                              )}
                              <button onClick={() => onNavigateToRenders?.()} className="text-[10px] text-blue-500 hover:text-blue-700 font-medium cursor-pointer ml-auto">
                                View all renders →
                              </button>
                            </div>
                          </div>
                          {/* NeedsReview is a completed status, not a failure — but it can mean the asset
                              never actually got placed (e.g. every shot's compositing call failed and
                              fell back to original footage). Surface that plainly to every user, not just
                              admins, since a silent "Finished"-looking status is exactly the confusing gap
                              this is meant to close. */}
                          {isRenderNeedsReview && currentRender.lastErrorMessage && (
                            <div className="p-2.5 bg-amber-100 border border-amber-200 rounded-lg">
                              <div className="text-[9px] font-mono font-bold text-amber-700 uppercase mb-0.5">Why this needs review</div>
                              <div className="text-[10px] text-amber-800 leading-relaxed">{currentRender.lastErrorMessage}</div>
                            </div>
                          )}
                          {/* Admin-only failure reason */}
                          {isRenderFailed && currentRender.lastErrorMessage && userRole === 'Admin' && (
                            <div className="p-2.5 bg-red-100 border border-red-200 rounded-lg">
                              <div className="text-[9px] font-mono font-bold text-red-600 uppercase mb-0.5">Failure Reason (admin)</div>
                              <div className="text-[10px] text-red-700 font-mono leading-relaxed break-all">{currentRender.lastErrorMessage}</div>
                            </div>
                          )}
                        </div>
                      );
                    })()}
                  </div>
                )}
              </div>
            )}

            {/* PHASE 4: View renders (only after submitting at least one) */}
            {placedSurfaceCount > 0 && onNavigateToRenders && (
              <button onClick={onNavigateToRenders} className="w-full inline-flex items-center justify-center gap-2 px-4 py-3 bg-slate-600 hover:bg-slate-500 text-white font-bold text-sm rounded-xl cursor-pointer transition-all shadow-sm">
                View Renders <ArrowRight className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>

        {/* ── Re-run Detection Confirmation Modal ── */}
        {redetectConfirmOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-md w-full mx-4 p-6"
            >
              <div className="flex items-start gap-3 mb-4">
                <div className="h-10 w-10 rounded-xl bg-red-100 flex items-center justify-center shrink-0">
                  <AlertTriangle className="h-5 w-5 text-red-600" />
                </div>
                <div className="flex-1">
                  <h3 className="text-sm font-bold text-slate-800 font-display">Delete All Surfaces?</h3>
                  <p className="text-xs text-slate-500 mt-1.5 leading-relaxed">
                    This will delete <strong>ALL</strong> existing surfaces for this scene, including{' '}
                    <strong className="text-red-600">{surfacesForScene.filter(sf => sf.status === 'Approved').length} approved placement(s)</strong>.
                    Ad slots and approval records will be permanently lost. This cannot be undone.
                  </p>
                  <p className="text-xs text-slate-400 mt-2">
                    New surfaces will be detected from scratch after deletion.
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2 justify-end">
                <button
                  onClick={() => setRedetectConfirmOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-lg cursor-pointer transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={() => {
                    setRedetectConfirmOpen(false);
                    if (onDetectSurfacesForScene && activeVideo && selectedSceneId) {
                      runAction(() => onDetectSurfacesForScene(selectedSceneId, selectedVideo));
                    }
                  }}
                  className="px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-500 rounded-lg cursor-pointer transition-colors shadow-sm"
                >
                  Delete &amp; Re-detect
                </button>
              </div>
            </motion.div>
          </div>
        )}

        {/* ── Delete Surface Confirmation Modal ── */}
        {deleteSurfaceConfirmId && (() => {
          const targetSurface = surfacesForScene.find(sf => sf.id === deleteSurfaceConfirmId);
          return (
            <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
              <motion.div
                initial={{ opacity: 0, scale: 0.95 }}
                animate={{ opacity: 1, scale: 1 }}
                className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-md w-full mx-4 p-6"
              >
                <div className="flex items-start gap-3 mb-4">
                  <div className="h-10 w-10 rounded-xl bg-red-100 flex items-center justify-center shrink-0">
                    <Trash2 className="h-5 w-5 text-red-600" />
                  </div>
                  <div className="flex-1">
                    <h3 className="text-sm font-bold text-slate-800 font-display">Delete Surface "{targetSurface?.surfaceType}"?</h3>
                    <p className="text-xs text-slate-500 mt-1.5 leading-relaxed">
                      This permanently deletes this surface and all its ad slots and approvals. This cannot be undone.
                    </p>
                    {targetSurface?.status === 'Approved' && (
                      <p className="text-xs text-red-600 mt-2 font-semibold">
                        This surface is approved — reject or exclude it first.
                      </p>
                    )}
                  </div>
                </div>
                <div className="flex items-center gap-2 justify-end">
                  <button
                    onClick={() => setDeleteSurfaceConfirmId(null)}
                    className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-lg cursor-pointer transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    disabled={deletingSurfaceId === deleteSurfaceConfirmId}
                    onClick={async () => {
                      if (!onDeleteSurface) return;
                      setDeletingSurfaceId(deleteSurfaceConfirmId);
                      try {
                        await onDeleteSurface(deleteSurfaceConfirmId);
                        setDeleteSurfaceConfirmId(null);
                      } catch (err: any) {
                        setActionError(err.message || 'Failed to delete surface.');
                      } finally {
                        setDeletingSurfaceId(null);
                      }
                    }}
                    className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-500 disabled:bg-red-300 rounded-lg cursor-pointer transition-colors shadow-sm"
                  >
                    {deletingSurfaceId === deleteSurfaceConfirmId ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
                    Delete Surface
                  </button>
                </div>
              </motion.div>
            </div>
          );
        })()}

        {/* ── Delete Render Confirmation Modal ── */}
        {deleteRenderConfirmId && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-md w-full mx-4 p-6"
            >
              <div className="flex items-start gap-3 mb-4">
                <div className="h-10 w-10 rounded-xl bg-red-100 flex items-center justify-center shrink-0">
                  <Trash2 className="h-5 w-5 text-red-600" />
                </div>
                <div className="flex-1">
                  <h3 className="text-sm font-bold text-slate-800 font-display">Delete Render #{deleteRenderConfirmId.slice(0, 8)}?</h3>
                  <p className="text-xs text-slate-500 mt-1.5 leading-relaxed">
                    This permanently deletes this render and its output file. This cannot be undone.
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2 justify-end">
                <button
                  onClick={() => setDeleteRenderConfirmId(null)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-lg cursor-pointer transition-colors"
                >
                  Cancel
                </button>
                <button
                  disabled={deletingRenderId === deleteRenderConfirmId}
                  onClick={async () => {
                    if (!onDeleteRender) return;
                    setDeletingRenderId(deleteRenderConfirmId);
                    try {
                      await onDeleteRender(deleteRenderConfirmId);
                      setDeleteRenderConfirmId(null);
                    } catch (err: any) {
                      setActionError(err.message || 'Failed to delete render.');
                    } finally {
                      setDeletingRenderId(null);
                    }
                  }}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-500 disabled:bg-red-300 rounded-lg cursor-pointer transition-colors shadow-sm"
                >
                  {deletingRenderId === deleteRenderConfirmId ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
                  Delete Render
                </button>
              </div>
            </motion.div>
          </div>
        )}

        {/* ── Delete All Surfaces Confirmation Modal ── */}
        {deleteAllSurfacesConfirmOpen && (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              className="bg-white rounded-2xl shadow-2xl border border-slate-200 max-w-md w-full mx-4 p-6"
            >
              <div className="flex items-start gap-3 mb-4">
                <div className="h-10 w-10 rounded-xl bg-red-100 flex items-center justify-center shrink-0">
                  <Trash2 className="h-5 w-5 text-red-600" />
                </div>
                <div className="flex-1">
                  <h3 className="text-sm font-bold text-slate-800 font-display">Delete All {surfacesForScene.length} Surfaces?</h3>
                  <p className="text-xs text-slate-500 mt-1.5 leading-relaxed">
                    This permanently deletes <strong>every surface</strong> in this scene, along with all their ad slots
                    and approvals. This cannot be undone.
                  </p>
                  {surfacesForScene.some(sf => sf.status === 'Approved') && (
                    <p className="text-xs text-red-600 mt-2 font-semibold">
                      This scene has approved surfaces — reject or exclude them first.
                    </p>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-2 justify-end">
                <button
                  onClick={() => setDeleteAllSurfacesConfirmOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 bg-slate-100 hover:bg-slate-200 rounded-lg cursor-pointer transition-colors"
                >
                  Cancel
                </button>
                <button
                  disabled={deletingAllSurfaces}
                  onClick={async () => {
                    if (!onDeleteAllSurfaces || !selectedSceneId) return;
                    setDeletingAllSurfaces(true);
                    try {
                      await onDeleteAllSurfaces(selectedSceneId);
                      setDeleteAllSurfacesConfirmOpen(false);
                    } catch (err: any) {
                      setActionError(err.message || 'Failed to delete all surfaces.');
                    } finally {
                      setDeletingAllSurfaces(false);
                    }
                  }}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-500 disabled:bg-red-300 rounded-lg cursor-pointer transition-colors shadow-sm"
                >
                  {deletingAllSurfaces ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
                  Delete All Surfaces
                </button>
              </div>
            </motion.div>
          </div>
        )}

    </motion.div>
  );
};
