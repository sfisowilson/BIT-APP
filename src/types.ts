export interface ContentItem {
  id: string;
  title: string;
  duration: string;
  resolution: string;
  frameRate: number;
  sourceChannel: string;
  storageKey: string;
  ingestionStatus: "Staging" | "Transcoding" | "SceneDetecting" | "Completed" | "Failed";
  campaignId?: string;  // MReq 10: video linked to a campaign
  createdAt: string;
  // ── Pipeline stage timestamps ──
  stagingCompletedAt?: string;
  transcodingStartedAt?: string;
  transcodingCompletedAt?: string;
  sceneDetectingStartedAt?: string;
  sceneDetectingCompletedAt?: string;
  // ── Error tracking ──
  lastErrorMessage?: string;
  lastErrorAt?: string;
}

export interface SceneItem {
  id: string;
  contentId: string;
  startFrame: number;
  endFrame: number;
  sceneIndex: number;
  durationSeconds: number;
  qaStatus: "Unchecked" | "Approved" | "Flagged";
  aiPrompt?: string;
  aiStatus?: "idle" | "processing" | "completed" | "failed";
  aiOutputDescription?: string;
  aiModelUsed?: string;
}

/** Shape returned by the .NET API (JSON strings for complex types) */
export interface SurfaceItemResponse {
  id: string;
  sceneId: string;
  surfaceType: string;
  boundaryCoordinatesJson: string;  // Serialized JSON array of {x,y} points
  estimatedDepth: number;
  orientationVectorJson: string;    // Serialized JSON {yaw,pitch,roll}
  confidenceScore: number;
  viabilityScore: number;
  status: string;
  exclusionReason?: string;
  placementImageUrl?: string;
}

/** Parsed surface with deserialized coordinates and orientation */
export interface SurfaceItem {
  id: string;
  sceneId: string;
  surfaceType: string;
  boundaryCoordinates: { x: number; y: number }[];
  estimatedDepth: number;
  orientationVector: { yaw: number; pitch: number; roll: number };
  confidenceScore: number;
  viabilityScore: number;
  status: "Candidate" | "Approved" | "Excluded" | "Pending";
  exclusionReason?: string;
  placementImageUrl?: string;
}

/** Parse a SurfaceItemResponse from the API into a SurfaceItem with proper types */
export function parseSurfaceItem(raw: SurfaceItemResponse): SurfaceItem {
  let coords: { x: number; y: number }[] = [];
  let orientation: { yaw: number; pitch: number; roll: number } = { yaw: 0, pitch: 0, roll: 0 };
  
  try {
    coords = JSON.parse(raw.boundaryCoordinatesJson || '[]');
  } catch { /* keep default */ }
  
  try {
    orientation = JSON.parse(raw.orientationVectorJson || '{}');
  } catch { /* keep default */ }
  
  return {
    id: raw.id,
    sceneId: raw.sceneId,
    surfaceType: raw.surfaceType,
    boundaryCoordinates: coords,
    estimatedDepth: raw.estimatedDepth,
    orientationVector: orientation,
    confidenceScore: raw.confidenceScore,
    viabilityScore: raw.viabilityScore,
    status: raw.status as SurfaceItem['status'],
    exclusionReason: raw.exclusionReason,
    placementImageUrl: raw.placementImageUrl,
  };
}

export interface CampaignItem {
  id: string;
  name: string;
  namingStructureCode: string;
  scheduleStart: string;
  scheduleEnd: string;
  targetRegion: string;
  totalBudget: number;
  status: "Draft" | "Active" | "Completed" | "Paused";
  createdAt: string;
}

export interface CreativeAsset {
  id: string;
  name: string;
  type: "Image" | "Logo" | "Video";
  storageKey: string;
  fileSize: string;
  dimensions: string;
  brandCategory: string;
  campaignId?: string;  // MReq 10: asset can be assigned to a campaign
  thumbnailUrl?: string; // computed by backend for uploaded files
}

