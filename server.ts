import express from 'express';
import path from 'path';
import { fileURLToPath } from 'url';
import { createServer as createViteServer } from 'vite';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const PORT = 3000;

app.use(express.json({ limit: '100mb' }));
app.use(express.urlencoded({ extended: true, limit: '100mb' }));

// ── In-Memory / File-Persisted Database ─────────────────────────────────

interface UserSession {
  id: string;
  fullName: string;
  email: string;
  role: 'Admin' | 'Editor' | 'Advertiser';
  accountStatus: string;
  passwordHash?: string;
  lastLoginAt?: string;
}

let db = {
  users: [
    { id: 'usr-01', fullName: 'Sabelo Nkosi', email: 'admin@brandinserts.tech', password: 'admin123', role: 'Admin', accountStatus: 'Active' },
    { id: 'usr-02', fullName: 'Sfiso Dlamini', email: 'loverboy.sfiso@gmail.com', password: 'editor123', role: 'Editor', accountStatus: 'Active' },
    { id: 'usr-03', fullName: 'Thabo Ndlovu', email: 'advertiser@brandinserts.tech', password: 'advertiser123', role: 'Advertiser', accountStatus: 'Active' },
  ],
  contentItems: [
    {
      id: 'v-01',
      title: 'Orlando Pirates vs Kaizer Chiefs - SADC Derby Main Match',
      duration: '01:30:00',
      resolution: '1920x1080 (1080p)',
      frameRate: 50,
      sourceChannel: 'SuperSport Variety 4',
      storageKey: 's3://bit-raw-ingest/derby_pirates_chiefs_2026.mxf',
      ingestionStatus: 'Completed',
      createdAt: new Date(Date.now() - 3 * 86400000).toISOString(),
    },
    {
      id: 'v-02',
      title: 'M1 Gauteng Highway Aerial Drone - Advertising Survey Route',
      duration: '00:03:15',
      resolution: '3840x2160 (4K)',
      frameRate: 60,
      sourceChannel: 'Direct Upload (Drone-04)',
      storageKey: 's3://bit-raw-ingest/gauteng_highway_survey.mp4',
      ingestionStatus: 'Completed',
      createdAt: new Date(Date.now() - 1 * 86400000).toISOString(),
    },
    {
      id: 'v-03',
      title: 'Staged Living Room Segment - OTT Interactive Screen Test',
      duration: '00:05:40',
      resolution: '1920x1080 (1080p)',
      frameRate: 25,
      sourceChannel: 'Studio Ingest Box A',
      storageKey: 's3://bit-raw-ingest/living_room_ott_tests.mov',
      ingestionStatus: 'Staging',
      createdAt: new Date().toISOString(),
    },
  ],
  sceneItems: [
    { id: 's-01', contentId: 'v-01', startFrame: 0, endFrame: 1500, sceneIndex: 1, durationSeconds: 30, qaStatus: 'Approved' },
    { id: 's-02', contentId: 'v-01', startFrame: 1500, endFrame: 4500, sceneIndex: 2, durationSeconds: 60, qaStatus: 'Approved' },
    { id: 's-03', contentId: 'v-01', startFrame: 4500, endFrame: 7500, sceneIndex: 3, durationSeconds: 60, qaStatus: 'Unchecked' },
    { id: 's-04', contentId: 'v-02', startFrame: 0, endFrame: 1800, sceneIndex: 1, durationSeconds: 30, qaStatus: 'Approved' },
    { id: 's-05', contentId: 'v-02', startFrame: 1800, endFrame: 5400, sceneIndex: 2, durationSeconds: 60, qaStatus: 'Approved' },
  ],
  surfaceItems: [
    {
      id: 'sf-01', sceneId: 's-01',
      surfaceType: 'Stadium Perimeter LED Board',
      boundaryCoordinatesJson: JSON.stringify([{ x: 102, y: 720 }, { x: 890, y: 720 }, { x: 895, y: 790 }, { x: 100, y: 790 }]),
      estimatedDepth: 18.5,
      orientationVectorJson: JSON.stringify({ yaw: 2, pitch: -1, roll: 0 }),
      confidenceScore: 0.94, viabilityScore: 0.88, status: 'Candidate',
    },
    {
      id: 'sf-02', sceneId: 's-01',
      surfaceType: 'Spectator Face (Close-up)',
      boundaryCoordinatesJson: JSON.stringify([{ x: 450, y: 210 }, { x: 510, y: 210 }, { x: 510, y: 280 }, { x: 450, y: 280 }]),
      estimatedDepth: 4.2,
      orientationVectorJson: JSON.stringify({ yaw: 45, pitch: 10, roll: 5 }),
      confidenceScore: 0.98, viabilityScore: 0.0, status: 'Excluded',
      exclusionReason: 'MReq 4 (Brand Safety Violation): Face detection filter permanently triggered.',
    },
    {
      id: 'sf-03', sceneId: 's-02',
      surfaceType: 'Mid-pitch Stadium 3D Grass Mat',
      boundaryCoordinatesJson: JSON.stringify([{ x: 300, y: 550 }, { x: 980, y: 570 }, { x: 1100, y: 680 }, { x: 120, y: 640 }]),
      estimatedDepth: 22.1,
      orientationVectorJson: JSON.stringify({ yaw: -5, pitch: -22, roll: 2 }),
      confidenceScore: 0.89, viabilityScore: 0.92, status: 'Approved',
    },
    {
      id: 'sf-04', sceneId: 's-02',
      surfaceType: 'Pre-existing Coca-Cola Pitch Sign',
      boundaryCoordinatesJson: JSON.stringify([{ x: 50, y: 520 }, { x: 180, y: 520 }, { x: 180, y: 560 }, { x: 50, y: 560 }]),
      estimatedDepth: 28.0,
      orientationVectorJson: JSON.stringify({ yaw: -12, pitch: -5, roll: 0 }),
      confidenceScore: 0.96, viabilityScore: 0.15, status: 'Excluded',
      exclusionReason: 'Competitive Separation: Active Coca-Cola billboard pre-detected in-scene.',
    },
    {
      id: 'sf-05', sceneId: 's-04',
      surfaceType: 'Highway Overhead Gantry Board',
      boundaryCoordinatesJson: JSON.stringify([{ x: 400, y: 150 }, { x: 880, y: 170 }, { x: 880, y: 320 }, { x: 400, y: 300 }]),
      estimatedDepth: 35.4,
      orientationVectorJson: JSON.stringify({ yaw: 1, pitch: 5, roll: -1 }),
      confidenceScore: 0.95, viabilityScore: 0.96, status: 'Candidate',
    },
  ],
  campaigns: [
    {
      id: 'c-01', name: 'Coca-Cola SADC Winter Oasis',
      namingStructureCode: 'UZ01EP12_COKE',
      scheduleStart: new Date(Date.now() - 30 * 86400000).toISOString(),
      scheduleEnd: new Date(Date.now() + 60 * 86400000).toISOString(),
      targetRegion: 'SADC (Zambia, Zimbabwe, SA)',
      totalBudget: 450000, status: 'Active',
      createdAt: new Date(Date.now() - 30 * 86400000).toISOString(),
    },
    {
      id: 'c-02', name: 'Nike AirMax Streetwear Launch',
      namingStructureCode: 'UZ02EP04_NIKE',
      scheduleStart: new Date(Date.now() - 15 * 86400000).toISOString(),
      scheduleEnd: new Date(Date.now() + 15 * 86400000).toISOString(),
      targetRegion: 'Gauteng Metro',
      totalBudget: 280000, status: 'Active',
      createdAt: new Date(Date.now() - 15 * 86400000).toISOString(),
    },
    {
      id: 'c-03', name: 'Samsung Neo-QLED Showcase',
      namingStructureCode: 'UZ05EP08_SAMS',
      scheduleStart: new Date(Date.now() + 28 * 86400000).toISOString(),
      scheduleEnd: new Date(Date.now() + 58 * 86400000).toISOString(),
      targetRegion: 'Nationwide South Africa',
      totalBudget: 620000, status: 'Draft',
      createdAt: new Date().toISOString(),
    },
  ],
  creativeAssets: [
    { id: 'as-01', name: 'Coke Classic Red Landscape Banner', type: 'Image', storageKey: 's3://bit-assets/coke_classic_red_banner.png', fileSize: '1.2 MB', dimensions: '1920x540', brandCategory: 'Beverages (Non-Alcoholic)', campaignId: 'c-01' },
    { id: 'as-02', name: 'Nike Swoosh High-Contrast White', type: 'Logo', storageKey: 's3://bit-assets/nike_swoosh_alpha.png', fileSize: '450 KB', dimensions: '1024x1024', brandCategory: 'Apparel & Footwear', campaignId: 'c-02' },
    { id: 'as-03', name: 'Samsung Neon Glow Video Overlay', type: 'Video', storageKey: 's3://bit-assets/samsung_glow_h264.mp4', fileSize: '18.4 MB', dimensions: '1920x1080', brandCategory: 'Consumer Electronics', campaignId: 'c-03' },
  ],
  renders: [
    {
      id: 'r-01', contentId: 'v-01', surfaceId: 'sf-03',
      campaignId: 'c-01', assetId: 'as-01',
      exportPreset: 'Broadcast-ProRes',
      storageKey: 's3://bit-finished-renders/rendered_derby_coke_final.mxf',
      renderStatus: 'Finished', progress: 100,
      processingDurationMs: 42500,
      createdAt: new Date(Date.now() - 2 * 86400000).toISOString(),
    },
  ],
  eventLogs: [
    { id: 'l-01', timestamp: new Date(Date.now() - 2 * 3600000).toISOString(), eventCode: 'AUTH_JWT_SUCCESS', severity: 'Info', module: 'IdentityGateway', user: 'loverboy.sfiso@gmail.com', description: 'User logged in successfully from authorized workspace context.' },
    { id: 'l-02', timestamp: new Date(Date.now() - 1.5 * 3600000).toISOString(), eventCode: 'INGEST_META_FFMPEG', severity: 'Info', module: 'IngestionService', user: 'System', description: 'Extracted metadata stream for v-02: 4K (3840x2160) at 60fps.' },
    { id: 'l-03', timestamp: new Date(Date.now() - 1.2 * 3600000).toISOString(), eventCode: 'AI_EXCLUSION_TRIGGERED', severity: 'Warning', module: 'BrandSafetyClassifier', user: 'System', description: 'Exclusion triggered on Scene 1 spectator face overlay (MReq 4 violation).' },
    { id: 'l-04', timestamp: new Date(Date.now() - 0.5 * 3600000).toISOString(), eventCode: 'GPU_NODE_PRORES_EXPORT', severity: 'Info', module: 'CompositingEngine', user: 'System', description: 'Render composite job completed successfully in 42.5 seconds on GPU Node #03.' },
  ],
  alarms: [
    { id: 'al-01', timestamp: new Date(Date.now() - 12 * 3600000).toISOString(), severity: 'Minor', source: 'SMTP Gateway Relay', description: 'Delay detected in cellular SMS queue gateway fallback stream.', isActive: false },
    { id: 'al-02', timestamp: new Date(Date.now() - 5000).toISOString(), severity: 'Critical', source: 'GPU Render Node #02', description: 'Critical hardware timeout: VRAM capacity exceeded under concurrent batch composite loading.', isActive: true },
  ],
  platformSettings: {
    engine_detection: 'basic',
    engine_brand_analysis: 'basic',
    engine_compositing: 'basic',
  },
  brandSafetyRules: [
    { id: 'bs-01', category: 'Alcohol', active: true, description: 'Exclude alcohol ads near youth-targeted content' },
  ],
  roleRequests: [],
  userPreferences: {},
};

