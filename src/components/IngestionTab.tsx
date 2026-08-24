import React from 'react';
import { motion } from 'motion/react';
import { Video, Plus, Trash2, Sparkles, Loader2, Info, CheckCircle, Clock, Film, Play, Eye, Search, RefreshCw, AlertTriangle, RotateCcw } from 'lucide-react';
import { ContentItem, SceneItem, VideoProbeResult, SplitMode } from '../types';
import { usePaginatedData } from '../hooks/usePaginatedData';
import { getDetectionStatus, probeVideoFile } from '../apiClient';
import { Pagination } from './Pagination';

interface IngestionTabProps {
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  scenesForVideo: SceneItem[];
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  onNavigateToPlacements: () => void;
  newVideoTitle: string;
  setNewVideoTitle: (v: string) => void;
  newVideoRes: string;
  setNewVideoRes: (v: string) => void;
  newVideoFps: number | '';
  setNewVideoFps: (v: number | '') => void;
  newVideoDuration: string;
  setNewVideoDuration: (v: string) => void;
  newVideoChannel: string;
  setNewVideoChannel: (v: string) => void;
  newVideoFile: File | null;
  setNewVideoFile: (f: File | null) => void;
  handleIngestVideo: (e: React.FormEvent) => void;
  ingestError: string | null;
  ingesting: boolean;
  uploadProgress?: number; // 0-100 for upload progress bar
  chunkProgress?: string;  // e.g. "12/48 chunks" for chunked upload
  handleDeleteContent?: (id: string) => void;
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string, splitMode?: SplitMode, runSurfaceDetection?: boolean) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
  selectedCampaignId?: string | null;
  campaignList?: { id: string; name: string }[];
  /** Called after ingest/delete to refresh data externally if needed */
  onDataChanged?: () => void;
  // ── Pipeline re-run handlers ──
  onRetranscode?: (contentId: string) => Promise<void>;
  onRedetectScenes?: (contentId: string, videoTitle: string, splitMode?: SplitMode, runSurfaceDetection?: boolean) => Promise<void>;
  onResetPipeline?: (contentId: string) => Promise<void>;
  isPipelineActionPending?: string | null; // contentId of item being acted on
  probeKey: string | null;
  setProbeKey: (key: string | null) => void;
  /** Scene detection split strategy for the next Run/Re-detect click. "cut" is faster —
   *  it maps every camera cut 1:1 to a scene and skips SAM3 embedding/clustering entirely. */
  splitMode: SplitMode;
  setSplitMode: (mode: SplitMode) => void;
  /** Fuse two or more consecutive scenes into one — manual alternative to AI clustering,
   *  typically used after "Cut" split mode. Rejects non-consecutive selections server-side. */
  onMergeScenes?: (sceneIds: string[], contentId: string) => Promise<void>;
}

/** Pipeline stage display order with icons and labels */
const PIPELINE_STAGES = [
  { key: 'Staging',       label: 'Staging',        icon: Clock,      color: 'text-slate-400', bg: 'bg-slate-100' },
  { key: 'Transcoding',   label: 'Transcoding',    icon: Loader2,    color: 'text-brand-500',  bg: 'bg-brand-50' },
  { key: 'SceneDetecting',label: 'Scene Detection', icon: Sparkles,   color: 'text-fuchsia-500', bg: 'bg-fuchsia-50' },
  { key: 'Completed',     label: 'Ready for QA',   icon: CheckCircle, color: 'text-emerald-600', bg: 'bg-emerald-50' },
] as const;

