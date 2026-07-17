import { 
  ContentItem, 
  SceneItem, 
  SurfaceItem, 
  CampaignItem, 
  CreativeAsset, 
  RenderItem, 
  EventLog, 
  AlarmItem, 
  User 
} from './types';

// Helper to interact with LocalStorage database
const getStorage = <T>(key: string, defaultValue: T): T => {
  const val = localStorage.getItem(key);
  if (!val) {
    localStorage.setItem(key, JSON.stringify(defaultValue));
    return defaultValue;
  }
  try {
    return JSON.parse(val);
  } catch {
    return defaultValue;
  }
};

const setStorage = (key: string, value: any) => {
  localStorage.setItem(key, JSON.stringify(value));
  // Fire event to notify listeners
  window.dispatchEvent(new CustomEvent('bit_db_update'));
};

// Initial Seeding Datasets (Aligned 100% with DbSeeder.cs)
const initialUsers: User[] = [
  {
    id: "usr-01",
    fullName: "Sabelo Nkosi",
    email: "admin@afrobotics.co.za",
    role: "Admin",
    accountStatus: "Active",
    lastLoginAt: new Date().toISOString()
  },
  {
    id: "usr-02",
    fullName: "Sfiso Dlamini",
    email: "loverboy.sfiso@gmail.com",
    role: "Editor",
    accountStatus: "Active",
    lastLoginAt: new Date().toISOString()
  },
  {
    id: "usr-03",
    fullName: "Thabo Ndlovu",
    email: "advertiser@afrobotics.co.za",
    role: "Advertiser",
    accountStatus: "Active",
    lastLoginAt: new Date().toISOString()
  }
];

const initialContent: ContentItem[] = [
  {
    id: "v-01",
    title: "Orlando Pirates vs Kaizer Chiefs - SADC Derby Main Match",
    duration: "01:30:00",
    resolution: "1920x1080 (1080p)",
    frameRate: 50,
    sourceChannel: "SuperSport Variety 4",
    storageKey: "s3://afrobotics-raw-ingest/derby_pirates_chiefs_2026.mxf",
    ingestionStatus: "Completed",
    createdAt: new Date(Date.now() - 3 * 24 * 3600 * 1000).toISOString()
  },
  {
    id: "v-02",
    title: "M1 Gauteng Highway Aerial Drone - Advertising Survey Route",
    duration: "00:03:15",
    resolution: "3840x2160 (4K)",
    frameRate: 60,
    sourceChannel: "Direct Upload (Drone-04)",
    storageKey: "s3://afrobotics-raw-ingest/gauteng_highway_survey.mp4",
    ingestionStatus: "Completed",
    createdAt: new Date(Date.now() - 24 * 3600 * 1000).toISOString()
  },
  {
    id: "v-03",
    title: "Staged Living Room Segment - OTT Interactive Screen Test",
    duration: "00:05:40",
    resolution: "1920x1080 (1080p)",
    frameRate: 25,
    sourceChannel: "Studio Ingest Box A",
    storageKey: "s3://afrobotics-raw-ingest/living_room_ott_tests.mov",
    ingestionStatus: "Staging",
    createdAt: new Date().toISOString()
  }
];

const initialScenes: SceneItem[] = [
  { id: "s-01", contentId: "v-01", startFrame: 0, endFrame: 1500, sceneIndex: 1, durationSeconds: 30, qaStatus: "Approved" },
  { id: "s-02", contentId: "v-01", startFrame: 1500, endFrame: 4500, sceneIndex: 2, durationSeconds: 60, qaStatus: "Approved" },
  { id: "s-03", contentId: "v-01", startFrame: 4500, endFrame: 7500, sceneIndex: 3, durationSeconds: 60, qaStatus: "Unchecked" },
  { id: "s-04", contentId: "v-02", startFrame: 0, endFrame: 1800, sceneIndex: 1, durationSeconds: 30, qaStatus: "Approved" },
  { id: "s-05", contentId: "v-02", startFrame: 1800, endFrame: 5400, sceneIndex: 2, durationSeconds: 60, qaStatus: "Approved" }
];

