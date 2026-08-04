import React from 'react';
import { useNavigate } from 'react-router-dom';
import { motion } from 'motion/react';
import { Cpu, Download, Film, Loader2, CheckCircle, XCircle, Clock, RefreshCw, ExternalLink, AlertTriangle } from 'lucide-react';
import type { RenderItem } from '../types';
import { usePaginatedData } from '../hooks/usePaginatedData';
import { Pagination } from './Pagination';
import { fetchPaginated } from '../apiClient';

/** Builds the deep-link back to the Placement/Editor screen this render was kicked off from —
 * consumed by App.tsx's query-param handling for the "placements" view. */
function buildPlacementUrl(r: RenderItem): string {
  const params = new URLSearchParams({ contentId: r.contentId });
  if (r.sceneId) params.set('sceneId', r.sceneId);
  if (r.surfaceId) params.set('surfaceId', r.surfaceId);
  return `/c/${r.campaignId}/placements?${params.toString()}`;
}

interface RendersTabProps {
  campaignId?: string;
  campaignName?: string;
  onRetryRender?: (renderId: string) => Promise<void>;
  userRole?: 'Admin' | 'Editor' | 'Advertiser';
}

export const RendersTab: React.FC<RendersTabProps> = ({ campaignId, campaignName, onRetryRender, userRole }) => {
  const navigate = useNavigate();

  // ── Paginated render queue, scoped to the selected campaign ──
  const {
    data: renders,
    page: rendersPage,
    totalPages: rendersTotalPages,
    totalCount: rendersTotalCount,
    hasPreviousPage: rendersHasPrev,
    hasNextPage: rendersHasNext,
    setPage: setRendersPage,
    setFilters: setRenderFilters,
    refresh: refreshRenders,
  } = usePaginatedData<RenderItem>('/api/renders', { campaignId }, { defaultPageSize: 20 });

  const [statusFilter, setStatusFilter] = React.useState('');
  React.useEffect(() => {
    setRenderFilters({ campaignId, renderStatus: statusFilter || undefined });
  }, [campaignId, statusFilter]);

  // ── Status breakdown — true totals across the whole campaign, not just this page ──
  const [statusCounts, setStatusCounts] = React.useState({ queued: 0, processing: 0, finished: 0, failed: 0 });
  const refreshStatusCounts = React.useCallback(async () => {
    const params = { campaignId, pageSize: 1 };
    const [queued, processing, finished, failed] = await Promise.all([
      fetchPaginated<RenderItem>('/api/renders', { ...params, renderStatus: 'Queued' }),
      fetchPaginated<RenderItem>('/api/renders', { ...params, renderStatus: 'Processing' }),
      fetchPaginated<RenderItem>('/api/renders', { ...params, renderStatus: 'Finished' }),
      fetchPaginated<RenderItem>('/api/renders', { ...params, renderStatus: 'Failed' }),
    ]);
    setStatusCounts({
      queued: queued.totalCount,
      processing: processing.totalCount,
      finished: finished.totalCount,
      failed: failed.totalCount,
    });
  }, [campaignId]);

  React.useEffect(() => { refreshStatusCounts(); }, [refreshStatusCounts]);

  const pendingCount = statusCounts.queued + statusCounts.processing;
  const [retryingId, setRetryingId] = React.useState<string | null>(null);

  return (
    <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -10 }} className="space-y-6" key="renders_tab">
      <div className="bg-white border border-slate-200/95 rounded-2xl p-6 shadow-sm">
        <div className="flex items-center gap-3 mb-4">
          <div className="h-10 w-10 rounded-xl bg-blue-50 flex items-center justify-center">
            <Film className="h-5 w-5 text-blue-600" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-slate-800 font-display">Render Queue</h2>
            <p className="text-xs text-slate-500">
              {campaignName ? `Campaign: ${campaignName} · ` : ''}
              {rendersTotalCount} render{rendersTotalCount !== 1 ? 's' : ''} · {statusCounts.finished} completed · {pendingCount} in progress
            </p>
          </div>
        </div>

        {/* Stats — clickable shortcuts into the status filter */}
        <div className="grid grid-cols-3 gap-3 mb-4">
          <button
            type="button"
            onClick={() => setStatusFilter(statusFilter === 'Processing' ? '' : 'Processing')}
            className={`bg-amber-50 border rounded-xl p-3 text-center cursor-pointer transition-all ${statusFilter === 'Processing' ? 'border-amber-400 ring-1 ring-amber-300' : 'border-amber-200 hover:border-amber-300'}`}
          >
            <div className="text-2xl font-bold text-amber-600">{pendingCount}</div>
            <div className="text-[10px] font-mono font-bold text-amber-500 uppercase">Processing</div>
          </button>
          <button
            type="button"
            onClick={() => setStatusFilter(statusFilter === 'Finished' ? '' : 'Finished')}
            className={`bg-emerald-50 border rounded-xl p-3 text-center cursor-pointer transition-all ${statusFilter === 'Finished' ? 'border-emerald-400 ring-1 ring-emerald-300' : 'border-emerald-200 hover:border-emerald-300'}`}
          >
            <div className="text-2xl font-bold text-emerald-600">{statusCounts.finished}</div>
            <div className="text-[10px] font-mono font-bold text-emerald-500 uppercase">Completed</div>
          </button>
          <button
            type="button"
            onClick={() => setStatusFilter(statusFilter === 'Failed' ? '' : 'Failed')}
            className={`bg-red-50 border rounded-xl p-3 text-center cursor-pointer transition-all ${statusFilter === 'Failed' ? 'border-red-400 ring-1 ring-red-300' : 'border-red-200 hover:border-red-300'}`}
          >
            <div className="text-2xl font-bold text-red-500">{statusCounts.failed}</div>
            <div className="text-[10px] font-mono font-bold text-red-400 uppercase">Failed</div>
          </button>
        </div>
        {statusFilter && (
          <div className="mb-4 flex items-center gap-2 text-[10px] text-slate-500 font-mono">
            <span>Filtered to: <strong className="text-slate-700">{statusFilter}</strong></span>
            <button type="button" onClick={() => setStatusFilter('')} className="text-blue-600 hover:text-blue-700 cursor-pointer font-bold">✕ Clear filter</button>
          </div>
        )}

        {renders.length === 0 ? (
          <div className="text-center py-12">
            <Cpu className="h-12 w-12 text-slate-200 mx-auto mb-3" />
            <h3 className="text-sm font-bold text-slate-400">
              {statusFilter ? `No ${statusFilter.toLowerCase()} renders` : 'No renders submitted yet'}
            </h3>
            <p className="text-xs text-slate-400 mt-1">
              {statusFilter ? 'Try clearing the filter.' : 'Go to the Editor tab, place assets on surfaces, approve, and submit for rendering.'}
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {renders.map(r => {
              const isProcessing = r.renderStatus === 'Processing' || r.renderStatus === 'Queued';
              const isFinished = r.renderStatus === 'Finished';
              const isNeedsReview = r.renderStatus === 'NeedsReview';
              const isFailed = r.renderStatus === 'Failed';
              return (
                <div key={r.id} className={`border rounded-xl p-4 transition-all ${isProcessing ? 'border-amber-200 bg-amber-50/30' : isFinished ? 'border-emerald-200 bg-emerald-50/30' : isNeedsReview ? 'border-amber-200 bg-amber-50/30' : 'border-red-200 bg-red-50/30'}`}>
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex items-center gap-2.5">
                      {isProcessing ? <Loader2 className="h-4 w-4 text-amber-500 animate-spin" /> :
                       isFinished ? <CheckCircle className="h-4 w-4 text-emerald-500" /> :
                       isNeedsReview ? <AlertTriangle className="h-4 w-4 text-amber-500" /> :
                       <XCircle className="h-4 w-4 text-red-500" />}
                      <span className="text-sm font-bold text-slate-800">Render #{r.id.slice(0, 8)}</span>
                      <span className={`text-[10px] font-mono px-1.5 py-0.5 rounded ${isProcessing ? 'bg-amber-100 text-amber-700' : isFinished ? 'bg-emerald-100 text-emerald-700' : isNeedsReview ? 'bg-amber-100 text-amber-700' : 'bg-red-100 text-red-700'}`}>
                        {r.renderStatus}
                      </span>
                    </div>
                    <div className="flex items-center gap-2 text-[10px] text-slate-400 font-mono">
                      <Clock className="h-3 w-3" />
                      {r.processingDurationMs ? `${(r.processingDurationMs / 1000).toFixed(1)}s` : '—'}
                    </div>
                  </div>

                  {/* Where this render came from */}
                  <div className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5 text-[10px] text-slate-500 mb-2">
                    {r.contentTitle && (
                      <span className="font-semibold text-slate-600 truncate max-w-[220px]" title={r.contentTitle}>{r.contentTitle}</span>
                    )}
                    {r.sceneIndex != null && <span>· Scene #{r.sceneIndex}</span>}
                    {r.surfaceType ? (
                      <span>· {r.surfaceType}</span>
                    ) : r.promptText ? (
                      <span className="truncate max-w-[240px]" title={r.promptText}>· "{r.promptText}"</span>
                    ) : null}
                    {r.assetName && <span>· {r.assetName}</span>}
                    <a
                      href={buildPlacementUrl(r)}
                      onClick={(e) => { e.preventDefault(); navigate(buildPlacementUrl(r)); }}
                      className="inline-flex items-center gap-0.5 text-blue-600 hover:text-blue-700 font-semibold cursor-pointer"
                    >
                      <ExternalLink className="h-2.5 w-2.5" /> View in Placements
                    </a>
                  </div>

                  {/* Progress bar */}
                  {isProcessing && (
                    <div className="mb-2">
                      <div className="flex justify-between text-[9px] font-mono text-amber-600 mb-0.5">
                        <span>Compositing frames...</span>
                        <span>{r.progress || 0}%</span>
                      </div>
                      <div className="w-full bg-slate-200 rounded-full h-1.5 overflow-hidden">
                        <div className="bg-amber-500 h-full rounded-full transition-all duration-500" style={{ width: `${r.progress || 0}%` }} />
                      </div>
                    </div>
                  )}

                  {/* Actions */}
                  <div className="flex items-center gap-2 mt-2">
                    {(isFinished || isNeedsReview) && r.storageKey && (
                      <a href={r.storageKey} download className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm ${isNeedsReview ? 'bg-amber-600 hover:bg-amber-500' : 'bg-emerald-600 hover:bg-emerald-500'}`}>
                        <Download className="h-3 w-3" /> Download MP4
                      </a>
                    )}
                    {isFailed && (
                      <div className="flex items-center gap-2">
                        <span className="text-[10px] text-red-500 font-medium">Render failed.</span>
                        {onRetryRender && (
                          <button
                            onClick={async () => {
                              setRetryingId(r.id);
                              try {
                                await onRetryRender(r.id);
                                refreshRenders();
                                refreshStatusCounts();
                              } finally {
                                setRetryingId(null);
                              }
                            }}
                            disabled={retryingId === r.id}
                            className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-red-600 hover:bg-red-500 disabled:bg-red-300 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm"
                          >
                            {retryingId === r.id ? (
                              <><Loader2 className="h-3 w-3 animate-spin" /> Retrying...</>
                            ) : (
                              <><RefreshCw className="h-3 w-3" /> Retry Render</>
                            )}
                          </button>
                        )}
                      </div>
                    )}
                    {isFailed && r.lastErrorMessage && userRole === 'Admin' && (
                      <div className="mt-2 p-2.5 bg-red-100 border border-red-200 rounded-lg">
                        <div className="text-[9px] font-mono font-bold text-red-600 uppercase mb-0.5">Failure Reason (admin)</div>
                        <div className="text-[10px] text-red-700 font-mono leading-relaxed break-all">{r.lastErrorMessage}</div>
                      </div>
                    )}
                    {isNeedsReview && r.lastErrorMessage && (
                      <div className="mt-2 p-2.5 bg-amber-100 border border-amber-200 rounded-lg">
                        <div className="text-[9px] font-mono font-bold text-amber-700 uppercase mb-0.5">Why this needs review</div>
                        <div className="text-[10px] text-amber-800 leading-relaxed">{r.lastErrorMessage}</div>
                      </div>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}

        <Pagination
          page={rendersPage}
          totalPages={rendersTotalPages}
          hasPreviousPage={rendersHasPrev}
          hasNextPage={rendersHasNext}
          onPageChange={setRendersPage}
        />
      </div>
    </motion.div>
  );
};
