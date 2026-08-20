# React Component Prop Contracts

**Version:** 1.0 | **Date:** 2026-07-23 | **Source:** Verified from actual component files

> **⚠️ This is the canonical reference for component interfaces. If a prop or component is not listed here with its exact signature, verify before using. Do not assume props.**

---

## `CampaignsTab`

**File:** `src/components/CampaignsTab.tsx`
**Route:** `/c/:campaignId/assets`

```typescript
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
```

The `campaignList`/`assetList` props are only used for cross-references that need the global unpaginated first-page snapshot: the "selected campaign" fallback lookup, per-card asset-count badges, and the "Assign to Campaign" quick-pick `<select>` in the Unassigned Assets panel (a known, documented gap for campaigns beyond page 1 — see `governance/nfrs/pagination-consistency-fix.md`). The three actual list UIs (Campaign Database grid, Campaign Assets, Unassigned Assets) each self-fetch their own paginated copy via `usePaginatedData` + `<Pagination>`, independent of these props — matching the pattern already used by `IngestionTab`'s Content catalog. All five mutation props (`handleCreateAsset`, `handleUpdateAsset`, `handleAssociateAsset`, `handleUnassociateAsset`, `handleDeleteAsset`, `handleDeleteCampaign`) are called through internal wrappers that `refresh()` the relevant paginated hook(s) afterward.

**Metadata extraction flow:** When a video file is attached, the browser extracts a quick first pass (duration, resolution) to populate form fields immediately. Simultaneously, the file is uploaded to `POST /api/content/probe` which runs ffprobe. When ffprobe completes, the returned values **overwrite** the form fields — ffprobe is the source of truth. A green "Verified by ffprobe" indicator confirms the values were applied. The `probeKey` from the probe response is passed to the upload endpoint so the file is reused without a second upload. If ffprobe fails, a warning is shown and browser-detected values remain in place.

---

## `RendersTab`

**File:** `src/components/RendersTab.tsx`
**Route:** `/c/:campaignId/renders`

```typescript
interface RendersTabProps {
  campaignId?: string;
  campaignName?: string;
  onRetryRender?: (renderId: string) => Promise<void>;
  userRole?: 'Admin' | 'Editor' | 'Advertiser';
}
```

Self-fetches its render list via `usePaginatedData<RenderItem>('/api/renders', { campaignId })` + `<Pagination>` (no longer takes a `renderList` prop). The three stat cards (Processing/Completed/Failed) are true campaign-wide totals fetched via 4 parallel `pageSize:1` count-only requests (Queued, Processing, Finished, Failed) — not derived from the current page — and double as clickable shortcuts that set the `renderStatus` filter.

---

## `IngestionTab`

**File:** `src/components/IngestionTab.tsx`
**Route:** `/c/:campaignId/content`

```typescript
interface IngestionTabProps {
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  scenesForVideo: SceneItem[];
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  onNavigateToPlacements: () => void;
  newVideoTitle: string;
  setNewVideoTitle: (v: string) => void;
  newVideoRes: string;
  setNewVideoRes: (v: string) => void;
  newVideoFps: number;
  setNewVideoFps: (v: number) => void;
  newVideoDuration: string;
  setNewVideoDuration: (v: string) => void;
  newVideoChannel: string;
  setNewVideoChannel: (v: string) => void;
  newVideoFile: File | null;
  setNewVideoFile: (f: File | null) => void;
  handleIngestVideo: (e: React.FormEvent) => void;
  ingestError: string | null;
  ingesting: boolean;
  uploadProgress?: number;
  chunkProgress?: string;
  handleDeleteContent?: (id: string) => void;
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string, splitMode?: SplitMode) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
  selectedCampaignId?: string | null;
  campaignList?: { id: string; name: string }[];
  onDataChanged?: () => void;
  onRetranscode?: (contentId: string) => Promise<void>;
  onRedetectScenes?: (contentId: string, videoTitle: string, splitMode?: SplitMode) => Promise<void>;
  onResetPipeline?: (contentId: string) => Promise<void>;
  isPipelineActionPending?: string | null;
  probeKey: string | null;
  setProbeKey: (key: string | null) => void;
  splitMode: SplitMode;
  setSplitMode: (m: SplitMode) => void;
  /** Fuse 2+ consecutive scenes into one — manual alternative to AI clustering, typically used after "Cut" split mode. */
  onMergeScenes?: (sceneIds: string[], contentId: string) => Promise<void>;
}
```

