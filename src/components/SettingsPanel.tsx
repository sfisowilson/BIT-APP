import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { Settings, Mail, Upload, Sliders, Save, Loader2, CheckCircle, AlertCircle } from 'lucide-react';
import { fetchWithAuth } from '../apiClient';

type SettingsMap = Record<string, string>;

interface SettingsPanelProps {
  /** Re-fetch handler so AdminConsoleTab can refresh if needed */
}

export const SettingsPanel: React.FC<SettingsPanelProps> = () => {
  const [settings, setSettings] = useState<SettingsMap>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saveMsg, setSaveMsg] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [activeTab, setActiveTab] = useState<'smtp' | 'upload' | 'pipeline'>('smtp');
  const [testEmail, setTestEmail] = useState('');
  const [testingEmail, setTestingEmail] = useState(false);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth('/api/admin/settings');
      const data = await res.json();
      setSettings(data);
    } catch (err) {
      console.error('Failed to load settings', err);
    } finally {
      setLoading(false);
    }
  };

  const updateField = (key: string, value: string) => {
    setSettings(prev => ({ ...prev, [key]: value }));
  };

  const handleSave = async () => {
    setSaving(true);
    setSaveMsg(null);
    try {
      const res = await fetchWithAuth('/api/admin/settings', {
        method: 'PUT',
        body: JSON.stringify(settings),
      });
      if (!res.ok) throw new Error('Save failed');
      await res.json();
      setSaveMsg({ type: 'success', text: 'Settings saved successfully.' });
    } catch (err: any) {
      setSaveMsg({ type: 'error', text: err.message || 'Failed to save settings.' });
    } finally {
      setSaving(false);
      setTimeout(() => setSaveMsg(null), 4000);
    }
  };

  const handleTestEmail = async () => {
    if (!testEmail.trim()) return;
    setTestingEmail(true);
    try {
      const res = await fetchWithAuth('/api/admin/settings/test-email', {
        method: 'POST',
        body: JSON.stringify({ toEmail: testEmail }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error);
      setSaveMsg({ type: 'success', text: data.message });
    } catch (err: any) {
      setSaveMsg({ type: 'error', text: err.message || 'Test email failed.' });
    } finally {
      setTestingEmail(false);
      setTimeout(() => setSaveMsg(null), 4000);
    }
  };

  const renderField = (key: string, label: string, placeholder: string, type: 'text' | 'number' | 'password' = 'text') => (
    <div key={key}>
      <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">{label}</label>
      <input
        type={type}
        value={settings[key] || ''}
        onChange={e => updateField(key, e.target.value)}
        placeholder={placeholder}
        className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
      />
    </div>
  );

  if (loading) {
    return (
      <div className="bg-white border border-slate-200/90 rounded-2xl p-12 text-center">
        <Loader2 className="h-6 w-6 animate-spin text-blue-500 mx-auto mb-3" />
        <p className="text-xs text-slate-400 font-mono">Loading platform settings...</p>
      </div>
    );
  }

  return (
    <div className="bg-white border border-slate-200/90 rounded-2xl shadow-sm overflow-hidden">
      {/* Header */}
      <div className="p-5 border-b border-slate-200 bg-slate-50/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="p-2 bg-amber-50 text-amber-600 rounded-lg">
            <Settings className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-extrabold text-slate-800 uppercase tracking-widest font-display">Platform Settings</h3>
            <p className="text-[10px] text-slate-400 mt-0.5">Configure SMTP, upload limits, and pipeline parameters</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {saveMsg && (
            <div className={`flex items-center gap-1 px-3 py-1.5 rounded-lg text-[10px] font-bold ${saveMsg.type === 'success' ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-red-50 text-red-700 border border-red-200'}`}>
              {saveMsg.type === 'success' ? <CheckCircle className="h-3 w-3" /> : <AlertCircle className="h-3 w-3" />}
              {saveMsg.text}
            </div>
          )}
          <button
            onClick={handleSave}
            disabled={saving}
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-bold rounded-lg text-xs cursor-pointer transition-colors disabled:opacity-50"
          >
            {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            {saving ? 'Saving...' : 'Save All Settings'}
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-slate-200">
        {([
          { id: 'smtp', label: 'SMTP Email', icon: Mail },
          { id: 'upload', label: 'Upload & Proxy', icon: Upload },
          { id: 'pipeline', label: 'Pipeline', icon: Sliders },
        ] as const).map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`flex items-center gap-2 px-5 py-3 text-xs font-bold transition-colors cursor-pointer border-b-2 ${
              activeTab === tab.id
                ? 'text-blue-600 border-blue-600 bg-blue-50/30'
                : 'text-slate-400 border-transparent hover:text-slate-600 hover:bg-slate-50'
            }`}
          >
            <tab.icon className="h-3.5 w-3.5" />
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="p-6 space-y-4">
        {activeTab === 'smtp' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {renderField('smtp_host', 'SMTP Host', 'smtp.sendgrid.net')}
              {renderField('smtp_port', 'SMTP Port', '587', 'number')}
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {renderField('smtp_user', 'SMTP Username', 'apikey')}
              {renderField('smtp_password', 'SMTP Password', '••••••••', 'password')}
            </div>
            {renderField('smtp_from_email', 'From Email Address', 'noreply@afrobotics.co.za')}

            <div className="pt-4 border-t border-slate-100">
              <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-2">
                Test SMTP Connection
              </label>
              <div className="flex gap-2">
                <input
                  type="email"
                  value={testEmail}
                  onChange={e => setTestEmail(e.target.value)}
                  placeholder="your-email@example.com"
                  className="flex-1 px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                />
                <button
                  onClick={handleTestEmail}
                  disabled={testingEmail || !testEmail.trim()}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white font-bold rounded-lg text-xs cursor-pointer transition-colors disabled:opacity-50 flex items-center gap-1.5 shrink-0"
                >
                  {testingEmail ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Mail className="h-3.5 w-3.5" />}
                  Send Test
                </button>
              </div>
              <p className="text-[9px] text-slate-400 mt-1.5">Sends a verification email. Leave SMTP Host empty to disable email (console-only mode).</p>
            </div>
          </div>
        )}

        {activeTab === 'upload' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {renderField('upload_max_video_bytes', 'Max Video Upload (bytes)', '10737418240', 'number')}
              {renderField('upload_max_asset_bytes', 'Max Asset Upload (bytes)', '104857600', 'number')}
            </div>
            <div className="pt-3 border-t border-slate-100">
              <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-3">Proxy Generation</h4>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Enabled</label>
                  <select
                    value={settings['proxy_enabled'] || 'true'}
                    onChange={e => updateField('proxy_enabled', e.target.value)}
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                  >
                    <option value="true">Yes — generate H.264 proxies</option>
                    <option value="false">No — skip proxy generation</option>
                  </select>
                </div>
                {renderField('proxy_video_bitrate', 'Video Bitrate', '8M')}
              </div>
              <div className="grid grid-cols-2 gap-4 mt-3">
                {renderField('proxy_max_width', 'Max Width (px)', '1920', 'number')}
                {renderField('proxy_max_height', 'Max Height (px)', '1080', 'number')}
              </div>
            </div>
          </div>
        )}

        {activeTab === 'pipeline' && (
          <div className="space-y-4">
            <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Frame Rate Bounds</h4>
            <div className="grid grid-cols-2 gap-4">
              {renderField('fps_min', 'Minimum FPS', '1', 'number')}
              {renderField('fps_max', 'Maximum FPS', '960', 'number')}
            </div>
            <div className="pt-3 border-t border-slate-100">
              <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-3">Scene Detection</h4>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {renderField('scene_detect_threshold', 'FFprobe Threshold (0.1–1.0)', '0.3', 'number')}
                {renderField('fallback_scene_secs', 'Fallback Scene Duration (seconds)', '30', 'number')}
              </div>
            </div>
            <div className="pt-3 border-t border-slate-100">
              <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-3">Session Timeout</h4>
              <div className="grid grid-cols-2 gap-4">
                {renderField('idle_timeout_minutes', 'Idle Timeout (minutes)', '28', 'number')}
                {renderField('idle_countdown_seconds', 'Countdown Warning (seconds)', '60', 'number')}
              </div>
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {renderField('jwt_expiry_hours', 'JWT Token Lifetime (hours)', '8', 'number')}
              {renderField('jwt_refresh_window_hours', 'JWT Refresh Window (hours)', '2', 'number')}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
