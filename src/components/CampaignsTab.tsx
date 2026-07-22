import React from 'react';
import { motion } from 'motion/react';
import { Sliders, Plus, Upload, Trash2, Link2, Link2Off, Eye, Package, ArrowRightLeft, Edit3, Check, X } from 'lucide-react';
import { CampaignItem, CreativeAsset, BRAND_CATEGORIES } from '../types';
import { FilterableSelect } from './FilterableSelect';

interface CampaignsTabProps {
  campaignList: CampaignItem[];
  assetList: CreativeAsset[];
  selectedCampaignId: string | null;
  setSelectedCampaignId: (id: string | null) => void;
  newAssetName: string;
  setNewAssetName: (v: string) => void;
  newAssetType: "Image" | "Logo" | "Video";
  setNewAssetType: (v: "Image" | "Logo" | "Video") => void;
  newAssetCategory: string;
  setNewAssetCategory: (v: string) => void;
  handleCreateAsset: (e: React.FormEvent, campaignId?: string) => void;
  handleUpdateAsset?: (assetId: string, data: { name?: string; type?: string; brandCategory?: string; file?: File }) => void;
  handleAssociateAsset: (assetId: string, campaignId: string) => Promise<void>;
  handleUnassociateAsset: (assetId: string) => Promise<void>;
  handleDeleteCampaign?: (id: string) => void;
  handleDeleteAsset?: (id: string) => void;
  newAssetFile: File | null;
  setNewAssetFile: (f: File | null) => void;
}