// Helper for paginated response
function paginate<T>(items: T[], page = 1, pageSize = 20) {
  const pageNum = Math.max(1, Number(page) || 1);
  const size = Math.max(1, Number(pageSize) || 20);
  const totalCount = items.length;
  const totalPages = Math.ceil(totalCount / size) || 1;
  const start = (pageNum - 1) * size;
  const pagedItems = items.slice(start, start + size);
  return {
    items: pagedItems,
    totalCount,
    page: pageNum,
    pageSize: size,
    totalPages,
    hasPreviousPage: pageNum > 1,
    hasNextPage: pageNum < totalPages,
  };
}

// ── Authentication Endpoints ───────────────────────────────────────────

app.post('/api/auth/login', (req, res) => {
  const { email, password } = req.body || {};
  let user = db.users.find(u => u.email.toLowerCase() === (email || '').toLowerCase());

  if (!user) {
    // Auto-provision user session for development/preview
    user = {
      id: `usr-${Date.now().toString(36)}`,
      fullName: email ? email.split('@')[0] : 'User',
      email: email || 'user@brandinserts.tech',
      password: password || 'password',
      role: email?.includes('admin') ? 'Admin' : 'Editor',
      accountStatus: 'Active',
    };
    db.users.push(user);
  }

  const token = `jwt-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 9)}`;
  const userSession: UserSession = {
    id: user.id,
    fullName: user.fullName,
    email: user.email,
    role: user.role as any,
    accountStatus: user.accountStatus,
  };

  res.json({ token, user: userSession });
});

