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
  handleAiSplitAnalyze?: (contentId: string, videoTitle: string) => Promise<void>;
  aiAnalyzingVideoId?: string | null;
  selectedCampaignId?: string | null;
  campaignList?: { id: string; name: string }[];
  onDataChanged?: () => void;
  onRetranscode?: (contentId: string) => Promise<void>;
  onRedetectScenes?: (contentId: string, videoTitle: string) => Promise<void>;
  onResetPipeline?: (contentId: string) => Promise<void>;
  isPipelineActionPending?: string | null;
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

Props: (read from App.tsx usage — no standalone interface file)

```typescript
// Used as: <CampaignDashboard statsSummary={statsSummary} contentCount={contentList.length} ... />
interface CampaignDashboardProps {
  statsSummary: StatsSummary | null;
  contentCount: number;
  sceneCount: number;
  surfaceCount: number;
  renderCount: number;
  campaignName: string;
}
```

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

Props: (read from App.tsx usage)
```typescript
interface EditorTabProps {
  scenesForVideo: SceneItem[];
  selectedSceneId: string;
  setSelectedSceneId: (v: string) => void;
  surfaceList: SurfaceItem[];
  selectedSurfaceId: string | null;
  setSelectedSurfaceId: (v: string | null) => void;
  handleApproveSurface?: (surfaceId: string) => void;
  selectedVideo: string;
}
```

---

## `ComposerTab`

**File:** `src/components/ComposerTab.tsx`
**Route:** `/c/:campaignId/renders`

Props: (read from App.tsx usage)
```typescript
interface ComposerTabProps {
  renderList: RenderItem[];
  campaignList: CampaignItem[];
  contentList: ContentItem[];
  surfaceList: SurfaceItem[];
  assetList: CreativeAsset[];
  handleDispatchRender?: (dto: { contentId: string; surfaceId: string; campaignId: string; assetId: string; exportPreset: string }) => Promise<void>;
}
```

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

Props: (read from App.tsx usage)
```typescript
interface AdminConsoleTabProps {
  users: User[];
  currentUser: UserSession | null;
  handleCreateUser?: (dto: { fullName: string; email: string; password: string; role: string }) => Promise<void>;
  handleUpdateUser?: (id: string, dto: { role?: string; accountStatus?: string }) => Promise<void>;
  // ... settings, brand safety, role requests handlers
}
```

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

```typescript
interface AttentionBellProps {
  count: number;
  onClick: () => void;
}
```

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