function PipelineIndicator({ status }: { status: string }) {
  const currentIdx = PIPELINE_STAGES.findIndex(s => s.key === status);
  return (
    <div className="flex items-center gap-1">
      {PIPELINE_STAGES.map((stage, idx) => {
        const isComplete = idx < currentIdx;
        const isCurrent = idx === currentIdx;
        const isPending = idx > currentIdx;
        const Icon = stage.icon;
        return (
          <React.Fragment key={stage.key}>
            {idx > 0 && (
              <div className={`h-0.5 w-3 rounded-full ${isComplete || isCurrent ? 'bg-brand-400' : 'bg-slate-200'}`} />
            )}
            <div
              className={`flex items-center gap-0.5 px-1.5 py-0.5 rounded-full text-[8px] font-bold transition-all ${
                isComplete ? 'bg-emerald-50 text-emerald-600' :
                isCurrent ? `${stage.bg} ${stage.color} animate-pulse` :
                'bg-slate-100 text-slate-300'
              }`}
              title={`${stage.label}${isComplete ? ' ✓' : isCurrent ? ' (active)' : ''}`}
            >
              <Icon className={`h-2.5 w-2.5 ${isCurrent && stage.key === 'Transcoding' ? 'animate-spin' : ''}`} />
              {isCurrent && <span className="hidden sm:inline">{stage.label}</span>}
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
}

export const IngestionTab: React.FC<IngestionTabProps> = ({
  selectedVideo,
  setSelectedVideo,
  scenesForVideo,
  selectedSceneId,
  setSelectedSceneId,
  onNavigateToPlacements,
  newVideoTitle,
  setNewVideoTitle,
  newVideoRes,
  setNewVideoRes,
  newVideoFps,
  setNewVideoFps,
  newVideoDuration,
  setNewVideoDuration,
  newVideoChannel,
  setNewVideoChannel,
  newVideoFile,
  setNewVideoFile,
  handleIngestVideo,
  ingestError,
  ingesting,
  uploadProgress = 0,
  chunkProgress = '',
  handleDeleteContent,
  handleAiSplitAnalyze,
  aiAnalyzingVideoId,
  selectedCampaignId,
  campaignList,
  onDataChanged,
  onRetranscode,
  onRedetectScenes,
  onResetPipeline,
  isPipelineActionPending,
  probeKey,
  setProbeKey,
  splitMode,
  setSplitMode,
  onMergeScenes,
}) => {
  // ── Paginated content list ──
  const {
    data: contentData,
    loading: contentLoading,
    page: contentPage,
    totalPages: contentTotalPages,
    totalCount: contentTotalCount,
    hasPreviousPage: contentHasPrev,
    hasNextPage: contentHasNext,
    setPage: setContentPage,
    setFilters: setContentFilters,
    refresh: refreshContent,
  } = usePaginatedData<ContentItem>('/api/content', {
    campaignId: selectedCampaignId || undefined,
  }, { defaultPageSize: 12 });

  const [contentStatusFilter, setContentStatusFilter] = React.useState('');
  const [contentSearchFilter, setContentSearchFilter] = React.useState('');
  const [reDetectConfirmId, setReDetectConfirmId] = React.useState<string | null>(null);
  // Per-video toggle for whether scene detection also runs Gemini surface detection
  // (the slowest part of the pipeline) or just detects scene/shot cuts. Defaults to off.
  const [runSurfaceDetectionMap, setRunSurfaceDetectionMap] = React.useState<Record<string, boolean>>({});
  // ── Manual scene merge (Cut mode → fuse consecutive scenes into one) ──
  const [scenesToMerge, setScenesToMerge] = React.useState<Set<string>>(new Set());
  const [merging, setMerging] = React.useState(false);
  const [mergeError, setMergeError] = React.useState<string | null>(null);

  // Selection is per-video — clear it when the active video changes.
  React.useEffect(() => {
    setScenesToMerge(new Set());
    setMergeError(null);
  }, [selectedVideo]);

  const toggleSceneForMerge = (sceneId: string) => {
    setMergeError(null);
    setScenesToMerge(prev => {
      const next = new Set(prev);
      if (next.has(sceneId)) next.delete(sceneId);
      else next.add(sceneId);
      return next;
    });
  };

  const handleMergeSelected = async () => {
    if (!onMergeScenes || scenesToMerge.size < 2 || !selectedVideo) return;
    setMerging(true);
    setMergeError(null);
    try {
      await onMergeScenes(Array.from(scenesToMerge), selectedVideo);
      setScenesToMerge(new Set());
    } catch (err: any) {
      setMergeError(err.message || 'Failed to merge scenes.');
    } finally {
      setMerging(false);
    }
  };

  React.useEffect(() => {
    setContentFilters({
      campaignId: selectedCampaignId || undefined,
      ingestionStatus: contentStatusFilter || undefined,
      search: contentSearchFilter || undefined,
    });
  }, [selectedCampaignId, contentStatusFilter, contentSearchFilter]);

  // ── Poll individual detection progress for items in SceneDetecting ──
  const [detectionProgressMap, setDetectionProgressMap] = React.useState<Record<string, number>>({});
  const detectingIds = contentData
    .filter(c => c.ingestionStatus === 'SceneDetecting')
    .map(c => c.id);

  React.useEffect(() => {
    if (detectingIds.length === 0) {
      // Clear stale entries when nothing is detecting
      if (Object.keys(detectionProgressMap).length > 0) setDetectionProgressMap({});
      return;
    }

    const interval = setInterval(async () => {
      let needsRefresh = false;
      const updates: Record<string, number> = {};

      await Promise.all(detectingIds.map(async (id) => {
        try {
          const status = await getDetectionStatus(id);
          updates[id] = status.progress;
          if (status.completed || status.failed) needsRefresh = true;
        } catch { /* silent — will retry next interval */ }
      }));

      setDetectionProgressMap(prev => ({ ...prev, ...updates }));
      if (needsRefresh) refreshContent();
    }, 3000);

    return () => clearInterval(interval);
  }, [detectingIds.join(',')]); // eslint-disable-line react-hooks/exhaustive-deps

  const selectedCampaignName = campaignList?.find(c => c.id === selectedCampaignId)?.name;

  // ── Video probe: ffprobe metadata extraction via backend ──
  const [probeResult, setProbeResult] = React.useState<VideoProbeResult | null>(null);
  const [probeProgress, setProbeProgress] = React.useState(0);
  const [probeError, setProbeError] = React.useState<string | null>(null);
  const [probeRunning, setProbeRunning] = React.useState(false);
  const [probeApplied, setProbeApplied] = React.useState(false);

  // MReq 1: Extract metadata from uploaded video file via browser (quick first pass)
  const [metadataExtracted, setMetadataExtracted] = React.useState(false);
  const videoRef = React.useRef<HTMLVideoElement | null>(null);

  const extractVideoMetadata = (file: File) => {
    setMetadataExtracted(false);
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    videoRef.current = video;
    video.preload = 'metadata';
    video.onloadedmetadata = () => {
      const seconds = video.duration;
      if (seconds && isFinite(seconds)) {
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = Math.floor(seconds % 60);
        setNewVideoDuration(`${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`);
      }
      if (video.videoWidth && video.videoHeight) {
        const w = video.videoWidth;
        const h = video.videoHeight;
        const maxDim = Math.max(w, h);
        if (maxDim >= 3840) setNewVideoRes(`${w}x${h} (4K)`);
        else if (maxDim >= 1920) setNewVideoRes(`${w}x${h} (1080p)`);
        else if (maxDim >= 1280) setNewVideoRes(`${w}x${h} (720p)`);
        else setNewVideoRes(`${w}x${h}`);
      }
      setNewVideoFps(25);
      setMetadataExtracted(true);
      URL.revokeObjectURL(url);
    };
    video.onerror = () => {
      setMetadataExtracted(false);
      URL.revokeObjectURL(url);
    };
    video.src = url;
  };

  // Start ffprobe probe when file changes — ffprobe is the source of truth
  const startProbe = React.useCallback(async (file: File) => {
    setProbeResult(null);
    setProbeError(null);
    setProbeProgress(0);
    setProbeApplied(false);
    setProbeKey(null);
    setProbeRunning(true);

    try {
      const result = await probeVideoFile(file, (pct) => {
        setProbeProgress(pct);
      });
      setProbeResult(result);
      setProbeKey(result.probeKey);

      // ffprobe is the ground truth — overwrite browser defaults automatically
      setNewVideoDuration(result.duration);
      setNewVideoFps(result.fps);
      setNewVideoRes(result.resolution);
      setProbeApplied(true);
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        setProbeError(err.message || 'Failed to probe video.');
      }
    } finally {
      setProbeRunning(false);
    }
  }, [setProbeKey, setNewVideoDuration, setNewVideoFps, setNewVideoRes]);

  // Auto-extract metadata and start probe when file changes
  React.useEffect(() => {
    if (newVideoFile) {
      setNewVideoTitle(newVideoFile.name.replace(/\.[^.]+$/, ''));
      extractVideoMetadata(newVideoFile);
      startProbe(newVideoFile);
    } else {
      setMetadataExtracted(false);
      setProbeResult(null);
      setProbeKey(null);
      setProbeApplied(false);
    }
  }, [newVideoFile]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="ingestion_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-brand-50 border border-brand-100 rounded-2xl p-5 text-xs text-brand-800 flex items-start gap-3 shadow-xs">
        <Video className="h-5 w-5 text-brand-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-brand-900">
            {selectedCampaignId && selectedCampaignName
              ? `Content for: ${selectedCampaignName}`
              : 'Step 2: Video Ingestion Pipeline'}
          </h4>
          <p className="mt-1 text-brand-700 leading-normal">
            {selectedCampaignId
              ? <>Videos ingested for <strong>{selectedCampaignName}</strong>. New uploads will be automatically linked to this campaign.</>
              : <><strong>1. Register</strong> video metadata (title, duration, resolution, frame rate, source).{' '}
            <strong>2. System auto-transcodes</strong> to a normalised working format.{' '}
            <strong>3. Scene-cut detection</strong> splits footage into indexed segments.{' '}
            <strong>4. Ready for QA</strong> — scenes flow into the QA Workbench for surface approval.
            Watch the pipeline indicator {">"} track progress for each video.</>
            }
          </p>
        </div>
      </div>

      {/* Ingest Form */}
      <div className="col-span-1 space-y-8">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">Register Video Metadata</h2>
          <p className="text-xs text-slate-500 mb-6 font-sans">
            Attach a video file to auto-extract metadata. Fields are populated from the source file.
          </p>

          <form onSubmit={async (e) => { await handleIngestVideo(e); refreshContent(); }} className="space-y-4">
            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
                Video Title / Broadcast Name (auto from file)
              </label>
              <input 
                type="text" 
                value={newVideoTitle || 'Select a video file to auto-populate'}
                readOnly
                className="w-full border rounded-lg px-3 py-1.5 text-xs bg-slate-100 border-slate-200 text-slate-500 pointer-events-none select-none"
              />
            </div>

            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
                Target Native Resolution (auto-detected)
              </label>
              <input 
                type="text"
                value={newVideoRes || '—'}
                readOnly
                className="w-full border rounded-lg px-2 py-1.5 text-xs font-mono bg-slate-100 border-slate-200 text-slate-500 pointer-events-none select-none"
              />
            </div>

            <div className="grid grid-cols-3 gap-2 items-start">
              <div className="flex flex-col">
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono leading-tight min-h-[2rem]">
                  Duration HH:MM:SS (auto)
                </label>
                <input
                  type="text"
                  value={newVideoDuration || '00:00:00'}
                  readOnly
                  className="w-full border rounded-lg px-2 py-1.5 text-xs font-mono bg-slate-100 border-slate-200 text-slate-500 pointer-events-none select-none"
                />
              </div>
              <div className="flex flex-col">
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono leading-tight min-h-[2rem]">Native FPS</label>
                <input
                  type="number"
                  value={newVideoFps}
                  onChange={(e) => setNewVideoFps(e.target.value === '' ? '' : Number(e.target.value))}
                  min={1}
                  max={960}
                  step={1}
                  list="fps-presets"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs font-mono text-slate-800 focus:bg-white focus:outline-none focus:border-brand-500 transition-colors"
                  required
                />
                <datalist id="fps-presets">
                  <option value="8" />
                  <option value="12" />
                  <option value="15" />
                  <option value="24" />
                  <option value="25" />
                  <option value="30" />
                  <option value="48" />
                  <option value="50" />
                  <option value="60" />
                  <option value="120" />
                  <option value="144" />
                  <option value="240" />
                  <option value="480" />
                  <option value="576" />
                  <option value="960" />
                </datalist>
              </div>
              <div className="flex flex-col">
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono leading-tight min-h-[2rem]">
                  Source Channel
                </label>
                <input
                  type="text"
                  value={newVideoChannel}
                  onChange={(e) => setNewVideoChannel(e.target.value)}
                  placeholder="SuperSport Variety"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-brand-500 transition-colors"
                  required
                />
              </div>
            </div>

            <label className="border border-dashed border-slate-200 rounded-xl p-4 bg-slate-50/50 text-center cursor-pointer hover:border-brand-300 hover:bg-brand-50/30 transition-colors block">
              {newVideoFile ? (
                <>
                  <Video className="h-6 w-6 text-brand-500 mx-auto mb-2" />
                  <span className="text-2xs text-brand-600 block font-semibold">{newVideoFile.name}</span>
                  <span className="text-[10px] text-slate-400 block mt-1">{(newVideoFile.size / (1024 * 1024)).toFixed(1)} MB — click to change</span>
                </>
              ) : (
                <>
                  <Video className="h-6 w-6 text-slate-400 mx-auto mb-2" />
                  <span className="text-2xs text-slate-500 block font-semibold">Attach source file (MP4, MOV, MXF, AVI)</span>
                  <span className="text-[10px] text-slate-400 block mt-1">System transcodes to normalised format automatically.</span>
                </>
              )}
              <input type="file" accept="video/*,.mxf,.mov,.mp4,.avi,.mkv,.webm" className="hidden"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) setNewVideoFile(file);
                }} />
            </label>

            {ingestError && (
              <p className="text-2xs text-red-600 font-semibold font-mono bg-red-50 p-2.5 rounded-lg border border-red-100">{ingestError}</p>
            )}

            {/* Upload progress bar (visible during upload) */}
            {ingesting && uploadProgress > 0 && (
              <div className="space-y-1.5">
                <div className="flex justify-between text-[10px] text-slate-500 font-mono">
                  <span>Uploading{newVideoFile ? ` ${newVideoFile.name}` : ''}...</span>
                  <span className="font-bold">
                    {chunkProgress ? `${chunkProgress} · ` : ''}{uploadProgress}%
                  </span>
                </div>
                <div className="w-full bg-slate-200 rounded-full h-2 overflow-hidden">
                  <div
                    className="bg-brand-500 h-full rounded-full transition-all duration-300 ease-out"
                    style={{ width: `${uploadProgress}%` }}
                  />
                </div>
                {newVideoFile && !chunkProgress && (
                  <div className="text-[9px] text-slate-400 font-mono text-right">
                    {((newVideoFile.size * (uploadProgress / 100)) / (1024 * 1024)).toFixed(0)} MB of {(newVideoFile.size / (1024 * 1024)).toFixed(0)} MB
                  </div>
                )}
                {chunkProgress && (
                  <div className="text-[9px] text-slate-400 font-mono text-right">
                    Chunked upload · {(newVideoFile ? (newVideoFile.size / (1024 * 1024 * 1024)).toFixed(1) : '?')} GB total
                  </div>
                )}
              </div>
            )}

            <div className="flex items-center gap-1.5 text-[10px] text-slate-500 font-medium bg-slate-50 px-3 py-2 rounded-lg border border-slate-200">
              <Sparkles className="h-3 w-3" /> Metadata auto-extracted from video file. Select a file below to populate all fields.
            </div>

            {/* ── Probe progress indicator ── */}
            {probeRunning && (
              <div className="space-y-1.5 bg-brand-50 border border-brand-100 rounded-lg p-3">
                <div className="flex items-center gap-2 text-[10px] text-brand-700 font-mono">
                  <Loader2 className="h-3 w-3 animate-spin" />
                  <span>Analysing video with ffprobe...</span>
                  <span className="font-bold ml-auto">{probeProgress}%</span>
                </div>
                <div className="w-full bg-brand-200 rounded-full h-1.5 overflow-hidden">
                  <div
                    className="bg-brand-500 h-full rounded-full transition-all duration-300"
                    style={{ width: `${probeProgress}%` }}
                  />
                </div>
              </div>
            )}

            {/* ── Probe error ── */}
            {probeError && (
              <div className="text-[10px] text-amber-700 font-mono bg-amber-50 p-2.5 rounded-lg border border-amber-100 flex items-start gap-1.5">
                <AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" />
                <span>{probeError} — using browser-detected values. You can still upload, but verify the metadata.</span>
              </div>
            )}

            {/* ── ffprobe confirmed — values applied ── */}
            {probeApplied && probeResult && (
              <div className="flex items-center gap-2 text-[10px] text-emerald-700 font-medium bg-emerald-50 px-3 py-2 rounded-lg border border-emerald-100">
                <CheckCircle className="h-3.5 w-3.5 text-emerald-500" />
                <span>
                  Verified by ffprobe: {probeResult.fps}fps, {probeResult.resolution}, {probeResult.duration}
                  {probeResult.codec !== 'unknown' && ` (${probeResult.codec})`}
                </span>
              </div>
            )}

            <button 
              type="submit" 
              disabled={ingesting}
              className={`w-full inline-flex items-center justify-center gap-2 px-3 py-2 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer ${ingesting ? 'bg-brand-400 cursor-wait' : 'bg-brand-600 hover:bg-brand-500'}`}
            >
              {ingesting ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  {chunkProgress ? `Uploading ${chunkProgress}` : uploadProgress > 0 ? `Uploading ${uploadProgress}%...` : 'Uploading & Starting Pipeline...'}
                </>
              ) : (
                <>
                  <Plus className="h-3.5 w-3.5" />
                  Register &amp; Start Pipeline
                </>
              )}
            </button>
          </form>
        </div>

        {/* Pipeline legend */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-4 shadow-sm">
          <h4 className="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-2 font-mono">Pipeline Stages</h4>
          <div className="space-y-1.5">
            {PIPELINE_STAGES.map(s => {
              const Icon = s.icon;
              return (
                <div key={s.key} className="flex items-center gap-2 text-[10px] text-slate-600">
                  <Icon className={`h-3 w-3 ${s.color}`} />
                  <span className="font-mono font-bold">{s.key}</span>
                  <span className="text-slate-400">→ {s.label}</span>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Ingested Video Catalog List */}
      <div className="col-span-2 space-y-6">
        {/* ── Video Preview Player (shows when a video is selected) ── */}
        {(() => {
          const activeVideo = contentData.find(v => v.id === selectedVideo);
          if (!activeVideo) return null;
          const isLocalFile = activeVideo.storageKey?.startsWith('/api/content/file/');

          return (
            <div className="bg-slate-950 border border-slate-700 rounded-2xl overflow-hidden shadow-xl">
              <div className="relative aspect-video bg-black flex items-center justify-center">
                {isLocalFile ? (
                  /* Real video playback */
                  <video
                    src={activeVideo.storageKey}
                    controls
                    className="absolute inset-0 w-full h-full object-contain"
                    preload="metadata"
                  >
                    Your browser does not support video playback.
                  </video>
                ) : (
                  /* Fallback: simulated broadcast frame */
                  <div className="absolute inset-0 bg-gradient-to-br from-slate-800 via-slate-900 to-slate-950 flex items-center justify-center">
                    <div className="absolute inset-0 opacity-20">
                      <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_center,_var(--tw-gradient-stops))] from-emerald-900/40 via-transparent to-transparent"></div>
                      <div className="absolute bottom-0 left-0 right-0 h-1/3 bg-gradient-to-t from-emerald-900/30 to-transparent"></div>
                      <div className="absolute bottom-[15%] left-[5%] right-[5%] h-px bg-white/10"></div>
                      <div className="absolute top-1/2 left-0 right-0 h-px bg-white/10 -translate-y-1/2"></div>
                      <div className="absolute top-1/2 left-1/2 w-[15%] h-[20%] -translate-x-1/2 -translate-y-1/2 border border-white/10 rounded-full"></div>
                    </div>
                    <div className="relative z-10 text-center">
                      <div className="h-16 w-16 rounded-full bg-white/10 backdrop-blur flex items-center justify-center mx-auto mb-3 border border-white/10">
                        <Play className="h-7 w-7 text-white/80 ml-1" />
                      </div>
                      <p className="text-white/50 text-xs font-mono">No video file — upload an MP4/MOV to see playback</p>
                    </div>
                  </div>
                )}
              </div>

              {/* Scene timeline — moved out of the video container (was absolutely positioned
                  over it, hiding the native <video> controls) into normal flow below it. */}
              <div className="bg-slate-950/90 border-t border-slate-700/50 px-4 py-3">
                <div className="flex items-center gap-2 mb-1.5">
                  <Eye className="h-3 w-3 text-brand-400" />
                  <span className="text-[9px] font-mono text-slate-400 uppercase tracking-wider">
                    {activeVideo.title.length > 45 ? activeVideo.title.substring(0, 45) + '...' : activeVideo.title}
                  </span>
                  <span className="text-[9px] text-slate-500 ml-auto font-mono">{activeVideo.duration}</span>
                </div>
                {/* Timeline track */}
                <div className="relative h-5 bg-slate-800 rounded-md overflow-hidden border border-slate-700/50">
                  {/* Scene blocks */}
                  {scenesForVideo.length > 0 ? (
                    scenesForVideo.map((scene, idx) => {
                      const colors = ['bg-brand-500/60', 'bg-emerald-500/60', 'bg-fuchsia-500/60', 'bg-amber-500/60', 'bg-cyan-500/60'];
                      const totalFrames = scenesForVideo.reduce((max, s) => Math.max(max, s.endFrame), 1);
                      const leftPct = (scene.startFrame / totalFrames) * 100;
                      const widthPct = ((scene.endFrame - scene.startFrame) / totalFrames) * 100;
                      const isActive = scene.id === selectedSceneId;
                      return (
                        <div
                          key={scene.id}
                          onClick={(e) => { e.stopPropagation(); setSelectedSceneId(scene.id); }}
                          className={`absolute top-0 h-full ${colors[idx % colors.length]} border-r border-white/20 flex items-center px-1.5 cursor-pointer hover:brightness-125 transition-all ${isActive ? 'ring-2 ring-yellow-400 brightness-125 z-10' : ''}`}
                          style={{ left: `${leftPct}%`, width: `${Math.max(widthPct, 2)}%` }}
                          title={`Scene #${scene.sceneIndex}: ${scene.durationSeconds}s (frames ${scene.startFrame}–${scene.endFrame})`}
                        >
                          <span className="text-[7px] font-bold text-white drop-shadow truncate">
                            S{scene.sceneIndex}
                          </span>
                        </div>
                      );
                    })
                  ) : (
                    <div className="absolute inset-0 flex items-center justify-center">
                      <span className="text-[8px] text-slate-500 font-mono">No scenes detected — run Scene Detection</span>
                    </div>
                  )}
                </div>
                {/* Time markers */}
                <div className="flex justify-between mt-1 text-[8px] text-slate-500 font-mono">
                  <span>00:00</span>
                  <span>{activeVideo.duration}</span>
                </div>
              </div>
            </div>
          );
        })()}

        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 font-display">
              {selectedCampaignId ? `Campaign Videos` : 'Video Pipeline Catalog'}
            </h3>
            <span className="text-[10px] text-slate-400 font-mono">
              {contentData.length} of {contentTotalCount} video{contentTotalCount !== 1 ? 's' : ''}
            </span>
          </div>

          {/* Filter bar */}
          <div className="flex flex-wrap items-center gap-2 mb-4">
            <select
              value={contentStatusFilter}
              onChange={e => setContentStatusFilter(e.target.value)}
              className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-[10px] text-slate-700 focus:outline-none focus:border-brand-400"
            >
              <option value="">All Statuses</option>
              <option value="Staging">Staging</option>
              <option value="Transcoding">Transcoding</option>
              <option value="SceneDetecting">Scene Detecting</option>
              <option value="Completed">Completed</option>
              <option value="Failed">Failed</option>
            </select>
            <div className="relative flex-1 min-w-[150px]">
              <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3 w-3 text-slate-400" />
              <input
                type="text"
                value={contentSearchFilter}
                onChange={e => setContentSearchFilter(e.target.value)}
                placeholder="Search videos..."
                className="w-full bg-slate-50 border border-slate-200 rounded-lg pl-6 pr-2 py-1 text-[10px] text-slate-700 focus:outline-none focus:border-brand-400"
              />
            </div>
            <div
              className="inline-flex items-center rounded-lg border border-slate-200 bg-slate-50 p-0.5 text-[10px] font-mono font-bold"
              title={splitMode === 'cut'
                ? "Cut mode: 1 scene per camera cut. Fastest — skips SAM3 keyframe embedding/clustering entirely."
                : "Scene mode: SAM3-clustered scenes group related shots together (e.g. a dialogue's back-and-forth cuts into one scene)."}
            >
              <button
                type="button"
                onClick={() => setSplitMode('scene')}
                className={`px-2.5 py-1 rounded-md transition-colors cursor-pointer ${
                  splitMode === 'scene' ? 'bg-white text-brand-600 shadow-xs' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                Scene
              </button>
              <button
                type="button"
                onClick={() => setSplitMode('cut')}
                className={`px-2.5 py-1 rounded-md transition-colors cursor-pointer ${
                  splitMode === 'cut' ? 'bg-white text-brand-600 shadow-xs' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                Cut
              </button>
            </div>
          </div>

          <div className="space-y-4">
            {contentLoading && (
              <div className="text-center py-12 text-xs text-slate-400">Loading videos...</div>
            )}
            {!contentLoading && contentData.length === 0 && (
              <div className="text-center py-12 text-xs text-slate-400 bg-slate-50 rounded-xl border border-dashed border-slate-200">
                <Film className="h-8 w-8 mx-auto mb-2 text-slate-300" />
                {selectedCampaignId ? (
                  <>
                    <p>No videos linked to this campaign yet.</p>
                    <p className="text-[10px] mt-1">Use the form to upload a video — it will be automatically linked to {selectedCampaignName}.</p>
                  </>
                ) : (
                  <>
                    <p>No videos registered yet.</p>
                    <p className="text-[10px] mt-1">Use the form to register broadcast feed metadata and start the pipeline.</p>
                  </>
                )}
              </div>
            )}
            {!contentLoading && contentData.map(video => {
              const isSelected = selectedVideo === video.id;
              const isComplete = video.ingestionStatus === 'Completed';
              return (
                <div 
                  key={video.id} 
                  onClick={() => setSelectedVideo(video.id)}
                  className={`border rounded-xl p-4 transition-all cursor-pointer ${
                    isSelected 
                      ? 'bg-brand-50/40 border-brand-400 shadow-sm' 
                      : 'bg-slate-50/30 border-slate-200 hover:border-slate-300'
                  }`}
                  id={`video_card_${video.id}`}
                >
                  <div className="flex flex-col md:flex-row md:items-center justify-between gap-2">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-2xs font-mono font-bold text-brand-600">ID: {video.id}</span>
                        <PipelineIndicator status={video.ingestionStatus} />
                      </div>
                      <h4 className="text-sm font-bold text-slate-800 font-display mt-1.5">{video.title}</h4>
                      <p className="text-xs text-slate-400 mt-1 font-mono">Storage: {video.storageKey}</p>
                    </div>

                    <div className="flex items-center gap-4 shrink-0">
                      <div className="text-right text-xs text-slate-500 font-mono">
                        <div>{video.duration} · {video.resolution}</div>
                        <div>{video.frameRate} FPS · {video.sourceChannel}</div>
                      </div>
                      {handleDeleteContent && (
                        <button
                          type="button"
                          onClick={async (e) => {
                            e.stopPropagation();
                            handleDeleteContent(video.id);
                            refreshContent();
                          }}
                          className="p-1.5 rounded-lg text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors shrink-0"
                          title="Delete Video"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Mini scene strip — visual preview of scene cuts */}
                  <div className="mt-3 flex items-center gap-2">
                    <div className="flex-1 h-3 bg-slate-200 rounded-full overflow-hidden relative">
                      {(() => {
                        // Show scenes if this is the selected video, else show placeholder
                        const videoScenes = video.id === selectedVideo ? scenesForVideo : [];
                        if (videoScenes.length > 0) {
                          const totalFrames = videoScenes.reduce((max, s) => Math.max(max, s.endFrame), 1);
                          return videoScenes.map((scene, idx) => {
                            const colors = ['bg-brand-400', 'bg-emerald-400', 'bg-fuchsia-400', 'bg-amber-400'];
                            const leftPct = (scene.startFrame / totalFrames) * 100;
                            const widthPct = Math.max(((scene.endFrame - scene.startFrame) / totalFrames) * 100, 1.5);
                            return (
                              <div key={scene.id}
                                onClick={(e) => { e.stopPropagation(); setSelectedSceneId(scene.id); onNavigateToPlacements(); }}
                                className={`absolute top-0 h-full ${colors[idx % colors.length]} opacity-70 cursor-pointer hover:opacity-100 transition-opacity`}
                                style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                              />
                            );
                          });
                        }
                        return <div className="absolute inset-0 bg-slate-300 rounded-full" />;
                      })()}
                    </div>
                    <span className="text-[9px] text-slate-400 font-mono shrink-0">
                      {video.id === selectedVideo ? `${scenesForVideo.length} scenes` : `${video.duration}`}
                    </span>
                  </div>

                  {/* Detection progress bar (only while SceneDetecting) */}
                  {video.ingestionStatus === 'SceneDetecting' && (
                    <div className="mt-3">
                      <div className="flex items-center justify-between mb-1">
                        <span className="text-[9px] font-mono font-bold text-fuchsia-600 flex items-center gap-1">
                          <Sparkles className="h-3 w-3" />
                          Detecting scenes...
                        </span>
                        <span className="text-[9px] font-mono font-bold text-fuchsia-600">
                          {detectionProgressMap[video.id] ?? video.detectionProgress}%
                        </span>
                      </div>
                      <div className="w-full bg-slate-200 rounded-full h-2 overflow-hidden">
                        <div
                          className="bg-fuchsia-500 h-full rounded-full transition-all duration-500 ease-out"
                          style={{ width: `${detectionProgressMap[video.id] ?? video.detectionProgress}%` }}
                        />
                      </div>
                    </div>
                  )}

                  {/* Actions bar */}
                  <div className="mt-3 pt-3 border-t border-slate-200/50 flex flex-wrap items-center gap-2">
                    {((!isComplete && handleAiSplitAnalyze) || (isComplete && onRedetectScenes)) && (
                      <label
                        className="inline-flex items-center gap-1.5 text-[9px] font-mono text-slate-500 cursor-pointer select-none"
                        onClick={(e) => e.stopPropagation()}
                        title="When off, only scene/shot cuts are detected (fast). Surfaces can be detected later per-scene from the QA Workbench."
                      >
                        <input
                          type="checkbox"
                          checked={runSurfaceDetectionMap[video.id] ?? false}
                          onChange={(e) =>
                            setRunSurfaceDetectionMap(prev => ({ ...prev, [video.id]: e.target.checked }))
                          }
                          className="h-3 w-3 accent-fuchsia-600"
                        />
                        Detect surfaces with Gemini (slower)
                      </label>
                    )}
                    {!isComplete && handleAiSplitAnalyze && (
                      <button
                        type="button"
                        disabled={aiAnalyzingVideoId !== null || isPipelineActionPending !== null}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleAiSplitAnalyze(video.id, video.title, splitMode, runSurfaceDetectionMap[video.id] ?? false);
                        }}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all border cursor-pointer ${
                          aiAnalyzingVideoId === video.id
                            ? 'bg-fuchsia-50 border-fuchsia-200 text-fuchsia-600'
                            : 'bg-fuchsia-600 hover:bg-fuchsia-500 border-fuchsia-700 text-white shadow-xs'
                        }`}
                      >
                        {aiAnalyzingVideoId === video.id ? (
                          <><Loader2 className="h-3 w-3 animate-spin" /> Detecting scenes...</>
                        ) : (
                          <><Sparkles className="h-3 w-3" /> Run Scene Detection</>
                        )}
                      </button>
                    )}
                    {isComplete && onRedetectScenes && (
                      <button
                        type="button"
                        disabled={isPipelineActionPending !== null}
                        onClick={async (e) => {
                          e.stopPropagation();
                          if (reDetectConfirmId !== video.id) {
                            // First click — show confirmation
                            setReDetectConfirmId(video.id);
                            return;
                          }
                          // Second click — confirmed
                          setReDetectConfirmId(null);
                          await onRedetectScenes(video.id, video.title, splitMode, runSurfaceDetectionMap[video.id] ?? false);
                        }}
                        onBlur={() => setReDetectConfirmId(null)}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all border cursor-pointer ${
                          isPipelineActionPending === video.id
                            ? 'bg-amber-50 border-amber-200 text-amber-600'
                            : reDetectConfirmId === video.id
                            ? 'bg-red-500 hover:bg-red-400 border-red-600 text-white shadow-xs'
                            : 'bg-amber-500 hover:bg-amber-400 border-amber-600 text-white shadow-xs'
                        }`}
                        title={reDetectConfirmId === video.id
                          ? "Click again to confirm — this will destroy all existing scenes and surfaces"
                          : "Re-run scene detection to regenerate scene cuts and surfaces"}
                      >
                        {isPipelineActionPending === video.id ? (
                          <><Loader2 className="h-3 w-3 animate-spin" /> Re-running...</>
                        ) : reDetectConfirmId === video.id ? (
                          <><AlertTriangle className="h-3 w-3" /> Click to confirm re-detect</>
                        ) : (
                          <><RefreshCw className="h-3 w-3" /> Re-detect Scenes</>
                        )}
                      </button>
                    )}
                    {isComplete && (
                      <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-[9px] font-bold bg-emerald-50 text-emerald-700 border border-emerald-100">
                        <CheckCircle className="h-3 w-3" /> Scenes indexed — proceed to QA Workbench
                      </span>
                    )}
                    {video.ingestionStatus === 'Failed' && onResetPipeline && (
                      <button
                        type="button"
                        disabled={isPipelineActionPending !== null}
                        onClick={async (e) => {
                          e.stopPropagation();
                          await onResetPipeline(video.id);
                        }}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all border cursor-pointer ${
                          isPipelineActionPending === video.id
                            ? 'bg-red-50 border-red-200 text-red-600'
                            : 'bg-red-500 hover:bg-red-400 border-red-600 text-white shadow-xs'
                        }`}
                        title="Reset pipeline back to Staging to retry"
                      >
                        {isPipelineActionPending === video.id ? (
                          <><Loader2 className="h-3 w-3 animate-spin" /> Resetting...</>
                        ) : (
                          <><RotateCcw className="h-3 w-3" /> Reset Pipeline</>
                        )}
                      </button>
                    )}
                    {video.ingestionStatus === 'Failed' && video.lastErrorMessage && (
                      <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-[9px] font-bold bg-red-50 text-red-700 border border-red-100 max-w-[250px] truncate" title={video.lastErrorMessage}>
                        <AlertTriangle className="h-3 w-3 shrink-0" /> {video.lastErrorMessage}
                      </span>
                    )}
                    {isSelected && scenesForVideo.length > 0 && (
                      <span className="text-[9px] text-brand-600 font-mono">
                        ↓ {scenesForVideo.length} scene{scenesForVideo.length !== 1 ? 's' : ''} detected below
                      </span>
                    )}
                  </div>

                  {/* Scene cuts list shown for selected video */}
                  {isSelected && (
                    <div className="mt-4 pt-4 border-t border-slate-200/80">
                      <div className="text-[10px] uppercase tracking-wider font-extrabold text-slate-400 mb-2 font-mono flex items-center gap-2">
                        <Film className="h-3 w-3" />
                        Indexed Scene Cuts
                        {scenesForVideo.length > 0 && <span className="text-brand-500">({scenesForVideo.length})</span>}
                      </div>
                      {scenesForVideo.length === 0 ? (
                        <div className="text-2xs text-slate-400 italic bg-slate-50 rounded-lg p-3 border border-dashed border-slate-200">
                          No scenes detected yet. Click <strong>"Run Scene Detection"</strong> above to trigger scene-cut analysis, or wait for the automated pipeline.
                        </div>
                      ) : (
                        <>
                          {onMergeScenes && (
                            <div className="flex items-center gap-2 mb-2 text-[9px] text-slate-400 font-mono">
                              <Sparkles className="h-3 w-3" />
                              Check two or more <strong>consecutive</strong> scenes to fuse them into one — a manual alternative to AI clustering.
                            </div>
                          )}
                          {/* Selection bar lives OUTSIDE the scrollable grid below so it — and the
                              Merge button — stay visible no matter how far you've scrolled through
                              a long scene list, instead of requiring a scroll to the very bottom. */}
                          {onMergeScenes && scenesToMerge.size > 0 && (
                            <div className="mb-2 flex items-center gap-2 bg-fuchsia-50 border border-fuchsia-200 rounded-lg px-3 py-2">
                              <span className="text-[10px] font-mono font-bold text-fuchsia-700">
                                {scenesToMerge.size} scene{scenesToMerge.size !== 1 ? 's' : ''} selected
                              </span>
                              <button
                                type="button"
                                disabled={merging || scenesToMerge.size < 2}
                                onClick={handleMergeSelected}
                                className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[9px] font-mono font-bold uppercase tracking-wider bg-fuchsia-600 hover:bg-fuchsia-500 disabled:bg-fuchsia-300 text-white cursor-pointer transition-colors"
                              >
                                {merging ? <><Loader2 className="h-3 w-3 animate-spin" /> Merging...</> : 'Merge Selected'}
                              </button>
                              <button
                                type="button"
                                disabled={merging}
                                onClick={() => setScenesToMerge(new Set())}
                                className="text-[9px] font-mono font-bold uppercase text-slate-500 hover:text-slate-700 cursor-pointer"
                              >
                                Clear
                              </button>
                              {mergeError && (
                                <span className="text-[9px] text-red-600 font-mono ml-auto">{mergeError}</span>
                              )}
                            </div>
                          )}
                          <div className="grid grid-cols-2 md:grid-cols-4 gap-2 max-h-[440px] overflow-y-auto pr-1">
                          {scenesForVideo.map(scene => {
                            const checked = scenesToMerge.has(scene.id);
                            return (
                            <div
                              key={scene.id}
                              onClick={() => { setSelectedSceneId(scene.id); onNavigateToPlacements(); }}
                              className={`relative bg-white border rounded-lg overflow-hidden font-mono text-[10px] transition-all cursor-pointer ${
                                checked
                                  ? 'border-fuchsia-400 bg-fuchsia-50 shadow-sm ring-1 ring-fuchsia-300'
                                  : scene.id === selectedSceneId
                                  ? 'border-brand-400 bg-brand-50 shadow-sm ring-1 ring-brand-300'
                                  : 'border-slate-200/80 hover:border-brand-300 hover:bg-brand-50/30'
                              }`}
                            >
                              {onMergeScenes && (
                                <input
                                  type="checkbox"
                                  checked={checked}
                                  onChange={() => toggleSceneForMerge(scene.id)}
                                  onClick={(e) => e.stopPropagation()}
                                  className="absolute top-2 right-2 h-3.5 w-3.5 cursor-pointer accent-fuchsia-600 z-10"
                                  title="Select for merge"
                                />
                              )}
                              {/* Thumbnail — extracted server-side at the scene's middle frame — makes
                                  visually scanning/selecting among many scenes far faster than text alone. */}
                              {scene.thumbnailPath ? (
                                <img
                                  src={`/api/content/file/${scene.thumbnailPath}`}
                                  alt={`Scene #${scene.sceneIndex} thumbnail`}
                                  className="w-full aspect-video object-cover bg-slate-100"
                                  loading="lazy"
                                />
                              ) : (
                                <div className="w-full aspect-video bg-slate-100 flex items-center justify-center">
                                  <Film className="h-4 w-4 text-slate-300" />
                                </div>
                              )}
                              <div className="p-2.5">
                                <div className="text-slate-800 font-bold">Scene #{scene.sceneIndex}</div>
                                <div className="text-slate-400 mt-1">Frames: {scene.startFrame}–{scene.endFrame}</div>
                                <div className="text-slate-400">{scene.durationSeconds}s</div>
                                <div className={`text-[9px] mt-1 font-bold ${
                                  scene.qaStatus === 'Approved' ? 'text-emerald-600' :
                                  scene.qaStatus === 'PendingReview' ? 'text-brand-600' :
                                  scene.qaStatus === 'Flagged' ? 'text-red-500' : 'text-slate-400'
                                }`}>
                                  {scene.qaStatus}
                                </div>
                                <div className="text-[8px] text-brand-400 mt-1.5 font-sans flex items-center gap-0.5">
                                  <Eye className="h-2.5 w-2.5" /> Click to review surfaces
                                </div>
                              </div>
                            </div>
                            );
                          })}
                          </div>
                        </>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>

          <Pagination
            page={contentPage}
            totalPages={contentTotalPages}
            hasPreviousPage={contentHasPrev}
            hasNextPage={contentHasNext}
            onPageChange={setContentPage}
          />
        </div>
      </div>
    </motion.div>
  );
};