app.post('/api/auth/refresh', (req, res) => {
  const { token } = req.body || {};
  const primaryAdmin = db.users[0];
  const userSession: UserSession = {
    id: primaryAdmin.id,
    fullName: primaryAdmin.fullName,
    email: primaryAdmin.email,
    role: primaryAdmin.role as any,
    accountStatus: primaryAdmin.accountStatus,
  };
  res.json({ token: token || 'jwt-refreshed-token', user: userSession });
});

app.post('/api/auth/forgot-password', (req, res) => {
  res.json({ success: true, message: 'Password reset link sent to your email.' });
});

app.post('/api/auth/change-password', (req, res) => {
  res.json({ success: true, message: 'Password changed successfully.' });
});

// ── Users & Role Requests ──────────────────────────────────────────────

app.get('/api/users', (req, res) => {
  const { page, pageSize } = req.query;
  res.json(paginate(db.users, Number(page), Number(pageSize)));
});

app.post('/api/users', (req, res) => {
  const newUser = { id: `usr-${Date.now().toString(36)}`, ...req.body };
  db.users.push(newUser);
  res.json(newUser);
});

app.post('/api/users/update', (req, res) => {
  const { id, ...updates } = req.body || {};
  const user = db.users.find(u => u.id === id);
  if (user) {
    Object.assign(user, updates);
  }
  res.json(user || req.body);
});