---

## `CampaignSidebar`

**File:** `src/components/CampaignSidebar.tsx`

```typescript
type SidebarView = 'dashboard' | 'assets' | 'content' | 'placements' | 'renders' | 'reports' | 'admin' | 'telemetry' | 'analytics';

interface CampaignSidebarProps {
  selectedCampaignId: string | null;
  userRole: 'Admin' | 'Editor' | 'Advertiser';
  campaignAssetCount: number;
  contentCount: number;
  renderCount: number;
}
```

---

## `CampaignDashboard`

**File:** `src/components/CampaignDashboard.tsx`
**Route:** `/c/:campaignId`

```typescript
interface CampaignDashboardProps {
  campaign: CampaignItem;
  assets: CreativeAsset[];
  contentList: ContentItem[];
  renders: RenderItem[];
  hasApprovedPlacements?: boolean;  // from GET /api/campaigns/{id}/summary — drives the "Placements" pipeline step
  onNavigate: (view: SidebarView) => void;
}
```

The "Recent Renders" widget shows a "▶ Watch"/"✕ Close" toggle and a "⬇ Download" link for any render where `hasPlayableFile(r)` is true (`renderStatus` is `Finished` or `NeedsReview` and `storageKey` starts with `/api/`). Watch renders an inline `<video src={r.storageKey} controls autoPlay>` player; Download uses `<a href={r.storageKey} download>`. Both reuse the same `GET /api/renders/{id}/download` endpoint — `Content-Disposition: attachment` only forces a download on top-level navigation (the `<a download>` case), not on a `<video src>` resource fetch, so no backend change was needed to support both behaviors from one URL.

---

## `InvoicePanel`

**File:** `src/components/InvoicePanel.tsx`
**Rendered in:** `App.tsx`'s `activeView === 'reports'` view, below the campaign stats grid.

```typescript
interface InvoicePanelProps {
  campaignId: string;
}
```

Self-fetching (owns its own loading/error state) — calls `fetchCampaignInvoice(campaignId)` on mount and whenever `campaignId` changes, and on a manual "Refresh" button. Renders the returned `InvoiceSummary`'s line items table plus subtotal/render-processing-fees/VAT/total footer. Read-only report view; never mutates pipeline state.

---

## `CampaignSelector`

**File:** `src/components/CampaignSelector.tsx`

```typescript
interface CampaignSelectorProps {
  campaigns: { id: string; name: string }[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onCreateNew: () => void;
}
```

---

## `EditorTab`

**File:** `src/components/EditorTab.tsx`
**Route:** `/c/:campaignId/placements`

Props: (condensed — see `EditorTabProps` in the file for the full ~30-field interface, e.g. AI-suggestion and render-tracking props)
```typescript
interface EditorTabProps {
  contentList: ContentItem[];
  selectedVideo: string;
  setSelectedVideo: (v: string) => void;
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  scenesForVideo: SceneItem[];
  surfacesForScene: SurfaceItem[];
  selectedSurfaceId: string;
  setSelectedSurfaceId: (v: string) => void;
  handleSurfaceDecision: (decision: "Approved" | "Rejected") => void;
  currentSurface: SurfaceItem | undefined;
  assetList: CreativeAsset[];
  campaignList: CampaignItem[];
  selectedCampaignId?: string;
  surfaceAssetPairs: Record<string, string>; // surfaceId -> assetId
  onPlaceAsset: (surfaceId: string, assetId: string) => void;
  onSubmitPlacement: (surfaceId: string, assetId: string, campaignId: string) => Promise<boolean>;
  renderList?: RenderItem[];
  onRetryRender?: (renderId: string) => Promise<void>;
  onDetectSurfacesForScene?: (sceneId: string, contentId: string) => Promise<void>;
  onDeleteScene?: (sceneId: string) => Promise<void>; // DELETE /api/scenes/{id} — blocked server-side if any surface is Approved
  onDeleteAllScenes?: (contentId: string) => Promise<void>; // DELETE /api/content/{contentId}/scenes — blocked server-side if any surface is Approved
  // AI Placement Assistant — "Generate New" mode (prompt-based AI video placement, no surface required)
  onSubmitPromptPlacement?: (dto: CreatePromptRenderRequest) => Promise<void>;
  onApprovePromptSplice?: (renderId: string) => Promise<void>;
  onRejectPromptPlacement?: (renderId: string) => Promise<void>;
  activePromptRender?: RenderItem | null;
  // Surface-Anchored mode — "Anchor & Generate" (FLUX Kontext + Kling O1, anchored on a real surface)
  onSubmitSurfaceAnchor?: (dto: CreateSurfaceAnchorRenderRequest) => Promise<void>;
}
```

