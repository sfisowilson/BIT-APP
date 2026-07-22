import React, { useState, useEffect } from 'react';
import { Bell, BellOff, Loader2 } from 'lucide-react';
import { fetchWithAuth } from '../apiClient';

const ALL_NOTIFICATION_TYPES = [
  { key: 'UserCreated', label: 'Account created' },
  { key: 'RoleChanged', label: 'Role changes' },
  { key: 'StatusChanged', label: 'Account status changes' },
  { key: 'UserDeleted', label: 'Account removal' },
  { key: 'RoleRequestApproved', label: 'Role request approved' },
  { key: 'RoleRequestRejected', label: 'Role request rejected' },
  { key: 'RenderCompleted', label: 'Render completed' },
  { key: 'RenderFailed', label: 'Render failed' },
  { key: 'PlacementApproved', label: 'Placement approved' },
  { key: 'PlacementRejected', label: 'Placement rejected' },
  { key: 'CampaignCreated', label: 'Campaign created' },
  { key: 'IngestionCompleted', label: 'Ingestion complete' },
  { key: 'IngestionFailed', label: 'Ingestion failed' },
];

export const NotificationPreferencesPanel: React.FC = () => {
  const [muted, setMuted] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    load();
  }, []);

  const load = async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth('/api/user/preferences');
      const data = await res.json();
      setMuted(data.mutedNotifications || []);
    } catch (err) {
      console.error('Failed to load preferences', err);
    } finally {
      setLoading(false);
    }
  };

  const toggle = async (type: string) => {
    const newMuted = muted.includes(type)
      ? muted.filter(t => t !== type)
      : [...muted, type];
    setMuted(newMuted);
    setSaving(true);
    try {
      await fetchWithAuth('/api/user/preferences', {
        method: 'PUT',
        body: JSON.stringify({ mutedNotifications: newMuted }),
      });
    } catch (err) {
      console.error('Failed to save preferences', err);
      setMuted(muted); // revert
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="p-4 text-center">
        <Loader2 className="h-4 w-4 animate-spin text-slate-400 mx-auto" />
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2 mb-2">
        <Bell className="h-3.5 w-3.5 text-slate-400" />
        <span className="text-[10px] font-bold text-slate-500 uppercase tracking-wider font-mono">Notification Preferences</span>
        {saving && <Loader2 className="h-3 w-3 animate-spin text-blue-500 ml-auto" />}
      </div>
      <div className="space-y-1 max-h-[200px] overflow-y-auto">
        {ALL_NOTIFICATION_TYPES.map(nt => (
          <label
            key={nt.key}
            className="flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-slate-50 cursor-pointer transition-colors"
          >
            <input
              type="checkbox"
              checked={!muted.includes(nt.key)}
              onChange={() => toggle(nt.key)}
              className="h-3 w-3 rounded border-slate-300 text-blue-600 focus:ring-blue-500"
            />
            <span className="text-[10px] text-slate-600">{nt.label}</span>
            {muted.includes(nt.key) && <BellOff className="h-3 w-3 text-slate-300 ml-auto" />}
          </label>
        ))}
      </div>
    </div>
  );
};