app.delete('/api/users/:id', (req, res) => {
  db.users = db.users.filter(u => u.id !== req.params.id);
  res.json({ success: true });
});

app.post('/api/user/request-role', (req, res) => {
  const request = { id: `req-${Date.now().toString(36)}`, timestamp: new Date().toISOString(), ...req.body };
  db.roleRequests.push(request as never);
  res.json({ success: true, message: 'Role upgrade request submitted.' });
});

app.get('/api/user/preferences', (req, res) => {
  res.json(db.userPreferences);
});

app.post('/api/user/preferences', (req, res) => {
  db.userPreferences = { ...db.userPreferences, ...req.body };
  res.json({ success: true, preferences: db.userPreferences });
});

// ── Campaigns ─────────────────────────────────────────────────────────

app.get('/api/campaigns', (req, res) => {
  res.json(db.campaigns);
});

app.get('/api/campaigns/:id', (req, res) => {
  const item = db.campaigns.find(c => c.id === req.params.id);
  if (!item) return res.status(404).json({ error: 'Campaign not found' });
  res.json(item);
});

app.post('/api/campaigns', (req, res) => {
  const existing = db.campaigns.find(c => c.id === req.body.id);
  if (existing) {
    Object.assign(existing, req.body);
    return res.json(existing);
  }
  const newCamp = { id: `c-${Date.now().toString(36)}`, createdAt: new Date().toISOString(), ...req.body };
  db.campaigns.push(newCamp);
  res.json(newCamp);
});

