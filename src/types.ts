export interface ContentItem {
  id: string;
  title: string;
  duration: string;
  resolution: string;
  width: number;   // actual video width from ffprobe
  height: number;  // actual video height from ffprobe
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
  // ── Background job tracking ──
  detectionProgress: number;  // 0–100, updated by Hangfire during scene detection
  detectionJobId?: string;    // Hangfire job ID for status polling
  isDetectionPaused: boolean; // Whether the detection job is paused by an operator
  jobState?: string;          // Hangfire job state: Enqueued, Processing, Paused, Succeeded, Failed, Cancelled
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
  detectedAtFrame?: number;
  trackingPointsJson?: string;  // Serialized JSON array of {frame,x,y} — see SurfaceItem.trackingPoints
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
  detectedAtFrame?: number;              // frame where detected, for video seek
  /** Flat, frame-ordered list of {frame,x,y} centroid points from shot-aware tracking — lets the
   * Placement Workbench draw a single moving point following the surface during scene playback.
   * Empty until a render has actually run for this surface (tracking only happens during render). */
  trackingPoints: { frame: number; x: number; y: number }[];
}

/** Parse a SurfaceItemResponse from the API into a SurfaceItem with proper types */
export function parseSurfaceItem(raw: SurfaceItemResponse): SurfaceItem {
  let coords: { x: number; y: number }[] = [];
  let orientation: { yaw: number; pitch: number; roll: number } = { yaw: 0, pitch: 0, roll: 0 };
  
  try {
    const rawCoords = JSON.parse(raw.boundaryCoordinatesJson || '[]');
    coords = rawCoords.map((c: any) => {
      // Handle both formats: {X,Y} (old) | {x,y} (new) | [x,y] (Gemini raw)
      if (Array.isArray(c)) return { x: Number(c[0]) || 0, y: Number(c[1]) || 0 };
      return { x: Number(c.x ?? c.X) || 0, y: Number(c.y ?? c.Y) || 0 };
    });
  } catch { /* keep default */ }
  
  try {
    orientation = JSON.parse(raw.orientationVectorJson || '{}');
  } catch { /* keep default */ }

  let trackingPoints: { frame: number; x: number; y: number }[] = [];
  try {
    if (raw.trackingPointsJson) trackingPoints = JSON.parse(raw.trackingPointsJson);
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
    detectedAtFrame: raw.detectedAtFrame,
    trackingPoints,
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
  /** Null for RenderMode "PromptEdit" — those have no click/quad-detected boundary. */
  surfaceId?: string | null;
  campaignId: string;
  assetId: string;
  exportPreset: string;
  storageKey: string;
  renderStatus: "Queued" | "Processing" | "Finished" | "Failed" | "NeedsReview" | "PreviewReady" | "Rejected";
  progress: number;
  processingDurationMs: number;
  lastErrorMessage?: string;
  createdAt: string;
  /** Scene this render targets — always resolved by the backend (directly for "PromptEdit" renders, via surfaceId for Interactive ones). Null only if unresolvable (e.g. deleted surface). */
  sceneId?: string | null;
  /** User's free-text placement instruction for a "PromptEdit" render. */
  promptText?: string | null;
  /** Download path for the not-yet-approved AI-generated preview clip, set once renderStatus reaches "PreviewReady". */
  previewStorageKey?: string | null;
  /** Undefined/"Interactive" (click/quad placement) vs "PromptEdit" (free-text AI video generation). */
  renderMode?: "Interactive" | "PromptEdit" | null;
  /** ContentItem.Title — null if the source content has since been deleted. */
  contentTitle?: string | null;
  /** SceneItem.SceneIndex for the resolved scene — null if unresolvable. */
  sceneIndex?: number | null;
  /** SurfaceItem.SurfaceType — null for PromptEdit renders (no surface) or a deleted surface. */
  surfaceType?: string | null;
  /** CreativeAsset.Name — null if the asset has since been deleted. */
  assetName?: string | null;
}

/** fal-ai/kling-video/o1/video-to-video/edit's real, hard input-duration constraints — mirrored
 * from KlingPromptEditService.MinPromptEditDurationSeconds/MaxPromptEditDurationSeconds on the
 * backend (dotnet-api/Services/KlingPromptEditService.cs). Keep both sides in sync manually. */
export const MIN_PROMPT_EDIT_DURATION_SECONDS = 3.0;
export const MAX_PROMPT_EDIT_DURATION_SECONDS = 10.05;

/** Request to dispatch a prompt-based AI placement preview (the "AI Placement Assistant →
 * Generate New" flow). No surfaceId — the AI model infers placement purely from promptText
 * plus the asset image. */
export interface CreatePromptRenderRequest {
  contentId: string;
  sceneId: string;
  campaignId: string;
  assetId: string;
  promptText: string;
  exportPreset?: string;
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

/** Mirrors dotnet-api/DTOs/InvoiceDtos.cs — one billable line item, one per Finished render. */
export interface InvoiceLineItem {
  id: string;
  description: string;
  surfaceType: string;
  durationSeconds: number;
  viabilityScore: number;
  unitRate: number;
  amount: number;
}

/** Mirrors dotnet-api/DTOs/InvoiceDtos.cs's InvoiceSummaryDto — the real, backend-calculated
 * campaign invoice (exposure seconds × viability multiplier + render processing costs + VAT),
 * returned by GET /api/campaigns/{id}/invoice. */
export interface InvoiceSummary {
  invoiceNumber: string;
  campaignId: string;
  campaignName: string;
  clientName: string;
  invoiceDate: string;
  lineItems: InvoiceLineItem[];
  subtotal: number;
  renderProcessingFees: number;
  taxAmount: number;
  totalAmount: number;
  currency: string;
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

// ─── Job Management Types ─────────────────────────────────────────────

/** A detection job as returned by GET /api/jobs */
export interface DetectionJob {
  jobId: string | null;
  contentId: string;
  videoTitle: string;
  state: string;           // Enqueued, Processing, Paused, Succeeded, Failed, Cancelled
  isPaused: boolean;
  progress: number;        // 0–100
  ingestionStatus: string;
  startedAt?: string;
  completedAt?: string;
  lastErrorMessage?: string;
}

/** Paginated response wrapper for jobs list */
export interface JobsListResponse {
  data: DetectionJob[];
  count: number;
}

/** A single shot (camera cut) within a scene — a scene can span multiple shots. */
export interface ShotItem {
  id: string;
  shotIndex: number;
  startFrame: number;
  endFrame: number;
  keyframeTimestamp: number;
  keyframeUrl: string | null;
}

// ─── Interactive Placement Types ─────────────────────────────────────

/** Request to preview-segment a clicked point on a video frame via SAM3 video-rle */
export interface SegmentPreviewRequest {
  contentId: string;
  frameIndex: number;
  x: number;
  y: number;
}

/** Response from SAM3 video-rle preview segmentation */
export interface SegmentPreviewResponse {
  maskPolygonJson: string;       // JSON [{x,y},...] polygon for SVG overlay
  confidence: number;
  trackId: number;
  surfaceType: string;
  frameIndex: number;
  boundsXMin: number;
  boundsYMin: number;
  boundsXMax: number;
  boundsYMax: number;
}

/** Request to dispatch an interactive placement render */
export interface InteractiveRenderRequest {
  contentId: string;
  surfaceId: string;
  campaignId: string;
  assetId: string;
  assetType: 'Generative' | 'Planar';
  exportPreset?: string;
}

/** Request to persist a SurfaceItem from an interactive "Insert Product" click (SAM3 mask) */
export interface CreateSurfaceFromClickRequest {
  contentId: string;
  frameIndex: number;
  maskPolygonJson: string;
  surfaceType?: string;
}

/** Request to persist a SurfaceItem from an interactive "Place Signage" 4-corner quad */
export interface CreateSurfaceFromQuadRequest {
  contentId: string;
  frameIndex: number;
  quadCornersJson: string;
  surfaceType?: string;
}

/** Response from surfaces/from-click and surfaces/from-quad */
export interface CreateSurfaceResponse {
  surfaceId: string;
  sceneId: string;
}

/** Asset type classification — drives compositing engine selection */
export type AssetType = 'Generative' | 'Planar';

/** Quality classification for completed renders */
export type QualityTier = 'AI' | 'Exact' | 'Standard';

/** Compositing engine that produced a render */
export type CompositingEngine = 'pikaswaps' | 'PlanarWarp' | 'ffmpeg-luma' | 'ffmpeg-perspective';

/** Parsed polygon from maskPolygonJson */
export interface MaskPolygon {
  points: { x: number; y: number }[];
  bounds: { xMin: number; yMin: number; xMax: number; yMax: number };
  confidence: number;
  trackId: number;
}

/** Parse a SegmentPreviewResponse's maskPolygonJson into a MaskPolygon */
export function parseMaskPolygon(response: SegmentPreviewResponse): MaskPolygon {
  let points: { x: number; y: number }[] = [];
  try {
    const raw = JSON.parse(response.maskPolygonJson || '[]');
    points = raw.map((c: any) => ({
      x: Number(c.x ?? c.X ?? 0),
      y: Number(c.y ?? c.Y ?? 0),
    }));
  } catch { /* empty */ }
  return {
    points,
    bounds: {
      xMin: response.boundsXMin,
      yMin: response.boundsYMin,
      xMax: response.boundsXMax,
      yMax: response.boundsYMax,
    },
    confidence: response.confidence,
    trackId: response.trackId,
  };
}
