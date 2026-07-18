import React from 'react';
import { motion } from 'motion/react';
import { Video, Plus, Trash2, Sparkles, Loader2, Info } from 'lucide-react';
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
  newVideoChannel: string;
  setNewVideoChannel: (v: string) => void;
  handleIngestVideo: (e: React.FormEvent) => void;
  handleDeleteContent?: (id: string) => void;
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
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
  newVideoChannel,
  setNewVideoChannel,
  handleIngestVideo,
  handleDeleteContent,
  handleAiSplitAnalyze,
  aiAnalyzingVideoId,
}) => {
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
          <h4 className="font-bold text-sm text-blue-900">Step 2: Video Ingestion &amp; Scene Splitting</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            Ingest high-bitrate broadcast feeds (such as MXF, ProRes, or raw MP4 files) into high-performance cloud storage channels. The system triggers background scene-cut detection using computer-vision histograms (<strong>MReq 1</strong>). This splits footage into stable visual segments for precise, frame-perfect ad placements.
          </p>
        </div>
      </div>

      {/* Ingest Form */}
      <div className="col-span-1 space-y-8">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">Ingest Source Footage</h2>
          <p className="text-xs text-slate-500 mb-6 font-sans">Accept professional camera feeds and stage raw videos into high-performance object stores.</p>

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

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Native FPS</label>
                <input 
                  type="number" 
                  value={newVideoFps} 
                  onChange={(e) => setNewVideoFps(Number(e.target.value))} 
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors font-mono"
                  required
                />
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Ingest Stream Channel</label>
                <input 
                  type="text" 
                  value={newVideoChannel} 
                  onChange={(e) => setNewVideoChannel(e.target.value)} 
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
            </div>

            <label className="border border-dashed border-slate-200 rounded-xl p-4 bg-slate-50/50 text-center cursor-pointer hover:border-blue-300 hover:bg-blue-50/30 transition-colors block">
              <Video className="h-6 w-6 text-slate-400 mx-auto mb-2" />
              <span className="text-2xs text-slate-500 block font-semibold">Select MXF, ProRes, or high-bitrate raw files.</span>
              <span className="text-[10px] text-slate-400 block mt-1">MReq 1 Compliance: maintains absolute visual and duration integrity.</span>
              <input type="file" accept="video/*,.mxf,.mov,.mp4,.avi" className="hidden"
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file && !newVideoTitle) setNewVideoTitle(file.name.replace(/\.[^.]+$/, ''));
                }} />
            </label>

            <button 
              type="submit" 
              className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <Plus className="h-3.5 w-3.5" />
              Ingest Broadcast Feed
            </button>
          </form>
        </div>
      </div>

      {/* Ingested Video Catalog List */}
      <div className="col-span-2 space-y-6">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">Ingested Video catalog</h3>
          
          <div className="space-y-4">
            {contentList.map(video => {
              const isSelected = selectedVideo === video.id;
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
                        <span className={`px-2 py-0.5 rounded text-[9px] font-mono font-bold uppercase ${
                          video.ingestionStatus === 'Completed' ? 'bg-emerald-50 text-emerald-700 border border-emerald-100' :
                          video.ingestionStatus === 'Failed' ? 'bg-red-50 text-red-700 border border-red-100' :
                          'bg-blue-50 text-blue-700 border border-blue-100 animate-pulse'
                        }`}>
                          {video.ingestionStatus === 'Completed' ? 'Completed & Indexed' : video.ingestionStatus}
                        </span>
                      </div>
                      <h4 className="text-sm font-bold text-slate-800 font-display mt-1.5">{video.title}</h4>
                      <p className="text-xs text-slate-400 mt-1 font-mono">S3 Staging Key: {video.storageKey}</p>
                      
                      {handleAiSplitAnalyze && (
                        <button
                          type="button"
                          disabled={aiAnalyzingVideoId !== null}
                          onClick={(e) => {
                            e.stopPropagation();
                            handleAiSplitAnalyze(video.id, video.title);
                          }}
                          className={`mt-3 inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all border shrink-0 cursor-pointer ${
                            aiAnalyzingVideoId === video.id
                              ? 'bg-fuchsia-50 border-fuchsia-200 text-fuchsia-600 animate-pulse'
                              : 'bg-fuchsia-600 hover:bg-fuchsia-500 border-fuchsia-700 text-white shadow-xs'
                          }`}
                        >
                          {aiAnalyzingVideoId === video.id ? (
                            <>
                              <Loader2 className="h-3.5 w-3.5 animate-spin text-fuchsia-600" />
                              <span>AI Spatial Analyzer running...</span>
                            </>
                          ) : (
                            <>
                              <Sparkles className="h-3.5 w-3.5" />
                              <span>Run Spatial Gemini AI Split & Analysis</span>
                            </>
                          )}
                        </button>
                      )}
                    </div>

                    <div className="flex items-center gap-4 shrink-0">
                      <div className="text-right text-xs text-slate-500 font-mono">
                        <div>Res: {video.resolution}</div>
                        <div>Rate: {video.frameRate} FPS</div>
                        <div>Channel: {video.sourceChannel}</div>
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

                  {/* Scene cuts list shown for selected video */}
                  {isSelected && (
                    <div className="mt-4 pt-4 border-t border-slate-200/80">
                      <div className="text-[10px] uppercase tracking-wider font-extrabold text-slate-400 mb-2 font-mono">
                        Detected Scene Cuts (FFmpeg Histogram Markers)
                      </div>
                      {scenesForVideo.length === 0 ? (
                        <div className="text-2xs text-slate-400 italic">Scene cut-detection processing in background...</div>
                      ) : (
                        <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                          {scenesForVideo.map(scene => (
                            <div key={scene.id} className="bg-white border border-slate-200/80 rounded-lg p-2.5 font-mono text-[10px]">
                              <div className="text-slate-800 font-bold">Scene #{scene.sceneIndex}</div>
                              <div className="text-slate-400 mt-1">Frames: {scene.startFrame} - {scene.endFrame}</div>
                              <div className="text-slate-400">Duration: {scene.durationSeconds}s</div>
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
