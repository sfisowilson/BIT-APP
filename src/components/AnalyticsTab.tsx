import React from 'react';
import { motion } from 'motion/react';
import { BarChart3, TrendingUp, Film, Clapperboard, MonitorSmartphone, Activity, AlertTriangle, Clock } from 'lucide-react';

interface AnalyticsTabProps {
  summary: AnalyticsSummary | null;
  loading: boolean;
}

export interface AnalyticsSummary {
  totalContent: number;
  totalScenes: number;
  totalSurfaces: number;
  totalRenders: number;
  totalCampaigns: number;
  activeAlarms: number;
  rendersLast7Days: number;
  contentLast7Days: number;
  avgRenderTimeMs: number;
}

export const AnalyticsTab: React.FC<AnalyticsTabProps> = ({ summary, loading }) => {
  if (loading || !summary) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="animate-spin h-8 w-8 border-2 border-blue-500 border-t-transparent rounded-full" />
      </div>
    );
  }

  const cards = [
    { label: 'Total Content', value: summary.totalContent, icon: Film, color: 'text-blue-600', bg: 'bg-blue-50' },
    { label: 'Scenes Indexed', value: summary.totalScenes, icon: Clapperboard, color: 'text-fuchsia-600', bg: 'bg-fuchsia-50' },
    { label: 'Ad Surfaces', value: summary.totalSurfaces, icon: MonitorSmartphone, color: 'text-amber-600', bg: 'bg-amber-50' },
    { label: 'Renders', value: summary.totalRenders, icon: TrendingUp, color: 'text-emerald-600', bg: 'bg-emerald-50' },
    { label: 'Active Campaigns', value: summary.totalCampaigns, icon: BarChart3, color: 'text-indigo-600', bg: 'bg-indigo-50' },
    { label: 'Active Alarms', value: summary.activeAlarms, icon: AlertTriangle, color: summary.activeAlarms > 0 ? 'text-red-600' : 'text-slate-400', bg: summary.activeAlarms > 0 ? 'bg-red-50' : 'bg-slate-50' },
    { label: 'Content (7d)', value: summary.contentLast7Days, icon: Activity, color: 'text-cyan-600', bg: 'bg-cyan-50' },
    { label: 'Renders (7d)', value: summary.rendersLast7Days, icon: TrendingUp, color: 'text-teal-600', bg: 'bg-teal-50' },
  ];

  const avgRenderSec = (summary.avgRenderTimeMs / 1000).toFixed(1);

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className="space-y-6"
      key="analytics_tab"
    >
      <div className="bg-gradient-to-r from-blue-600 to-indigo-700 rounded-2xl p-6 text-white shadow-lg">
        <h2 className="text-lg font-bold">Platform Analytics</h2>
        <p className="text-sm text-blue-100 mt-1">Real-time operational summary across all subsystems</p>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {cards.map(card => (
          <div key={card.label} className={`${card.bg} rounded-xl p-4 border border-slate-100 shadow-sm`}>
            <div className="flex items-center gap-2 mb-2">
              <card.icon className={`h-4 w-4 ${card.color}`} />
              <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider">{card.label}</span>
            </div>
            <div className={`text-2xl font-black ${card.color}`}>{card.value.toLocaleString()}</div>
          </div>
        ))}
      </div>

      {/* Avg render time card */}
      <div className="bg-white rounded-xl p-5 border border-slate-200 shadow-sm">
        <div className="flex items-center gap-2 mb-1">
          <Clock className="h-4 w-4 text-slate-400" />
          <span className="text-[10px] font-mono text-slate-500 uppercase tracking-wider">Average Render Time</span>
        </div>
        <div className="text-2xl font-black text-slate-800">{avgRenderSec}s</div>
        <div className="text-xs text-slate-400 mt-1">Across all completed render jobs</div>
      </div>
    </motion.div>
  );
};
