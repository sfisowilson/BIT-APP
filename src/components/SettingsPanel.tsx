import React, { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { Settings, Mail, Upload, Sliders, Save, Loader2, CheckCircle, AlertCircle, Cpu, Key, Video, Radar } from 'lucide-react';
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
  const [activeTab, setActiveTab] = useState<'smtp' | 'upload' | 'pipeline' | 'engine'>('smtp');
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
      // engine_tracking's dropdown has only one real option (sam3) and displays it via a
      // `|| 'sam3'` fallback when empty — which means the field can look correctly selected
      // while actually being empty in state, and there's no way to fix that by interacting
      // with a single-option <select> (browsers only fire onChange on an actual selection
      // change). Normalize it into state on load so Save always persists the real value.
      if (!data.engine_tracking) data.engine_tracking = 'sam3';
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
      setSaveMsg({ type: 'error', text: err.message });
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
      setSaveMsg({ type: 'error', text: err.message });
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
        className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-all"
      />
    </div>
  );

  if (loading) {
    return (
      <div className="bg-white border border-slate-200/90 rounded-2xl p-12 text-center">
        <Loader2 className="h-6 w-6 animate-spin text-brand-500 mx-auto mb-3" />
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
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-brand-600 hover:bg-brand-500 text-white font-bold rounded-lg text-xs cursor-pointer transition-colors disabled:opacity-50"
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
          { id: 'engine', label: 'AI Engine', icon: Cpu },
        ] as const).map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`flex items-center gap-2 px-5 py-3 text-xs font-bold transition-colors cursor-pointer border-b-2 ${
              activeTab === tab.id
                ? 'text-brand-600 border-brand-600 bg-brand-50/30'
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
            {renderField('smtp_from_email', 'From Email Address', 'noreply@brandinserts.tech')}
            {renderField('support_email', 'Support Contact Email', 'support@brandinserts.tech')}

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
                  className="flex-1 px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
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
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
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

        {activeTab === 'engine' && (
          <div className="space-y-6">
            <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 text-xs text-amber-800">
              <strong>Engine Selection</strong> — choose which AI service powers each pipeline stage. Switching takes effect on the next content ingestion. Store API keys securely — they are encrypted at rest in the database.
            </div>

            {/* ── API Keys Section ── */}
            <div>
              <div className="flex items-center gap-2 mb-3">
                <Key className="h-4 w-4 text-violet-500" />
                <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">API Keys</h4>
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {renderField('gemini_api_key', 'Gemini API Key', 'AIza...', 'password')}
                {renderField('falai_api_key', 'Fal.ai API Key', 'key-...', 'password')}
                {renderField('replicate_api_key', 'Replicate API Key', 'r8_...', 'password')}
                {renderField('google_vision_api_key', 'Google Vision API Key', 'AIza...', 'password')}
              </div>
            </div>

            {/* ── Surface Detection Engine ── */}
            <div className="pt-3 border-t border-slate-100">
              <div className="flex items-center gap-2 mb-3">
                <Cpu className="h-4 w-4 text-brand-500" />
                <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">Surface Detection Engine</h4>
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Engine</label>
                  <select
                    value={settings['engine_detection'] || 'replicate'}
                    onChange={e => updateField('engine_detection', e.target.value)}
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                  >
                    <option value="yolo">YOLO — Real-time object detection (local)</option>
                    <option value="grounding-dino">Grounding DINO v2 — Open-vocabulary (local)</option>
                    <option value="gemini">Gemini 3 Flash — Multimodal (cloud)</option>
                    <option value="google">Google — Cloud Vision</option>
                    <option value="replicate">Replicate — SAM 3 (cloud)</option>
                  </select>
                </div>
                {renderField('gemini_model', 'Gemini Model', 'gemini-3-flash')}
                {renderField('gemini_timeout_seconds', 'Request Timeout (seconds)', '90', 'number')}

                {/* Gemini API quota status */}
                {settings['gemini_quota_status'] && (() => {
                  try {
                    const q = JSON.parse(settings['gemini_quota_status']);
                    const remaining = parseInt(q.remaining);
                    const limit = parseInt(q.limit);
                    const isValid = !isNaN(remaining) && !isNaN(limit) && limit > 0;
                    const pct = isValid ? Math.round((remaining / limit) * 100) : 0;
                    const color = !isValid ? 'text-slate-400' : remaining <= 0 ? 'text-red-500' : pct < 25 ? 'text-amber-500' : 'text-emerald-500';
                    const label = !isValid ? 'Waiting for first API call...' : `${remaining}/${limit} remaining`;
                    return (
                      <div className="col-span-2 bg-slate-50 border border-slate-200 rounded-lg p-3 mt-1">
                        <div className="flex items-center justify-between mb-1">
                          <span className="text-[10px] font-bold text-slate-500 uppercase tracking-wider font-mono">Gemini API Quota</span>
                          <span className={`text-[10px] font-bold font-mono ${color}`}>{label}</span>
                        </div>
                        {isValid && (
                          <div className="w-full bg-slate-200 rounded-full h-1.5 overflow-hidden">
                            <div className={`h-full rounded-full transition-all ${remaining <= 0 ? 'bg-red-500' : pct < 25 ? 'bg-amber-500' : 'bg-emerald-500'}`}
                              style={{ width: `${Math.max(0, pct)}%` }} />
                          </div>
                        )}
                        <div className="text-[9px] text-slate-400 font-mono mt-1">
                          Last checked: {q.checkedAt || 'never'} | Retry after: {q.retryAfter || q.retryAfterSeconds || '?'}s
                        </div>
                      </div>
                    );
                  } catch { return null; }
                })()}
              </div>

              {/* YOLO-specific settings */}
              {settings['engine_detection'] === 'yolo' && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-3">
                {renderField('yolo_service_url', 'YOLO Service URL', 'http://localhost:8001')}
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Model Size</label>
                  <select
                    value={settings['yolo_model_size'] || 'large'}
                    onChange={e => updateField('yolo_model_size', e.target.value)}
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                  >
                    <option value="nano">Nano — ~6 MB, fastest</option>
                    <option value="small">Small — ~23 MB</option>
                    <option value="medium">Medium — ~52 MB</option>
                    <option value="large">Large — ~87 MB</option>
                    <option value="xlarge">XLarge — ~116 MB, most accurate</option>
                  </select>
                </div>
                {renderField('yolo_confidence', 'Confidence (0–1)', '0.35', 'number')}
                {renderField('yolo_iou', 'IoU Threshold (0–1)', '0.45', 'number')}
                {renderField('yolo_frame_skip', 'Frame Skip (1 = every frame)', '1', 'number')}
              </div>
              )}

              {/* Grounding DINO v2 settings */}
              {settings['engine_detection'] === 'grounding-dino' && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-3">
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Model Variant</label>
                  <select
                    value={settings['gd_model_variant'] || 'base'}
                    onChange={e => updateField('gd_model_variant', e.target.value)}
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                  >
                    <option value="base">Base — Full accuracy</option>
                    <option value="tiny">Tiny — Faster, lower accuracy</option>
                  </select>
                </div>
                {renderField('gd_box_threshold', 'Box Threshold (0–1)', '0.25', 'number')}
                {renderField('gd_text_threshold', 'Text Threshold (0–1)', '0.20', 'number')}
                {renderField('gd_detection_interval', 'Detection Interval (frames)', '10', 'number')}
                {renderField('gd_flow_motion_threshold', 'Flow Motion Threshold', '2.5', 'number')}
                {renderField('gd_track_min_frames', 'Min Track Frames', '3', 'number')}
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Modules</label>
                  <div className="space-y-1.5 mt-1">
                    {[
                      ['gd_enable_sam', 'SAM Segmentation'],
                      ['gd_enable_depth', 'Depth Anything V2'],
                      ['gd_enable_brand_safety', 'CLIP Brand Safety'],
                      ['gd_enable_tracking', 'Multi-Frame Tracking'],
                      ['gd_adaptive_frame_skip', 'Adaptive Frame Skip'],
                    ].map(([key, label]) => (
                      <label key={key} className="flex items-center gap-2 text-xs text-slate-600 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={settings[key] !== 'false'}
                          onChange={e => updateField(key, e.target.checked ? 'true' : 'false')}
                          className="rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                        />
                        {label}
                      </label>
                    ))}
                  </div>
                </div>
              </div>
              )}

              {/* Replicate SAM 3 settings */}
              {settings['engine_detection'] === 'replicate' && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-3">
                {renderField('replicate_sam3_model', 'SAM 3 Model Slug', 'lucataco/sam3-image')}
                {renderField('replicate_sam3_version', 'Version Hash (pin this)', 'abc123def...')}
                {renderField('replicate_box_threshold', 'Box Threshold (0–1)', '0.25', 'number')}
                {renderField('replicate_text_threshold', 'Text Threshold (0–1)', '0.20', 'number')}
              </div>
              )}
            </div>

            {/* ── Brand Analysis Engine ── */}
            <div className="pt-3 border-t border-slate-100">
              <div className="flex items-center gap-2 mb-3">
                <Radar className="h-4 w-4 text-emerald-500" />
                <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">Brand Analysis Engine</h4>
              </div>
              <div>
                <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Engine</label>
                <select
                  value={settings['engine_brand_analysis'] || 'gemini'}
                  onChange={e => updateField('engine_brand_analysis', e.target.value)}
                  className="w-full max-w-xs px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                >
                  <option value="google">Google — Cloud Vision (logo + text)</option>
                  <option value="gemini">Gemini 3 Flash — Multimodal analysis</option>
                </select>
              </div>
            </div>

            {/* ── Compositing Engine ── */}
            <div className="pt-3 border-t border-slate-100">
              <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-2">Compositing Engine</h4>
              <div>
                <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Engine</label>
                <select
                  value={settings['engine_compositing'] || 'opencv'}
                  onChange={e => updateField('engine_compositing', e.target.value)}
                  className="w-full max-w-xs px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                >
                  <option value="opencv">OpenCV — FFmpeg overlay + blend (single-frame preview)</option>
                  <option value="planar-warp">Planar Warp — Deterministic homography (flat signage, pixel-perfect)</option>
                  <option value="pikaswaps">Pikaswaps — fal.ai text-driven AI swap (3D products)</option>
                  <option value="fal-kontext-kling">FLUX Kontext + Kling O1 — Frame-accurate AI placement with video propagation</option>
                </select>
                <p className="text-[9px] text-slate-400 mt-1">
                  Interactive placements (click-to-place in the Editor) route to Planar Warp or Pikaswaps automatically based on placement type — this setting only affects the legacy single-frame compositing preview.
                </p>
              </div>
            </div>

            {/* ── Surface Tracking Engine (NEW) ── */}
            <div className="pt-3 border-t border-slate-100">
              <div className="flex items-center gap-2 mb-3">
                <Video className="h-4 w-4 text-purple-500" />
                <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono">Surface Tracking Engine</h4>
              </div>
              <p className="text-[9px] text-slate-400 mb-3">Tracks a placed surface across every shot/cut in its scene using SAM 3 video-rle, re-anchoring with a text prompt at each cut. Runs automatically inside the render job when a Planar or Generative interactive placement is dispatched.</p>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">Engine</label>
                  <select
                    value={settings['engine_tracking'] || 'sam3'}
                    onChange={e => updateField('engine_tracking', e.target.value)}
                    className="w-full px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg text-xs font-mono focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500"
                  >
                    <option value="sam3">SAM 3 — Fal.ai video-rle tracking</option>
                  </select>
                </div>
                {renderField('falai_sam3_endpoint', 'SAM 3 Image Endpoint', 'https://fal.run/fal-ai/sam-3/image')}
                {renderField('sam3_video_base_url', 'Public Video Base URL', 'https://your-tunnel.example.com')}
              </div>
              <p className="text-[9px] text-slate-400 mt-2">
                Public Video Base URL must be reachable by fal.ai's servers — it's prefixed onto video/clip paths so SAM 3 can download them. In local dev this is a tunnel URL (e.g. cloudflared/ngrok); update it whenever the tunnel restarts and gets a new address.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
