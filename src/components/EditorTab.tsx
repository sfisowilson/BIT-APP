import React from 'react';
import { motion } from 'motion/react';
import { Tv, Play, Shield, CheckCircle, AlertTriangle, Sparkles, Wand2, Check, Loader2, Eye, Layout } from 'lucide-react';
import { ContentItem, SceneItem, SurfaceItem, CreativeAsset, CampaignItem } from '../types';

interface EditorTabProps {
  contentList: ContentItem[];
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  scenesForVideo: SceneItem[];
  surfacesForScene: SurfaceItem[];
  selectedSurfaceId: string;
  setSelectedSurfaceId: (v: string) => void;
  rejectionReason: string;
  setRejectionReason: (v: string) => void;
  handleSurfaceDecision: (decision: "Approved" | "Rejected") => void;
  currentSurface: SurfaceItem | undefined;
  handleAiCustomizeScene: (sceneId: string, prompt: string) => Promise<void>;
  assetList?: CreativeAsset[];
  campaignList?: CampaignItem[];
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
  handleAiCustomizeScene,
  assetList = [],
  campaignList = [],
}) => {
  const [aiPromptText, setAiPromptText] = React.useState('');
  const [previewAssetId, setPreviewAssetId] = React.useState<string>('');
  const [selectedBlendMode, setSelectedBlendMode] = React.useState<'multiply' | 'overlay' | 'normal'>('multiply');
  const [ambientIntensity, setAmbientIntensity] = React.useState<number>(0.85);
  
  const currentScene = scenesForVideo.find(s => s.id === selectedSceneId);
  const activeVideo = contentList.find(v => v.id === selectedVideo);
  const isLocalVideo = activeVideo?.storageKey?.startsWith('/api/content/file/');

  React.useEffect(() => {
    setAiPromptText(currentScene?.aiPrompt || '');
  }, [selectedSceneId, currentScene?.aiPrompt]);

  const activePreviewAsset = assetList.find(a => a.id === previewAssetId);

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="editor_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Tv className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">Step 3: AI Surface Quality Approval Workbench</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            This visual review screen maps out recommended 3D plane boundaries detected via computer-vision models (<strong>MReq 2 &amp; 3</strong>). To keep the workflow easy to follow, select an ingested video and a scene segment, then click on the colored 3D geometry polygons inside the interactive player layout below. Evaluate scores and either Approve or Exclude each slot (<strong>MReq 11</strong>).
          </p>
        </div>
      </div>

      {/* Interactive Player Simulation and Frame Overlays (MReq 11) */}
      <div className="lg:col-span-2 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between border-b border-slate-100 pb-4 mb-5 gap-3">
            <div>
              <h2 className="text-lg font-bold text-slate-800 font-display">Editor Placements Workbench</h2>
              <p className="text-xs text-slate-400 mt-0.5">Perform visual QA of recommended computer-vision surfaces frame-by-frame.</p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-1 bg-slate-100 border border-slate-200 rounded-lg px-2 py-1 text-slate-600 font-mono text-[10px]">
                <Eye className="h-3 w-3 text-fuchsia-600 animate-pulse" />
                <span className="font-bold text-slate-700">AI Preview Asset:</span>
                <select 
                  value={previewAssetId} 
                  onChange={(e) => setPreviewAssetId(e.target.value)}
                  className="bg-transparent text-[10px] text-fuchsia-700 font-bold focus:outline-none border-none cursor-pointer"
                >
                  <option value="">None (Plain Outlines)</option>
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
                {contentList.filter(v => v.ingestionStatus === 'Completed').map(v => (
                  <option key={v.id} value={v.id}>{v.title}</option>
                ))}
              </select>

              <select 
                value={selectedSceneId} 
                onChange={(e) => setSelectedSceneId(e.target.value)}
                className="bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 font-mono focus:outline-none"
              >
                {scenesForVideo.map(s => (
                  <option key={s.id} value={s.id}>Scene #{s.sceneIndex}</option>
                ))}
              </select>
            </div>
          </div>

          {/* MReq 3: Video player with surface overlay for placement review */}
          <div className={`relative aspect-video bg-black border rounded-xl overflow-hidden group shadow-2xl transition-all duration-500 ${
            currentScene?.aiStatus === 'completed' 
              ? 'border-fuchsia-500 shadow-fuchsia-500/10' 
              : 'border-slate-700'
          }`}>
            {/* Real video playback when file exists */}
            {isLocalVideo && activeVideo ? (
              <video
                src={activeVideo.storageKey}
                className="absolute inset-0 w-full h-full object-contain"
                controls
                preload="metadata"
                id="qa_video_player"
              />
            ) : (
              /* Fallback: dark preview frame when no video file */
              <div className="absolute inset-0 bg-gradient-to-br from-slate-800 via-slate-900 to-slate-950 flex items-center justify-center">
                <div className="text-center">
                  <Play className="h-12 w-12 text-white/20 mx-auto mb-2" />
                  <p className="text-white/30 text-xs font-mono">No video file — upload in Ingestion tab</p>
                </div>
              </div>
            )}

            {/* AI Active Overlay indicator */}
            {currentScene?.aiStatus === 'completed' && (
              <div className="absolute top-4 left-4 z-10 bg-fuchsia-600/90 border border-fuchsia-400 text-white font-mono text-[9px] font-bold uppercase px-2.5 py-1 rounded-full flex items-center gap-1.5 shadow-md shadow-fuchsia-500/20 animate-pulse">
                <Sparkles className="h-3 w-3" />
                <span>AI Scene Customized (Active Preview)</span>
              </div>
            )}
            
            {/* Bounding vector polygons via SVG overlay on top of video */}
            <svg className="absolute inset-0 w-full h-full pointer-events-none z-10" viewBox="0 0 1280 720" id="player_overlay_svg" preserveAspectRatio="none">
              {/* MReq 3: Render surface polygons from surfacesForScene — clickable for selection */}
              {surfacesForScene.map(sf => {
                const isSelected = selectedSurfaceId === sf.id;
                const isExcluded = sf.status === "Excluded";
                const isApproved = sf.status === "Approved";
                
                // Parse coordinates
                const pointsString = sf.boundaryCoordinates.map(p => `${p.x},${p.y}`).join(" ");

                // Compute bounding calculations for realistic brand overlay centering
                const xs = sf.boundaryCoordinates.map(p => p.x);
                const ys = sf.boundaryCoordinates.map(p => p.y);
                const minX = Math.min(...xs);
                const maxX = Math.max(...xs);
                const minY = Math.min(...ys);
                const maxY = Math.max(...ys);
                const centerX = xs.reduce((a, b) => a + b, 0) / xs.length;
                const centerY = ys.reduce((a, b) => a + b, 0) / ys.length;
                const rotAngle = sf.orientationVector?.roll || 0;

                return (
                  <g key={sf.id} className="cursor-pointer pointer-events-auto" onClick={() => setSelectedSurfaceId(sf.id)} id={`svg_surface_${sf.id}`}>
                    <polygon 
                      points={pointsString}
                      fill={isExcluded ? "#ef4444" : isApproved ? "#10b981" : "#3b82f6"}
                      fillOpacity={isSelected ? 0.45 : 0.25}
                      stroke={isExcluded ? "#ef4444" : isApproved ? "#10b981" : "#3b82f6"}
                      strokeWidth={isSelected ? 3 : 2}
                      strokeDasharray={isSelected ? "none" : "6 3"}
                      className="transition-all duration-200"
                    />

                    {/* Realistic Spatial Brand Compositing Simulation — preview on any selected surface when asset chosen */}
                    {activePreviewAsset && isSelected && (
                      <g style={{ mixBlendMode: selectedBlendMode, opacity: ambientIntensity }}>
                        <polygon 
                          points={pointsString}
                          fill={
                            activePreviewAsset.brandCategory.includes('Beverage') ? '#1e3a8a' :
                            activePreviewAsset.brandCategory.includes('Automotive') || activePreviewAsset.brandCategory.includes('Motoring') ? '#1e293b' :
                            activePreviewAsset.brandCategory.includes('Telecom') || activePreviewAsset.brandCategory.includes('Mobile') ? '#701a75' :
                            activePreviewAsset.brandCategory.includes('Apparel') ? '#b45309' :
                            activePreviewAsset.brandCategory.includes('Electronics') || activePreviewAsset.brandCategory.includes('Technology') ? '#065f46' :
                            '#475569'
                          }
                          fillOpacity={0.8}
                        />
                        <text
                          x={centerX}
                          y={centerY + 4}
                          fill="#ffffff"
                          fontSize="12"
                          fontWeight="bold"
                          fontFamily="sans-serif"
                          letterSpacing="1.5"
                          textAnchor="middle"
                          transform={`rotate(${rotAngle}, ${centerX}, ${centerY})`}
                        >
                          {activePreviewAsset.name.toUpperCase()}
                        </text>
                        {/* Scanline/Grid perspective effect to look unified */}
                        <polygon 
                          points={pointsString}
                          fill="url(#grid)"
                          fillOpacity={0.2}
                        />
                      </g>
                    )}

                    {/* Bounding Center Flag text */}
                    <text 
                      x={sf.boundaryCoordinates[0].x} 
                      y={sf.boundaryCoordinates[0].y - 8} 
                      fill="white" 
                      fontSize="10" 
                      fontWeight="bold" 
                      className="font-mono bg-black"
                    >
                      {sf.surfaceType.slice(0, 20)} ({Math.round(sf.confidenceScore * 100)}%)
                    </text>
                  </g>
                );
              })}
            </svg>

            {/* HUD bar with scene metadata — not a second control bar */}
            <div className="absolute bottom-4 left-4 right-4 bg-slate-900/90 border border-slate-700 rounded-lg px-4 py-2 flex items-center justify-between text-[11px] font-mono text-slate-400 z-20 pointer-events-none">
              <div className="flex items-center gap-2">
                <Eye className="h-3 w-3 text-blue-400" />
                <span>Scene #{currentScene?.sceneIndex || '—'} · {surfacesForScene.length} surface{surfacesForScene.length !== 1 ? 's' : ''}</span>
              </div>
              <div>
                <span>{activeVideo?.resolution || '—'} · {activeVideo?.frameRate || '—'} FPS · {activeVideo?.duration || '—'}</span>
              </div>
            </div>
          </div>

          {/* Fine-Tuning controls */}
          <div className="mt-4 p-4.5 bg-slate-50 rounded-xl border border-slate-200/60 grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <div className="text-[10px] uppercase font-mono font-bold text-slate-500 mb-1.5 flex items-center gap-1.5">
                <Layout className="h-3.5 w-3.5 text-blue-500" />
                <span>Blend Mode Preview</span>
                <span className="text-[9px] text-slate-400 normal-case">— visible when surface selected + asset chosen</span>
              </div>
              <div className="flex gap-1.5">
                {[
                  { mode: 'normal', label: 'Flat Matte' },
                  { mode: 'multiply', label: 'Multiply (Tex/Wall)' },
                  { mode: 'overlay', label: 'Overlay (LED/Glow)' }
                ].map(b => (
                  <button
                    key={b.mode}
                    onClick={() => setSelectedBlendMode(b.mode as any)}
                    className={`px-2.5 py-1.5 text-[10px] font-semibold rounded-lg border transition-all cursor-pointer ${
                      selectedBlendMode === b.mode
                        ? 'bg-blue-600 border-blue-700 text-white shadow-xs'
                        : 'bg-white border-slate-200 text-slate-600 hover:bg-slate-55'
                    }`}
                  >
                    {b.label}
                  </button>
                ))}
              </div>
            </div>

            <div>
              <div className="text-[10px] uppercase font-mono font-bold text-slate-500 mb-1.5 flex justify-between items-center">
                <span>Ambient Light Exposure: {Math.round(ambientIntensity * 100)}%</span>
                <span className="text-[9px] font-semibold text-slate-400">Natural Shading</span>
              </div>
              <input
                type="range"
                min="0.2"
                max="1.0"
                step="0.05"
                value={ambientIntensity}
                onChange={(e) => setAmbientIntensity(parseFloat(e.target.value))}
                className="w-full h-1 bg-slate-200 rounded-lg appearance-none cursor-pointer accent-blue-600"
              />
            </div>
          </div>

          <div className="mt-4 text-xs text-slate-500 flex items-center gap-1.5 font-mono">
            <span className="h-2 w-2 rounded-full bg-blue-500 animate-pulse"></span>
            <span>Click colored boundary polygons above to select surfaces. Choose a Preview Asset and blend mode below to simulate brand insertion.</span>
          </div>
        </div>

        {/* AI Scene Pre-Render Customizer Card (MReq 2) */}
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-2.5 border-b border-slate-100 pb-4 mb-4">
            <div className="h-8 w-8 rounded-lg bg-fuchsia-50 flex items-center justify-center text-fuchsia-600">
              <Sparkles className="h-4.5 w-4.5" />
            </div>
            <div>
              <h3 className="text-sm font-bold text-slate-800 font-display flex items-center gap-1.5">
                AI Scene Pre-Render Customizer
              </h3>
              <p className="text-[11px] text-slate-400">Modify visual composition elements of this specific scene before final rendering.</p>
            </div>
          </div>

          {currentScene ? (
            <div className="space-y-4">
              <div className="text-2xs font-mono bg-fuchsia-50/50 text-fuchsia-700 border border-fuchsia-100/50 p-2.5 rounded-lg flex items-center justify-between">
                <span>Active Target: Scene #{currentScene.sceneIndex}</span>
                <span className="font-bold">Frames: {currentScene.startFrame} - {currentScene.endFrame} ({currentScene.durationSeconds}s)</span>
              </div>

              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">
                  Generative Scene Modification Prompt
                </label>
                <textarea
                  value={aiPromptText}
                  onChange={(e) => setAiPromptText(e.target.value)}
                  placeholder="e.g., Modify the scene color grading to look like a cinematic golden hour sunset, increase background lighting intensity, and align ambient fog depth."
                  rows={3}
                  disabled={currentScene.aiStatus === 'processing'}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 text-xs text-slate-800 focus:outline-none focus:border-fuchsia-500/50 resize-none font-sans disabled:opacity-60"
                />
              </div>

              <div className="flex justify-between items-center">
                <span className="text-[10px] font-mono text-slate-400">Powered by Gemini 3.5 Flash</span>
                <button
                  type="button"
                  onClick={() => handleAiCustomizeScene(currentScene.id, aiPromptText)}
                  disabled={!aiPromptText.trim() || currentScene.aiStatus === 'processing'}
                  className="inline-flex items-center gap-1.5 px-3.5 py-1.5 bg-fuchsia-600 hover:bg-fuchsia-500 disabled:bg-fuchsia-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm shadow-fuchsia-500/10"
                >
                  {currentScene.aiStatus === 'processing' ? (
                    <>
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      Invoking Model...
                    </>
                  ) : (
                    <>
                      <Wand2 className="h-3.5 w-3.5" />
                      Apply AI Scene Update
                    </>
                  )}
                </button>
              </div>

              {/* Status and Details Outcomes */}
              {currentScene.aiStatus === 'processing' && (
                <div className="p-4 bg-slate-50 border border-slate-200/60 rounded-xl space-y-2 animate-pulse">
                  <div className="flex items-center gap-2 text-xs font-medium text-slate-600 font-mono">
                    <Loader2 className="h-4 w-4 animate-spin text-fuchsia-500" />
                    <span>Gemini model generating VFX pipeline details...</span>
                  </div>
                  <div className="h-1.5 w-full bg-slate-200 rounded-full overflow-hidden">
                    <div className="h-full bg-fuchsia-500 rounded-full animate-pulse" style={{ width: '60%' }}></div>
                  </div>
                </div>
              )}

              {currentScene.aiStatus === 'completed' && (
                <div className="p-4 bg-fuchsia-50/20 border border-fuchsia-100 rounded-xl space-y-3">
                  <div className="flex items-center justify-between">
                    <span className="text-2xs font-mono font-bold text-fuchsia-700 flex items-center gap-1">
                      <Check className="h-3.5 w-3.5 text-fuchsia-600" />
                      AI SCENE TRANSFORMATION REGISTERED
                    </span>
                    <span className="text-[9px] bg-fuchsia-100 text-fuchsia-700 px-2 py-0.5 rounded font-mono font-bold">
                      {currentScene.aiModelUsed || 'Gemini'}
                    </span>
                  </div>

                  <div className="space-y-1">
                    <div className="text-[10px] font-mono text-slate-400 font-bold uppercase">Visual Outcome Description</div>
                    <p className="text-xs text-slate-700 leading-relaxed font-sans bg-white p-2.5 rounded-lg border border-slate-200/50 shadow-2xs">
                      {currentScene.aiOutputDescription}
                    </p>
                  </div>

                  {currentScene.aiOutputDescription && (
                    <div className="flex flex-wrap gap-1.5 pt-1">
                      <span className="text-[9px] bg-slate-50 border border-slate-250/50 px-2 py-0.5 rounded text-slate-600 font-mono">
                        Model: {currentScene.aiModelUsed || 'Gemini'}
                      </span>
                      <span className="text-[9px] bg-slate-50 border border-slate-250/50 px-2 py-0.5 rounded text-slate-600 font-mono">
                        Status: Complete
                      </span>
                    </div>
                  )}
                </div>
              )}

              {currentScene.aiStatus === 'failed' && (
                <div className="p-3.5 bg-red-50 border border-red-100 rounded-xl text-red-700 text-xs font-medium">
                  ⚠️ AI Scene customization failed. Please check the API key settings and try again.
                </div>
              )}
            </div>
          ) : (
            <div className="text-xs text-slate-400 italic text-center py-6">
              Select a valid scene segment above to unlock the AI Customizer workbench.
            </div>
          )}
        </div>
      </div>

      {/* Surface metadata information & approvals sidebar */}
      <div className="col-span-1 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">Relational Placement Metadata</h3>
          
          {currentSurface ? (
            <div className="space-y-4">
              <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80">
                <div className="text-[10px] uppercase tracking-wider text-slate-400 font-mono font-bold">Surface Type</div>
                <div className="text-sm font-bold text-slate-800 mt-0.5">{currentSurface.surfaceType}</div>
              </div>

              <div className="grid grid-cols-2 gap-2 text-xs font-mono">
                <div className="bg-slate-50 p-2.5 rounded-lg border border-slate-200/80">
                  <span className="text-slate-400">Confidence:</span>
                  <div className="text-slate-800 font-bold mt-0.5">{(currentSurface.confidenceScore * 100).toFixed(0)} %</div>
                </div>
                <div className="bg-slate-50 p-2.5 rounded-lg border border-slate-200/80">
                  <span className="text-slate-400">Depth Metric:</span>
                  <div className="text-slate-800 font-bold mt-0.5">{currentSurface.estimatedDepth} meters</div>
                </div>
              </div>

              {/* 3D vector parameters */}
              <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80 font-mono text-[10px] text-slate-500">
                <div className="border-b border-slate-200/50 pb-1 mb-1.5 font-bold uppercase tracking-wide text-slate-400">Orientation Planes (3D Yaw/Pitch/Roll)</div>
                <div>Yaw: {currentSurface.orientationVector.yaw}°</div>
                <div>Pitch: {currentSurface.orientationVector.pitch}°</div>
                <div>Roll: {currentSurface.orientationVector.roll}°</div>
              </div>

              <div className="bg-slate-50 p-3 rounded-lg border border-slate-200/80">
                <div className="flex justify-between items-center text-xs">
                  <span className="text-slate-500 font-medium">Inventory Status:</span>
                  <span className={`px-2 py-0.5 rounded text-[10px] font-bold uppercase ${
                    currentSurface.status === 'Approved' ? 'bg-emerald-50 text-emerald-700 border border-emerald-100' :
                    currentSurface.status === 'Excluded' ? 'bg-red-50 text-red-700 border border-red-100' :
                    'bg-blue-50 text-blue-700 border border-blue-100'
                  }`}>{currentSurface.status}</span>
                </div>
                {currentSurface.exclusionReason && (
                  <p className="text-2xs text-red-600 leading-normal mt-2 italic bg-red-50 p-2 rounded border border-red-100">Exclusion Reason: {currentSurface.exclusionReason}</p>
                )}
              </div>

              {/* Interactive Approvals buttons (MReq 11) */}
              {currentSurface.status === "Excluded" && currentSurface.exclusionReason?.includes("MReq 4") ? (
                <div className="p-3.5 bg-red-50 border border-red-200/60 rounded-xl text-red-700 text-xs">
                  <Shield className="h-4 w-4 inline mr-1 text-red-600" />
                  <strong>Security Blocklist Activated:</strong> Face classification overrides cannot be bypassed or manually approved by any active user role (<strong>MReq 4 Safeguard</strong>).
                </div>
              ) : (
                <div className="space-y-3 pt-2">
                  <button 
                    onClick={() => handleSurfaceDecision("Approved")}
                    className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs rounded-lg cursor-pointer transition-all shadow-xs"
                  >
                    <CheckCircle className="h-3.5 w-3.5" />
                    Approve Placement Surface
                  </button>

                  <div className="pt-3 border-t border-slate-100">
                    <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Manual Exclusion Reason</label>
                    <input 
                      type="text" 
                      value={rejectionReason} 
                      onChange={(e) => setRejectionReason(e.target.value)} 
                      placeholder="e.g., Low contrast lighting"
                      className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-800 focus:outline-none focus:border-red-500/50 mb-2"
                    />
                    <button 
                      onClick={() => handleSurfaceDecision("Rejected")}
                      disabled={!rejectionReason}
                      className="w-full inline-flex items-center justify-center gap-2 px-3 py-1.5 bg-red-50 hover:bg-red-100 border border-red-200 text-red-600 font-semibold text-xs rounded-lg cursor-pointer transition-all disabled:opacity-40"
                    >
                      <AlertTriangle className="h-3.5 w-3.5" />
                      Exclude Surface Location
                    </button>
                  </div>
                </div>
              )}
            </div>
          ) : (
            <div className="text-xs text-slate-400 italic bg-slate-50 p-4 rounded-xl border border-slate-200/50 text-center">No surface coordinates selected. Click on a highlighted box on the visual player area.</div>
          )}
        </div>
      </div>
    </motion.div>
  );
};