Owns the interactive-placement UI state (not passed as props): `interactionMode` ('product'|'signage'), `interactiveMask`/`interactiveQuad` (from `SurfaceClickOverlay`), `interactiveAssetId`, and `shotsForScene` (fetched via `fetchShotsForScene` whenever `selectedSceneId` changes, passed down to `SurfaceClickOverlay` as the `shots` prop). "Approve & Render" first calls `createSurfaceFromClick`/`createSurfaceFromQuad` to obtain a real `surfaceId`, then `confirmInteractivePlacement`.

The "AI Placement Assistant" card also owns `assistantMode` ('match'|'generate', default 'match'), a toggle in the card header. `'match'` renders the existing Gemini-based surface/asset auto-pairing body unchanged (`suggestPlacements` → `onPlaceAsset`, never generates video). `'generate'` renders `<PromptGeneratePanel>` (see below) — a structurally separate flow that never touches `interactionMode`, `SurfaceClickOverlay`, or surface geometry at all.

---

## `PromptGeneratePanel`

**File:** `src/components/PromptGeneratePanel.tsx`

Rendered inside `EditorTab`'s "AI Placement Assistant" card when `assistantMode === 'generate'`. Implements the "Generate New" flow: free-text prompt + single asset reference → Kling O1 video edit → preview → approve (splice into full video) or reject.

```typescript
interface PromptGeneratePanelProps {
  currentScene: SceneItem | undefined;
  campaignAssets: CreativeAsset[];
  contentId: string;
  campaignId?: string;
  activePromptRender?: RenderItem | null;
  onSubmit: (dto: CreatePromptRenderRequest) => Promise<void>;
  onApprove: (renderId: string) => Promise<void>;
  onReject: (renderId: string) => Promise<void>;
}
```

