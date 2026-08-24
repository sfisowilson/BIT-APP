import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { UserPlus, Check, X, Clock, Loader2 } from 'lucide-react';
import { fetchWithAuth } from '../apiClient';

interface RoleRequestItem {
  id: string;
  userId: string;
  fullName: string;
  email: string;
  role: string;
  requestedRole: string;
  reason: string | null;
  status: string;
  requestedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
}

export const RoleRequestsPanel: React.FC = () => {
  const [requests, setRequests] = useState<RoleRequestItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [filter, setFilter] = useState<'all' | 'Pending' | 'Approved' | 'Rejected'>('all');

  useEffect(() => { loadRequests(); }, [filter]);

  const loadRequests = async () => {
    setLoading(true);
    try {
      const qs = filter !== 'all' ? `?status=${filter}` : '';
      const res = await fetchWithAuth(`/api/admin/role-requests${qs}`);
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error((err as any).error || 'Failed to load role requests');
      }
      const data = await res.json();
      setRequests(Array.isArray(data) ? data : []);
    } catch (err) {
      console.error('Failed to load role requests', err);
      setRequests([]);
    } finally {
      setLoading(false);
    }
  };

  const handleDecision = async (id: string, action: 'approve' | 'reject') => {
    setActionLoading(id);
    try {
      const res = await fetchWithAuth(`/api/admin/role-requests/${id}/${action}`, { method: 'POST' });
      if (!res.ok) throw new Error();
      await loadRequests();
    } catch {
      alert(`Failed to ${action} request.`);
    } finally {
      setActionLoading(null);
    }
  };

  const pendingCount = requests.filter(r => r.status === 'Pending').length;

  if (loading) {
    return (
      <div className="bg-white border border-slate-200/90 rounded-2xl p-8 text-center">
        <Loader2 className="h-5 w-5 animate-spin text-brand-500 mx-auto mb-2" />
        <p className="text-xs text-slate-400 font-mono">Loading role requests...</p>
      </div>
    );
  }

  return (
    <div className="bg-white border border-slate-200/90 rounded-2xl shadow-sm overflow-hidden">
      <div className="p-5 border-b border-slate-200 bg-slate-50/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="p-2 bg-amber-50 text-amber-600 rounded-lg">
            <UserPlus className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-extrabold text-slate-800 uppercase tracking-widest font-display">Role Requests</h3>
            <p className="text-[10px] text-slate-400 mt-0.5">
              {pendingCount} pending · {requests.length} total
            </p>
          </div>
        </div>
        <div className="flex gap-1">
          {(['all', 'Pending', 'Approved', 'Rejected'] as const).map(f => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-3 py-1 rounded-lg text-[10px] font-bold cursor-pointer transition-colors ${
                filter === f ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-500 hover:bg-slate-200'
              }`}
            >
              {f === 'all' ? 'All' : f}
            </button>
          ))}
        </div>
      </div>

      {requests.length === 0 ? (
        <div className="p-12 text-center text-xs text-slate-400 font-mono">
          <Clock className="h-6 w-6 text-slate-300 mx-auto mb-2" />
          No role requests found.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50/20 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">
                <th className="p-4 pl-6">User</th>
                <th className="p-4">Current Role</th>
                <th className="p-4">Requested</th>
                <th className="p-4">Reason</th>
                <th className="p-4">Status</th>
                <th className="p-4 pr-6 text-right">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-xs">
              {requests.map(r => (
                <tr key={r.id} className="hover:bg-slate-50/30">
                  <td className="p-4 pl-6">
                    <div className="font-semibold text-slate-900">{r.fullName}</div>
                    <div className="text-slate-400 font-mono text-[10px]">{r.email}</div>
                  </td>
                  <td className="p-4">
                    <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-slate-100 text-slate-600">{r.role}</span>
                  </td>
                  <td className="p-4">
                    <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                      r.requestedRole === 'Admin' ? 'bg-brand-50 text-brand-700' :
                      r.requestedRole === 'Editor' ? 'bg-indigo-50 text-indigo-700' :
                      'bg-emerald-50 text-emerald-700'
                    }`}>{r.requestedRole}</span>
                  </td>
                  <td className="p-4 text-slate-500 max-w-[200px] truncate" title={r.reason || undefined}>{r.reason || '—'}</td>
                  <td className="p-4">
                    <span className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                      r.status === 'Approved' ? 'bg-emerald-50 text-emerald-700' :
                      r.status === 'Rejected' ? 'bg-red-50 text-red-700' :
                      'bg-amber-50 text-amber-700'
                    }`}>{r.status}</span>
                  </td>
                  <td className="p-4 pr-6 text-right">
                    {r.status === 'Pending' && (
                      <div className="flex items-center justify-end gap-1">
                        <button
                          onClick={() => handleDecision(r.id, 'approve')}
                          disabled={actionLoading === r.id}
                          className="px-2.5 py-1 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-[10px] font-bold cursor-pointer transition-colors disabled:opacity-50 flex items-center gap-1"
                        >
                          {actionLoading === r.id ? <Loader2 className="h-3 w-3 animate-spin" /> : <Check className="h-3 w-3" />}
                          Approve
                        </button>
                        <button
                          onClick={() => handleDecision(r.id, 'reject')}
                          disabled={actionLoading === r.id}
                          className="px-2.5 py-1 bg-red-500 hover:bg-red-400 text-white rounded-lg text-[10px] font-bold cursor-pointer transition-colors disabled:opacity-50 flex items-center gap-1"
                        >
                          <X className="h-3 w-3" />
                          Reject
                        </button>
                      </div>
                    )}
                    {r.status !== 'Pending' && (
                      <span className="text-[10px] text-slate-400">{r.reviewedBy} · {new Date(r.reviewedAt!).toLocaleDateString()}</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};