/** Comprehensive brand categories for competitive separation and filtering (MReq 3, 10) */
export const BRAND_CATEGORIES = [
  'Apparel & Footwear',
  'Automotive',
  'Banking & Financial Services',
  'Beauty & Personal Care',
  'Beverages (Alcoholic)',
  'Beverages (Non-Alcoholic)',
  'Construction & Hardware',
  'Consumer Electronics',
  'Education & Training',
  'Energy & Utilities',
  'Entertainment & Media',
  'FMCG - Food & Snacks',
  'FMCG - Household Goods',
  'Gaming & eSports',
  'Government & Public Service',
  'Healthcare & Pharmaceuticals',
  'Insurance',
  'Logistics & Courier',
  'Luxury Goods',
  'Mobile Networks & Telecoms',
  'Motoring & Fuel',
  'NGO & Non-Profit',
  'Quick-Service Restaurants',
  'Real Estate & Property',
  'Retail & eCommerce',
  'Software & SaaS',
  'Sports & Fitness',
  'Streaming & Broadcasting',
  'Technology & IT Services',
  'Travel & Hospitality',
] as const;

export type BrandCategory = typeof BRAND_CATEGORIES[number];

export interface RenderItem {
  id: string;
  contentId: string;
  surfaceId: string;
  campaignId: string;
  assetId: string;
  exportPreset: string;
  storageKey: string;
  renderStatus: "Queued" | "Processing" | "Finished" | "Failed";
  progress: number;
  processingDurationMs: number;
  createdAt: string;
}

export interface EventLog {
  id: string;
  timestamp: string;
  eventCode: string;
  severity: "Info" | "Warning" | "Major" | "Critical";
  module: string;
  user: string;
  description: string;
}

export interface AlarmItem {
  id: string;
  timestamp: string;
  severity: "Minor" | "Major" | "Critical";
  source: string;
  description: string;
  isActive: boolean;
}

export interface User {
  id: string;
  fullName: string;
  email: string;
  role: "Admin" | "Editor" | "Advertiser";
  accountStatus: "Active" | "Suspended";
  lastLoginAt: string;
}

export interface AuthResponse {
  token: string;
  user: User;
}

export interface AdSlotItem {
  id: string;
  surfaceId: string;
  marketRegion: string;
  pricingValue: number;
  slotStatus: "Available" | "Reserved" | "Rendering" | "Completed";
  dimensions: string;
  campaignId?: string;
  createdAt: string;
}

/** Tracks which brand asset has been placed on which surface during the placement workflow */
export interface SurfaceAssetPair {
  surfaceId: string;
  assetId: string;
  placedAt: string; // ISO datetime
}

export interface ApprovalItem {
  id: string;
  adSlotId: string;
  campaignId: string;
  approverUserId: string;
  approverEmail: string;
  decision: "Approved" | "Rejected";
  rejectionReason?: string;
  timestamp: string;
}

export interface SchemaEntity {
  name: string;
  purpose: string;
  mreq: string;
  fields: string[];
}

export interface DaySchedule {
  day: number;
  phase: 'Phase 1: UI & REST API' | 'Phase 2: AI Engine & QA';
  title: string;
  description: string;
  mreqs: string[];
  activities: string[];
  risk: string;
  milestone: boolean;
}