export const CampaignsTab: React.FC<CampaignsTabProps> = ({
  campaignList,
  assetList,
  selectedCampaignId,
  setSelectedCampaignId,
  newAssetName,
  setNewAssetName,
  newAssetType,
  setNewAssetType,
  newAssetCategory,
  setNewAssetCategory,
  handleCreateAsset,
  handleUpdateAsset,
  handleAssociateAsset,
  handleUnassociateAsset,
  handleDeleteCampaign,
  handleDeleteAsset,
  newAssetFile,
  setNewAssetFile,
}) => {
  const [editingAssetId, setEditingAssetId] = React.useState<string | null>(null);
  const [editName, setEditName] = React.useState('');
  const [editType, setEditType] = React.useState<'Image' | 'Logo' | 'Video'>('Image');
  const [editCategory, setEditCategory] = React.useState('');
  const [editFile, setEditFile] = React.useState<File | null>(null);
  const [previewAsset, setPreviewAsset] = React.useState<CreativeAsset | null>(null);

  const selectedCampaign = campaignList.find(c => c.id === selectedCampaignId);
  const campaignAssets = assetList.filter(a => a.campaignId === selectedCampaignId);
  const unassignedAssets = assetList.filter(a => !a.campaignId);

  const startEditing = (asset: CreativeAsset) => {
    setEditingAssetId(asset.id);
    setEditName(asset.name);
    setEditType(asset.type as 'Image' | 'Logo' | 'Video');
    setEditCategory(asset.brandCategory);
    setEditFile(null);
  };

  const cancelEditing = () => {
    setEditingAssetId(null);
    setEditFile(null);
  };

  const saveEdit = async () => {
    if (!editingAssetId || !handleUpdateAsset) return;
    await handleUpdateAsset(editingAssetId, {
      name: editName,
      type: editType,
      brandCategory: editCategory,
      file: editFile || undefined
    });
    cancelEditing();
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -10 }}
      className="grid grid-cols-1 lg:grid-cols-12 gap-6"
      key="campaigns_tab"
    >
      {/* Informational guide */}
      <div className="lg:col-span-12 bg-blue-50 border border-blue-100 rounded-2xl p-5 text-xs text-blue-800 flex items-start gap-3 shadow-xs">
        <Sliders className="h-5 w-5 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <h4 className="font-bold text-sm text-blue-900">
            {selectedCampaignId ? `Assets: ${selectedCampaign?.name || 'Campaign'}` : 'Step 1: Campaign Planner & Asset Staging'}
          </h4>
          <p className="mt-1 text-blue-700 leading-normal">
            {selectedCampaignId
              ? <>Managing creative assets for <strong>{selectedCampaign?.name}</strong> ({selectedCampaign?.namingStructureCode}). Stage brand overlays and assign them to this campaign.</>
              : <><strong>Select a campaign</strong> to view and manage its creative assets. Create advertiser campaigns with strict naming codes, then stage brand overlays and assign them to campaigns. Unassigned assets appear in the staging area.</>
            }
          </p>
        </div>
      </div>

      {/* ── LEFT: Campaign Library (only when no campaign selected) ── */}
      {!selectedCampaignId && (
      <div className="lg:col-span-7 space-y-6">
        {/* Campaign Library — Selectable Cards */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
          <h3 className="text-sm font-bold uppercase tracking-wider text-slate-500 mb-4 font-display">
            Campaign Database
            <span className="ml-2 text-[10px] text-blue-500 normal-case font-normal">— click a campaign to manage its assets</span>
          </h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-[500px] overflow-y-auto pr-1">
            {campaignList.map(camp => {
              const isSelected = camp.id === selectedCampaignId;
              const assetCount = assetList.filter(a => a.campaignId === camp.id).length;
              return (
                <div 
                  key={camp.id} 
                  onClick={() => setSelectedCampaignId(isSelected ? null : camp.id)}
                  className={`
                    cursor-pointer rounded-xl p-4 border-2 transition-all duration-200 flex flex-col justify-between
                    ${isSelected 
                      ? 'border-blue-500 bg-blue-50/40 shadow-md ring-1 ring-blue-200' 
                      : 'border-slate-200/60 bg-slate-50/60 hover:border-blue-200 hover:bg-blue-50/20'
                    }
                  `}
                >
                  <div>
                    <div className="flex items-center justify-between">
                      <span className="text-3xs font-mono font-bold text-slate-400">ID: {camp.id}</span>
                      <div className="flex items-center gap-1.5">
                        <span className={`px-2 py-0.5 rounded text-[8px] font-bold font-mono uppercase ${
                          camp.status === 'Active' ? 'bg-green-50 text-green-600' :
                          camp.status === 'Draft' ? 'bg-blue-50 text-blue-600' :
                          camp.status === 'Completed' ? 'bg-slate-100 text-slate-500' :
                          'bg-amber-50 text-amber-600'
                        }`}>{camp.status}</span>
                        {handleDeleteCampaign && (
                          <button
                            type="button"
                            onClick={(e) => { e.stopPropagation(); handleDeleteCampaign(camp.id); }}
                            className="p-1 rounded text-slate-400 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors"
                            title="Delete Campaign"
                          >
                            <Trash2 className="h-3 w-3" />
                          </button>
                        )}
                      </div>
                    </div>
                    <h4 className="text-sm font-bold text-slate-800 font-display mt-2">{camp.name}</h4>
                    <p className="text-2xs text-slate-500 mt-1 font-mono">Code: {camp.namingStructureCode}</p>
                  </div>
                  
                  <div className="mt-3 pt-3 border-t border-slate-200/50 flex items-center justify-between text-2xs text-slate-500">
                    <span>Region: {camp.targetRegion}</span>
                    <div className="flex items-center gap-3">
                      <span className="flex items-center gap-1 text-blue-600 font-semibold">
                        <Package className="h-3 w-3" /> {assetCount}
                      </span>
                      <span className="font-bold text-slate-700">${camp.totalBudget.toLocaleString()}</span>
                    </div>
                  </div>
                  {isSelected && (
                    <div className="mt-2 text-[10px] text-blue-600 font-semibold flex items-center gap-1">
                      <Eye className="h-3 w-3" /> Selected — manage assets in right panel →
                    </div>
                  )}
                </div>
              );
            })}
            {campaignList.length === 0 && (
              <div className="col-span-2 text-center py-8 text-xs text-slate-400">
                No campaigns registered yet. Create your first campaign above.
              </div>
            )}
          </div>
        </div>
      </div>
      )}

      {/* ── RIGHT: Campaign Asset Manager ── */}
      <div className={`${selectedCampaignId ? 'lg:col-span-12' : 'lg:col-span-5'} space-y-6`}>
        {/* Selected Campaign Detail + Its Assets */}
        {selectedCampaign ? (
          <>
            {/* Campaign Detail Header */}
            <div className="bg-white border-2 border-blue-300 rounded-2xl p-5 shadow-sm">
              <div className="flex items-center justify-between mb-3">
                <div className="flex items-center gap-2">
                  <div className="h-8 w-8 bg-blue-100 rounded-lg flex items-center justify-center">
                    <Package className="h-4 w-4 text-blue-600" />
                  </div>
                  <div>
                    <h3 className="text-sm font-bold text-slate-800 font-display">{selectedCampaign.name}</h3>
                    <p className="text-[10px] text-slate-500 font-mono">{selectedCampaign.namingStructureCode}</p>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => setSelectedCampaignId(null)}
                  className="text-xs text-slate-400 hover:text-slate-600 cursor-pointer"
                >
                  ✕ Deselect
                </button>
              </div>
              <div className="grid grid-cols-3 gap-2 text-center">
                <div className="bg-slate-50 rounded-lg p-2">
                  <p className="text-[10px] text-slate-500">Budget</p>
                  <p className="text-xs font-bold text-slate-800">${selectedCampaign.totalBudget.toLocaleString()}</p>
                </div>
                <div className="bg-slate-50 rounded-lg p-2">
                  <p className="text-[10px] text-slate-500">Region</p>
                  <p className="text-xs font-bold text-slate-800 truncate">{selectedCampaign.targetRegion}</p>
                </div>
                <div className="bg-slate-50 rounded-lg p-2">
                  <p className="text-[10px] text-slate-500">Assets</p>
                  <p className="text-xs font-bold text-blue-600">{campaignAssets.length}</p>
                </div>
              </div>
            </div>

            {/* Campaign's Assets */}
            <div className="bg-white border border-slate-200/90 rounded-2xl p-5 shadow-sm">
              <h3 className="text-sm font-bold text-slate-800 font-display mb-3 flex items-center gap-2">
                <Link2 className="h-4 w-4 text-blue-500" />
                Campaign Assets ({campaignAssets.length})
              </h3>
              
              {campaignAssets.length === 0 ? (
                <div className="text-center py-6 text-xs text-slate-400 space-y-2">
                  <Package className="h-8 w-8 mx-auto text-slate-300" />
                  <p>No assets assigned to this campaign yet.</p>
                  <p className="text-[10px]">Create a new asset below or assign from the unassigned library.</p>
                </div>
              ) : (
                <div className="space-y-2 max-h-[300px] overflow-y-auto pr-1">
                  {campaignAssets.map(as => (
                    <div key={as.id} className="bg-blue-50/40 border border-blue-100 rounded-lg p-3">
                      {editingAssetId === as.id ? (
                        /* Inline Edit Form */
                        <div className="space-y-2">
                          <input type="text" value={editName} onChange={(e) => setEditName(e.target.value)}
                            className="w-full bg-white border border-slate-200 rounded px-2 py-1 text-xs" />
                          <div className="grid grid-cols-2 gap-1">
                            <select value={editType} onChange={(e) => setEditType(e.target.value as any)}
                              className="bg-white border border-slate-200 rounded px-1 py-1 text-[10px]">
                              <option value="Image">PNG Image</option>
                              <option value="Logo">Alpha Logo</option>
                              <option value="Video">MP4 Overlay</option>
                            </select>
                            <FilterableSelect value={editCategory} onChange={setEditCategory}
                              options={BRAND_CATEGORIES} placeholder="Category..." />
                          </div>
                          <label className="border border-dashed border-slate-200 rounded p-1.5 text-center cursor-pointer block">
                            <span className="text-[9px] text-slate-400">{editFile ? editFile.name : 'Replace file (optional)'}</span>
                            <input type="file" accept="image/png,image/jpeg" className="hidden"
                              onChange={(e) => { const f = e.target.files?.[0]; if (f) setEditFile(f); }} />
                          </label>
                          <div className="flex gap-1">
                            <button onClick={saveEdit} className="flex-1 flex items-center justify-center gap-1 px-2 py-1 bg-emerald-600 text-white text-[10px] font-bold rounded cursor-pointer">
                              <Check className="h-3 w-3" /> Save
                            </button>
                            <button onClick={cancelEditing} className="flex-1 flex items-center justify-center gap-1 px-2 py-1 bg-slate-200 text-slate-600 text-[10px] font-bold rounded cursor-pointer">
                              <X className="h-3 w-3" /> Cancel
                            </button>
                          </div>
                        </div>
                      ) : (
                        /* Normal display */
                        <div className="flex items-center justify-between group">
                          <div className="flex items-center gap-3 min-w-0 flex-1">
                            {/* Thumbnail — clickable to view full */}
                            <div
                              className="h-10 w-10 rounded-lg bg-slate-100 flex items-center justify-center shrink-0 overflow-hidden border border-slate-200 cursor-pointer hover:border-blue-400 hover:shadow-sm transition-all"
                              onClick={() => as.thumbnailUrl && setPreviewAsset(as)}
                              title={as.thumbnailUrl ? 'Click to view full image' : 'No preview available'}
                            >
                              {as.thumbnailUrl ? (
                                <img src={as.thumbnailUrl} alt={as.name} className="h-full w-full object-cover" />
                              ) : (
                                <Package className="h-5 w-5 text-slate-300" />
                              )}
                            </div>
                            <div className="min-w-0 flex-1">
                              <p className="text-2xs font-bold text-slate-800 truncate">{as.name}</p>
                              <div className="flex items-center gap-2 mt-1">
                                <span className="text-[8px] px-1.5 py-0.5 rounded bg-blue-100 text-blue-600 font-mono">{as.type}</span>
                                <span className="text-[8px] text-slate-400 font-mono">{as.brandCategory}</span>
                                <span className="text-[8px] text-slate-300 font-mono">{as.dimensions}</span>
                              </div>
                            </div>
                          </div>
                          <div className="flex items-center gap-1 shrink-0">
                            {/* View full asset */}
                            {as.thumbnailUrl && (
                              <button type="button" onClick={() => setPreviewAsset(as)}
                                className="p-1 rounded text-slate-300 hover:text-fuchsia-500 hover:bg-fuchsia-50 cursor-pointer transition-colors opacity-0 group-hover:opacity-100" title="View full image">
                                <Eye className="h-3 w-3" />
                              </button>
                            )}
                            {handleUpdateAsset && (
                              <button type="button" onClick={() => startEditing(as)}
                                className="p-1 rounded text-slate-300 hover:text-blue-500 hover:bg-blue-50 cursor-pointer transition-colors opacity-0 group-hover:opacity-100" title="Edit">
                                <Edit3 className="h-3 w-3" />
                              </button>
                            )}
                            {handleDeleteAsset && (
                              <button type="button" onClick={() => handleDeleteAsset(as.id)}
                                className="p-1 rounded text-slate-300 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors opacity-0 group-hover:opacity-100" title="Delete">
                                <Trash2 className="h-3 w-3" />
                              </button>
                            )}
                            <button type="button" onClick={() => handleUnassociateAsset(as.id)}
                              className="p-1 rounded text-slate-400 hover:text-amber-500 hover:bg-amber-50 cursor-pointer transition-colors" title="Remove from campaign">
                              <Link2Off className="h-3.5 w-3.5" />
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}

              {/* Quick Add Asset to this Campaign */}
              <div className="mt-4 pt-4 border-t border-slate-200">
                <p className="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3 font-mono">Quick Add Asset to This Campaign</p>
                <form onSubmit={(e) => handleCreateAsset(e, selectedCampaign.id)} className="space-y-3">
                  <input 
                    type="text" 
                    value={newAssetName} 
                    onChange={(e) => setNewAssetName(e.target.value)} 
                    placeholder="Asset name (e.g., Coke Transparent Banner)"
                    className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                    required
                  />
                  <div className="grid grid-cols-2 gap-2">
                    <select 
                      value={newAssetType} 
                      onChange={(e) => setNewAssetType(e.target.value as any)}
                      className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                    >
                      <option value="Image">PNG Image</option>
                      <option value="Logo">Alpha Logo</option>
                      <option value="Video">MP4 Overlay</option>
                    </select>
                    <FilterableSelect
                      value={newAssetCategory}
                      onChange={setNewAssetCategory}
                      options={BRAND_CATEGORIES}
                      placeholder="Search category..."
                      required
                    />
                  </div>
                  <label className="border border-dashed border-slate-200 rounded-lg p-2 bg-slate-50/50 text-center cursor-pointer hover:border-blue-300 hover:bg-blue-50/30 transition-colors block">
                    {newAssetFile ? (
                      <span className="text-[10px] text-blue-600 font-medium">✓ {newAssetFile.name}</span>
                    ) : (
                      <span className="text-[10px] text-slate-400">+ Attach file (optional)</span>
                    )}
                    <input type="file" accept="image/png,image/jpeg" className="hidden"
                      onChange={(e) => { const f = e.target.files?.[0]; if (f) setNewAssetFile(f); }} />
                  </label>
                  <button 
                    type="submit" 
                    className="w-full inline-flex items-center justify-center gap-2 px-3 py-1.5 bg-blue-600 hover:bg-blue-500 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
                  >
                    <Plus className="h-3 w-3" />
                    Add Asset to {selectedCampaign.name.length > 20 ? selectedCampaign.name.substring(0, 20) + '...' : selectedCampaign.name}
                  </button>
                </form>
              </div>
            </div>
          </>
        ) : (
          /* No campaign selected — show unassigned assets + prompt */
          <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm">
            <div className="text-center mb-5">
              <Eye className="h-10 w-10 mx-auto text-slate-300 mb-2" />
              <h3 className="text-sm font-bold text-slate-600 font-display">Select a Campaign</h3>
              <p className="text-xs text-slate-400 mt-1">Click any campaign card on the left to view and manage its creative assets.</p>
            </div>

            {/* Unassigned Assets Staging */}
            <div className="border-t border-slate-100 pt-5">
              <h4 className="text-xs font-bold uppercase tracking-wider text-slate-500 mb-3 font-display flex items-center gap-2">
                <ArrowRightLeft className="h-3.5 w-3.5 text-amber-500" />
                Unassigned Assets ({unassignedAssets.length})
              </h4>
              
              {unassignedAssets.length === 0 ? (
                <p className="text-[10px] text-slate-400 text-center py-4">All assets are assigned to campaigns.</p>
              ) : (
                <div className="space-y-2 max-h-[400px] overflow-y-auto pr-1">
                  {unassignedAssets.map(as => (
                    <div key={as.id} className="bg-amber-50/30 border border-amber-100 rounded-lg p-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-3 min-w-0 flex-1">
                          {/* Thumbnail — clickable to view full */}
                          <div
                            className="h-10 w-10 rounded-lg bg-slate-100 flex items-center justify-center shrink-0 overflow-hidden border border-amber-200 cursor-pointer hover:border-blue-400 hover:shadow-sm transition-all"
                            onClick={() => as.thumbnailUrl && setPreviewAsset(as)}
                            title={as.thumbnailUrl ? 'Click to view full image' : 'No preview available'}
                          >
                            {as.thumbnailUrl ? (
                              <img src={as.thumbnailUrl} alt={as.name} className="h-full w-full object-cover" />
                            ) : (
                              <Package className="h-5 w-5 text-slate-300" />
                            )}
                          </div>
                          <div className="min-w-0 flex-1">
                            <p className="text-2xs font-bold text-slate-800 truncate">{as.name}</p>
                            <div className="flex items-center gap-2 mt-1">
                              <span className="text-[8px] px-1.5 py-0.5 rounded bg-amber-100 text-amber-600 font-mono">{as.type}</span>
                              <span className="text-[8px] text-slate-400 font-mono">{as.brandCategory}</span>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-1 shrink-0">
                          {/* View full asset */}
                          {as.thumbnailUrl && (
                            <button type="button" onClick={() => setPreviewAsset(as)}
                              className="p-1 rounded text-slate-300 hover:text-fuchsia-500 hover:bg-fuchsia-50 cursor-pointer transition-colors" title="View full image">
                              <Eye className="h-3 w-3" />
                            </button>
                          )}
                          {handleDeleteAsset && (
                            <button
                              type="button"
                              onClick={() => handleDeleteAsset(as.id)}
                              className="p-1 rounded text-slate-300 hover:text-red-500 hover:bg-red-50 cursor-pointer transition-colors"
                              title="Delete Asset"
                            >
                              <Trash2 className="h-3 w-3" />
                            </button>
                          )}
                        </div>
                      </div>
                      {/* Quick assign dropdown */}
                      <div className="mt-2 flex items-center gap-2">
                        <span className="text-[8px] text-slate-400 shrink-0">Assign to:</span>
                        <select
                          className="flex-1 text-[10px] bg-white border border-slate-200 rounded px-1.5 py-1 text-slate-600 focus:outline-none focus:border-blue-400"
                          defaultValue=""
                          onChange={async (e) => {
                            if (e.target.value) {
                              await handleAssociateAsset(as.id, e.target.value);
                              e.target.value = '';
                            }
                          }}
                        >
                          <option value="" disabled>Select campaign...</option>
                          {campaignList.map(c => (
                            <option key={c.id} value={c.id}>{c.name} ({c.namingStructureCode})</option>
                          ))}
                        </select>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Generic Asset Upload (no campaign context) */}
            <div className="mt-5 pt-5 border-t border-slate-100">
              <p className="text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-3 font-mono">Stage New Asset (Unassigned)</p>
              <form onSubmit={(e) => handleCreateAsset(e)} className="space-y-3">
                <input 
                  type="text" 
                  value={newAssetName} 
                  onChange={(e) => setNewAssetName(e.target.value)} 
                  placeholder="Asset name (e.g., Nike Swoosh Logo)"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  required
                />
                <div className="grid grid-cols-2 gap-2">
                  <select 
                    value={newAssetType} 
                    onChange={(e) => setNewAssetType(e.target.value as any)}
                    className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2 py-1.5 text-xs text-slate-800 focus:bg-white focus:outline-none focus:border-blue-500 transition-colors"
                  >
                    <option value="Image">PNG Image</option>
                    <option value="Logo">Alpha Logo</option>
                    <option value="Video">MP4 Overlay</option>
                  </select>
                  <FilterableSelect
                    value={newAssetCategory}
                    onChange={setNewAssetCategory}
                    options={BRAND_CATEGORIES}
                    placeholder="Search category..."
                    required
                  />
                </div>
                <label className="border border-dashed border-slate-200 rounded-lg p-3 bg-slate-50/50 text-center cursor-pointer hover:border-blue-300 hover:bg-blue-50/30 transition-colors block">
                  {newAssetFile ? (
                    <div className="flex items-center justify-center gap-2 text-xs text-blue-600 font-medium">
                      <Check className="h-4 w-4" />
                      {newAssetFile.name} ({(newAssetFile.size / 1024).toFixed(0)} KB)
                    </div>
                  ) : (
                    <>
                      <Upload className="h-5 w-5 text-slate-400 mx-auto mb-1" />
                      <span className="text-[10px] text-slate-400 block">Drag &amp; drop or click to upload</span>
                    </>
                  )}
                  <input type="file" accept="image/png,image/jpeg,video/mp4" className="hidden"
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (file) {
                        setNewAssetFile(file);
                        if (!newAssetName) setNewAssetName(file.name.replace(/\.[^.]+$/, ''));
                      }
                    }} />
                </label>
                <button 
                  type="submit" 
                  className="w-full inline-flex items-center justify-center gap-2 px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer"
                >
                  <Upload className="h-3 w-3" />
                  Stage Asset to Library
                </button>
              </form>
            </div>
          </div>
        )}
      </div>

      {/* Asset Preview Modal */}
      {previewAsset && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-sm p-4"
          onClick={() => setPreviewAsset(null)}
        >
          <div
            className="relative bg-white rounded-2xl shadow-2xl max-w-3xl w-full max-h-[90vh] overflow-hidden flex flex-col"
            onClick={(e) => e.stopPropagation()}
          >
            {/* Header */}
            <div className="flex items-center justify-between px-5 py-3 border-b border-slate-200">
              <div className="min-w-0">
                <h3 className="text-sm font-bold text-slate-800 truncate">{previewAsset.name}</h3>
                <div className="flex items-center gap-2 mt-0.5">
                  <span className="text-[10px] px-1.5 py-0.5 rounded bg-blue-100 text-blue-600 font-mono">{previewAsset.type}</span>
                  <span className="text-[10px] text-slate-400 font-mono">{previewAsset.brandCategory}</span>
                  <span className="text-[10px] text-slate-300 font-mono">{previewAsset.dimensions} · {previewAsset.fileSize}</span>
                </div>
              </div>
              <button
                onClick={() => setPreviewAsset(null)}
                className="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-100 cursor-pointer transition-colors shrink-0"
                title="Close"
              >
                <X className="h-5 w-5" />
              </button>
            </div>
            {/* Image */}
            <div className="flex-1 flex items-center justify-center bg-slate-900/5 p-6 min-h-[300px]">
              {previewAsset.thumbnailUrl ? (
                <img
                  src={previewAsset.thumbnailUrl}
                  alt={previewAsset.name}
                  className="max-w-full max-h-[60vh] object-contain rounded-lg shadow-md"
                />
              ) : (
                <div className="text-center text-slate-400">
                  <Package className="h-16 w-16 mx-auto mb-3 text-slate-300" />
                  <p className="text-sm font-medium">No preview available</p>
                  <p className="text-xs mt-1">This asset has no uploaded image file.</p>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </motion.div>
  );
};
