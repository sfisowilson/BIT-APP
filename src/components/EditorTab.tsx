import React from 'react';
import { motion } from 'motion/react';
import {
  Tv, Play, Shield, CheckCircle, AlertTriangle, Sparkles, Wand2,
  Loader2, Eye, Layout, Image, Package, ArrowRight, Search, Cpu,
  MapPin, ChevronRight, X, Upload
} from 'lucide-react';
import { ContentItem, SceneItem, SurfaceItem, CreativeAsset, CampaignItem, SurfaceAssetPair } from '../types';

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
  handleSurfaceDecision: (decision: "Approved" | "Rejected") => void;
  currentSurface: SurfaceItem | undefined;

  // Asset inventory
  assetList: CreativeAsset[];
  campaignList: CampaignItem[];

  // Phase 1: AI analysis trigger from placements screen
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string) => Promise<void>;
  aiAnalyzingVideoId?: string | null;

  // Phase 2: Asset placement on surfaces
  selectedCampaignId?: string;
  surfaceAssetPairs: Record<string, string>; // surfaceId -> assetId
  onPlaceAsset: (surfaceId: string, assetId: string) => void;
  onRemoveAsset: (surfaceId: string) => void;
  onSubmitPlacement: (surfaceId: string, assetId: string, campaignId: string) => void;

  // Phase 3: AI asset suggestion
  onAiSuggestAssets?: (surfaceId: string) => Promise<{ assetId: string; reason: string }[]>;
  isSuggestingAssets?: Record<string, boolean>;
  aiSuggestions?: Record<string, { assetId: string; reason: string }[]>;

  // Scene approval workflow
  handleSceneApprove?: (sceneId: string) => void;

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
}) => {
  const [previewMode, setPreviewMode] = React.useState(false);
  const [aiPromptText, setAiPromptText] = React.useState('');
  const [previewAssetId, setPreviewAssetId] = React.useState<string>('');
  const [selectedBlendMode, setSelectedBlendMode] = React.useState<'multiply' | 'overlay' | 'normal'>('multiply');
  const [ambientIntensity, setAmbientIntensity] = React.useState<number>(0.85);
  const [showingPlacementPanel, setShowingPlacementPanel] = React.useState<boolean>(true);
  const [submitConfirming, setSubmitConfirming] = React.useState<string>('');

  const currentScene = scenesForVideo.find(s => s.id === selectedSceneId);
  const activeVideo = contentList.find(v => v.id === selectedVideo);
  const isLocalVideo = activeVideo?.storageKey?.startsWith('/api/content/file/');
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
  const videoWidth = activeVideo?.resolution ? parseInt(activeVideo.resolution.split('x')[0]) || 1280 : 1280;
  const videoHeight = activeVideo?.resolution ? parseInt(activeVideo.resolution.split('x')[1]) || 720 : 720;
  const viewBoxValue = `0 0 ${videoWidth} ${videoHeight}`;

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
                This scene hasn't been analyzed for placement opportunities yet. Run the AI Scene Analysis to detect
                billboards, screens, walls, and other surfaces where brand assets can be placed.
              </p>
              <div className="flex items-center gap-3 mt-4">
                {handleAiSplitAnalyze && activeVideo && (
                  <button
                    onClick={() => handleAiSplitAnalyze(selectedVideo, activeVideo.title)}
                    disabled={aiAnalyzingVideoId === selectedVideo}
                    className="inline-flex items-center gap-2 px-4 py-2 bg-amber-600 hover:bg-amber-500 disabled:bg-amber-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm"
                  >
                    {aiAnalyzingVideoId === selectedVideo ? (
                      <>
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        Analyzing Video...
                      </>
                    ) : (
                      <>
                        <Sparkles className="h-3.5 w-3.5" />
                        Run AI Scene Analysis
                      </>
                    )}
                  </button>
                )}
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
      {hasCompletedVideos && (
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
                        className="px-2.5 py-1 text-[10px] font-bold rounded-lg border border-fuchsia-200 bg-fuchsia-50 hover:bg-fuchsia-100 text-fuchsia-700 disabled:opacity-50 cursor-pointer transition-colors"
                      >
                        {compositingPreview ? <><Loader2 className="h-3 w-3 inline animate-spin" /> Compositing...</> : '🎬 Composite Preview'}
                      </button>
                    )}
                    <button onClick={() => setPreviewMode(!previewMode)}
                      className="px-2.5 py-1 text-[10px] font-bold rounded-lg border bg-white hover:bg-slate-50 cursor-pointer transition-colors">
                      {previewMode ? 'Hide Overlay' : '👁 Show Overlay'}
                    </button>
                    {currentScene.qaStatus !== 'Approved' && handleSceneApprove && (
                      <button onClick={() => handleSceneApprove(currentScene.id)}
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
                <svg className="absolute inset-0 w-full h-full pointer-events-none z-10" viewBox={viewBoxValue} id="player_overlay_svg" preserveAspectRatio="none">
                  <defs>
                    <pattern id="grid" width="20" height="20" patternUnits="userSpaceOnUse">
                      <path d="M 20 0 L 0 0 0 20" fill="none" stroke="rgba(255,255,255,0.15)" strokeWidth="0.5" />
                    </pattern>
                  </defs>
                  {surfacesForScene.map(sf => {
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

                    return (
                      <g key={sf.id} className="cursor-pointer pointer-events-auto" onClick={() => setSelectedSurfaceId(sf.id)} id={`svg_surface_${sf.id}`}>
                        <polygon
                          points={pointsString}
                          fill={fillColor}
                          fillOpacity={isSelected ? 0.45 : 0.25}
                          stroke={fillColor}
                          strokeWidth={isSelected ? 3 : 2}
                          strokeDasharray={isSelected ? "none" : "6 3"}
                          className="transition-all duration-200"
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
                      </g>
                    );
                  })}
                </svg>

                <div className="absolute bottom-4 left-4 right-4 bg-slate-900/90 border border-slate-700 rounded-lg px-4 py-2 flex items-center justify-between text-[11px] font-mono text-slate-400 z-20 pointer-events-none">
                  <div className="flex items-center gap-2">
                    <Eye className="h-3 w-3 text-blue-400" />
                    <span>Scene #{currentScene?.sceneIndex || '—'} · {surfacesForScene.length} surface{surfacesForScene.length !== 1 ? 's' : ''} · {placedSurfaceCount} placed</span>
                  </div>
                  <div><span>{activeVideo?.resolution || '—'} · {activeVideo?.frameRate || '—'} FPS</span></div>
                </div>
              </div>

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
                    return (
                      <div key={sf.id} className="flex items-center justify-between bg-emerald-50/50 border border-emerald-200/60 rounded-lg px-3 py-2">
                        <div className="flex items-center gap-3">
                          <div className="h-8 w-8 rounded-lg bg-emerald-100 flex items-center justify-center overflow-hidden border border-emerald-200">
                            {asset.thumbnailUrl ? (
                              <img src={asset.thumbnailUrl} alt={asset.name} className="h-full w-full object-cover" />
                            ) : (
                              <Image className="h-4 w-4 text-emerald-600" />
                            )}
                          </div>
                          <div>
                            <div className="text-xs font-bold text-slate-800">{sf.surfaceType} ← {asset.name}</div>
                            <div className="text-[10px] text-slate-400 font-mono">{asset.type} · {asset.brandCategory} · {Math.round(sf.confidenceScore * 100)}% conf.</div>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          <button onClick={() => onRemoveAsset(sf.id)} className="text-[10px] text-red-500 hover:text-red-700 font-medium cursor-pointer">Remove</button>
                          <button
                            onClick={() => { setSubmitConfirming(sf.id); onSubmitPlacement(sf.id, asset.id, selectedCampaignId || ''); setTimeout(() => setSubmitConfirming(''), 2000); }}
                            disabled={submitConfirming === sf.id}
                            className="inline-flex items-center gap-1 px-2.5 py-1 bg-emerald-600 hover:bg-emerald-500 disabled:bg-emerald-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all"
                          >
                            {submitConfirming === sf.id ? <><Loader2 className="h-3 w-3 animate-spin" />Submitting...</> : <><Cpu className="h-3 w-3" />Submit for Render</>}
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* AI Placement Assistant — auto-places assets on surfaces from natural language */}
            <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
              <div className="flex items-center gap-2.5 border-b border-slate-100 pb-4 mb-4">
                <div className="h-8 w-8 rounded-lg bg-emerald-50 flex items-center justify-center text-emerald-600">
                  <Sparkles className="h-4 w-4" />
                </div>
                <div>
                  <h3 className="text-sm font-bold text-slate-800 font-display flex items-center gap-1.5">
                    AI Placement Assistant
                  </h3>
                  <p className="text-[11px] text-slate-400">
                    Describe which assets to place on which surfaces. <strong>Never modifies the original scene.</strong>
                  </p>
                </div>
              </div>

              {currentScene ? (
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
                    onClick={() => {
                      if (!aiPromptText.trim()) return;
                      const prompt = aiPromptText.toLowerCase();
                      let placed = 0;
                      for (const asset of campaignAssets) {
                        if (surfaceAssetPairs[Object.keys(surfaceAssetPairs).find(sid => surfaceAssetPairs[sid] === asset.id) || '']) continue;
                        for (const sf of surfacesForScene) {
                          if (surfaceAssetPairs[sf.id]) continue;
                          const assetNameLower = asset.name.toLowerCase();
                          const surfaceLower = sf.surfaceType.toLowerCase();
                          if (prompt.includes(assetNameLower) && prompt.includes(surfaceLower)) {
                            onPlaceAsset(sf.id, asset.id);
                            placed++;
                            break;
                          }
                        }
                      }
                      if (placed === 0) {
                        for (const sf of surfacesForScene) {
                          if (surfaceAssetPairs[sf.id]) continue;
                          for (const asset of campaignAssets) {
                            if (Object.values(surfaceAssetPairs).includes(asset.id)) continue;
                            const assetWords = asset.name.toLowerCase().split(/\s+/);
                            if (assetWords.some(w => prompt.includes(w))) {
                              onPlaceAsset(sf.id, asset.id);
                              placed++;
                              break;
                            }
                          }
                          if (placed >= surfacesForScene.filter(s => !surfaceAssetPairs[s.id]).length) break;
                        }
                      }
                      if (placed === 0) {
                        for (let i = 0; i < Math.min(campaignAssets.length, surfacesForScene.length); i++) {
                          const sf = surfacesForScene[i];
                          if (!surfaceAssetPairs[sf.id]) {
                            onPlaceAsset(sf.id, campaignAssets[i].id);
                            placed++;
                          }
                        }
                      }
                      setAiPromptText('');
                    }}
                    disabled={!aiPromptText.trim() || campaignAssets.length === 0 || surfacesForScene.length === 0}
                    className="w-full inline-flex items-center justify-center gap-2 px-3.5 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:bg-slate-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm"
                  >
                    <Wand2 className="h-3.5 w-3.5" />
                    Auto-Place Assets from Instructions
                  </button>

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
                      <button onClick={() => handleSurfaceDecision("Approved")} className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs rounded-lg cursor-pointer transition-all shadow-xs"><CheckCircle className="h-3.5 w-3.5" />Approve Surface</button>
                      <div className="pt-3 border-t border-slate-100">
                        <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Exclusion Reason</label>
                        <input type="text" value={rejectionReason} onChange={(e) => setRejectionReason(e.target.value)} placeholder="e.g., Low contrast lighting" className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800 focus:outline-none focus:border-red-500/50 mb-2" />
                        <button onClick={() => handleSurfaceDecision("Rejected")} disabled={!rejectionReason} className="w-full inline-flex items-center justify-center gap-2 px-3 py-1.5 bg-red-50 hover:bg-red-100 border border-red-200 text-red-600 font-semibold text-xs rounded-lg cursor-pointer transition-all disabled:opacity-40"><AlertTriangle className="h-3.5 w-3.5" />Exclude Surface</button>
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
                              <div className="flex-1 min-w-0"><div className="text-[10px] font-bold text-slate-800">{suggAsset.name}</div><div className="text-[9px] text-slate-400 truncate">{sugg.reason}</div></div>
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
                                <div className="flex-1 min-w-0"><div className="text-xs font-bold text-slate-800 truncate">{asset.name}</div><div className="text-[10px] text-slate-400 font-mono">{asset.type} · {asset.brandCategory}</div></div>
                                <div className="text-[10px] text-slate-300 font-mono">{asset.dimensions}</div>
                              </button>
                            ))}
                          </div>
                        )}
                      </>
                    )}

                    {/* Submit for render — only after scene is approved */}
                    {getPlacedAsset(currentSurface.id) && currentScene && (
                      currentScene.qaStatus === 'Approved' ? (
                        <button
                          onClick={() => { const asset = getPlacedAsset(currentSurface.id)!; setSubmitConfirming(currentSurface.id); onSubmitPlacement(currentSurface.id, asset.id, selectedCampaignId || ''); setTimeout(() => setSubmitConfirming(''), 2000); }}
                          disabled={submitConfirming === currentSurface.id}
                          className="w-full inline-flex items-center justify-center gap-2 px-3 py-2.5 bg-emerald-600 hover:bg-emerald-500 disabled:bg-emerald-300 text-white font-bold text-xs rounded-lg cursor-pointer transition-all shadow-sm"
                        >
                          {submitConfirming === currentSurface.id ? <><Loader2 className="h-3.5 w-3.5 animate-spin" />Submitting to Render Queue...</> : <><Cpu className="h-3.5 w-3.5" />Submit Placement for Rendering</>}
                        </button>
                      ) : (
                        <div className="text-[10px] text-amber-600 bg-amber-50 p-2.5 rounded-lg border border-amber-200 text-center">
                          ⚠️ Approve this scene before rendering. Use "✓ Approve Scene" above the video player.
                        </div>
                      )
                    )}
                  </div>
                )}
              </div>
            )}

            {/* PHASE 4: Continue to Renders */}
            {placedSurfaceCount > 0 && onNavigateToRenders && (
              <button onClick={onNavigateToRenders} className="w-full inline-flex items-center justify-center gap-2 px-4 py-3 bg-blue-600 hover:bg-blue-500 text-white font-bold text-sm rounded-xl cursor-pointer transition-all shadow-sm">
                Continue to Renders <ArrowRight className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>
      )}
    </motion.div>
  );
};
