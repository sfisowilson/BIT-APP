import React, { useMemo } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { 
  Package, Film, Tv, Cpu, FileText, Users, Activity, 
  LayoutDashboard, BarChart3, ListOrdered 
} from 'lucide-react';

export type SidebarView = 'dashboard' | 'assets' | 'content' | 'placements' | 'renders' | 'reports' | 'admin' | 'telemetry' | 'analytics' | 'jobs';

interface SidebarItem {
  id: SidebarView;
  label: string;
  icon: React.FC<{ className?: string }>;
  requiresCampaign: boolean;
  role?: 'Admin' | 'Editor' | 'Advertiser';
  /** URL path relative to root, use :campaignId placeholder */
  getPath: (campaignId: string | null) => string;
}

const ALL_ITEMS: SidebarItem[] = [
  { id: 'dashboard',  label: 'Dashboard',   icon: LayoutDashboard, requiresCampaign: true,  getPath: (cid) => `/c/${cid}` },
  { id: 'assets',     label: 'Assets',      icon: Package,         requiresCampaign: true,  getPath: (cid) => `/c/${cid}/assets` },
  { id: 'content',    label: 'Content',     icon: Film,            requiresCampaign: true,  getPath: (cid) => `/c/${cid}/content` },
  { id: 'placements', label: 'Placements',  icon: Tv,              requiresCampaign: true,  getPath: (cid) => `/c/${cid}/placements` },
  { id: 'renders',    label: 'Renders',     icon: Cpu,             requiresCampaign: true,  getPath: (cid) => `/c/${cid}/renders` },
  { id: 'reports',    label: 'Reports',     icon: FileText,        requiresCampaign: true,  getPath: (cid) => `/c/${cid}/reports` },
  { id: 'admin',      label: 'Admin',       icon: Users,           requiresCampaign: false, role: 'Admin', getPath: () => '/admin' },
  { id: 'telemetry',  label: 'Telemetry',   icon: Activity,        requiresCampaign: false, getPath: () => '/telemetry' },
  { id: 'analytics',  label: 'Analytics',   icon: BarChart3,       requiresCampaign: false, getPath: () => '/analytics' },
  { id: 'jobs',       label: 'Jobs',        icon: ListOrdered,     requiresCampaign: false, getPath: () => '/jobs' },
];

interface CampaignSidebarProps {
  selectedCampaignId: string | null;
  userRole: 'Admin' | 'Editor' | 'Advertiser';
  campaignAssetCount: number;
  contentCount: number;
  renderCount: number;
}

/** Derive the current active view from the URL path */
function useActiveView(): SidebarView | null {
  const location = useLocation();
  return useMemo(() => {
    const parts = location.pathname.split('/').filter(Boolean);
    if (parts[0] === 'c' && parts[1]) {
      return (parts[2] as SidebarView) || 'dashboard';
    }
    if (parts[0] === 'admin') return 'admin' as SidebarView;
    if (parts[0] === 'telemetry') return 'telemetry' as SidebarView;
    if (parts[0] === 'analytics') return 'analytics' as SidebarView;
    if (parts[0] === 'jobs') return 'jobs' as SidebarView;
    return null;
  }, [location.pathname]);
}

export const CampaignSidebar: React.FC<CampaignSidebarProps> = ({
  selectedCampaignId,
  userRole,
  campaignAssetCount,
  contentCount,
  renderCount,
}) => {
  const navigate = useNavigate();
  const activeView = useActiveView();
  const campaignSelected = !!selectedCampaignId;

  const campaignItems = ALL_ITEMS.filter(i => i.requiresCampaign);
  const platformItems = ALL_ITEMS.filter(i => !i.requiresCampaign && (!i.role || i.role === userRole));

  const renderItem = (item: SidebarItem, disabled: boolean) => {
    const Icon = item.icon;
    const isActive = activeView === item.id;
    const count = item.id === 'assets' ? campaignAssetCount :
                  item.id === 'content' ? contentCount :
                  item.id === 'renders' ? renderCount : undefined;
    const path = disabled ? '#' : item.getPath(selectedCampaignId);

    return (
      <button
        key={item.id}
        onClick={() => !disabled && navigate(path)}
        disabled={disabled}
        className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer text-left ${
          isActive
            ? 'bg-brand-50 text-brand-700 border-l-2 border-brand-600'
            : disabled
              ? 'text-slate-300 cursor-not-allowed'
              : 'text-slate-600 hover:bg-slate-100'
        }`}
      >
        <Icon className={`h-4 w-4 shrink-0 ${isActive ? 'text-brand-600' : disabled ? 'text-slate-300' : 'text-slate-400'}`} />
        <span className="flex-1">{item.label}</span>
        {count !== undefined && count > 0 && (
          <span className={`text-[10px] font-mono font-bold px-1.5 py-0.5 rounded ${
            isActive ? 'bg-brand-200 text-brand-700' : 'bg-slate-200 text-slate-500'
          }`}>
            {count}
          </span>
        )}
      </button>
    );
  };

  return (
    <aside className="w-[220px] shrink-0 flex flex-col gap-6" id="campaign_sidebar">
      {/* Campaign Workspace */}
      <div>
        <h3 className="text-[9px] font-bold uppercase tracking-widest text-slate-400 mb-2 px-3 font-mono">
          Campaign Workspace
        </h3>
        <div className="space-y-0.5">
          {campaignItems.map(item => renderItem(item, !campaignSelected))}
        </div>
      </div>

      {/* Divider */}
      <div className="border-t border-slate-200" />

      {/* Platform */}
      <div>
        <h3 className="text-[9px] font-bold uppercase tracking-widest text-slate-400 mb-2 px-3 font-mono">
          Platform
        </h3>
        <div className="space-y-0.5">
          {platformItems.map(item => renderItem(item, false))}
        </div>
      </div>
    </aside>
  );
};