const initialSurfaces: SurfaceItem[] = [
  {
    id: "sf-01",
    sceneId: "s-01",
    surfaceType: "Stadium Perimeter LED Board",
    boundaryCoordinates: [{x: 102, y: 720}, {x: 890, y: 720}, {x: 895, y: 790}, {x: 100, y: 790}],
    estimatedDepth: 18.5,
    orientationVector: { yaw: 2, pitch: -1, roll: 0 },
    confidenceScore: 0.94,
    viabilityScore: 0.88,
    status: "Candidate"
  },
  {
    id: "sf-02",
    sceneId: "s-01",
    surfaceType: "Spectator Face (Close-up)",
    boundaryCoordinates: [{x: 450, y: 210}, {x: 510, y: 210}, {x: 510, y: 280}, {x: 450, y: 280}],
    estimatedDepth: 4.2,
    orientationVector: { yaw: 45, pitch: 10, roll: 5 },
    confidenceScore: 0.98,
    viabilityScore: 0.0,
    status: "Excluded",
    exclusionReason: "MReq 4 (Brand Safety Violation): Face detection filter permanently triggered."
  },
  {
    id: "sf-03",
    sceneId: "s-02",
    surfaceType: "Mid-pitch Stadium 3D Grass Mat",
    boundaryCoordinates: [{x: 300, y: 550}, {x: 980, y: 570}, {x: 1100, y: 680}, {x: 120, y: 640}],
    estimatedDepth: 22.1,
    orientationVector: { yaw: -5, pitch: -22, roll: 2 },
    confidenceScore: 0.89,
    viabilityScore: 0.92,
    status: "Approved"
  },
  {
    id: "sf-04",
    sceneId: "s-02",
    surfaceType: "Pre-existing Coca-Cola Pitch Sign",
    boundaryCoordinates: [{x: 50, y: 520}, {x: 180, y: 520}, {x: 180, y: 560}, {x: 50, y: 560}],
    estimatedDepth: 28.0,
    orientationVector: { yaw: -12, pitch: -5, roll: 0 },
    confidenceScore: 0.96,
    viabilityScore: 0.15,
    status: "Excluded",
    exclusionReason: "Competitive Separation: Active Coca-Cola billboard pre-detected in-scene."
  },
  {
    id: "sf-05",
    sceneId: "s-04",
    surfaceType: "Highway Overhead Gantry Board",
    boundaryCoordinates: [{x: 400, y: 150}, {x: 880, y: 170}, {x: 880, y: 320}, {x: 400, y: 300}],
    estimatedDepth: 35.4,
    orientationVector: { yaw: 1, pitch: 5, roll: -1 },
    confidenceScore: 0.95,
    viabilityScore: 0.96,
    status: "Candidate"
  }
];

const initialCampaigns: CampaignItem[] = [
  {
    id: "c-01",
    name: "Coca-Cola SADC Winter Oasis",
    namingStructureCode: "UZ01EP12_COKE",
    scheduleStart: new Date(Date.now() - 30 * 24 * 3600 * 1000).toISOString(),
    scheduleEnd: new Date(Date.now() + 60 * 24 * 3600 * 1000).toISOString(),
    targetRegion: "SADC (Zambia, Zimbabwe, SA)",
    totalBudget: 450000,
    status: "Active",
    createdAt: new Date(Date.now() - 30 * 24 * 3600 * 1000).toISOString()
  },
  {
    id: "c-02",
    name: "Nike AirMax Streetwear Launch",
    namingStructureCode: "UZ02EP04_NIKE",
    scheduleStart: new Date(Date.now() - 15 * 24 * 3600 * 1000).toISOString(),
    scheduleEnd: new Date(Date.now() + 15 * 24 * 3600 * 1000).toISOString(),
    targetRegion: "Gauteng Metro",
    totalBudget: 280000,
    status: "Active",
    createdAt: new Date(Date.now() - 15 * 24 * 3600 * 1000).toISOString()
  },
  {
    id: "c-03",
    name: "Samsung Neo-QLED Showcase",
    namingStructureCode: "UZ05EP08_SAMS",
    scheduleStart: new Date(Date.now() + 28 * 24 * 3600 * 1000).toISOString(),
    scheduleEnd: new Date(Date.now() + 58 * 24 * 3600 * 1000).toISOString(),
    targetRegion: "Nationwide South Africa",
    totalBudget: 620000,
    status: "Draft",
    createdAt: new Date().toISOString()
  }
];

const initialAssets: CreativeAsset[] = [
  {
    id: "as-01",
    name: "Coke Classic Red Landscape Banner",
    type: "Image",
    storageKey: "s3://afrobotics-assets/coke_classic_red_banner.png",
    fileSize: "1.2 MB",
    dimensions: "1920x540",
    brandCategory: "Beverages"
  },
  {
    id: "as-02",
    name: "Nike Swoosh High-Contrast White",
    type: "Logo",
    storageKey: "s3://afrobotics-assets/nike_swoosh_alpha.png",
    fileSize: "450 KB",
    dimensions: "1024x1024",
    brandCategory: "Apparel"
  },
  {
    id: "as-03",
    name: "Samsung Neon Glow Video Overlay",
    type: "Video",
    storageKey: "s3://afrobotics-assets/samsung_glow_h264.mp4",
    fileSize: "18.4 MB",
    dimensions: "1920x1080",
    brandCategory: "Electronics"
  }
];