Gates on `currentScene.durationSeconds ∈ [MIN_PROMPT_EDIT_DURATION_SECONDS, MAX_PROMPT_EDIT_DURATION_SECONDS]` (`src/types.ts`, mirrors `KlingPromptEditService`'s backend constants) — out-of-range scenes show a warning instead of the form. Renders one of three bodies based on `activePromptRender?.renderStatus`: the submit form (default/`Failed`), a `Queued`/`Processing` disabled-form state, or — once `PreviewReady` — a `<video src="/api/renders/{id}/preview" controls>` player with "Approve & Splice"/"Reject & Retry" buttons.

---

`ComposerTab` was removed (dead/unrouted — never rendered from `App.tsx`). Render dispatch and asset↔surface placement both live in `EditorTab` (see above): the legacy AI-detected-surface flow via `onSubmitPlacement`, and the interactive click/draw flow via `SurfaceClickOverlay` + "Approve & Render". Both now dispatch through `POST /api/renders/interactive`.

---

## `TelemetryTab`

**File:** `src/components/TelemetryTab.tsx`
**Route:** `/telemetry`

```typescript
interface TelemetryTabProps {
  logs: EventLog[];
  alarms: AlarmItem[];
  onAcknowledgeAlarm?: (id: string) => void;
}
```

---

## `AdminConsoleTab`

**File:** `src/components/AdminConsoleTab.tsx`
**Route:** `/admin`

```typescript
interface AdminConsoleTabProps {
  onTriggerLog?: (code: string, severity: 'Info' | 'Warning' | 'Major' | 'Critical', module: string, user: string, desc: string) => void;
  currentUser: User | null;
}
```

Fully self-managed — fetches, creates, updates, and deletes users via its own local `fetchWithAuth` helper (not `apiClient.ts`), independent of `App.tsx`'s global state. The directory table self-fetches via `usePaginatedData<User>('/api/users', { search }, ...)` + `<Pagination>` (search is server-side, matching fullName/email/role/accountStatus). The five metric cards (Total/Admin/Editor/Advertiser/Suspended) are true directory-wide totals from 4 parallel `pageSize:1` count-only requests, refreshed alongside the main list after every create/update/delete/status-toggle mutation.

---

## `AnalyticsTab`

**File:** `src/components/AnalyticsTab.tsx`
**Route:** `/analytics`

```typescript
interface AnalyticsTabProps {
  statsSummary: StatsSummary | null;
}
```

---

## `BrandSafetyPanel`

**File:** `src/components/BrandSafetyPanel.tsx`

Embedded in `AdminConsoleTab`. Manages permanent exclusion list.

---

## `SettingsPanel`

**File:** `src/components/SettingsPanel.tsx`

Embedded in `AdminConsoleTab`. Reads/writes platform settings via `api/admin/settings`.

---

## `RoleRequestsPanel`

**File:** `src/components/RoleRequestsPanel.tsx`

Embedded in `AdminConsoleTab`. Approves/rejects role elevation requests.

---

## `NotificationPreferencesPanel`

**File:** `src/components/NotificationPreferencesPanel.tsx`

Mutes/unmutes notification types per user.

---

## `AttentionBell`

**File:** `src/components/AttentionBell.tsx`

Self-fetching, no props — polls `GET /api/notifications/attention` every 60s. Categories with no dedicated resolution page reachable from the bell (`pendingSurfaces`, `failedRenders`, `failedContent`) render a "✕ mark as seen" button that calls `dismissAttentionCategory(category)` (`POST /api/notifications/attention/dismiss`) and replaces local state with the response — this clears the *current* backlog only; items created after the dismiss still count. `pendingRoleRequests`/`activeAlarms` instead navigate to their real resolution page (`/admin`, `/telemetry`) on click, same as before.

---

## `Pagination`

**File:** `src/components/Pagination.tsx`

```typescript
interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  onPageChange: (page: number) => void;
}
```

---

## `FilterableSelect`

**File:** `src/components/FilterableSelect.tsx`

```typescript
interface FilterableSelectProps {
  options: readonly string[] | string[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
}
```

---

## `PipelineProgress`

**File:** `src/components/PipelineProgress.tsx`

```typescript
interface PipelineProgressProps {
  status: 'Staging' | 'Transcoding' | 'SceneDetecting' | 'Completed' | 'Failed';
  progress?: number; // 0-100
  errorMessage?: string;
}
```

---

## `NotFoundPage`

**File:** `src/components/NotFoundPage.tsx`

No props. Static 404 page.

---

## `BitLogo`

**File:** `src/components/BitLogo.tsx`

```typescript
interface BitLogoProps {
  className?: string;
  size?: number;
}
```

---

## Custom Hooks

### `useChunkedUpload`
**File:** `src/hooks/useChunkedUpload.ts`
```typescript
function useChunkedUpload(): {
  upload: (file: File, url: string, metadata: Record<string, string>) => Promise<void>;
  progress: number;
  chunkInfo: string;
  isUploading: boolean;
  error: string | null;
  abort: () => void;
}
```

### `usePaginatedData`
**File:** `src/hooks/usePaginatedData.ts`
```typescript
function usePaginatedData<T>(fetchFn: (page: number) => Promise<{ items: T[]; totalCount: number; totalPages: number }>): {
  items: T[];
  page: number;
  totalCount: number;
  totalPages: number;
  setPage: (p: number) => void;
  isLoading: boolean;
}
```

### `useIdleTimer`
**File:** `src/hooks/useIdleTimer.ts`
```typescript
function useIdleTimer(timeoutMs: number, onTimeout: () => void): {
  resetTimer: () => void;
  isIdle: boolean;
}
```

### `SurfaceClickOverlay`
**File:** `src/components/SurfaceClickOverlay.tsx`
```typescript
interface SurfaceClickOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  contentId: string;
  currentFrame: number;
  frameRate: number;
  mode: 'product' | 'signage';
  assetUrl?: string;
  shots?: ShotItem[]; // shots making up the current scene; renders a "Shot N / M" cut-awareness badge
  onMaskReceived?: (polygon: MaskPolygon) => void;
  onQuadConfirmed?: (corners: [QuadPoint, QuadPoint, QuadPoint, QuadPoint]) => void;
  onCancel?: () => void;
}

type InteractionMode = 'product' | 'signage';

interface QuadPoint {
  x: number;  // native video pixel coordinates
  y: number;
}
```
