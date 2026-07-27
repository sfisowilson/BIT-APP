import React from 'react';
import { motion } from 'motion/react';
import { Cpu, Download, Film, Loader2, CheckCircle, XCircle, Clock } from 'lucide-react';
import type { RenderItem } from '../types';

interface RendersTabProps {
  renderList: RenderItem[];
  campaignName?: string;
}

export const RendersTab: React.FC<RendersTabProps> = ({ renderList, campaignName }) => {
  const pending = renderList.filter(r => r.renderStatus === 'Processing' || r.renderStatus === 'Queued');
  const finished = renderList.filter(r => r.renderStatus === 'Finished');
  const failed = renderList.filter(r => r.renderStatus === 'Failed');

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
              {renderList.length} render{renderList.length !== 1 ? 's' : ''} · {finished.length} completed · {pending.length} in progress
            </p>
          </div>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-3 gap-3 mb-6">
          <div className="bg-amber-50 border border-amber-200 rounded-xl p-3 text-center">
            <div className="text-2xl font-bold text-amber-600">{pending.length}</div>
            <div className="text-[10px] font-mono font-bold text-amber-500 uppercase">Processing</div>
          </div>
          <div className="bg-emerald-50 border border-emerald-200 rounded-xl p-3 text-center">
            <div className="text-2xl font-bold text-emerald-600">{finished.length}</div>
            <div className="text-[10px] font-mono font-bold text-emerald-500 uppercase">Completed</div>
          </div>
          <div className="bg-red-50 border border-red-200 rounded-xl p-3 text-center">
            <div className="text-2xl font-bold text-red-500">{failed.length}</div>
            <div className="text-[10px] font-mono font-bold text-red-400 uppercase">Failed</div>
          </div>
        </div>

        {renderList.length === 0 ? (
          <div className="text-center py-12">
            <Cpu className="h-12 w-12 text-slate-200 mx-auto mb-3" />
            <h3 className="text-sm font-bold text-slate-400">No renders submitted yet</h3>
            <p className="text-xs text-slate-400 mt-1">Go to the Editor tab, place assets on surfaces, approve, and submit for rendering.</p>
          </div>
        ) : (
          <div className="space-y-2">
            {renderList.map(r => {
              const isProcessing = r.renderStatus === 'Processing' || r.renderStatus === 'Queued';
              const isFinished = r.renderStatus === 'Finished';
              const isFailed = r.renderStatus === 'Failed';
              return (
                <div key={r.id} className={`border rounded-xl p-4 transition-all ${isProcessing ? 'border-amber-200 bg-amber-50/30' : isFinished ? 'border-emerald-200 bg-emerald-50/30' : 'border-red-200 bg-red-50/30'}`}>
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex items-center gap-2.5">
                      {isProcessing ? <Loader2 className="h-4 w-4 text-amber-500 animate-spin" /> :
                       isFinished ? <CheckCircle className="h-4 w-4 text-emerald-500" /> :
                       <XCircle className="h-4 w-4 text-red-500" />}
                      <span className="text-sm font-bold text-slate-800">Render #{r.id.slice(0, 8)}</span>
                      <span className={`text-[10px] font-mono px-1.5 py-0.5 rounded ${isProcessing ? 'bg-amber-100 text-amber-700' : isFinished ? 'bg-emerald-100 text-emerald-700' : 'bg-red-100 text-red-700'}`}>
                        {r.renderStatus}
                      </span>
                    </div>
                    <div className="flex items-center gap-2 text-[10px] text-slate-400 font-mono">
                      <Clock className="h-3 w-3" />
                      {r.processingDurationMs ? `${(r.processingDurationMs / 1000).toFixed(1)}s` : '—'}
                    </div>
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
                    {isFinished && r.storageKey && (
                      <a href={r.storageKey} download className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-[10px] rounded-lg cursor-pointer transition-all shadow-sm">
                        <Download className="h-3 w-3" /> Download MP4
                      </a>
                    )}
                    {isFailed && (
                      <span className="text-[10px] text-red-500 font-medium">Render failed. Try re-submitting from the Editor tab.</span>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </motion.div>
  );
};