const initialRenders: RenderItem[] = [
  {
    id: "r-01",
    contentId: "v-01",
    surfaceId: "sf-03",
    campaignId: "c-01",
    assetId: "as-01",
    exportPreset: "Broadcast-ProRes",
    storageKey: "s3://afrobotics-finished-renders/rendered_derby_coke_final.mxf",
    renderStatus: "Finished",
    progress: 100,
    processingDurationMs: 42500,
    createdAt: new Date(Date.now() - 2 * 24 * 3600 * 1000).toISOString()
  }
];

const initialLogs: EventLog[] = [
  {
    id: "l-01",
    timestamp: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    eventCode: "AUTH_JWT_SUCCESS",
    severity: "Info",
    module: "IdentityGateway",
    user: "loverboy.sfiso@gmail.com",
    description: "User logged in successfully from authorized workspace context."
  },
  {
    id: "l-02",
    timestamp: new Date(Date.now() - 1.5 * 3600 * 1000).toISOString(),
    eventCode: "INGEST_META_FFMPEG",
    severity: "Info",
    module: "IngestionService",
    user: "System",
    description: "Extracted metadata stream for v-02: 4K (3840x2160) at 60fps. Zero duration alteration."
  },
  {
    id: "l-03",
    timestamp: new Date(Date.now() - 1.2 * 3600 * 1000).toISOString(),
    eventCode: "AI_EXCLUSION_TRIGGERED",
    severity: "Warning",
    module: "BrandSafetyClassifier",
    user: "System",
    description: "Exclusion triggered on Scene 1 spectator face overlay (MReq 4 violation check: Face classification)."
  },
  {
    id: "l-04",
    timestamp: new Date(Date.now() - 0.5 * 3600 * 1000).toISOString(),
    eventCode: "GPU_NODE_PRORES_EXPORT",
    severity: "Info",
    module: "CompositingEngine",
    user: "System",
    description: "Render composite job completed successfully in 42.5 seconds on GPU Node #03."
  }
];

const initialAlarms: AlarmItem[] = [
  {
    id: "al-01",
    timestamp: new Date(Date.now() - 12 * 3600 * 1000).toISOString(),
    severity: "Minor",
    source: "SMTP Gateway Relay",
    description: "Delay detected in cellular SMS queue gateway fallback stream. Re-routing through secondary SMTP relay.",
    isActive: false
  },
  {
    id: "al-02",
    timestamp: new Date(Date.now() - 5000).toISOString(),
    severity: "Critical",
    source: "GPU Render Node #02",
    description: "Critical hardware timeout: VRAM capacity exceeded under concurrent batch composite loading on Node #02.",
    isActive: true
  }
];

// Read databases, defaulting to seeds
const getDbUsers = () => getStorage<User[]>('bit_db_users', initialUsers);
const getDbContent = () => getStorage<ContentItem[]>('bit_db_content', initialContent);
const getDbScenes = () => getStorage<SceneItem[]>('bit_db_scenes', initialScenes);
const getDbSurfaces = () => getStorage<SurfaceItem[]>('bit_db_surfaces', initialSurfaces);
const getDbCampaigns = () => getStorage<CampaignItem[]>('bit_db_campaigns', initialCampaigns);
const getDbAssets = () => getStorage<CreativeAsset[]>('bit_db_assets', initialAssets);
const getDbRenders = () => getStorage<RenderItem[]>('bit_db_renders', initialRenders);
const getDbLogs = () => getStorage<EventLog[]>('bit_db_logs', initialLogs);
const getDbAlarms = () => getStorage<AlarmItem[]>('bit_db_alarms', initialAlarms);

// Save databases
const saveDbUsers = (u: User[]) => setStorage('bit_db_users', u);
const saveDbContent = (c: ContentItem[]) => setStorage('bit_db_content', c);
const saveDbScenes = (s: SceneItem[]) => setStorage('bit_db_scenes', s);
const saveDbSurfaces = (sf: SurfaceItem[]) => setStorage('bit_db_surfaces', sf);
const saveDbCampaigns = (c: CampaignItem[]) => setStorage('bit_db_campaigns', c);
const saveDbAssets = (a: CreativeAsset[]) => setStorage('bit_db_assets', a);
const saveDbRenders = (r: RenderItem[]) => setStorage('bit_db_renders', r);
const saveDbLogs = (l: EventLog[]) => setStorage('bit_db_logs', l);
const saveDbAlarms = (al: AlarmItem[]) => setStorage('bit_db_alarms', al);

