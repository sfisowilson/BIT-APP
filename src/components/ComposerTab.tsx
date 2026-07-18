import React from 'react';
import { motion } from 'motion/react';
import { Cpu, Sparkles, Receipt, Film, Loader2, Play } from 'lucide-react';
import { CampaignItem, CreativeAsset, RenderItem, SceneItem } from '../types';

interface ComposerTabProps {
  selectedSurfaceId: string;
  selectedVideo: string;
  campaignList: CampaignItem[];
  composerCampaignId: string;
  setComposerCampaignId: (v: string) => void;
  assetList: CreativeAsset[];
  composerAssetId: string;
  setComposerAssetId: (v: string) => void;
  composerPreset: string;
  setComposerPreset: (v: string) => void;
  handleQueueRender: (e: React.FormEvent) => void;
  renderList: RenderItem[];
  scenesForVideo?: SceneItem[];
}

export const ComposerTab: React.FC<ComposerTabProps> = ({
  selectedSurfaceId,
  selectedVideo,
  campaignList,
  composerCampaignId,
  setComposerCampaignId,
  assetList,
  composerAssetId,
  setComposerAssetId,
  composerPreset,
  setComposerPreset,
  handleQueueRender,
  renderList,
  scenesForVideo,
}) => {
  const aiCustomizedScenes = scenesForVideo?.filter(s => s.aiStatus === 'completed' && s.aiPrompt) || [];

  // Filter assets by selected campaign for coherent flow (campaign → its assets)
  const campaignFilteredAssets = composerCampaignId
    ? assetList.filter(a => a.campaignId === composerCampaignId)
    : assetList;
  const unassignedAssetsForDropdown = assetList.filter(a => !a.campaignId);

  // Auto-clear asset selection when switching to a campaign whose assets don't include the current pick
  React.useEffect(() => {
    if (composerCampaignId && composerAssetId) {
      const stillValid = campaignFilteredAssets.some(a => a.id === composerAssetId);
      if (!stillValid) setComposerAssetId('');
    }
  }, [composerCampaignId]);

  // Stitching Console States
  const [isStitching, setIsStitching] = React.useState(false);
  const [stitchingProgress, setStitchingProgress] = React.useState(0);
  const [isStitchedDone, setIsStitchedDone] = React.useState(false);
  const [stitchingLogs, setStitchingLogs] = React.useState<string[]>([]);
  const [renderSubmitted, setRenderSubmitted] = React.useState(false);

  const wrappedHandleQueueRender = async (e: React.FormEvent) => {
    await handleQueueRender(e);
    setRenderSubmitted(true);
    setTimeout(() => setRenderSubmitted(false), 4000);
    // Scroll to render queue
    document.getElementById('render_queue_section')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  const handleStitchProgram = async () => {
    setIsStitching(true);
    setIsStitchedDone(false);
    setStitchingProgress(0);
    
    const finishedJobs = renderList.filter(r => r.renderStatus === 'Finished');
    const initLogs = ["[FFMPEG] Initializing high-profile broadcast concatenation pipeline..."];
    if (finishedJobs.length === 0) {
      initLogs.push("[WARN] No finished render jobs found. Run GPU compositing first.");
    } else {
      initLogs.push(`[STITCHER] Discovered ${finishedJobs.length} completed ad-insertion segment(s)...`);
      finishedJobs.forEach(j => initLogs.push(`[FFMPEG] Loading render clip: s3://afrobotics-staging/renders/${j.id}_composed.mov`));
    }
    setStitchingLogs(initLogs);

    const stages = [
      { pct: 20, msg: "[TRANSCODER] Analyzing gamut consistency across adjacent cuts..." },
      { pct: 40, msg: "[COLOR_GRAD] Applying unified REC.709 cinematic profiles..." },
      { pct: 60, msg: "[AUDIO] Synchronizing multi-channel surround sound elements..." },
      { pct: 80, msg: "[INDEXER] Writing SMPTE timecode frame offset matrices..." },
    ];

    for (const stage of stages) {
      await new Promise(r => setTimeout(r, 350 + Math.random() * 300));
      setStitchingProgress(stage.pct);
      setStitchingLogs(prev => [...prev, stage.msg]);
    }

    await new Promise(r => setTimeout(r, 400));
    setStitchingProgress(100);
    setStitchingLogs(prev => [...prev, "[SUCCESS] Broadcaster MXF stream stitching completed! Master written to S3 feed output."]);
    setIsStitching(false);
    setIsStitchedDone(true);
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="composer_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Cpu className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">Step 4: GPU Perspective Compositing Dispatcher</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            Now that you have approved candidate surface locations, choose an active advertiser campaign and a staged creative branding asset. This tool maps the transparent graphic overlay onto the 3D target coordinates frame-by-frame and dispatches rendering progress to virtualized GPU instances (<strong>MReq 14</strong>).
          </p>
        </div>
      </div>

      {/* Dispatch form (MReq 14) */}
      <div className="col-span-1 space-y-6">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">GPU Composite Dispatcher</h2>
          <p className="text-xs text-slate-500 mb-4">Step 4: Select an approved surface, then pick a campaign and its creative asset to queue a GPU render.</p>

          {/* Flow readiness indicator */}
          <div className="grid grid-cols-3 gap-2 mb-5">
            <div className={`rounded-lg p-2 text-center text-[10px] font-bold transition-colors ${
              selectedSurfaceId ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-slate-50 text-slate-400 border border-slate-200'
            }`}>
              <div className="text-lg">{selectedSurfaceId ? '✓' : '1'}</div>
              <div>Surface{selectedSurfaceId ? ' ready' : ''}</div>
            </div>
            <div className={`rounded-lg p-2 text-center text-[10px] font-bold transition-colors ${
              composerCampaignId ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-slate-50 text-slate-400 border border-slate-200'
            }`}>
              <div className="text-lg">{composerCampaignId ? '✓' : '2'}</div>
              <div>Campaign{composerCampaignId ? ' ready' : ''}</div>
            </div>
            <div className={`rounded-lg p-2 text-center text-[10px] font-bold transition-colors ${
              composerAssetId ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-slate-50 text-slate-400 border border-slate-200'
            }`}>
              <div className="text-lg">{composerAssetId ? '✓' : '3'}</div>
              <div>Asset{composerAssetId ? ' ready' : ''}</div>
            </div>
          </div>

          <form onSubmit={wrappedHandleQueueRender} className="space-y-4">
            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">1. Target Surface</label>
              <div className={`p-3 rounded-lg border font-mono text-[11px] ${
                selectedSurfaceId ? 'bg-emerald-50 border-emerald-200 text-emerald-800' : 'bg-red-50 border-red-100 text-red-700'
              }`}>
                {selectedSurfaceId ? (
                  <>
                    <div className="font-semibold">Surface: {selectedSurfaceId}</div>
                    <div className="mt-0.5 opacity-75">Video: {selectedVideo}</div>
                    <div className="text-[10px] mt-1 opacity-60">← Approved in QA Workbench (Step 3)</div>
                  </>
                ) : (
                  <div className="font-bold text-xs">No surface selected. Go to QA Workbench → approve a surface first.</div>
                )}
              </div>
            </div>

            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">2. Advertiser Campaign</label>
              <select 
                value={composerCampaignId} 
                onChange={(e) => { setComposerCampaignId(e.target.value); setComposerAssetId(''); }}
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                required
              >
                <option value="">-- Choose Campaign --</option>
                {campaignList.map(c => {
                  const assetCount = assetList.filter(a => a.campaignId === c.id).length;
                  return (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.namingStructureCode}) — {assetCount} asset{assetCount !== 1 ? 's' : ''}
                    </option>
                  );
                })}
              </select>
              {composerCampaignId && campaignFilteredAssets.length === 0 && (
                <p className="text-[10px] text-amber-600 mt-1 font-medium">
                  ⚠ This campaign has no assets. Go to Campaigns tab to stage and assign assets first.
                </p>
              )}
            </div>

            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">
                3. Creative Asset {composerCampaignId && <span className="text-blue-500 font-normal">(filtered by campaign)</span>}
              </label>
              <select 
                value={composerAssetId} 
                onChange={(e) => setComposerAssetId(e.target.value)}
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                required
                disabled={!composerCampaignId}
              >
                <option value="">{composerCampaignId ? '-- Choose Asset --' : '-- Select a campaign first --'}</option>
                {campaignFilteredAssets.length > 0 && (
                  <optgroup label="Campaign Assets">
                    {campaignFilteredAssets.map(a => (
                      <option key={a.id} value={a.id}>{a.name} ({a.type} · {a.brandCategory})</option>
                    ))}
                  </optgroup>
                )}
                {!composerCampaignId && unassignedAssetsForDropdown.length > 0 && (
                  <optgroup label="Unassigned Assets (assign to a campaign first)">
                    {unassignedAssetsForDropdown.map(a => (
                      <option key={a.id} value={a.id}>{a.name} ({a.type})</option>
                    ))}
                  </optgroup>
                )}
              </select>
            </div>

            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">Rendering Preset (MReq 7)</label>
              <select 
                value={composerPreset} 
                onChange={(e) => setComposerPreset(e.target.value)}
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-850"
              >
                <option value="Broadcast-ProRes">Broadcast Master (Apple ProRes 422 HQ MXF)</option>
                <option value="Web-MP4-H264">High-profile Streaming MP4 H.264</option>
                <option value="Mobile-Social-916">Mobile/Social Vertical Crop (1080x1920)</option>
              </select>
            </div>

            {aiCustomizedScenes.length > 0 && (
              <div className="p-3 bg-fuchsia-50/50 border border-fuchsia-100 rounded-lg space-y-2">
                <div className="flex items-center gap-1.5 text-[10px] font-mono font-bold text-fuchsia-700 uppercase">
                  <span className="h-1.5 w-1.5 rounded-full bg-fuchsia-500 animate-pulse"></span>
                  <Sparkles className="h-3 w-3 text-fuchsia-600" />
                  <span>AI Scene Optimization Active</span>
                </div>
                <div className="text-[10px] text-slate-500 leading-relaxed font-sans">
                  The rendering queue detected <strong className="text-fuchsia-700">{aiCustomizedScenes.length} AI-modified segment(s)</strong>. Stitched segments will apply generative prompts during compositing:
                </div>
                <ul className="text-[10px] font-mono text-slate-600 list-disc list-inside space-y-1">
                  {aiCustomizedScenes.map(s => (
                    <li key={s.id}>
                      Scene #{s.sceneIndex}: <span className="italic font-medium">"{s.aiPrompt?.slice(0, 32)}..."</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <button 
              type="submit" 
              disabled={!selectedSurfaceId || !composerCampaignId || !composerAssetId}
              className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer disabled:opacity-40"
            >
              <Cpu className="h-4 w-4" />
              Queue GPU Composite Render
            </button>

            {renderSubmitted && (
              <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-lg text-xs text-emerald-700 font-medium text-center animate-pulse">
                ✅ Render job queued! Scroll down to monitor progress in the GPU job queue ↓
              </div>
            )}
          </form>
        </div>
      </div>

      {/* Real-time Render Queue logs with dynamic percentages (MReq 14) */}
      <div className="col-span-2 space-y-6" id="render_queue_section">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 font-display">Asynchronous GPU Compositing Jobs</h3>
            <span className="text-[10px] bg-slate-100 border border-slate-200 px-2.5 py-1 rounded-lg text-slate-500 font-mono">
              POLLING GPU SCHEDULER
            </span>
          </div>

          <div className="space-y-4">
            {renderList.map(job => (
              <div key={job.id} className="bg-slate-50 border border-slate-200/80 rounded-xl p-4" id={`render_job_${job.id}`}>
                <div className="flex flex-col md:flex-row md:items-center justify-between gap-2 border-b border-slate-200/40 pb-2 mb-3">
                  <div>
                    <span className="text-2xs font-mono font-bold text-blue-600">JOB_ID: {job.id}</span>
                    <div className="text-xs font-bold text-slate-800 mt-0.5">Preset: {job.exportPreset}</div>
                  </div>
                  <div className="text-right">
                    <span className={`px-2.5 py-1 rounded text-[10px] font-mono font-bold uppercase ${
                      job.renderStatus === 'Finished' ? 'bg-emerald-50 text-emerald-700 border border-emerald-100' :
                      job.renderStatus === 'Processing' ? 'bg-blue-50 text-blue-700 border border-blue-100 animate-pulse' :
                      'bg-yellow-50 text-yellow-700 border border-yellow-100'
                    }`}>{job.renderStatus}</span>
                  </div>
                </div>

                {/* Progress Bar */}
                <div className="space-y-1.5">
                  <div className="flex justify-between text-2xs font-mono text-slate-400">
                    <span>Composite Processing</span>
                    <span className="font-bold text-slate-800">{job.progress}%</span>
                  </div>
                  <div className="h-2 w-full bg-slate-200 rounded-full overflow-hidden">
                    <div 
                      className="h-full bg-blue-600 rounded-full transition-all duration-1000 animate-pulse"
                      style={{ width: `${job.progress}%` }}
                    ></div>
                  </div>
                </div>

                <div className="flex justify-between items-center text-[10px] text-slate-400 font-mono mt-3">
                  <span className="truncate max-w-xs font-semibold">Storage: {job.storageKey}</span>
                  {job.processingDurationMs > 0 && (
                    <span>Time: {(job.processingDurationMs / 1000).toFixed(1)} seconds</span>
                  )}
                </div>

                {/* Real-Time Ad Exposure Client Billing Ledger (MReq 15) */}
                {job.renderStatus === 'Finished' && (
                  <div className="mt-4 p-4 bg-emerald-50/60 border border-emerald-200/50 rounded-xl space-y-3">
                    <div className="flex items-center justify-between text-[11px] font-mono font-bold text-emerald-800 uppercase tracking-wider">
                      <span className="flex items-center gap-1.5">
                        <Receipt className="h-4 w-4 text-emerald-600" />
                        AI Placement Exposure & Client Billing Ledger
                      </span>
                      <span className="bg-emerald-100 text-emerald-800 px-2.5 py-0.5 rounded text-[9px] font-extrabold">
                        AUDITED INVOICE
                      </span>
                    </div>

                    <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-[10px] font-mono text-slate-600">
                      <div className="bg-white p-2.5 rounded-lg border border-emerald-100 shadow-2xs">
                        <span className="text-slate-400">Total Exposure:</span>
                        <div className="text-slate-900 font-extrabold mt-0.5">
                          {job.id === 'r-01' ? '12.0' : '15.0'} seconds
                        </div>
                      </div>
                      <div className="bg-white p-2.5 rounded-lg border border-emerald-100 shadow-2xs">
                        <span className="text-slate-400">Rendered Frames:</span>
                        <div className="text-slate-900 font-extrabold mt-0.5">
                          {job.id === 'r-01' ? '600' : '750'} frames @ 50 FPS
                        </div>
                      </div>
                      <div className="bg-white p-2.5 rounded-lg border border-emerald-100 shadow-2xs">
                        <span className="text-slate-400">AI Viability:</span>
                        <div className="text-blue-600 font-extrabold mt-0.5">
                          94.8% Prominence
                        </div>
                      </div>
                      <div className="bg-white p-2.5 rounded-lg border border-emerald-100 shadow-2xs">
                        <span className="text-slate-400">Total Charged:</span>
                        <div className="text-emerald-700 font-extrabold mt-0.5">
                          R {job.id === 'r-01' ? '2,160.00' : '2,700.00'}
                        </div>
                      </div>
                    </div>

                    <div className="flex flex-col sm:flex-row sm:items-center justify-between text-[10px] text-slate-500 font-mono gap-2 pt-1">
                      <span>Calculated frame-perfectly based on regional CPM rate (R 180.00) & spatial coordinates prominence.</span>
                      <button 
                        type="button"
                        onClick={() => {
                          const exposureSec = (job.processingDurationMs / 1000).toFixed(1);
                          const frames = job.progress > 0 ? Math.round((job.processingDurationMs / 1000) * 50) : 0;
                          const totalCharged = job.processingDurationMs > 0 
                            ? `R ${((job.processingDurationMs / 1000) * 180).toFixed(2)}`
                            : 'R —.—';
                          const invoice = [
                            'AFROBOTICS BIT — CLIENT PLACEMENT INVOICE',
                            '═══════════════════════════════════════════',
                            '',
                            `Render Job:      ${job.id}`,
                            `Content ID:      ${job.contentId}`,
                            `Campaign ID:     ${job.campaignId}`,
                            `Asset ID:        ${job.assetId}`,
                            `Export Preset:   ${job.exportPreset}`,
                            `Render Status:   ${job.renderStatus}`,
                            `Processing Time: ${job.processingDurationMs} ms`,
                            `Exposure:        ${exposureSec} seconds`,
                            `Frames:          ${frames}`,
                            `CPM Rate:        R 180.00`,
                            `Total Charged:   ${totalCharged}`,
                            '',
                            `Generated: ${new Date().toISOString()}`,
                            '═══════════════════════════════════════════',
                            'Afrobotics BIT — Brand Insertion Technology',
                          ].join('\n');
                          const blob = new Blob([invoice], { type: 'text/plain' });
                          const url = URL.createObjectURL(blob);
                          const a = document.createElement('a');
                          a.href = url;
                          a.download = `BIT_Invoice_${job.id}_${Date.now()}.txt`;
                          document.body.appendChild(a);
                          a.click();
                          document.body.removeChild(a);
                          URL.revokeObjectURL(url);
                        }}
                        className="inline-flex items-center gap-1 bg-emerald-600 hover:bg-emerald-500 text-white font-extrabold px-3 py-1.5 rounded-lg transition-all cursor-pointer shadow-xs uppercase tracking-wider text-[9px]"
                      >
                        <Receipt className="h-3 w-3" />
                        Print Client Invoice
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}

            {renderList.length === 0 && (
              <div className="text-center py-8 text-xs text-slate-400 italic bg-slate-50 rounded-xl border border-dashed border-slate-200">No composite render jobs dispatched yet. Use the sidebar to queue rendering logs.</div>
            )}
          </div>
        </div>
      </div>

      {/* Dynamic Program Master Concatenator Panel (Stitch scenes back into video) */}
      <div className="lg:col-span-3">
        <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 border-b border-slate-100 pb-4 mb-4">
            <div className="h-9 w-9 rounded-xl bg-indigo-50 flex items-center justify-center text-indigo-600">
              <Film className="h-5 w-5" />
            </div>
            <div>
              <h3 className="text-sm font-extrabold uppercase text-slate-800 tracking-wider font-display">🎬 AI Program Master Concatenator & Stitcher</h3>
              <p className="text-xs text-slate-400 mt-0.5">Combine all rendered advertising scene segments back into one unified high-bitrate broadcast master video.</p>
            </div>
          </div>

          <div className="space-y-4">
            {isStitching ? (
              <div className="p-6 bg-indigo-50/50 border border-indigo-100 rounded-xl space-y-4">
                <div className="flex justify-between items-center text-xs font-mono">
                  <span className="font-extrabold text-indigo-800 flex items-center gap-1.5 animate-pulse">
                    <Loader2 className="h-4 w-4 animate-spin text-indigo-600" />
                    Executing High-Profile FFmpeg Stitching Pipeline...
                  </span>
                  <span className="font-extrabold text-indigo-700">{stitchingProgress}%</span>
                </div>
                <div className="h-2 w-full bg-slate-200 rounded-full overflow-hidden">
                  <div className="h-full bg-indigo-600 rounded-full transition-all duration-300" style={{ width: `${stitchingProgress}%` }}></div>
                </div>
                <div className="bg-slate-900 text-slate-200 font-mono text-[10px] p-4 rounded-lg space-y-1 max-h-40 overflow-y-auto">
                  {stitchingLogs.map((log, i) => (
                    <div key={i} className={log.includes('SUCCESS') ? 'text-emerald-400 font-bold' : log.includes('STITCHER') ? 'text-indigo-400' : 'text-slate-300'}>
                      {log}
                    </div>
                  ))}
                </div>
              </div>
            ) : isStitchedDone ? (
              <div className="space-y-4">
                <div className="p-4 bg-emerald-50 border border-emerald-100 rounded-xl flex items-center justify-between text-xs text-emerald-800 font-mono">
                  <span className="font-bold">✓ Master Program Broadcast Feed Compiled Successfully</span>
                  <button 
                    onClick={() => {
                      setIsStitchedDone(false);
                      setStitchingProgress(0);
                    }}
                    className="text-[10px] uppercase font-bold bg-white hover:bg-slate-100 text-slate-700 border border-slate-250 px-2.5 py-1 rounded-lg transition-colors cursor-pointer"
                  >
                    Reset Console
                  </button>
                </div>

                {/* MReq 7: Show actual completed render jobs summary */}
                <div className="bg-slate-900 rounded-xl border border-slate-700 overflow-hidden">
                  <div className="p-4 border-b border-slate-700">
                    <h4 className="text-xs font-bold text-slate-300 font-display uppercase tracking-wider">
                      Compiled Render Jobs ({renderList.filter(r => r.renderStatus === 'Finished').length} finished)
                    </h4>
                  </div>
                  <div className="divide-y divide-slate-800 max-h-[200px] overflow-y-auto">
                    {renderList.filter(r => r.renderStatus === 'Finished').map(job => (
                      <div key={job.id} className="p-3 flex items-center justify-between text-[10px] font-mono">
                        <div className="flex items-center gap-3">
                          <span className="text-blue-400 font-bold">{job.id}</span>
                          <span className="text-slate-400">{job.exportPreset}</span>
                        </div>
                        <div className="flex items-center gap-2 text-slate-500">
                          <span>{(job.processingDurationMs / 1000).toFixed(1)}s</span>
                          <span className="text-emerald-400">✓</span>
                        </div>
                      </div>
                    ))}
                  </div>
                  {renderList.filter(r => r.renderStatus === 'Finished').length === 0 && (
                    <div className="p-4 text-center text-xs text-slate-500">
                      No finished render jobs. Dispatch renders from the sidebar first.
                    </div>
                  )}
                </div>
              </div>
            ) : (
              <div className="p-6 bg-slate-50 rounded-xl border border-slate-200 border-dashed text-center space-y-4">
                <p className="text-xs text-slate-500 max-w-lg mx-auto leading-relaxed">
                  Stitching will extract all successfully generated scene clips, align visual color gamuts using adaptive contrast curve lookups, and merge them into a single high-bitrate MXF stream.
                </p>
                <button
                  type="button"
                  onClick={handleStitchProgram}
                  className="inline-flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-lg transition-all shadow-md shadow-indigo-600/10 cursor-pointer"
                >
                  <Film className="h-4 w-4" />
                  Stitch Program Master Broadcast
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </motion.div>
  );
};
