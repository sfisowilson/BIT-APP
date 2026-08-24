import React from 'react';
import { motion } from 'motion/react';
import {
  Package, Film, Tv, Cpu, FileText, DollarSign, MapPin,
  Calendar, Plus, ArrowRight, Play, Download, X
} from 'lucide-react';
import { CampaignItem, CreativeAsset, RenderItem, ContentItem } from '../types';
import { PipelineProgress, computePipelineSteps } from './PipelineProgress';
import { SidebarView } from './CampaignSidebar';

interface CampaignDashboardProps {
  campaign: CampaignItem;
  assets: CreativeAsset[];
  contentList: ContentItem[];
  renders: RenderItem[];
  hasApprovedPlacements?: boolean;
  onNavigate: (view: SidebarView) => void;
}

export const CampaignDashboard: React.FC<CampaignDashboardProps> = ({
  campaign,
  assets,
  contentList,
  renders,
  hasApprovedPlacements = false,
  onNavigate,
}) => {
  const hasAssets = assets.length > 0;
  const hasContent = contentList.some(v => v.ingestionStatus === 'Completed');
  const hasRenders = renders.length > 0;
  const pipelineSteps = computePipelineSteps(hasAssets, hasContent, hasApprovedPlacements, hasRenders);

  // A render only has a real, playable file once it's actually finished compositing — earlier
  // statuses carry a placeholder storageKey (an s3:// key that predates the render ever running).
  const [watchingRenderId, setWatchingRenderId] = React.useState<string | null>(null);
  const hasPlayableFile = (r: RenderItem) =>
    (r.renderStatus === 'Finished' || r.renderStatus === 'NeedsReview') && r.storageKey.startsWith('/api/');

  const quickActions = [
    { view: 'assets' as SidebarView, icon: Package, label: 'Add Asset', desc: 'Stage brand overlays' },
    { view: 'content' as SidebarView, icon: Film, label: 'Ingest Video', desc: 'Upload & detect scenes' },
    { view: 'placements' as SidebarView, icon: Tv, label: 'Review Placements', desc: 'QA workbench' },
    { view: 'renders' as SidebarView, icon: Cpu, label: 'Queue Render', desc: 'GPU compositing' },
  ];

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className="space-y-6"
    >
      {/* Campaign Header */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm">
        <div className="flex items-start justify-between">
          <div>
            <div className="flex items-center gap-2 mb-1">
              <span className={`px-2 py-0.5 rounded text-[9px] font-bold font-mono uppercase ${
                campaign.status === 'Active' ? 'bg-emerald-50 text-emerald-600' :
                campaign.status === 'Draft' ? 'bg-brand-50 text-brand-600' : 'bg-slate-100 text-slate-500'
              }`}>{campaign.status}</span>
              <span className="text-[10px] text-slate-400 font-mono">{campaign.namingStructureCode}</span>
            </div>
            <h2 className="text-xl font-extrabold text-slate-900 font-display">{campaign.name}</h2>
          </div>
        </div>

        {/* Pipeline Progress */}
        <div className="mt-5 pt-4 border-t border-slate-100">
          <PipelineProgress steps={pipelineSteps} />
        </div>

        {/* Stats Grid */}
        <div className="grid grid-cols-4 gap-3 mt-5">
          {[
            { icon: DollarSign, label: 'Budget', value: `R${campaign.totalBudget.toLocaleString()}` },
            { icon: MapPin, label: 'Region', value: campaign.targetRegion },
            { icon: Package, label: 'Assets', value: `${assets.length} staged` },
            { icon: Cpu, label: 'Renders', value: `${renders.length} jobs` },
          ].map((stat, idx) => {
            const Icon = stat.icon;
            return (
              <div key={idx} className="bg-slate-50 rounded-xl p-3 text-center">
                <Icon className="h-4 w-4 text-slate-400 mx-auto mb-1" />
                <div className="text-[10px] text-slate-500 font-mono">{stat.label}</div>
                <div className="text-sm font-bold text-slate-800 mt-0.5 truncate" title={String(stat.value)}>{stat.value}</div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Quick Actions */}
      <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm">
        <h3 className="text-sm font-bold text-slate-800 font-display mb-4">Quick Actions</h3>
        <div className="grid grid-cols-2 gap-3">
          {quickActions.map(action => {
            const Icon = action.icon;
            return (
              <button
                key={action.view}
                onClick={() => onNavigate(action.view)}
                className="flex items-center gap-3 p-4 bg-slate-50 hover:bg-brand-50 border border-slate-200 hover:border-brand-200 rounded-xl transition-all cursor-pointer text-left group"
              >
                <div className="h-9 w-9 rounded-lg bg-white border border-slate-200 flex items-center justify-center group-hover:border-brand-200 transition-colors">
                  <Icon className="h-4.5 w-4.5 text-slate-500 group-hover:text-brand-600 transition-colors" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-bold text-slate-800">{action.label}</div>
                  <div className="text-[10px] text-slate-400">{action.desc}</div>
                </div>
                <ArrowRight className="h-4 w-4 text-slate-300 group-hover:text-brand-500 transition-colors" />
              </button>
            );
          })}
        </div>
      </div>

      {/* Recent Renders */}
      {renders.length > 0 && (
        <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold text-slate-800 font-display mb-3">Recent Renders</h3>
          <div className="space-y-2">
            {renders.slice(0, 3).map(r => {
              const playable = hasPlayableFile(r);
              const isWatching = watchingRenderId === r.id;
              return (
                <div key={r.id} className="bg-slate-50 rounded-lg overflow-hidden">
                  <div className="flex items-center justify-between p-3 text-xs">
                    <div className="flex items-center gap-3 min-w-0">
                      <span className="font-mono font-bold text-brand-600 truncate" title={r.id}>{r.id}</span>
                      <span className="text-slate-500 shrink-0">{r.exportPreset}</span>
                    </div>
                    <div className="flex items-center gap-2 shrink-0">
                      {playable && (
                        <>
                          <button
                            type="button"
                            onClick={() => setWatchingRenderId(isWatching ? null : r.id)}
                            title={isWatching ? 'Close player' : 'Watch this render'}
                            className="inline-flex items-center gap-1 px-2 py-1 rounded text-[10px] font-bold bg-brand-50 text-brand-600 hover:bg-brand-100 cursor-pointer transition-colors"
                          >
                            {isWatching ? <X className="h-3 w-3" /> : <Play className="h-3 w-3" />}
                            {isWatching ? 'Close' : 'Watch'}
                          </button>
                          <a
                            href={r.storageKey}
                            download
                            title="Download this render"
                            className="inline-flex items-center gap-1 px-2 py-1 rounded text-[10px] font-bold bg-emerald-50 text-emerald-600 hover:bg-emerald-100 cursor-pointer transition-colors"
                          >
                            <Download className="h-3 w-3" /> Download
                          </a>
                        </>
                      )}
                      <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                        r.renderStatus === 'Finished' ? 'bg-emerald-50 text-emerald-600' :
                        r.renderStatus === 'Processing' ? 'bg-brand-50 text-brand-600' : 'bg-slate-100 text-slate-500'
                      }`}>{r.renderStatus}</span>
                    </div>
                  </div>
                  {isWatching && (
                    <div className="p-3 pt-0">
                      <video src={r.storageKey} controls autoPlay className="w-full rounded-lg border border-slate-200 bg-black" />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </motion.div>
  );
};