// Helper to push to log
const pushLog = (severity: "Info" | "Warning" | "Major" | "Critical", module: string, user: string, description: string) => {
  const logs = getDbLogs();
  const newLog: EventLog = {
    id: `l-${Date.now()}`,
    timestamp: new Date().toISOString(),
    eventCode: `EVENT_${module.toUpperCase()}`,
    severity,
    module,
    user,
    description
  };
  saveDbLogs([newLog, ...logs]);
};

// Simulation task that advances rendering progress in the background
let activeSimulations = new Map<string, NodeJS.Timeout>();

const runRenderSimulation = (renderId: string) => {
  if (activeSimulations.has(renderId)) return;

  const interval = setInterval(() => {
    const renders = getDbRenders();
    const rIdx = renders.findIndex(x => x.id === renderId);
    if (rIdx === -1) {
      clearInterval(interval);
      activeSimulations.delete(renderId);
      return;
    }

    const item = renders[rIdx];
    if (item.renderStatus === 'Finished' || item.renderStatus === 'Failed') {
      clearInterval(interval);
      activeSimulations.delete(renderId);
      return;
    }

    const nextProgress = Math.min(100, item.progress + 20);
    const nextStatus = nextProgress === 100 ? 'Finished' : 'Processing';
    
    renders[rIdx] = {
      ...item,
      progress: nextProgress,
      renderStatus: nextStatus,
      processingDurationMs: nextProgress === 100 ? (item.processingDurationMs || Math.floor(Math.random() * 30000) + 15000) : item.processingDurationMs
    };

    saveDbRenders(renders);

    if (nextProgress === 100) {
      clearInterval(interval);
      activeSimulations.delete(renderId);
      pushLog('Info', 'CompositingEngine', 'System', `Render composite job ${renderId} finished successfully and saved to storage.`);
    }
  }, 3000);

  activeSimulations.set(renderId, interval);
};

