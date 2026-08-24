import React from 'react';
import { motion } from 'motion/react';
import { FileText } from 'lucide-react';
import { CampaignItem, ContentItem, RenderItem, CreativeAsset } from '../types';
import { fetchWithAuth } from '../apiClient';

interface CampaignReportsViewProps {
  campaign: CampaignItem | undefined;
  contentList: ContentItem[];
  renderList: RenderItem[];
  assetList: CreativeAsset[];
}

const statusBadgeClass = (status: string) =>
  status === 'Finished' ? 'bg-emerald-100 text-emerald-700' :
  status === 'Failed' || status === 'Rejected' ? 'bg-red-100 text-red-700' :
  status === 'PreviewReady' || status === 'NeedsReview' ? 'bg-amber-100 text-amber-700' :
  'bg-brand-100 text-brand-700';

/** Best-effort format label from a storage URL's file extension — there's no persisted
 * codec/container field on ContentItem or RenderItem, so this is the only format info
 * available without a new ffprobe-on-demand backend call. */
const formatFromUrl = (url: string | null | undefined): string => {
  if (!url) return '—';
  const match = url.split('?')[0].match(/\.([a-zA-Z0-9]+)$/);
  return match ? match[1].toUpperCase() : '—';
};

const formatBytes = (bytes: number): string => {
  if (bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex++;
  }
  return `${value.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
};

export const CampaignReportsView: React.FC<CampaignReportsViewProps> = ({
  campaign, contentList, renderList, assetList,
}) => {
  // Neither ContentItem nor RenderItem persists a file size — the actual files are real,
  // already-served-with-Content-Length endpoints though, so a HEAD request gives an accurate
  // size without needing a backend/schema change. Cached by URL so switching between scenes/
  // re-renders doesn't refetch what's already known.
  const [sizes, setSizes] = React.useState<Record<string, number | null>>({});

  const urlsToSize = React.useMemo(() => {
    const urls = new Set<string>();
    contentList.forEach(v => { if (v.storageKey) urls.add(v.storageKey); });
    renderList.forEach(r => { const u = r.storageKey || r.previewStorageKey; if (u) urls.add(u); });
    return Array.from(urls);
  }, [contentList, renderList]);

  React.useEffect(() => {
    const pending = urlsToSize.filter(u => !(u in sizes));
    if (pending.length === 0) return;
    let cancelled = false;
    (async () => {
      const results = await Promise.all(pending.map(async (url) => {
        try {
          const r = await fetchWithAuth(url, { method: 'HEAD' });
          const len = r.headers.get('content-length');
          return [url, len ? parseInt(len, 10) : null] as const;
        } catch {
          return [url, null] as const;
        }
      }));
      if (cancelled) return;
      setSizes(prev => {
        const next = { ...prev };
        for (const [url, size] of results) next[url] = size;
        return next;
      });
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlsToSize]);

  const sizeLabel = (url: string | null | undefined): string => {
    if (!url) return '—';
    const size = sizes[url];
    if (size === undefined) return '…';
    if (size === null) return '—';
    return formatBytes(size);
  };

  return (
    <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="max-w-4xl mx-auto py-8 space-y-6" key="reports">
      <div className="text-center">
        <FileText className="h-12 w-12 text-slate-300 mx-auto mb-3" />
        <h3 className="text-lg font-bold text-slate-800 font-display">Campaign Reports</h3>
        <p className="text-sm text-slate-500 mt-2">Video technical details and render history for this campaign.</p>
      </div>
      <div className="p-6 bg-white border border-slate-200 rounded-xl shadow-sm text-left space-y-2">
        <div className="text-xs font-mono text-slate-600 flex justify-between">
          <span>Assets Staged:</span>
          <span className="font-bold">{assetList.length}</span>
        </div>
        <div className="text-xs font-mono text-slate-600 flex justify-between">
          <span>Renders Completed:</span>
          <span className="font-bold">{renderList.filter(r => r.renderStatus === 'Finished').length}</span>
        </div>
        <div className="text-xs font-mono text-slate-600 flex justify-between">
          <span>Total Processing Time:</span>
          <span className="font-bold">{(renderList.reduce((sum, r) => sum + r.processingDurationMs, 0) / 1000).toFixed(1)}s</span>
        </div>
      </div>

      {/* Per-video technical details (input format/size) + every rendered item for that clip
          (output format/size) */}
      {contentList.map(video => {
        const videoRenders = renderList.filter(r => r.contentId === video.id)
          .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        return (
          <div key={video.id} className="bg-white border border-slate-200 rounded-xl shadow-sm p-5 text-left">
            <h4 className="text-sm font-bold text-slate-800 mb-3">{video.title}</h4>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-2 text-[10px] font-mono mb-4">
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Resolution</div><div className="font-bold text-slate-700">{video.resolution || '—'}</div></div>
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Frame Rate</div><div className="font-bold text-slate-700">{video.frameRate ? `${video.frameRate} fps` : '—'}</div></div>
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Duration</div><div className="font-bold text-slate-700">{video.duration || '—'}</div></div>
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Source</div><div className="font-bold text-slate-700">{video.sourceChannel || '—'}</div></div>
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Input Format</div><div className="font-bold text-slate-700">{formatFromUrl(video.storageKey)}</div></div>
              <div className="bg-slate-50 rounded-lg p-2"><div className="text-slate-400">Input Size</div><div className="font-bold text-slate-700">{sizeLabel(video.storageKey)}</div></div>
            </div>
            <div className="text-[10px] uppercase tracking-wider font-bold text-slate-400 font-mono mb-2">
              Rendered Items ({videoRenders.length})
            </div>
            {videoRenders.length === 0 ? (
              <div className="text-xs text-slate-400 italic">No renders yet for this video.</div>
            ) : (
              <div className="space-y-1.5">
                {videoRenders.map(r => {
                  const outputUrl = r.storageKey || r.previewStorageKey;
                  return (
                    <div key={r.id} className="flex flex-wrap items-center gap-2 px-2.5 py-1.5 rounded-lg border border-slate-200 text-[10px]">
                      <span className={`px-1.5 py-0.5 rounded font-bold font-mono shrink-0 ${statusBadgeClass(r.renderStatus)}`}>{r.renderStatus}</span>
                      <span className="text-slate-400 font-mono shrink-0">{r.renderMode || 'Interactive'}</span>
                      {r.sceneIndex != null && <span className="text-slate-400 font-mono shrink-0">Scene #{r.sceneIndex}</span>}
                      <span className="text-slate-400 font-mono shrink-0" title="Export preset (output format)">{r.exportPreset || formatFromUrl(outputUrl)}</span>
                      <span className="text-slate-400 font-mono shrink-0" title="Output file size">{sizeLabel(outputUrl)}</span>
                      <span className="text-slate-400 font-mono shrink-0">{(r.processingDurationMs / 1000).toFixed(1)}s</span>
                      <span className="text-slate-400 font-mono ml-auto shrink-0">{new Date(r.createdAt).toLocaleString()}</span>
                      {outputUrl && (
                        <a href={outputUrl} target="_blank" rel="noreferrer" className="text-brand-500 hover:text-brand-700 font-semibold shrink-0">View</a>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        );
      })}
    </motion.div>
  );
};