app.delete('/api/campaigns/:id', (req, res) => {
  db.campaigns = db.campaigns.filter(c => c.id !== req.params.id);
  res.json({ success: true });
});

// ── Content ───────────────────────────────────────────────────────────

app.get('/api/content', (req, res) => {
  let items = [...db.contentItems];
  if (req.query.ingestionStatus) {
    items = items.filter(i => i.ingestionStatus === req.query.ingestionStatus);
  }
  if (req.query.page || req.query.pageSize) {
    return res.json(paginate(items, Number(req.query.page), Number(req.query.pageSize)));
  }
  res.json(items);
});

app.get('/api/content/:id', (req, res) => {
  const item = db.contentItems.find(c => c.id === req.params.id);
  if (!item) return res.status(404).json({ error: 'Content item not found' });
  res.json(item);
});

app.post('/api/content', (req, res) => {
  const newItem = {
    id: `v-${Date.now().toString(36)}`,
    ingestionStatus: 'Staging',
    createdAt: new Date().toISOString(),
    ...req.body,
  };
  db.contentItems.unshift(newItem);
  res.json(newItem);
});

app.post('/api/content/upload', (req, res) => {
  const newItem = {
    id: `v-${Date.now().toString(36)}`,
    title: req.body.title || 'Uploaded Video Segment',
    duration: '00:02:30',
    resolution: '1920x1080 (1080p)',
    frameRate: 30,
    sourceChannel: 'Direct Upload',
    storageKey: `/api/content/file/uploaded_${Date.now()}.mp4`,
    ingestionStatus: 'Completed',
    createdAt: new Date().toISOString(),
  };
  db.contentItems.unshift(newItem);
  res.json(newItem);
});

app.delete('/api/content/:id', (req, res) => {
  db.contentItems = db.contentItems.filter(c => c.id !== req.params.id);
  res.json({ success: true });
});

app.get('/api/content/:id/scenes', (req, res) => {
  const scenes = db.sceneItems.filter(s => s.contentId === req.params.id);
  res.json(scenes);
});

app.post('/api/content/:contentId/transition', (req, res) => {
  const item = db.contentItems.find(c => c.id === req.params.contentId);
  if (item) item.ingestionStatus = req.body.targetStage || 'Completed';
  res.json({ success: true, id: req.params.contentId, ingestionStatus: item?.ingestionStatus, message: 'Stage transitioned' });
});

app.post('/api/content/:contentId/retranscode', (req, res) => {
  res.json({ success: true, id: req.params.contentId, ingestionStatus: 'Transcoding', message: 'Transcoding restarted' });
});

app.post('/api/content/:contentId/redetect-scenes', (req, res) => {
  res.json({ success: true, id: req.params.contentId, ingestionStatus: 'SceneDetection', message: 'Scene detection restarted' });
});

app.post('/api/content/:contentId/mark-failed', (req, res) => {
  const item = db.contentItems.find(c => c.id === req.params.contentId);
  if (item) item.ingestionStatus = 'Failed';
  res.json({ success: true, id: req.params.contentId, ingestionStatus: 'Failed' });
});

app.post('/api/content/:contentId/reset', (req, res) => {
  const item = db.contentItems.find(c => c.id === req.params.contentId);
  if (item) item.ingestionStatus = 'Staging';
  res.json({ success: true, id: req.params.contentId, ingestionStatus: 'Staging', message: 'Pipeline reset' });
});

// ── Scenes & Surfaces ──────────────────────────────────────────────────

app.get('/api/scenes/:sceneId/surfaces', (req, res) => {
  const surfaces = db.surfaceItems.filter(s => s.sceneId === req.params.sceneId);
  res.json(surfaces);
});