export const DATABASE_SCHEMA: SchemaEntity[] = [
  {
    name: 'Content',
    purpose: 'Stores main video files, upload states, and structural video metadata.',
    mreq: 'MReq 1, 13, 25',
    fields: ['ContentID (UUID - Primary Key)', 'Title (Varchar)', 'Duration (Interval)', 'Resolution (Varchar)', 'FrameRate (Numeric)', 'SourceChannel (Varchar)', 'StorageKey (Varchar - Object Store link)', 'IngestionStatus (Enum: Staging, Transcoding, SceneDetecting, Completed, Failed)', 'CreatedAt (Timestamp)', 'ModifiedAt (Timestamp)']
  },
  {
    name: 'Scene',
    purpose: 'Stores indexed scene cuts detected per video item to divide workloads.',
    mreq: 'MReq 1, 25',
    fields: ['SceneID (UUID - Primary Key)', 'ContentID (UUID - Foreign Key -> Content)', 'StartFrame (Integer)', 'EndFrame (Integer)', 'SceneIndex (Integer)', 'DurationSeconds (Numeric)', 'QA_Status (Enum: Unchecked, Approved, Flagged)', 'CreatedAt (Timestamp)']
  },
  {
    name: 'Surface',
    purpose: 'Stores candidate surfaces detected in video scenes (billboards, screens, etc.) with 3D depth and scoring metrics.',
    mreq: 'MReq 2, 3, 25',
    fields: ['SurfaceID (UUID - Primary Key)', 'SceneID (UUID - Foreign Key -> Scene)', 'SurfaceType (Varchar - e.g. "Screen", "Wall")', 'BoundaryCoordinates (JSONB - 2D points)', 'EstimatedDepth (Numeric)', 'OrientationVector (JSONB - 3D plane)', 'ConfidenceScore (Numeric)', 'ViabilityScore (Numeric)', 'Status (Varchar - e.g. "Candidate", "Excluded")', 'CreatedAt (Timestamp)']
  },
  {
    name: 'Campaign',
    purpose: 'Schedules, budgets, and operational metadata for advertiser campaigns.',
    mreq: 'MReq 10, 25',
    fields: ['CampaignID (UUID - Primary Key)', 'AdvertiserID (UUID - Foreign Key -> Users)', 'CampaignName (Varchar)', 'NamingStructureCode (Varchar - e.g. "UZ01EP12_COKE")', 'ScheduleStart (Timestamp)', 'ScheduleEnd (Timestamp)', 'TargetRegion (Varchar)', 'TotalBudget (Decimal)', 'Status (Enum: Draft, Active, Completed, Paused)', 'CreatedAt (Timestamp)']
  },
  {
    name: 'CreativeAsset',
    purpose: 'Brand creative assets (images, transparent logos, video clips) in the asset library.',
    mreq: 'MReq 10, 13, 25',
    fields: ['AssetID (UUID - Primary Key)', 'AdvertiserID (UUID - Foreign Key -> Users)', 'AssetType (Enum: Image, Logo, Video)', 'StorageKey (Varchar - Object store reference)', 'FileSize (BigInt)', 'OriginalDimensions (Varchar)', 'BrandCategory (Varchar - for separation check)', 'CreatedAt (Timestamp)']
  },
  {
    name: 'Render',
    purpose: 'Logs finished composited output videos and export format presets.',
    mreq: 'MReq 7, 14, 25',
    fields: ['RenderID (UUID - Primary Key)', 'ContentID (UUID - Foreign Key -> Content)', 'AdSlotID (UUID - Foreign Key -> AdSlot)', 'CampaignID (UUID - Foreign Key -> Campaign)', 'ExportPreset (Varchar - e.g. "Broadcast-ProRes")', 'StorageKey (Varchar - Object Store location)', 'RenderStatus (Enum: Queued, Processing, Finished, Failed)', 'ProcessingDurationMs (Integer)', 'CreatedAt (Timestamp)']
  }
];

