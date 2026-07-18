import React from 'react';
import { motion } from 'motion/react';
import { Video, Plus, Trash2, Sparkles, Loader2, Info, CheckCircle, Clock, Film, Play, Eye } from 'lucide-react';
import { ContentItem, SceneItem } from '../types';

interface IngestionTabProps {
  contentList: ContentItem[];
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  scenesForVideo: SceneItem[];
  newVideoTitle: string;
  setNewVideoTitle: (v: string) => void;
  newVideoRes: string;
  setNewVideoRes: (v: string) => void;
  newVideoFps: number;
  setNewVideoFps: (v: number) => void;
  newVideoDuration: string;
  setNewVideoDuration: (v: string) => void;
  newVideoChannel: string;
  setNewVideoChannel: (v: string) => void;
  newVideoFile: File | null;
  setNewVideoFile: (f: File | null) => void;
  handleIngestVideo: (e: React.FormEvent) => void;
  ingestError: string | null;
  handleDeleteContent?: (id: string) => void;
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
}

/** Pipeline stage display order with icons and labels */
const PIPELINE_STAGES = [
  { key: 'Staging',       label: 'Staging',        icon: Clock,      color: 'text-slate-400', bg: 'bg-slate-100' },
  { key: 'Transcoding',   label: 'Transcoding',    icon: Loader2,    color: 'text-blue-500',  bg: 'bg-blue-50' },
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
              <div className={`h-0.5 w-3 rounded-full ${isComplete || isCurrent ? 'bg-blue-400' : 'bg-slate-200'}`} />
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
  contentList,
  selectedVideo,
  setSelectedVideo,
  scenesForVideo,
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
  handleDeleteContent,
  handleAiSplitAnalyze,
  aiAnalyzingVideoId,
}) => {
  const sceneCountByVideo: Record<string, number> = {};
  contentList.forEach(v => { sceneCountByVideo[v.id] = 0; });
  // Note: scenesForVideo only has scenes for the currently selected video

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="ingestion_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Video className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">Step 2: Video Ingestion — MReq 1 Pipeline</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            <strong>1. Register</strong> video metadata (title, duration, resolution, frame rate, source).{' '}
            <strong>2. System auto-transcodes</strong> to a normalised working format.{' '}
            <strong>3. Scene-cut detection</strong> splits footage into indexed segments.{' '}
            <strong>4. Ready for QA</strong> — scenes flow into the QA Workbench for surface approval.
            Watch the pipeline indicator {">"} track progress for each video.
          </p>
        </div>
      </div>

      {/* Ingest Form */}
      <div className="col-span-1 space-y-8">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">Register Video Metadata</h2>
          <p className="text-xs text-slate-500 mb-6 font-sans">
            Per <strong>MReq 1</strong>, provide the extracted metadata for the source broadcast feed. 
            The system will automatically begin transcoding and scene detection.
          </p>

          <form onSubmit={handleIngestVideo} className="space-y-4">
            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Video Title / Broadcast Name</label>
              <input 
                type="text" 
                value={newVideoTitle} 
                onChange={(e) => setNewVideoTitle(e.target.value)} 
                placeholder="e.g., EPL Matchday 25 - Chelsea vs Arsenal"
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                required
              />
            </div>

            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Target Native Resolution</label>
              <select 
                value={newVideoRes} 
                onChange={(e) => setNewVideoRes(e.target.value)}
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
              >
                <option value="1920x1080 (1080p)">1920x1080 (1080p Broadcast Proxy)</option>
                <option value="3840x2160 (4K)">3840x2160 (4K Cinema Master)</option>
                <option value="1280x720">1280x720 (Web streaming)</option>
              </select>
            </div>

            <div className="grid grid-cols-3 gap-2">
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Duration (HH:MM:SS)</label>
                <input 
                  type="text" 
                  value={newVideoDuration} 
                  onChange={(e) => setNewVideoDuration(e.target.value)} 
                  placeholder="00:05:00"
                  pattern="^\\d{2}:[0-5]\\d:[0-5]\\d$"
                  title="MReq 1: Perfect HH:MM:SS format required"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors font-mono"
                  required
                />
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Native FPS</label>
                <select
                  value={newVideoFps}
                  onChange={(e) => setNewVideoFps(Number(e.target.value))}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors font-mono"
                >
                  <option value={24}>24 FPS (Cinema)</option>
                  <option value={25}>25 FPS (PAL Broadcast)</option>
                  <option value={30}>30 FPS (NTSC)</option>
                  <option value={50}>50 FPS (Sports)</option>
                  <option value={60}>60 FPS (High Frame Rate)</option>
                </select>
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Source Channel</label>
                <input 
                  type="text" 
                  value={newVideoChannel} 
                  onChange={(e) => setNewVideoChannel(e.target.value)} 
                  placeholder="SuperSport Variety"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
            </div>

            <label className="border border-dashed border-slate-200 rounded-xl p-4 bg-slate-50/50 text-center cursor-pointer hover:border-blue-300 hover:bg-blue-50/30 transition-colors block">
              {newVideoFile ? (
                <>
                  <Video className="h-6 w-6 text-blue-500 mx-auto mb-2" />
                  <span className="text-2xs text-blue-600 block font-semibold">{newVideoFile.name}</span>
                  <span className="text-[10px] text-slate-400 block mt-1">{(newVideoFile.size / (1024 * 1024)).toFixed(1)} MB — click to change</span>
                </>
              ) : (
                <>
                  <Video className="h-6 w-6 text-slate-400 mx-auto mb-2" />
                  <span className="text-2xs text-slate-500 block font-semibold">Attach source file (MP4, MOV, MXF, AVI)</span>
                  <span className="text-[10px] text-slate-400 block mt-1">MReq 1: System transcodes to normalised format automatically.</span>
                </>
              )}
              <input type="file" accept="video/*,.mxf,.mov,.mp4,.avi" className="hidden"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) {
                    setNewVideoFile(file);
                    if (!newVideoTitle) setNewVideoTitle(file.name.replace(/\.[^.]+$/, ''));
                  }
                }} />
            </label>

            {ingestError && (
              <p className="text-2xs text-red-600 font-semibold font-mono bg-red-50 p-2.5 rounded-lg border border-red-100">{ingestError}</p>
            )}

            <button 
              type="submit" 
              className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <Plus className="h-3.5 w-3.5" />
              Register &amp; Start Pipeline
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
          const activeVideo = contentList.find(v => v.id === selectedVideo);
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

                {/* Top metadata bar */}
                <div className="absolute top-0 left-0 right-0 p-3 flex items-center justify-between z-10 pointer-events-none">
                  <div className="flex items-center gap-2">
                    <span className="px-2 py-0.5 rounded text-[9px] font-bold bg-black/50 text-white backdrop-blur font-mono">
                      {activeVideo.resolution}
                    </span>
                    <span className="px-2 py-0.5 rounded text-[9px] font-bold bg-black/50 text-white backdrop-blur font-mono">
                      {activeVideo.frameRate} FPS
                    </span>
                    {isLocalFile && (
                      <span className="px-2 py-0.5 rounded text-[9px] font-bold bg-emerald-600/70 text-white backdrop-blur font-mono">
                        REAL FILE
                      </span>
                    )}
                  </div>
                  <PipelineIndicator status={activeVideo.ingestionStatus} />
                </div>

                {/* Bottom timeline bar with scene markers */}
                <div className="absolute bottom-0 left-0 right-0 bg-slate-950/90 border-t border-slate-700/50 px-4 py-3">
                  <div className="flex items-center gap-2 mb-1.5">
                    <Eye className="h-3 w-3 text-blue-400" />
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
                        const colors = ['bg-blue-500/60', 'bg-emerald-500/60', 'bg-fuchsia-500/60', 'bg-amber-500/60', 'bg-cyan-500/60'];
                        const totalFrames = scenesForVideo.reduce((max, s) => Math.max(max, s.endFrame), 1);
                        const leftPct = (scene.startFrame / totalFrames) * 100;
                        const widthPct = ((scene.endFrame - scene.startFrame) / totalFrames) * 100;
                        return (
                          <div
                            key={scene.id}
                            className={`absolute top-0 h-full ${colors[idx % colors.length]} border-r border-white/20 flex items-center px-1.5 cursor-pointer hover:brightness-125 transition-all`}
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
                    {/* Playhead */}
                    <div className="absolute top-0 bottom-0 w-0.5 bg-red-500 z-10 shadow-[0_0_6px_rgba(239,68,68,0.5)]" style={{ left: '15%' }}>
                      <div className="absolute -top-1 left-1/2 -translate-x-1/2 h-2.5 w-2.5 bg-red-500 rounded-full shadow-[0_0_6px_rgba(239,68,68,0.7)]"></div>
                    </div>
                  </div>
                  {/* Time markers */}
                  <div className="flex justify-between mt-1 text-[8px] text-slate-500 font-mono">
                    <span>00:00</span>
                    <span>{activeVideo.duration}</span>
                  </div>
                </div>
              </div>
            </div>
          );
        })()}

        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 font-display">Video Pipeline Catalog</h3>
            <span className="text-[10px] text-slate-400 font-mono">{contentList.length} video{contentList.length !== 1 ? 's' : ''} registered</span>
          </div>
          
          <div className="space-y-4">
            {contentList.length === 0 && (
              <div className="text-center py-12 text-xs text-slate-400 bg-slate-50 rounded-xl border border-dashed border-slate-200">
                <Film className="h-8 w-8 mx-auto mb-2 text-slate-300" />
                <p>No videos registered yet.</p>
                <p className="text-[10px] mt-1">Use the form to register broadcast feed metadata and start the pipeline.</p>
              </div>
            )}
            {contentList.map(video => {
              const isSelected = selectedVideo === video.id;
              const isComplete = video.ingestionStatus === 'Completed';
              return (
                <div 
                  key={video.id} 
                  onClick={() => setSelectedVideo(video.id)}
                  className={`border rounded-xl p-4 transition-all cursor-pointer ${
                    isSelected 
                      ? 'bg-blue-50/40 border-blue-400 shadow-sm' 
                      : 'bg-slate-50/30 border-slate-200 hover:border-slate-300'
                  }`}
                  id={`video_card_${video.id}`}
                >
                  <div className="flex flex-col md:flex-row md:items-center justify-between gap-2">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-2xs font-mono font-bold text-blue-600">ID: {video.id}</span>
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
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDeleteContent(video.id);
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
                            const colors = ['bg-blue-400', 'bg-emerald-400', 'bg-fuchsia-400', 'bg-amber-400'];
                            const leftPct = (scene.startFrame / totalFrames) * 100;
                            const widthPct = Math.max(((scene.endFrame - scene.startFrame) / totalFrames) * 100, 1.5);
                            return (
                              <div key={scene.id}
                                className={`absolute top-0 h-full ${colors[idx % colors.length]} opacity-70`}
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

                  {/* Actions bar */}
                  <div className="mt-3 pt-3 border-t border-slate-200/50 flex flex-wrap items-center gap-2">
                    {!isComplete && handleAiSplitAnalyze && (
                      <button
                        type="button"
                        disabled={aiAnalyzingVideoId !== null}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleAiSplitAnalyze(video.id, video.title);
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
                    {isComplete && (
                      <span className="inline-flex items-center gap-1 px-2 py-1 rounded text-[9px] font-bold bg-emerald-50 text-emerald-700 border border-emerald-100">
                        <CheckCircle className="h-3 w-3" /> Scenes indexed — proceed to QA Workbench
                      </span>
                    )}
                    {isSelected && scenesForVideo.length > 0 && (
                      <span className="text-[9px] text-blue-600 font-mono">
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
                        {scenesForVideo.length > 0 && <span className="text-blue-500">({scenesForVideo.length})</span>}
                      </div>
                      {scenesForVideo.length === 0 ? (
                        <div className="text-2xs text-slate-400 italic bg-slate-50 rounded-lg p-3 border border-dashed border-slate-200">
                          No scenes detected yet. Click <strong>"Run Scene Detection"</strong> above to trigger MReq 1 scene-cut analysis, or wait for the automated pipeline.
                        </div>
                      ) : (
                        <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                          {scenesForVideo.map(scene => (
                            <div key={scene.id} className="bg-white border border-slate-200/80 rounded-lg p-2.5 font-mono text-[10px] hover:border-blue-300 transition-colors">
                              <div className="text-slate-800 font-bold">Scene #{scene.sceneIndex}</div>
                              <div className="text-slate-400 mt-1">Frames: {scene.startFrame}–{scene.endFrame}</div>
                              <div className="text-slate-400">{scene.durationSeconds}s</div>
                              <div className={`text-[9px] mt-1 font-bold ${
                                scene.qaStatus === 'Approved' ? 'text-emerald-600' : 
                                scene.qaStatus === 'Flagged' ? 'text-red-500' : 'text-slate-400'
                              }`}>
                                {scene.qaStatus}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </motion.div>
  );
};
