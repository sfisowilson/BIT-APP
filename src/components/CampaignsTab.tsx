import React from 'react';
import { motion } from 'motion/react';
import { Sliders, Plus, Upload, Trash2 } from 'lucide-react';
import { CampaignItem, CreativeAsset } from '../types';

interface CampaignsTabProps {
  campaignList: CampaignItem[];
  assetList: CreativeAsset[];
  newCampaignName: string;
  setNewCampaignName: (v: string) => void;
  newCampaignCode: string;
  setNewCampaignCode: (v: string) => void;
  newCampaignBudget: string;
  setNewCampaignBudget: (v: string) => void;
  newCampaignRegion: string;
  setNewCampaignRegion: (v: string) => void;
  handleCreateCampaign: (e: React.FormEvent) => void;
  campaignError: string | null;
  newAssetName: string;
  setNewAssetName: (v: string) => void;
  newAssetType: "Image" | "Logo" | "Video";
  setNewAssetType: (v: "Image" | "Logo" | "Video") => void;
  newAssetCategory: string;
  setNewAssetCategory: (v: string) => void;
  handleCreateAsset: (e: React.FormEvent) => void;
  handleDeleteCampaign?: (id: string) => void;
  handleDeleteAsset?: (id: string) => void;
}

export const CampaignsTab: React.FC<CampaignsTabProps> = ({
  campaignList,
  assetList,
  newCampaignName,
  setNewCampaignName,
  newCampaignCode,
  setNewCampaignCode,
  newCampaignBudget,
  setNewCampaignBudget,
  newCampaignRegion,
  setNewCampaignRegion,
  handleCreateCampaign,
  campaignError,
  newAssetName,
  setNewAssetName,
  newAssetType,
  setNewAssetType,
  newAssetCategory,
  setNewAssetCategory,
  handleCreateAsset,
  handleDeleteCampaign,
  handleDeleteAsset,
}) => {
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-3 gap-8"
      key="campaigns_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-3 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Sliders className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">Step 1: Campaign Planner &amp; Asset Staging</h4>
          <p className="mt-1 text-blue-700 leading-normal">
            This module is the starting point of the operational flow. Create advertiser campaign accounts and specify strict naming code structures (complying with <strong>MReq 10</strong>). Then, upload transparent logos or graphic overlays that will be positioned on the video boundaries.
          </p>
        </div>
      </div>

      {/* Campaigns column */}
      <div className="col-span-1 lg:col-span-2 space-y-8">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">Define Brand Campaign</h2>
          <p className="text-xs text-slate-500 mb-6">Create campaign schedules, regions and budgets to support automated relational ad placements (<strong>MReq 10</strong>).</p>

          <form onSubmit={handleCreateCampaign} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Campaign Name</label>
                <input 
                  type="text" 
                  value={newCampaignName} 
                  onChange={(e) => setNewCampaignName(e.target.value)} 
                  placeholder="e.g., Coke Zero Summer"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Naming Code (MReq 10)</label>
                <input 
                  type="text" 
                  value={newCampaignCode} 
                  onChange={(e) => setNewCampaignCode(e.target.value)} 
                  placeholder="e.g., COKE_ZERO_2026"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Allocation Budget (USD)</label>
                <input 
                  type="number" 
                  value={newCampaignBudget} 
                  onChange={(e) => setNewCampaignBudget(e.target.value)} 
                  placeholder="e.g., 15000"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Broadcast Target Territory</label>
                <select 
                  value={newCampaignRegion} 
                  onChange={(e) => setNewCampaignRegion(e.target.value)}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                >
                  <option value="SADC Region">SADC Region (Southern Africa)</option>
                  <option value="East Africa proxy">East Africa Broadcast proxy</option>
                  <option value="Global Streaming stream">Global Streaming streams</option>
                </select>
              </div>
            </div>

            {campaignError && (
              <p className="text-2xs text-red-600 font-semibold font-mono bg-red-50 p-2.5 rounded-lg border border-red-100">{campaignError}</p>
            )}

            <button 
              type="submit" 
              className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <Plus className="h-3.5 w-3.5" />
              Register Brand Campaign
            </button>
          </form>
        </div>

        {/* Existing campaigns library */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">Active Database Campaigns</h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {campaignList.map(camp => (
              <div key={camp.id} className="bg-slate-50/60 border border-slate-200/60 rounded-xl p-4 flex flex-col justify-between">
                <div>
                  <div className="flex items-center justify-between">
                    <span className="text-3xs font-mono font-bold text-slate-400">ID: {camp.id}</span>
                    <div className="flex items-center gap-1.5">
                      <span className="px-2 py-0.5 rounded text-[8px] font-bold bg-blue-50 text-blue-600 font-mono uppercase">{camp.status}</span>
                      {handleDeleteCampaign && (
                        <button
                          type="button"
                          onClick={() => handleDeleteCampaign(camp.id)}
                          className="p-1 rounded text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors"
                          title="Delete Campaign"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      )}
                    </div>
                  </div>
                  <h4 className="text-sm font-bold text-slate-800 font-display mt-2">{camp.name}</h4>
                  <p className="text-2xs text-slate-500 mt-1 font-mono">Structure Code: {camp.namingStructureCode}</p>
                </div>
                
                <div className="mt-4 pt-3 border-t border-slate-200/50 flex items-center justify-between text-2xs text-slate-500">
                  <span>Region: {camp.targetRegion}</span>
                  <span className="font-bold text-slate-700">${camp.totalBudget.toLocaleString()}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Creative assets column */}
      <div className="col-span-1 space-y-8">
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h2 className="text-lg font-bold text-slate-800 font-display mb-2">Stage Brand Overlay</h2>
          <p className="text-xs text-slate-500 mb-6 font-sans">Upload transparent logo PNG graphics or high-bitrate overlay media files.</p>

          <form onSubmit={handleCreateAsset} className="space-y-4">
            <div>
              <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Creative Asset Name</label>
              <input 
                type="text" 
                value={newAssetName} 
                onChange={(e) => setNewAssetName(e.target.value)} 
                placeholder="e.g., Coca-Cola Transparent Corner Banner"
                className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                required
              />
            </div>

            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Type</label>
                <select 
                  value={newAssetType} 
                  onChange={(e) => setNewAssetType(e.target.value as any)}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                >
                  <option value="Image">PNG Image</option>
                  <option value="Logo">Alpha Logo</option>
                  <option value="Video">MP4 Overlay</option>
                </select>
              </div>
              <div>
                <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">Brand Category</label>
                <input 
                  type="text" 
                  value={newAssetCategory} 
                  onChange={(e) => setNewAssetCategory(e.target.value)} 
                  placeholder="e.g., Beverages"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
              </div>
            </div>

            <div className="border border-dashed border-slate-200 rounded-xl p-4 bg-slate-50/50 text-center">
              <Upload className="h-6 w-6 text-slate-400 mx-auto mb-2" />
              <span className="text-2xs text-slate-500 block font-semibold">Drag &amp; drop creative file here or click to browse.</span>
              <span className="text-[10px] text-slate-400 block mt-1">Accepts transparent PNG, high-bitrate MP4 assets.</span>
            </div>

            <button 
              type="submit" 
              className="w-full inline-flex items-center justify-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              <Upload className="h-3.5 w-3.5" />
              Stage Asset to Cloud
            </button>
          </form>
        </div>

        {/* Staged assets catalog */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-3 font-display">Staged Asset Library (S3 Links)</h3>
          <div className="space-y-3">
            {assetList.map(as => (
              <div key={as.id} className="bg-slate-50/50 border border-slate-200/60 rounded-lg p-3">
                <div className="flex items-center justify-between">
                  <span className="text-2xs font-bold text-slate-800 truncate w-2/3">{as.name}</span>
                  <div className="flex items-center gap-1.5 shrink-0">
                    <span className="px-1.5 py-0.5 rounded text-[8px] font-bold bg-blue-50 text-blue-600 font-mono">{as.brandCategory}</span>
                    {handleDeleteAsset && (
                      <button
                        type="button"
                        onClick={() => handleDeleteAsset(as.id)}
                        className="p-1 rounded text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors"
                        title="Delete Asset"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </div>
                </div>
                <p className="text-[10px] text-slate-400 font-mono mt-1.5 truncate">{as.storageKey}</p>
                <div className="flex justify-between text-[10px] text-slate-400 mt-1 font-mono">
                  <span>Dimensions: {as.dimensions}</span>
                  <span>Size: {as.fileSize}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </motion.div>
  );
};
