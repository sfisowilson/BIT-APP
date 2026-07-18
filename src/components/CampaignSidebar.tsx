import React from 'react';
import { 
  Package, Film, Tv, Cpu, FileText, Users, Activity, 
  LayoutDashboard 
} from 'lucide-react';

export type SidebarView = 'dashboard' | 'assets' | 'content' | 'placements' | 'renders' | 'reports' | 'admin' | 'telemetry';

interface SidebarItem {
  id: SidebarView;
  label: string;
  icon: React.FC<{ className?: string }>;
  requiresCampaign: boolean;
  role?: 'Admin' | 'Editor' | 'Advertiser';
}

const ALL_ITEMS: SidebarItem[] = [
  { id: 'dashboard', label: 'Dashboard', icon: LayoutDashboard, requiresCampaign: true },
  { id: 'assets', label: 'Assets', icon: Package, requiresCampaign: true },
  { id: 'content', label: 'Content', icon: Film, requiresCampaign: true },
  { id: 'placements', label: 'Placements', icon: Tv, requiresCampaign: true },
  { id: 'renders', label: 'Renders', icon: Cpu, requiresCampaign: true },
  { id: 'reports', label: 'Reports', icon: FileText, requiresCampaign: true },
  { id: 'admin', label: 'Admin', icon: Users, requiresCampaign: false, role: 'Admin' },
  { id: 'telemetry', label: 'Telemetry', icon: Activity, requiresCampaign: false },
];

interface CampaignSidebarProps {
  activeView: SidebarView;
  onNavigate: (view: SidebarView) => void;
  campaignSelected: boolean;
  userRole: 'Admin' | 'Editor' | 'Advertiser';
  campaignAssetCount: number;
  contentCount: number;
  renderCount: number;
}

export const CampaignSidebar: React.FC<CampaignSidebarProps> = ({
  activeView,
  onNavigate,
  campaignSelected,
  userRole,
  campaignAssetCount,
  contentCount,
  renderCount,
}) => {
  const campaignItems = ALL_ITEMS.filter(i => i.requiresCampaign);
  const platformItems = ALL_ITEMS.filter(i => !i.requiresCampaign && (!i.role || i.role === userRole));

  const renderItem = (item: SidebarItem, disabled: boolean) => {
    const Icon = item.icon;
    const isActive = activeView === item.id;
    const count = item.id === 'assets' ? campaignAssetCount :
                  item.id === 'content' ? contentCount :
                  item.id === 'renders' ? renderCount : undefined;

    return (
      <button
        key={item.id}
        onClick={() => !disabled && onNavigate(item.id)}
        disabled={disabled}
        className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-lg text-xs font-semibold transition-all cursor-pointer text-left ${
          isActive
            ? 'bg-blue-50 text-blue-700 border-l-2 border-blue-600'
            : disabled
              ? 'text-slate-300 cursor-not-allowed'
              : 'text-slate-600 hover:bg-slate-100'
        }`}
      >
        <Icon className={`h-4 w-4 shrink-0 ${isActive ? 'text-blue-600' : disabled ? 'text-slate-300' : 'text-slate-400'}`} />
        <span className="flex-1">{item.label}</span>
        {count !== undefined && count > 0 && (
          <span className={`text-[10px] font-mono font-bold px-1.5 py-0.5 rounded ${
            isActive ? 'bg-blue-200 text-blue-700' : 'bg-slate-200 text-slate-500'
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