app.post('/api/surfaces/:surfaceId/approve', (req, res) => {
  const surface = db.surfaceItems.find(s => s.id === req.params.surfaceId);
  if (surface) {
    surface.status = surface.status === 'Approved' ? 'Candidate' : 'Approved';
  }
  res.json(surface || { success: true });
});

app.post('/api/scenes/update', (req, res) => {
  const { id, ...updates } = req.body || {};
  const scene = db.sceneItems.find(s => s.id === id);
  if (scene) Object.assign(scene, updates);
  res.json(scene || { success: true });
});

app.post('/api/scenes/ai-suggest-assets', (req, res) => {
  res.json({ suggestedAssetIds: db.creativeAssets.map(a => a.id), confidence: 0.92 });
});

app.post('/api/video/ai-split-analyze', (req, res) => {
  res.json({
    scenes: [
      { startFrame: 0, endFrame: 1500, durationSeconds: 30, score: 0.95 },
      { startFrame: 1500, endFrame: 4500, durationSeconds: 60, score: 0.88 },
    ],
  });
});

app.post('/api/video/ai-split-save', (req, res) => {
  res.json({ success: true, message: 'Scenes split and saved.' });
});

// ── Creative Assets ───────────────────────────────────────────────────

app.get('/api/assets', (req, res) => {
  let items = [...db.creativeAssets];
  if (req.query.campaignId) {
    items = items.filter(a => a.campaignId === req.query.campaignId);
  }
  res.json(items);
});

app.post('/api/assets', (req, res) => {
  const newAsset = { id: `as-${Date.now().toString(36)}`, ...req.body };
  db.creativeAssets.push(newAsset);
  res.json(newAsset);
});

app.post('/api/assets/upload', (req, res) => {
  const newAsset = {
    id: `as-${Date.now().toString(36)}`,
    name: 'New Asset',
    type: 'Image',
    storageKey: `/api/assets/file/asset_${Date.now()}.png`,
    fileSize: '1.0 MB',
    dimensions: '1920x1080',
    brandCategory: 'General',
    campaignId: '',
  };
  db.creativeAssets.push(newAsset);
  res.json(newAsset);
});

app.post('/api/assets/:id', (req, res) => {
  const asset = db.creativeAssets.find(a => a.id === req.params.id);
  if (asset) Object.assign(asset, req.body);
  res.json(asset || { success: true });
});

app.post('/api/assets/:id/campaign/:campaignId', (req, res) => {
  const asset = db.creativeAssets.find(a => a.id === req.params.id);
  if (asset) asset.campaignId = req.params.campaignId;
  res.json(asset || { success: true });
});

app.post('/api/assets/:id/unassociate', (req, res) => {
  const asset = db.creativeAssets.find(a => a.id === req.params.id);
  if (asset) delete (asset as any).campaignId;
  res.json(asset || { success: true });
});

app.delete('/api/assets/:id', (req, res) => {
  db.creativeAssets = db.creativeAssets.filter(a => a.id !== req.params.id);
  res.json({ success: true });
});

// ── Renders & Compositing ──────────────────────────────────────────────

app.get('/api/renders', (req, res) => {
  let items = [...db.renders];
  if (req.query.campaignId) {
    items = items.filter(r => r.campaignId === req.query.campaignId);
  }
  res.json(items);
});

app.post('/api/renders', (req, res) => {
  const newRender = {
    id: `r-${Date.now().toString(36)}`,
    renderStatus: 'Finished',
    progress: 100,
    processingDurationMs: 35000,
    storageKey: 's3://bit-finished-renders/render_output.mxf',
    createdAt: new Date().toISOString(),
    ...req.body,
  };
  db.renders.unshift(newRender);
  res.json(newRender);
});

app.post('/api/compositing/preview', (req, res) => {
  res.json({ success: true, previewUrl: '/api/renders/sample_preview.jpg' });
});

// ── Stats, Logs, Alarms & Admin ────────────────────────────────────────