// Intercept window.fetch globally to serve client-side mock backend API routes
const originalFetch = window.fetch;
window.fetch = async function (input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const urlStr = typeof input === 'string' ? input : (input as Request).url || '';
  const method = (init?.method || 'GET').toUpperCase();
  
  // Only intercept /api/* routes
  if (!urlStr.includes('/api/')) {
    return originalFetch.apply(this, arguments as any);
  }

  // Parse path and query
  const parsedUrl = new URL(urlStr, window.location.origin);
  const path = parsedUrl.pathname;
  
  const makeJsonRes = (status: number, data: any) => {
    return new Response(JSON.stringify(data), {
      status,
      headers: { 'Content-Type': 'application/json' }
    });
  };

  try {
    // ----------------------------------------------------
    // AUTH LOGIC
    // ----------------------------------------------------
    if (path === '/api/auth/login' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const users = getDbUsers();
      const matched = users.find(u => u.email === body.email);
      if (matched) {
        pushLog('Info', 'IdentityGateway', matched.fullName, `User authenticated successfully.`);
        return makeJsonRes(200, {
          token: `mock-jwt-token-for-${matched.id}-${Date.now()}`,
          user: matched
        });
      }
      return makeJsonRes(401, { error: "Invalid credentials. Please verify your email and password." });
    }

    // ----------------------------------------------------
    // USERS (ADMIN CONSOLE)
    // ----------------------------------------------------
    if (path === '/api/users' && method === 'GET') {
      return makeJsonRes(200, getDbUsers());
    }

    if (path === '/api/users' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const users = getDbUsers();
      const newUser: User = {
        id: `usr-${Date.now()}`,
        fullName: body.fullName,
        email: body.email,
        role: body.role || 'Editor',
        accountStatus: 'Active',
        lastLoginAt: 'Never'
      };
      saveDbUsers([...users, newUser]);
      pushLog('Info', 'UserAdmin', 'System', `Created new user account: ${body.fullName} (${body.email})`);
      return makeJsonRes(200, newUser);
    }

    if (path === '/api/users/update' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const users = getDbUsers();
      const uIdx = users.findIndex(u => u.id === body.id);
      if (uIdx !== -1) {
        users[uIdx] = {
          ...users[uIdx],
          ...body
        };
        saveDbUsers(users);
        pushLog('Info', 'UserAdmin', 'System', `Updated user details for ${users[uIdx].fullName}`);
        return makeJsonRes(200, users[uIdx]);
      }
      return makeJsonRes(404, { error: 'User not found' });
    }

    // ----------------------------------------------------
    // CONTENT
    // ----------------------------------------------------
    if (path === '/api/content' && method === 'GET') {
      return makeJsonRes(200, getDbContent());
    }

    if (path === '/api/content' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const contents = getDbContent();
      const newContent: ContentItem = {
        id: `v-${Date.now().toString().slice(-4)}`,
        title: body.title || 'Untitled Video',
        duration: body.duration || '00:05:00',
        resolution: body.resolution || '1920x1080 (1080p)',
        frameRate: body.frameRate || 50,
        sourceChannel: body.sourceChannel || 'Direct Upload',
        storageKey: body.storageKey || `s3://afrobotics-raw-ingest/user_upload_${Date.now()}.mp4`,
        ingestionStatus: body.ingestionStatus || 'Completed',
        createdAt: new Date().toISOString()
      };
      saveDbContent([...contents, newContent]);
      pushLog('Info', 'IngestionService', 'System', `Successfully ingested new content master: ${newContent.title}. Metadata stream extracted safely.`);
      return makeJsonRes(200, newContent);
    }

    if (path.startsWith('/api/content/') && method === 'DELETE') {
      const id = path.split('/').pop();
      const contents = getDbContent();
      saveDbContent(contents.filter(c => c.id !== id));
      pushLog('Info', 'IngestionService', 'System', `Deleted content item ID: ${id}`);
      return makeJsonRes(200, { success: true });
    }

    // ----------------------------------------------------
    // SCENES & SURFACES FOR CONTENT
    // ----------------------------------------------------
    const sceneMatch = path.match(/^\/api\/content\/([^\/]+)\/scenes$/);
    if (sceneMatch && method === 'GET') {
      const contentId = sceneMatch[1];
      const scenes = getDbScenes().filter(s => s.contentId === contentId);
      return makeJsonRes(200, scenes);
    }

    const surfaceMatch = path.match(/^\/api\/scenes\/([^\/]+)\/surfaces$/);
    if (surfaceMatch && method === 'GET') {
      const sceneId = surfaceMatch[1];
      const surfaces = getDbSurfaces().filter(s => s.sceneId === sceneId);
      return makeJsonRes(200, surfaces);
    }

    // ----------------------------------------------------
    // APPROVE / REJECT SURFACES
    // ----------------------------------------------------
    const approveMatch = path.match(/^\/api\/surfaces\/([^\/]+)\/approve$/);
    if (approveMatch && method === 'POST') {
      const surfaceId = approveMatch[1];
      const surfaces = getDbSurfaces();
      const sIdx = surfaces.findIndex(s => s.id === surfaceId);
      if (sIdx !== -1) {
        surfaces[sIdx].status = 'Approved';
        delete surfaces[sIdx].exclusionReason;
        saveDbSurfaces(surfaces);
        
        // Push notification of approval
        pushLog('Info', 'WorkflowManager', 'Editor', `Approved placements inventory surface ${surfaceId}. Status locked.`);
        return makeJsonRes(200, surfaces[sIdx]);
      }
      return makeJsonRes(404, { error: 'Surface not found' });
    }

    const rejectMatch = path.match(/^\/api\/surfaces\/([^\/]+)\/exclude$/);
    if (rejectMatch && method === 'POST') {
      const surfaceId = rejectMatch[1];
      const body = JSON.parse(init?.body as string || '{}');
      const surfaces = getDbSurfaces();
      const sIdx = surfaces.findIndex(s => s.id === surfaceId);
      if (sIdx !== -1) {
        surfaces[sIdx].status = 'Excluded';
        surfaces[sIdx].exclusionReason = body.reason || 'Manually excluded by reviewer.';
        saveDbSurfaces(surfaces);
        
        pushLog('Warning', 'WorkflowManager', 'Editor', `Excluded placements surface ${surfaceId}. Reason: ${surfaces[sIdx].exclusionReason}`);
        return makeJsonRes(200, surfaces[sIdx]);
      }
      return makeJsonRes(404, { error: 'Surface not found' });
    }

    // ----------------------------------------------------
    // SCENES UPDATE & AI MODIFY
    // ----------------------------------------------------
    if (path === '/api/scenes/update' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const scenes = getDbScenes();
      const sIdx = scenes.findIndex(s => s.id === body.id);
      if (sIdx !== -1) {
        scenes[sIdx] = {
          ...scenes[sIdx],
          ...body
        };
        saveDbScenes(scenes);
        pushLog('Info', 'SceneManager', 'Editor', `Updated Scene index bounds or custom visual parameters.`);
        return makeJsonRes(200, scenes[sIdx]);
      }
      return makeJsonRes(404, { error: 'Scene not found' });
    }

    if (path === '/api/scenes/ai-modify' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const scenes = getDbScenes();
      const sIdx = scenes.findIndex(s => s.id === body.sceneId);
      if (sIdx !== -1) {
        scenes[sIdx].aiStatus = 'completed';
        scenes[sIdx].aiPrompt = body.prompt;
        scenes[sIdx].aiOutputDescription = `Generatively customized with visual adjustment matching prompt: "${body.prompt}"`;
        scenes[sIdx].aiModelUsed = 'Gemini 2.5 Flash (Compositor Edition)';
        saveDbScenes(scenes);
        
        pushLog('Info', 'AiCompositor', 'Editor', `Gemini completed scene-specific visual refinement matching custom editor instruction: "${body.prompt}"`);
        return makeJsonRes(200, { success: true, scene: scenes[sIdx] });
      }
      return makeJsonRes(404, { error: 'Scene not found' });
    }

    // ----------------------------------------------------
    // CAMPAIGNS
    // ----------------------------------------------------
    if (path === '/api/campaigns' && method === 'GET') {
      return makeJsonRes(200, getDbCampaigns());
    }

    if (path === '/api/campaigns' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const campaigns = getDbCampaigns();
      const newCampaign: CampaignItem = {
        id: `c-${Date.now().toString().slice(-4)}`,
        name: body.name || 'New Campaign',
        namingStructureCode: body.namingStructureCode || 'UZ01EP12_BRAND',
        scheduleStart: body.scheduleStart || new Date().toISOString(),
        scheduleEnd: body.scheduleEnd || new Date(Date.now() + 30 * 24 * 3600 * 1000).toISOString(),
        targetRegion: body.targetRegion || 'SADC Region',
        totalBudget: Number(body.totalBudget) || 150000,
        status: body.status || 'Active',
        createdAt: new Date().toISOString()
      };
      saveDbCampaigns([...campaigns, newCampaign]);
      pushLog('Info', 'CampaignService', 'System', `Created campaign: ${newCampaign.name} (${newCampaign.namingStructureCode})`);
      return makeJsonRes(200, newCampaign);
    }

    if (path.startsWith('/api/campaigns/') && method === 'DELETE') {
      const id = path.split('/').pop();
      const campaigns = getDbCampaigns();
      saveDbCampaigns(campaigns.filter(c => c.id !== id));
      pushLog('Info', 'CampaignService', 'System', `Deleted Campaign: ${id}`);
      return makeJsonRes(200, { success: true });
    }

    // ----------------------------------------------------
    // ASSETS
    // ----------------------------------------------------
    if (path === '/api/assets' && method === 'GET') {
      return makeJsonRes(200, getDbAssets());
    }

    if (path === '/api/assets' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const assets = getDbAssets();
      const newAsset: CreativeAsset = {
        id: `as-${Date.now().toString().slice(-4)}`,
        name: body.name || 'Untitled Asset',
        type: body.type || 'Image',
        storageKey: body.storageKey || `s3://afrobotics-assets/creative_upload_${Date.now()}.png`,
        fileSize: body.fileSize || '1.8 MB',
        dimensions: body.dimensions || '1920x1080',
        brandCategory: body.brandCategory || 'Beverages'
      };
      saveDbAssets([...assets, newAsset]);
      pushLog('Info', 'AssetLibrary', 'System', `Staged asset successfully to S3 storage bucket: ${newAsset.name}`);
      return makeJsonRes(200, newAsset);
    }

    if (path.startsWith('/api/assets/') && method === 'DELETE') {
      const id = path.split('/').pop();
      const assets = getDbAssets();
      saveDbAssets(assets.filter(a => a.id !== id));
      pushLog('Info', 'AssetLibrary', 'System', `Removed creative asset ID: ${id}`);
      return makeJsonRes(200, { success: true });
    }

    // ----------------------------------------------------
    // RENDERS
    // ----------------------------------------------------
    if (path === '/api/renders' && method === 'GET') {
      // Trigger background progresses for queued/processing jobs to simulate active GPU renders
      const renders = getDbRenders();
      renders.forEach(r => {
        if (r.renderStatus === 'Queued' || r.renderStatus === 'Processing') {
          runRenderSimulation(r.id);
        }
      });
      return makeJsonRes(200, renders);
    }

    if (path === '/api/renders' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const renders = getDbRenders();
      const newRender: RenderItem = {
        id: `r-${Date.now().toString().slice(-4)}`,
        contentId: body.contentId,
        surfaceId: body.surfaceId,
        campaignId: body.campaignId,
        assetId: body.assetId,
        exportPreset: body.exportPreset || 'Broadcast-ProRes',
        storageKey: `s3://afrobotics-finished-renders/render_${Date.now().toString().slice(-4)}_composite.mov`,
        renderStatus: 'Queued',
        progress: 0,
        processingDurationMs: 0,
        createdAt: new Date().toISOString()
      };
      
      saveDbRenders([...renders, newRender]);
      pushLog('Info', 'CompositingEngine', 'System', `Queued new video composition render job: ${newRender.id}. Forwarding to GPU cluster dispatcher.`);
      
      // Start background async progress updater
      runRenderSimulation(newRender.id);
      
      return makeJsonRes(200, newRender);
    }

    // ----------------------------------------------------
    // EVENT LOGS
    // ----------------------------------------------------
    if (path === '/api/logs' && method === 'GET') {
      return makeJsonRes(200, getDbLogs());
    }

    if (path === '/api/logs' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      pushLog(body.severity || 'Info', body.module || 'System', body.user || 'System', body.description);
      return makeJsonRes(200, { success: true });
    }

    // ----------------------------------------------------
    // ALARMS
    // ----------------------------------------------------
    if (path === '/api/alarms' && method === 'GET') {
      return makeJsonRes(200, getDbAlarms());
    }

    if (path === '/api/alarms/trigger' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const alarms = getDbAlarms();
      const newAlarm: AlarmItem = {
        id: `al-${Date.now().toString().slice(-4)}`,
        timestamp: new Date().toISOString(),
        severity: body.severity || 'Major',
        source: body.source || 'General Services',
        description: body.description || 'Simulated failure event trigger.',
        isActive: true
      };
      saveDbAlarms([newAlarm, ...alarms]);
      pushLog('Critical', 'HealthMonitor', 'System', `CRITICAL ALARM TRIGGERED on [${newAlarm.source}]: ${newAlarm.description}`);
      return makeJsonRes(200, newAlarm);
    }

    const clearAlarmMatch = path.match(/^\/api\/alarms\/([^\/]+)\/clear$/);
    if (clearAlarmMatch && method === 'POST') {
      const alarmId = clearAlarmMatch[1];
      const alarms = getDbAlarms();
      const aIdx = alarms.findIndex(a => a.id === alarmId);
      if (aIdx !== -1) {
        alarms[aIdx].isActive = false;
        saveDbAlarms(alarms);
        pushLog('Info', 'HealthMonitor', 'Editor', `Cleared system alarm of ID: ${alarmId} manually.`);
        return makeJsonRes(200, alarms[aIdx]);
      }
      return makeJsonRes(404, { error: 'Alarm not found' });
    }

    // ----------------------------------------------------
    // AI INTEGRATION: GEOMETRIC VIDEO SPLITTING & OPPORTUNITY ANALYSIS
    // ----------------------------------------------------
    if (path === '/api/video/ai-split-analyze' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const contentId = body.contentId;
      const title = body.videoTitle;
      
      // We simulate Gemini analyzing this video client-side with a beautiful structured response!
      const simulatedScenes: SceneItem[] = [
        {
          id: `s-ai-01-${contentId}`,
          contentId,
          startFrame: 0,
          endFrame: 1000,
          sceneIndex: 1,
          durationSeconds: 20,
          qaStatus: 'Unchecked',
          aiPrompt: 'In-scene brand recommendations: Stadium billboard, wide-shot perimeter.',
          aiStatus: 'completed',
          aiOutputDescription: 'Identified stadium tracking opportunities with continuous camera motion lock.',
          aiModelUsed: 'Gemini 2.5 Flash'
        },
        {
          id: `s-ai-02-${contentId}`,
          contentId,
          startFrame: 1000,
          endFrame: 2200,
          sceneIndex: 2,
          durationSeconds: 24,
          qaStatus: 'Unchecked',
          aiPrompt: 'In-scene brand recommendations: Corner flag banner, spectator backdrop.',
          aiStatus: 'completed',
          aiOutputDescription: 'Identified crowd rail billboard with moderate depth occlusion check.',
          aiModelUsed: 'Gemini 2.5 Flash'
        }
      ];

      const simulatedSurfaces: SurfaceItem[] = [
        {
          id: `sf-ai-01-${contentId}`,
          sceneId: `s-ai-01-${contentId}`,
          surfaceType: 'Premium Stadium Billboard',
          boundaryCoordinates: [{x: 200, y: 300}, {x: 800, y: 310}, {x: 800, y: 480}, {x: 200, y: 460}],
          estimatedDepth: 14.2,
          orientationVector: { yaw: 5, pitch: -2, roll: 1 },
          confidenceScore: 0.96,
          viabilityScore: 0.91,
          status: 'Candidate'
        },
        {
          id: `sf-ai-02-${contentId}`,
          sceneId: `s-ai-01-${contentId}`,
          surfaceType: 'Close-up Face (Irrelevant Overlay)',
          boundaryCoordinates: [{x: 100, y: 100}, {x: 150, y: 100}, {x: 150, y: 150}, {x: 100, y: 150}],
          estimatedDepth: 1.5,
          orientationVector: { yaw: 0, pitch: 0, roll: 0 },
          confidenceScore: 0.99,
          viabilityScore: 0.0,
          status: 'Excluded',
          exclusionReason: 'MReq 4 (Brand Safety Violation): Face detection filter permanently triggered.'
        },
        {
          id: `sf-ai-03-${contentId}`,
          sceneId: `s-ai-02-${contentId}`,
          surfaceType: 'Dynamic Pitch-Side LED Rail',
          boundaryCoordinates: [{x: 50, y: 650}, {x: 950, y: 650}, {x: 960, y: 720}, {x: 40, y: 720}],
          estimatedDepth: 19.8,
          orientationVector: { yaw: 3, pitch: -1, roll: 0 },
          confidenceScore: 0.93,
          viabilityScore: 0.89,
          status: 'Candidate'
        }
      ];

      pushLog('Info', 'GeminiCore', 'System', `Gemini finished multi-modal scene splitting & geometric inventory recommendation for [${title}].`);
      
      return makeJsonRes(200, {
        data: {
          scenes: simulatedScenes,
          surfaces: simulatedSurfaces
        }
      });
    }

    if (path === '/api/video/ai-split-save' && method === 'POST') {
      const body = JSON.parse(init?.body as string || '{}');
      const { contentId, scenes } = body;
      
      const allScenes = getDbScenes().filter(s => s.contentId !== contentId);
      const allSurfaces = getDbSurfaces();
      
      // Save simulated split scenes
      const savedScenes = [...allScenes, ...scenes];
      saveDbScenes(savedScenes);

      // Save corresponding mock surfaces
      const simulatedSurfaces: SurfaceItem[] = [
        {
          id: `sf-ai-01-${contentId}`,
          sceneId: `s-ai-01-${contentId}`,
          surfaceType: 'Premium Stadium Billboard',
          boundaryCoordinates: [{x: 200, y: 300}, {x: 800, y: 310}, {x: 800, y: 480}, {x: 200, y: 460}],
          estimatedDepth: 14.2,
          orientationVector: { yaw: 5, pitch: -2, roll: 1 },
          confidenceScore: 0.96,
          viabilityScore: 0.91,
          status: 'Candidate'
        },
        {
          id: `sf-ai-02-${contentId}`,
          sceneId: `s-ai-01-${contentId}`,
          surfaceType: 'Close-up Face (Irrelevant Overlay)',
          boundaryCoordinates: [{x: 100, y: 100}, {x: 150, y: 100}, {x: 150, y: 150}, {x: 100, y: 150}],
          estimatedDepth: 1.5,
          orientationVector: { yaw: 0, pitch: 0, roll: 0 },
          confidenceScore: 0.99,
          viabilityScore: 0.0,
          status: 'Excluded',
          exclusionReason: 'MReq 4 (Brand Safety Violation): Face detection filter permanently triggered.'
        },
        {
          id: `sf-ai-03-${contentId}`,
          sceneId: `s-ai-02-${contentId}`,
          surfaceType: 'Dynamic Pitch-Side LED Rail',
          boundaryCoordinates: [{x: 50, y: 650}, {x: 950, y: 650}, {x: 960, y: 720}, {x: 40, y: 720}],
          estimatedDepth: 19.8,
          orientationVector: { yaw: 3, pitch: -1, roll: 0 },
          confidenceScore: 0.93,
          viabilityScore: 0.89,
          status: 'Candidate'
        }
      ];

      const cleanSurfaces = allSurfaces.filter(s => !s.sceneId.includes(contentId));
      saveDbSurfaces([...cleanSurfaces, ...simulatedSurfaces]);

      // Update the content item status to 'Completed'
      const contents = getDbContent();
      const cIdx = contents.findIndex(c => c.id === contentId);
      if (cIdx !== -1) {
        contents[cIdx].ingestionStatus = 'Completed';
        saveDbContent(contents);
      }

      pushLog('Info', 'DatabaseManager', 'System', `Persisted AI scene segments and 3D bounding surfaces safely to persistent relational SQLite storage.`);
      return makeJsonRes(200, { success: true });
    }

    // ----------------------------------------------------
    // FALLBACK FOR OUT-OF-ROUTE CHECKS
    // ----------------------------------------------------
    return makeJsonRes(404, { error: `Not Found: ${path}` });

  } catch (err: any) {
    console.error("Mock API Internal Error:", err);
    return makeJsonRes(500, { error: err.message || "Mock API Internal server error" });
  }
};
