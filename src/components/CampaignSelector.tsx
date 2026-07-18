import React, { useState, useRef, useEffect } from 'react';
import { ChevronDown, Plus, Check, Package } from 'lucide-react';
import { CampaignItem } from '../types';

interface CampaignSelectorProps {
  campaigns: CampaignItem[];
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  onCreateNew: () => void;
  assetCounts: Record<string, number>;
}

export const CampaignSelector: React.FC<CampaignSelectorProps> = ({
  campaigns,
  selectedId,
  onSelect,
  onCreateNew,
  assetCounts,
}) => {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const selected = campaigns.find(c => c.id === selectedId);

  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center gap-2 px-4 py-2 bg-white border border-slate-200 rounded-xl text-sm font-bold text-slate-800 hover:border-blue-300 hover:shadow-sm transition-all min-w-[200px] cursor-pointer"
      >
        {selected ? (
          <>
            <span className={`h-2 w-2 rounded-full ${
              selected.status === 'Active' ? 'bg-emerald-500' :
              selected.status === 'Draft' ? 'bg-blue-500' :
              selected.status === 'Completed' ? 'bg-slate-400' : 'bg-amber-500'
            }`} />
            <span className="flex-1 text-left truncate">{selected.name}</span>
            <span className="text-[10px] text-slate-400 font-mono font-normal">
              {assetCounts[selected.id] || 0} assets
            </span>
          </>
        ) : (
          <>
            <Package className="h-4 w-4 text-slate-400" />
            <span className="flex-1 text-left text-slate-400">Select campaign...</span>
          </>
        )}
        <ChevronDown className={`h-4 w-4 text-slate-400 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div className="absolute top-full mt-1 left-0 w-72 bg-white border border-slate-200 rounded-xl shadow-xl z-50 py-1 max-h-80 overflow-y-auto">
          {campaigns.length === 0 ? (
            <div className="px-4 py-3 text-xs text-slate-400 text-center">
              No campaigns yet. Create one below.
            </div>
          ) : (
            campaigns.map(c => {
              const isSelected = c.id === selectedId;
              return (
                <button
                  key={c.id}
                  onClick={() => { onSelect(c.id); setOpen(false); }}
                  className={`w-full text-left px-4 py-2.5 flex items-center gap-3 hover:bg-blue-50 transition-colors cursor-pointer ${
                    isSelected ? 'bg-blue-50' : ''
                  }`}
                >
                  <span className={`h-2.5 w-2.5 rounded-full shrink-0 ${
                    c.status === 'Active' ? 'bg-emerald-500' :
                    c.status === 'Draft' ? 'bg-blue-500' : 'bg-slate-400'
                  }`} />
                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-bold text-slate-800 truncate">{c.name}</div>
                    <div className="text-[10px] text-slate-400 font-mono">{c.namingStructureCode}</div>
                  </div>
                  <div className="flex items-center gap-1.5 shrink-0">
                    <span className="text-[10px] text-slate-400 font-mono">{assetCounts[c.id] || 0}</span>
                    {isSelected && <Check className="h-3.5 w-3.5 text-blue-600" />}
                  </div>
                </button>
              );
            })
          )}
          <div className="border-t border-slate-100 mt-1 pt-1">
            <button
              onClick={() => { onCreateNew(); setOpen(false); }}
              className="w-full text-left px-4 py-2.5 flex items-center gap-2 text-xs font-bold text-blue-600 hover:bg-blue-50 transition-colors cursor-pointer"
            >
              <Plus className="h-3.5 w-3.5" />
              Create New Campaign
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