app.get('/api/stats/summary', (req, res) => {
  res.json({
    totalContent: db.contentItems.length,
    totalScenes: db.sceneItems.length,
    totalSurfaces: db.surfaceItems.length,
    totalRenders: db.renders.length,
    totalCampaigns: db.campaigns.length,
    activeAlarms: db.alarms.filter(a => a.isActive).length,
    rendersLast7Days: db.renders.length,
    contentLast7Days: db.contentItems.length,
    avgRenderTimeMs: 38500,
  });
});

app.get('/api/logs', (req, res) => {
  const { page, pageSize } = req.query;
  res.json(paginate(db.eventLogs, Number(page), Number(pageSize)));
});

app.post('/api/logs', (req, res) => {
  const newLog = { id: `l-${Date.now().toString(36)}`, timestamp: new Date().toISOString(), ...req.body };
  db.eventLogs.unshift(newLog);
  res.json(newLog);
});

app.get('/api/alarms', (req, res) => {
  const { page, pageSize } = req.query;
  res.json(paginate(db.alarms, Number(page), Number(pageSize)));
});

app.post('/api/alarms/:id/clear', (req, res) => {
  const alarm = db.alarms.find(a => a.id === req.params.id);
  if (alarm) alarm.isActive = false;
  res.json(alarm || { success: true });
});

app.post('/api/alarms/trigger', (req, res) => {
  const alarm = { id: `al-${Date.now().toString(36)}`, timestamp: new Date().toISOString(), isActive: true, ...req.body };
  db.alarms.unshift(alarm);
  res.json(alarm);
});

app.get('/api/usage/csv', (req, res) => {
  res.setHeader('Content-Type', 'text/csv');
  res.setHeader('Content-Disposition', 'attachment; filename=usage.csv');
  res.send('Timestamp,User,Module,EventCode\n' + db.eventLogs.map(l => `${l.timestamp},${l.user},${l.module},${l.eventCode}`).join('\n'));
});

app.get('/api/admin/settings', (req, res) => {
  res.json(db.platformSettings);
});

app.post('/api/admin/settings', (req, res) => {
  db.platformSettings = { ...db.platformSettings, ...req.body };
  res.json({ success: true, settings: db.platformSettings });
});

app.post('/api/admin/settings/test-email', (req, res) => {
  res.json({ success: true, message: 'Test email dispatched successfully.' });
});

app.get('/api/admin/brand-safety', (req, res) => {
  res.json(db.brandSafetyRules);
});

app.post('/api/admin/brand-safety', (req, res) => {
  const rule = { id: `bs-${Date.now().toString(36)}`, active: true, ...req.body };
  db.brandSafetyRules.push(rule);
  res.json(rule);
});

app.post('/api/admin/brand-safety/:id/toggle', (req, res) => {
  const rule = db.brandSafetyRules.find(r => r.id === req.params.id);
  if (rule) rule.active = !rule.active;
  res.json(rule || { success: true });
});

app.get('/api/notifications/attention', (req, res) => {
  res.json({
    roleRequestsCount: db.roleRequests.length,
    activeAlarmsCount: db.alarms.filter(a => a.isActive).length,
    pendingSurfacesCount: db.surfaceItems.filter(s => s.status === 'Candidate').length,
  });
});

app.get('/api/admin/role-requests', (req, res) => {
  const { page, pageSize } = req.query;
  res.json(paginate(db.roleRequests, Number(page), Number(pageSize)));
});

app.post('/api/admin/role-requests/:id/:action', (req, res) => {
  db.roleRequests = db.roleRequests.filter((r: any) => r.id !== req.params.id);
  res.json({ success: true, action: req.params.action });
});

// ── Vite Middleware / Static Serving ───────────────────────────────────

async function start() {
  if (process.env.NODE_ENV !== 'production') {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), 'dist');
    app.use(express.static(distPath));
    app.get('*', (_req, res) => {
      res.sendFile(path.join(distPath, 'index.html'));
    });
  }

  app.listen(PORT, '0.0.0.0', () => {
    console.log(`BIT platform server running on http://0.0.0.0:${PORT}`);
  });
}

start();
