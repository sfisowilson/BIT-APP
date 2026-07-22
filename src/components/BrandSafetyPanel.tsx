import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { ShieldAlert, Plus, ToggleLeft, ToggleRight, Loader2 } from 'lucide-react';
import { fetchWithAuth } from '../apiClient';

interface BrandSafetyRule {
  id: string;
  category: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
}

export const BrandSafetyPanel: React.FC = () => {
  const [rules, setRules] = useState<BrandSafetyRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [newCategory, setNewCategory] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [adding, setAdding] = useState(false);
  const [togglingId, setTogglingId] = useState<string | null>(null);

  useEffect(() => { loadRules(); }, []);

  const loadRules = async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth('/api/admin/brand-safety');
      setRules(await res.json());
    } catch (err) {
      console.error('Failed to load brand safety rules', err);
    } finally {
      setLoading(false);
    }
  };

  const handleAdd = async () => {
    if (!newCategory.trim()) return;
    setAdding(true);
    try {
      await fetchWithAuth('/api/admin/brand-safety', {
        method: 'POST',
        body: JSON.stringify({ category: newCategory.trim(), description: newDescription.trim() || null }),
      });
      setNewCategory('');
      setNewDescription('');
      await loadRules();
    } catch (err) {
      console.error('Failed to add rule', err);
    } finally {
      setAdding(false);
    }
  };

  const handleToggle = async (id: string) => {
    setTogglingId(id);
    try {
      await fetchWithAuth(`/api/admin/brand-safety/${id}/toggle`, { method: 'POST' });
      await loadRules();
    } catch (err) {
      console.error('Failed to toggle rule', err);
    } finally {
      setTogglingId(null);
    }
  };

  const activeCount = rules.filter(r => r.isActive).length;

  if (loading) {
    return (
      <div className="bg-white border border-slate-200/90 rounded-2xl p-8 text-center">
        <Loader2 className="h-5 w-5 animate-spin text-blue-500 mx-auto mb-2" />
        <p className="text-xs text-slate-400 font-mono">Loading brand-safety rules...</p>
      </div>
    );
  }

  return (
    <div className="bg-white border border-slate-200/90 rounded-2xl shadow-sm overflow-hidden">
      <div className="p-5 border-b border-slate-200 bg-slate-50/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="p-2 bg-red-50 text-red-600 rounded-lg">
            <ShieldAlert className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-extrabold text-slate-800 uppercase tracking-widest font-display">Brand-Safety Exclusion List</h3>
            <p className="text-[10px] text-slate-400 mt-0.5">
              Permanent exclusion list — add-only. {activeCount} active rule{activeCount !== 1 ? 's' : ''}.
            </p>
          </div>
        </div>
      </div>

      {/* Add form */}
      <div className="p-4 border-b border-slate-100 bg-slate-50/30">
        <div className="flex gap-2">
          <input
            type="text"
            value={newCategory}
            onChange={e => setNewCategory(e.target.value)}
            placeholder="Category (e.g. Human Faces)"
            className="flex-1 px-3 py-2 bg-white border border-slate-200 rounded-lg text-xs focus:outline-none focus:border-red-500"
          />
          <input
            type="text"
            value={newDescription}
            onChange={e => setNewDescription(e.target.value)}
            placeholder="Description (optional)"
            className="flex-1 px-3 py-2 bg-white border border-slate-200 rounded-lg text-xs focus:outline-none focus:border-red-500"
          />
          <button
            onClick={handleAdd}
            disabled={adding || !newCategory.trim()}
            className="px-4 py-2 bg-red-600 hover:bg-red-500 text-white font-bold rounded-lg text-xs cursor-pointer transition-colors disabled:opacity-50 flex items-center gap-1.5 shrink-0"
          >
            {adding ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Plus className="h-3.5 w-3.5" />}
            Add
          </button>
        </div>
        <p className="text-[9px] text-slate-400 mt-1.5">Rules can only be added or deactivated — never deleted. This is enforced by design.</p>
      </div>

      {/* Rules list */}
      <div className="overflow-x-auto">
        {rules.length === 0 ? (
          <div className="p-8 text-center text-xs text-slate-400 font-mono">
            No exclusion rules defined. Add categories above.
          </div>
        ) : (
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="border-b border-slate-200 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">
                <th className="p-4 pl-6">Category</th>
                <th className="p-4">Description</th>
                <th className="p-4">Status</th>
                <th className="p-4">Added</th>
                <th className="p-4 pr-6 text-right">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-xs">
              {rules.map(r => (
                <tr key={r.id} className={`hover:bg-slate-50/30 ${!r.isActive ? 'opacity-50' : ''}`}>
                  <td className="p-4 pl-6 font-semibold text-slate-900">{r.category}</td>
                  <td className="p-4 text-slate-500 max-w-[250px] truncate">{r.description || '—'}</td>
                  <td className="p-4">
                    <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${r.isActive ? 'bg-red-50 text-red-700' : 'bg-slate-100 text-slate-500'}`}>
                      {r.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="p-4 text-slate-400 font-mono text-[10px]">{new Date(r.createdAt).toLocaleDateString()}</td>
                  <td className="p-4 pr-6 text-right">
                    <button
                      onClick={() => handleToggle(r.id)}
                      disabled={togglingId === r.id}
                      className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[10px] font-bold cursor-pointer transition-colors ${r.isActive ? 'bg-slate-100 hover:bg-slate-200 text-slate-600' : 'bg-emerald-50 hover:bg-emerald-100 text-emerald-700'}`}
                    >
                      {togglingId === r.id ? <Loader2 className="h-3 w-3 animate-spin" /> :
                       r.isActive ? <><ToggleRight className="h-3.5 w-3.5" /> Deactivate</> :
                                    <><ToggleLeft className="h-3.5 w-3.5" /> Activate</>}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};