export const TIMELINE_DATA: DaySchedule[] = [
  {
    day: 1,
    phase: 'Phase 1: UI & REST API',
    title: 'PostgreSQL Relational Models & JWT Security',
    description: 'Set up the database cluster, run DDL migrations for all core entities, and implement secure token authentication (JWT) over HTTPS with strict role authorization check routines.',
    mreqs: ['MReq 8 (Security)', 'MReq 9 (Roles)', 'MReq 25 (Data models)'],
    activities: [
      'Spin up PostgreSQL database instance and execute SQL migrations to create all schemas.',
      'Configure table constraints, indexes, and primary keys for all 11 core entities.',
      'Build C# Token Identity Service (JWT over HTTPS) with verification middleware.',
      'Scaffold endpoint authorization guards (Admin, Advertiser, Editor policies).'
    ],
    risk: 'Poor validation structure can lead to database injection. Mitigated by applying strongly-typed contract validation on all entry models.',
    milestone: false
  },
  {
    day: 2,
    phase: 'Phase 1: UI & REST API',
    title: 'Vue.js Project Scaffold & Backend API Controllers',
    description: 'Scaffold the frontend web shell using Vue.js and Tailwind CSS, and create the C# REST controller stubs in ASP.NET Core matching standard API routes.',
    mreqs: ['MReq 8 (Security)', 'MReq 18 (Admin portal)'],
    activities: [
      'Establish the Vue.js project shell with modern, high-contrast Tailwind styling.',
      'Set up global stores for session management, dashboard active tabs, and alerts.',
      'Develop the C# API Controllers (Campaign, Content, Surface, User) with path stubs.',
      'Integrate base cross-origin resource sharing (CORS) rules.'
    ],
    risk: 'Contract mismatch between C# models and Vue API payloads. Mitigated by setting up shared JSON schema contracts before writing code.',
    milestone: false
  },
  {
    day: 3,
    phase: 'Phase 1: UI & REST API',
    title: 'Campaign Management & Cloud Object Staging',
    description: 'Develop advertiser campaign panels in Vue, asset library interfaces, and implement the C# S3/Blob storage proxies for asset staging.',
    mreqs: ['MReq 10 (Campaigns)', 'MReq 13 (S3/Blob Storage)'],
    activities: [
      'Build Vue.js campaign creations wizard supporting strict naming structures (e.g. UZ01EP12_COKE).',
      'Create Asset Library dashboard with multi-file drag-and-drop triggers.',
      'Implement C# upload gateways validating sizes, dimensions, and categories.',
      'Connect uploads to secure cloud-hosted Object Storage and reference keys in PostgreSQL.'
    ],
    risk: 'Direct client-to-cloud uploads risk exposing API keys. Mitigated by proxying all requests securely through C# server-side tokens.',
    milestone: false
  },
  {
    day: 4,
    phase: 'Phase 1: UI & REST API',
    title: 'Content Ingestion Portal & Metadata Analyzers',
    description: 'Build the video upload panel for Content Owners and develop C# FFmpeg wrapper routines to extract original stream metadata without duration alteration.',
    mreqs: ['MReq 1 (Content Ingestion)', 'MReq 13 (S3/Blob Storage)'],
    activities: [
      'Build Content Owner upload view supporting professional broadcast formats (MP4, MOV, MXF, AVI).',
      'Integrate server-side FFmpeg metadata probe to extract duration, frame rates, and codec details.',
      'Add strict constraints preventing video duration edits or visual morphing of source clips.',
      'Set up scene cut indices staging tables in database.'
    ],
    risk: 'Extremely large video files stalling HTTP request pipelines. Mitigated by implementing chuck-based, resumable video streaming uploads.',
    milestone: false
  },
  {
    day: 5,
    phase: 'Phase 1: UI & REST API',
    title: 'Editor Workbench & Quality Approval Handover',
    description: 'Complete the visual Editor workbench, integrated approval state machines, notification gateway triggers, and central administrative portal.',
    mreqs: ['MReq 11 (Workflow)', 'MReq 12 (Notifications)', 'MReq 15 (SMS/SMTP)', 'MReq 18 (Admin UI)'],
    activities: [
      'Design and build the interactive Editor panel for approving/rejecting recommended slots.',
      'Implement permanent, non-negotiable approval workflows with mandatory reasons logs.',
      'Deploy C# SMTP and SMS queues triggered on task updates.',
      'Integrate live system health charts (DB size, storage keys, API call count) on Admin portal.'
    ],
    risk: 'Unapproved placements leaking into output rendering. Mitigated by database-level locks preventing renders unless AdSlot status is explicitly "Approved".',
    milestone: true
  },
  {
    day: 6,
    phase: 'Phase 2: AI Engine & QA',
    title: 'AI Interface Abstractions & Physical Scene Cutting',
    description: 'Code the Object-Oriented C# interfaces that enable decoupled AI engine models, and integrate the background scene-cut detection pipeline.',
    mreqs: ['MReq 1 (Ingestion)', 'MReq 25 (Database models)'],
    activities: [
      'Develop and export standard C# Interfaces (ISurfaceDetectionEngine, IMotionTracker, ICompositingEngine).',
      'Integrate background physical scene-cut parsing via server-side FFmpeg.',
      'Generate scene index bounding frames and store records securely in PostgreSQL Scene table.',
      'Connect Scene table records to the frontend Video Player.'
    ],
    risk: 'Heavy CPU loads during scene-cut parsing crashing the API. Mitigated by isolating scene-cutting to an asynchronous, background worker thread queue.',
    milestone: false
  },
  {
    day: 7,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Computer-Vision Surface Detection & Competitive Separation',
    description: 'Wrap SAM 2 (Segment Anything) and YOLO models inside C# service classes, and implement brand recognition to enforce competitive advertising separation.',
    mreqs: ['MReq 2 (Scene Analysis)', 'MReq 3 (Placement recommendation)'],
    activities: [
      'Implement concrete "Sam2SurfaceDetector" and "YoloSurfaceDetector" services implementing ISurfaceDetectionEngine.',
      'Construct depth estimation and 3D coordinate mapping solvers.',
      'Develop the Competitive Separation Algorithm: analyze in-scene text and logos to avoid positioning ads next to competitive brands.',
      'Expose identified surfaces coordinates to API controllers.'
    ],
    risk: 'High false-positive rate on non-viable reflective surfaces. Mitigated by introducing texture and reflection heuristic filters to discard windows.',
    milestone: false
  },
  {
    day: 8,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Locked Brand-Safety Enforcement Layer',
    description: 'Deploy rigid, non-bypassable filters directly inside the detection engine to guarantee advertisements are never placed on faces, children, government vehicles, or religious symbols.',
    mreqs: ['MReq 4 (Brand-Safety)', 'MReq 11 (Approvals)'],
    activities: [
      'Incorporate mandatory deep-learning object classifiers for human face and children detection.',
      'Hardcode permanent block lists preventing placements on government, emergency, or religious spaces.',
      'Ensure the rules engine cannot be bypassed by any user level, including Administrators.',
      'Enforce mandatory double-pass human approval checks.'
    ],
    risk: 'Sponsors or sales staff attempting to bypass brand filters. Mitigated by hardcoding rule checks directly inside the backend SQL and pipeline layers.',
    milestone: false
  },
  {
    day: 9,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Planar Motion Tracking & Stabilization',
    description: 'Integrate OpenCV tracking wrappers to keep inserted brand assets locked onto surfaces frame-to-frame without visual drifting.',
    mreqs: ['MReq 5 (Motion Tracking)'],
    activities: [
      'Deploy the OpenCV-based planar point tracker, implementing the IMotionTracker interface.',
      'Develop fallback point-interpolation routines for handling occlusions or fast panning.',
      'Add a re-validation drift scoring engine (rejecting slots with >2% drift).',
      'Expose tracking completion status and confidence ratings.'
    ],
    risk: 'Tracking drift over fast-moving action footage. Mitigated by deploying bidirectional tracking algorithms that calculate motion backward and forward.',
    milestone: false
  },
  {
    day: 10,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Compositing Engine & VFX Light-Matching',
    description: 'Deploy advanced perspective warp engines (Homography solve) and filters to simulate realistic scene lighting, shadows, and camera noise.',
    mreqs: ['MReq 6 (Compositing)'],
    activities: [
      'Implement C# matrix solvers to wrap/warp 2D images or video overlays into 3D perspective frames.',
      'Develop ambient light match algorithms, calculating surrounding luminance, contrast, and color values.',
      'Integrate synthetic video grain, camera noise, motion blur, and soft-shadow projection filters.',
      'Optimize asset scaling pipelines.'
    ],
    risk: 'Flat, unrealistic placements looking like "stickers". Mitigated by analyzing surrounding pixels to blend high-frequency color components.',
    milestone: false
  },
  {
    day: 11,
    phase: 'Phase 2: AI Engine & QA',
    title: 'GPU Render Dispatchers & Multi-Format Transcoders',
    description: 'Connect compositing models with virtualized GPU servers, manage asynchronous job batching queues, and establish preset export transcoders.',
    mreqs: ['MReq 7 (Rendering)', 'MReq 14 (GPU service)', 'MReq 23 (Throughput)'],
    activities: [
      'Build C# batch rendering queues integrating with cloud GPU instances (RunPod / EC2 GPU).',
      'Configure task dispatchers returning unique rendering progress percentages.',
      'Deploy standard FFmpeg transcoding pipelines supporting high-quality Broadcast-ProRes and web-ready MP4 H.264 profiles.',
      'Ensure rendered files are pushed directly back to secure Object Storage.'
    ],
    risk: 'GPU rendering nodes running out of video memory (VRAM) under batch pressure. Mitigated by implementing adaptive queue rate limiting.',
    milestone: true
  },
  {
    day: 12,
    phase: 'Phase 2: AI Engine & QA',
    title: 'BI Reporting, Monitoring Alarms & Audit Logging',
    description: 'Expose performance analytics on the Vue dashboard, connect logging services, set up system alerts, and establish audited usage transfers.',
    mreqs: ['MReq 16 (DSP)', 'MReq 19 (Statistics)', 'MReq 20 (Events)', 'MReq 21 (Alarms)', 'MReq 22 (Usage support)'],
    activities: [
      'Aggregate database metrics (impressions, slot counts, exposure times) and populate Vue BI Dashboard.',
      'Implement central Event Log recording all system errors, API status and user adjustments.',
      'Build the Alarms module: automatically broadcast high-priority SMS/Email alerts if storage or GPU interfaces fail.',
      'Write daily usage compliance logs to secure CSV files and transfer them to isolated archive servers.'
    ],
    risk: 'Telemetry logging degrading overall system throughput. Mitigated by processing all logging and analytic writes as asynchronous fire-and-forget tasks.',
    milestone: false
  },
  {
    day: 13,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Platform Redundancy & Mid-Render Failover Tests',
    description: 'Verify system load-balancing, evaluate database cluster locks, and execute failover recovery tests by manually crashing render nodes.',
    mreqs: ['MReq 17 (Redundancy)'],
    activities: [
      'Deploy redundant containers fronted by load balancers and execute server-crash drills.',
      'Verify that database connection pools dynamically redirect traffic during failover.',
      'Crash a render node during active compositing and verify that the queue system detects the silence, recovers original metadata, and re-allocates task.',
      'Validate that zero files or campaigns are corrupted during hard reboots.'
    ],
    risk: 'Race conditions on concurrent jobs during failover. Mitigated by implementing atomic transaction database blocks and unique queue task locks.',
    milestone: false
  },
  {
    day: 14,
    phase: 'Phase 2: AI Engine & QA',
    title: 'Platform Sizing, Visual Quality Check, & Master Sign-Off',
    description: 'Execute end-to-end load tests across 100+ standard videos, audit visual drift and light matching quality, measure daily volume capacities, and sign-off.',
    mreqs: ['MReq 23 (Throughput)', 'MReq 24 (Daily capacity)'],
    activities: [
      'Run stress tests auditing processing volumes against daily minutes goals per node.',
      'Perform human-in-the-loop quality evaluations of final renders to certify perspective and blending.',
      'Analyze unit rendering financial costs to guarantee operational profitability.',
      'Prepare user guides, secure config handovers, and execute official Release Sign-off.'
    ],
    risk: 'Late-stage defects discovered during deployment. Mitigated by enforcing strict continuous integration linting and automatic pipeline tests.',
    milestone: true
  }
];
