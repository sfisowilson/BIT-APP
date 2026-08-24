import React, { useState, useRef, useEffect, useMemo } from 'react';
import { ChevronDown, Plus, Check, Package, Search } from 'lucide-react';
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
  const [filter, setFilter] = useState('');
  const [highlightIndex, setHighlightIndex] = useState(-1);
  const ref = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const selected = campaigns.find(c => c.id === selectedId);

  // Filter campaigns by name
  const filteredCampaigns = useMemo(() => {
    if (!filter.trim()) return campaigns;
    const q = filter.toLowerCase();
    return campaigns.filter(c => c.name.toLowerCase().includes(q));
  }, [campaigns, filter]);

  // Close on outside click
  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
        setFilter('');
      }
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  // Reset highlight when filter changes
  useEffect(() => {
    setHighlightIndex(-1);
  }, [filter]);

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlightIndex >= 0 && listRef.current) {
      const item = listRef.current.children[highlightIndex] as HTMLElement | undefined;
      if (item) item.scrollIntoView({ block: 'nearest' });
    }
  }, [highlightIndex]);

  const selectCampaign = (id: string) => {
    onSelect(id);
    setOpen(false);
    setFilter('');
    setHighlightIndex(-1);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') {
        setOpen(true);
        e.preventDefault();
      }
      return;
    }

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        setHighlightIndex(prev => Math.min(prev + 1, filteredCampaigns.length - 1));
        break;
      case 'ArrowUp':
        e.preventDefault();
        setHighlightIndex(prev => Math.max(prev - 1, -1));
        break;
      case 'Enter':
        e.preventDefault();
        if (highlightIndex >= 0 && highlightIndex < filteredCampaigns.length) {
          selectCampaign(filteredCampaigns[highlightIndex].id);
        }
        break;
      case 'Escape':
        setOpen(false);
        setFilter('');
        break;
    }
  };

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => { setOpen(!open); if (!open) setTimeout(() => inputRef.current?.focus(), 50); }}
        className="flex items-center gap-2 px-4 py-2 bg-white border border-slate-200 rounded-xl text-sm font-bold text-slate-800 hover:border-brand-300 hover:shadow-sm transition-all min-w-[200px] cursor-pointer"
      >
        {selected ? (
          <>
            <span className={`h-2 w-2 rounded-full ${
              selected.status === 'Active' ? 'bg-emerald-500' :
              selected.status === 'Draft' ? 'bg-brand-500' :
              selected.status === 'Completed' ? 'bg-slate-400' : 'bg-amber-500'
            }`} />
            <span className="flex-1 text-left truncate" title={selected.name}>{selected.name}</span>
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
        <div className="absolute top-full mt-1 left-0 w-72 bg-white border border-slate-200 rounded-xl shadow-xl z-50 py-1 max-h-80 overflow-hidden flex flex-col">
          {/* Search filter input */}
          <div className="px-3 py-2 border-b border-slate-100">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-slate-400 pointer-events-none" />
              <input
                ref={inputRef}
                type="text"
                value={filter}
                onChange={(e) => { setFilter(e.target.value); if (!open) setOpen(true); }}
                onKeyDown={handleKeyDown}
                placeholder="Filter campaigns..."
                className="w-full bg-slate-50 border border-slate-200 rounded-lg pl-8 pr-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-brand-400 transition-colors"
              />
            </div>
          </div>

          {/* Campaign list */}
          <ul ref={listRef} className="overflow-y-auto flex-1">
            {filteredCampaigns.length === 0 ? (
              <li className="px-4 py-3 text-xs text-slate-400 text-center">
                {campaigns.length === 0 ? 'No campaigns yet. Create one below.' : `No campaigns match "${filter}"`}
              </li>
            ) : (
              filteredCampaigns.map((c, idx) => {
                const isSelected = c.id === selectedId;
                return (
                  <li key={c.id}>
                    <button
                      onClick={() => selectCampaign(c.id)}
                      onMouseEnter={() => setHighlightIndex(idx)}
                      className={`w-full text-left px-4 py-2.5 flex items-center gap-3 transition-colors cursor-pointer ${
                        idx === highlightIndex ? 'bg-brand-50' : isSelected ? 'bg-brand-50/60' : 'hover:bg-brand-50'
                      }`}
                    >
                      <span className={`h-2.5 w-2.5 rounded-full shrink-0 ${
                        c.status === 'Active' ? 'bg-emerald-500' :
                        c.status === 'Draft' ? 'bg-brand-500' : 'bg-slate-400'
                      }`} />
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-bold text-slate-800 truncate" title={c.name}>{c.name}</div>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <span className="text-[10px] text-slate-400 font-mono">{assetCounts[c.id] || 0}</span>
                        {isSelected && <Check className="h-3.5 w-3.5 text-brand-600" />}
                      </div>
                    </button>
                  </li>
                );
              })
            )}
          </ul>

          <div className="border-t border-slate-100 pt-1">
            <button
              onClick={() => { onCreateNew(); setOpen(false); setFilter(''); }}
              className="w-full text-left px-4 py-2.5 flex items-center gap-2 text-xs font-bold text-brand-600 hover:bg-brand-50 transition-colors cursor-pointer"
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
