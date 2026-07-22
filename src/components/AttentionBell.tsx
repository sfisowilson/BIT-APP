import React, { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Bell, AlertTriangle, UserPlus, MonitorSmartphone, Cpu, Film, ShieldAlert } from 'lucide-react';
import { fetchWithAuth } from '../apiClient';

interface AttentionCounts {
  totalAttention: number;
  pendingRoleRequests: number;
  pendingSurfaces: number;
  failedRenders: number;
  failedContent: number;
  activeAlarms: number;
}

export const AttentionBell: React.FC = () => {
  const [counts, setCounts] = useState<AttentionCounts | null>(null);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    fetchCounts();
    const interval = setInterval(fetchCounts, 30000); // poll every 30s
    return () => clearInterval(interval);
  }, []);

  // Close on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const fetchCounts = async () => {
    try {
      const res = await fetchWithAuth('/api/notifications/attention');
      setCounts(await res.json());
    } catch { /* silent */ }
  };

  const total = counts?.totalAttention ?? 0;
  const hasAttention = total > 0;

  const items = [
    { key: 'pendingRoleRequests', label: 'Pending Role Requests', icon: UserPlus, color: 'text-amber-600', bg: 'bg-amber-50', path: '/admin' },
    { key: 'pendingSurfaces', label: 'Surfaces Awaiting Review', icon: MonitorSmartphone, color: 'text-blue-600', bg: 'bg-blue-50', path: null },
    { key: 'failedRenders', label: 'Failed Render Jobs', icon: Cpu, color: 'text-red-600', bg: 'bg-red-50', path: null },
    { key: 'failedContent', label: 'Failed Content Ingestions', icon: Film, color: 'text-red-600', bg: 'bg-red-50', path: null },
    { key: 'activeAlarms', label: 'Active Platform Alarms', icon: AlertTriangle, color: 'text-orange-600', bg: 'bg-orange-50', path: '/telemetry' },
  ] as const;

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(!open)}
        className={`relative p-2 rounded-lg cursor-pointer transition-all border ${
          hasAttention
            ? 'text-amber-500 hover:text-amber-600 bg-amber-50 border-amber-200 animate-pulse'
            : 'text-slate-400 hover:text-slate-600 hover:bg-slate-100 border-slate-200'
        }`}
        title={hasAttention ? `${total} item${total !== 1 ? 's' : ''} need attention` : 'No items need attention'}
      >
        <Bell className="h-4 w-4" />
        {hasAttention && (
          <span className="absolute -top-1 -right-1 h-4 min-w-[16px] px-1 flex items-center justify-center rounded-full bg-red-500 text-white text-[8px] font-bold leading-none">
            {total > 99 ? '99+' : total}
          </span>
        )}
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, y: -8, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -8, scale: 0.95 }}
            className="absolute right-0 top-full mt-2 w-72 bg-white border border-slate-200 rounded-xl shadow-xl z-50 overflow-hidden"
          >
            <div className="p-3 border-b border-slate-100 bg-slate-50/50">
              <h4 className="text-xs font-bold text-slate-600 uppercase tracking-wider">Needs Attention</h4>
            </div>
            {total === 0 ? (
              <div className="p-6 text-center text-xs text-slate-400">
                <ShieldAlert className="h-6 w-6 text-emerald-400 mx-auto mb-2" />
                All clear — nothing needs attention
              </div>
            ) : (
              <div className="max-h-[320px] overflow-y-auto">
                {items.map(item => {
                  const count = counts?.[item.key] ?? 0;
                  if (count === 0) return null;
                  return (
                    <div
                      key={item.key}
                      className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50 cursor-pointer border-b border-slate-50 last:border-0 transition-colors"
                    >
                      <div className={`p-1.5 rounded-lg ${item.bg}`}>
                        <item.icon className={`h-3.5 w-3.5 ${item.color}`} />
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className="text-xs font-medium text-slate-700">{item.label}</p>
                      </div>
                      <span className={`text-xs font-bold px-2 py-0.5 rounded-full ${item.bg} ${item.color}`}>
                        {count}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
            <div className="p-2 border-t border-slate-100 bg-slate-50/30">
              <p className="text-[9px] text-slate-400 text-center">Refreshes every 30 seconds</p>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};
