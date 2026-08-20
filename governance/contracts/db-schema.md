# Database Schema Snapshot

**Version:** 1.0 | **Date:** 2026-07-23 | **Source:** `dotnet-api/Models/Models.cs` + migrations

> **⚠️ This is the canonical reference for the current DB schema. If a column or table is not listed here, it does NOT exist. Do not assume fields.**

---

## Table: Users

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | GUID string |
| `FullName` | string(100) | Yes | — | |
| `Email` | string | Yes | — | Unique, email format |
| `PasswordHash` | string | Yes | — | bcrypt hash |
| `Role` | string | Yes | `"Editor"` | Admin, Editor, Advertiser |
| `AccountStatus` | string | Yes | `"Active"` | Active, Suspended |
| `LastLoginAt` | DateTime | Yes | `UtcNow` | |
| `MutedNotifications` | string(1000) | Yes | `"[]"` | JSON array of muted notification types |
| `AttentionDismissals` | string(1000) | Yes | `"{}"` | JSON object: AttentionBell category key → UTC timestamp last dismissed. Items created after that timestamp still count. |

---

## Table: RoleRequests

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `UserId` | string | Yes | — | FK → Users |
| `RequestedRole` | string(50) | Yes | — | Admin, Editor, Advertiser |
| `Reason` | string(500) | No | — | |
| `Status` | string(20) | Yes | `"Pending"` | Pending, Approved, Rejected |
| `ReviewedBy` | string | No | — | |
| `RequestedAt` | DateTime | Yes | `UtcNow` | |
| `ReviewedAt` | DateTime | No | — | |

---

## Table: ContentItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `Title` | string(200) | Yes | — | |
| `Duration` | string | Yes | — | `HH:MM:SS` format |
| `Resolution` | string | Yes | — | e.g. `1920x1080` |
| `FrameRate` | int | Yes | — | e.g. 50, 60 |
| `SourceChannel` | string | Yes | — | |
| `StorageKey` | string | Yes | — | Object storage URI |
| `IngestionStatus` | string | Yes | `"Staging"` | Staging, Transcoding, SceneDetecting, Completed, Failed |
| `CampaignId` | string | No | — | FK → CampaignItems |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |
| `StagingCompletedAt` | DateTime | No | — | Pipeline timestamp |
| `TranscodingStartedAt` | DateTime | No | — | Pipeline timestamp |
| `TranscodingCompletedAt` | DateTime | No | — | Pipeline timestamp |
| `SceneDetectingStartedAt` | DateTime | No | — | Pipeline timestamp |
| `SceneDetectingCompletedAt` | DateTime | No | — | Pipeline timestamp |
| `LastErrorMessage` | string(500) | No | — | Pipeline error detail |
| `LastErrorAt` | DateTime | No | — | |
| `DetectionProgress` | int | Yes | `0` | 0-100 |
| `DetectionJobId` | string(100) | No | — | Hangfire job ID |
| `FinalAssemblyStatus` | string(20) | Yes | `"NotStarted"` | NotStarted, Processing, Finished, Failed |
| `FinalAssemblyProgress` | int | Yes | `0` | 0-100 |
| `FinalVideoStorageKey` | string | No | — | Download path (`/api/content/{id}/final-video`) for the combined video assembled from every scene's queued render (`RenderItems.IsQueuedForFinal`) plus original footage elsewhere |
| `FinalAssemblyErrorMessage` | string(500) | No | — | |
| `FinalAssemblyUpdatedAt` | DateTime | No | — | |

---

## Table: SceneItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `ContentId` | string | Yes | — | FK → ContentItems |
| `StartFrame` | int | Yes | — | |
| `EndFrame` | int | Yes | — | |
| `SceneIndex` | int | Yes | — | |
| `DurationSeconds` | double | Yes | — | |
| `QaStatus` | string | Yes | `"Unchecked"` | Unchecked, Approved, Flagged |
| `AiPrompt` | string | No | — | |
| `AiStatus` | string | No | — | idle, processing, completed, failed |
| `AiOutputDescription` | string | No | — | |
| `AiModelUsed` | string | No | — | |

---

## Table: ShotItems

> Added by the shot-detection/clustering pipeline (`ShotDetectionPipeline` + `ShotClusteringService`). A `SceneItem` is a temporally-contiguous group of one or more shots — this is what makes "a scene is not just one camera cut" true in this codebase. See `governance/plans/shot-aware-consistency.md`.

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `ContentId` | string | Yes | — | FK → ContentItems |
| `SceneId` | string | No | — | FK → SceneItems, `ON DELETE SET NULL`. Null = unassigned (pending clustering). **Single source of truth for shot→scene membership.** |
| `ShotIndex` | int | Yes | — | 0-based sequential index within the content video |
| `StartFrame` | int | Yes | — | |
| `EndFrame` | int | Yes | — | |
| `KeyframeTimestamp` | double | Yes | `0` | Seconds from start |
| `KeyframePath` | string(500) | No | — | Relative path, e.g. `keyframes/{contentId}/shot_0001.jpg`, served via `/api/content/file/` |
| `KeyframeEmbeddingJson` | string(20000) | No | — | SAM3 image embedding (JSON `float[]`) used for clustering similarity |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |

---

## Table: SurfaceItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `SceneId` | string | Yes | — | FK → SceneItems |
| `SurfaceType` | string | Yes | — | e.g. "TV Screen", "Billboard" |
| `BoundaryCoordinatesJson` | string | Yes | — | JSON array of `{x,y}` points |
| `EstimatedDepth` | double | Yes | — | Meters (heuristic) |
| `OrientationVectorJson` | string | Yes | — | JSON `{yaw,pitch,roll}` |
| `ConfidenceScore` | double | Yes | — | 0-1 YOLO confidence |
| `ViabilityScore` | double | Yes | — | 0-1 composite |
| `Status` | string | Yes | `"Candidate"` | Candidate, Approved, Excluded, Pending |
| `ExclusionReason` | string | No | — | |
| `PlacementImageUrl` | string | No | — | |
| `DetectedAtFrame` | int | No | — | Frame number where surface was detected (0-based) |
| `Sam3Prompt` | string(500) | No | — | Gemini-generated visual description for SAM3 segmentation; also used as the shot-aware re-anchor text prompt |
| `AssetType` | string(50) | Yes | `"Generative"` | "Generative" (pikaswaps) or "Planar" (homography warp) — drives which render job runs |
| `Source` | string(50) | Yes | `"AI"` | "AI" (auto-detected) or "Manual" (interactive click/draw) |
| `TrackingDataJson` | string(100000) | No | — | Shot-segmented per-frame data: `{shotSegments:[{shotId,shotIndex,startFrame,endFrame,status,trackId,confidence,frames:[...]}]}`. Generative frames: `{frame,rle,trackId}`. Planar frames: `{frame,corners:[{x,y}×4]}`. Falls back to a legacy flat array for surfaces tracked before shot-aware tracking existed. Produced by `ShotAwareTrackingService`. |
| `TrackingPointsJson` | text | No | — | Lightweight derived centroid: flat, frame-ordered `[{frame,x,y}, ...]` across every shot segment (quad-corner average for Planar, decoded-RLE-mask-pixel average for Generative). Computed alongside `TrackingDataJson` by `ShotAwareTrackingService`; lets the Placement Workbench draw a single moving point tracking the surface during scene playback without decoding RLE or understanding the shot-segmented structure client-side. Null until a render has actually run for this surface. |
| `TrackingStatus` | string(20) | Yes | `"NotTracked"` | NotTracked, Tracked (every shot tracked/re-anchored), PartialCoverage (some shots skipped, source video passes through), LockLost (seed shot failed or every shot skipped) |
| `CreatedAt` | DateTime | Yes | `UtcNow` | Rows predating this column were backfilled to `-infinity` by migration — treated as "always older than any dismissal" by the AttentionBell's per-category dismiss filter |

---

## Table: AdSlotItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `SurfaceId` | string | Yes | — | FK → SurfaceItems |
| `MarketRegion` | string | Yes | — | |
| `PricingValue` | decimal | Yes | — | |
| `SlotStatus` | string | Yes | `"Available"` | Available, Reserved, Rendering, Completed |
| `Dimensions` | string | Yes | — | |
| `CampaignId` | string | No | — | FK → CampaignItems |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |

---

## Table: CampaignItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `Name` | string(200) | Yes | — | |
| `NamingStructureCode` | string | Yes | — | Regex-validated |
| `ScheduleStart` | DateTime | Yes | — | |
| `ScheduleEnd` | DateTime | Yes | — | |
| `TargetRegion` | string | Yes | — | |
| `TotalBudget` | decimal | Yes | — | |
| `Status` | string | Yes | `"Draft"` | Draft, Active, Completed, Paused |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |

---

## Table: CreativeAssets

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `Name` | string(200) | Yes | — | |
| `Type` | string | Yes | `"Image"` | Image, Logo, Video |
| `StorageKey` | string | Yes | — | |
| `FileSize` | string | Yes | — | |
| `Dimensions` | string | Yes | — | |
| `BrandCategory` | string | Yes | — | One of 30 categories |
| `CampaignId` | string | No | — | FK → CampaignItems |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |
| `ThumbnailUrl` | — | — | — | COMPUTED, not persisted |

---

## Table: ApprovalItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `AdSlotId` | string | Yes | — | FK → AdSlotItems |
| `CampaignId` | string | Yes | — | FK → CampaignItems |
| `ApproverUserId` | string | Yes | — | FK → Users |
| `ApproverEmail` | string | Yes | — | |
| `Decision` | string | Yes | `"Approved"` | Approved, Rejected |
| `RejectionReason` | string | No | — | |
| `Timestamp` | DateTime | Yes | `UtcNow` | |

---

## Table: RenderItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `ContentId` | string | Yes | — | FK → ContentItems |
| `SurfaceId` | string | No | — | FK → SurfaceItems, `SetNull` on delete. Null for `RenderMode = "PromptEdit"` renders — those target a `SceneId` directly, with no detected/drawn boundary |
| `CampaignId` | string | Yes | — | FK → CampaignItems |
| `AssetId` | string | Yes | — | FK → CreativeAssets |
| `ExportPreset` | string | Yes | `"Web-Ready MP4"` | |
| `StorageKey` | string | Yes | — | |
| `RenderStatus` | string | Yes | `"Queued"` | Queued, Processing, Finished, NeedsReview (partial shot coverage or drift-check below threshold — not a failure), Failed, PreviewReady (PromptEdit only — preview generated, awaiting approval), Rejected (PromptEdit only — user declined the preview) |
| `SceneId` | string | No | — | FK → SceneItems, `SetNull` on delete. Always set for `RenderMode = "PromptEdit"` renders; Interactive renders derive their scene via `SurfaceId → SurfaceItem.SceneId` instead |
| `PromptText` | string(1000) | No | — | User's free-text placement instruction for a `PromptEdit` render. Null for Interactive renders |
| `PreviewStorageKey` | string | No | — | Download path (`/api/renders/{id}/preview`) for the not-yet-approved AI-generated preview clip, set once `ProcessPromptPreviewJob` reaches `RenderStatus = "PreviewReady"` |
| `KontextFrameStorageKey` | string | No | — | Download path (`/api/content/file/kontext-frames/kontext_{renderId}.png`) for the FLUX.1 Kontext composited frame image (PNG), set once `ProcessKontextFrameJob` reaches `RenderStatus = "KontextReady"`. Used as the visual reference (@Image1) when Kling O1 Edit propagates the edit across the scene. |
| `RenderMode` | string(20) | No | — | Null, `"Interactive"`, `"PromptEdit"`, `"SurfaceAnchor"` (fast path: FLUX Kontext + Kling in one job), or `"KontextStep"` (interactive: Kontext frame only, first step of the two-step workflow) |
| `Progress` | int | Yes | `0` | 0-100. `PromptEdit` preview generation caps at 90 (not 100) by design — it isn't a terminal state |
| `ProcessingDurationMs` | int | Yes | `0` | |
| `LastErrorMessage` | string(2000) | No | — | Diagnostic detail when `RenderStatus = Failed` |
| `CompositingEngine` | string(50) | No | `""` | "pikaswaps", "PlanarWarp", "ffmpeg-luma", "ffmpeg-perspective", or "kling-o1-edit" |
| `QualityTier` | string(20) | No | `""` | "AI" (pikaswaps or kling-o1-edit), "Exact" (planar warp), or "Standard" (ffmpeg fallback) |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |
| `IsQueuedForFinal` | bool | Yes | `false` | This render is the chosen one for its scene, to be spliced into the content's final assembled video (`ContentItems.FinalVideoStorageKey`). At most one render per scene at a time — see `RenderService.SetQueuedForFinalAsync` |
| `SceneClipStorageKey` | string | No | — | Download path (`/api/renders/{id}/scene-clip` for PromptEdit, or the same value as `StorageKey` for Interactive) for this render's output trimmed to just its scene — what final assembly actually splices in, since `StorageKey` is the full video for PromptEdit renders |

---

## Table: EventLogs

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `Timestamp` | DateTime | Yes | `UtcNow` | |
| `EventCode` | string | Yes | — | |
| `Severity` | string | Yes | `"Info"` | Info, Warning, Major, Critical |
| `Module` | string | Yes | — | |
| `User` | string | Yes | `"System"` | |
| `Description` | string | Yes | — | |

---

## Table: AlarmItems

| Column | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Id` | string (PK) | Yes | `Guid.NewGuid()` | |
| `Timestamp` | DateTime | Yes | `UtcNow` | |
| `Severity` | string | Yes | `"Minor"` | Minor, Major, Critical |
| `Source` | string | Yes | — | |
| `Description` | string | Yes | — | |
| `IsActive` | bool | Yes | `true` | |

---

## Table: UsageRecords

| Column | Type | Required | Notes |
|---|---|---|---|
| `Id` | string (PK) | Yes | |
| `Timestamp` | DateTime | Yes | |
| `UserId` | string(100) | No | |
| `UserEmail` | string(200) | No | |
| `RequestPath` | string(500) | Yes | |
| `HttpMethod` | string(10) | Yes | |
| `StatusCode` | int | Yes | |
| `ResponseTimeMs` | long | Yes | |
| `IpAddress` | string(50) | No | |

---

## Schema Patterns

- **All PKs:** `string Id = Guid.NewGuid().ToString()` (not auto-increment)
- **All timestamps:** `DateTime.UtcNow`
- **JSON-in-string:** `BoundaryCoordinatesJson`, `OrientationVectorJson` — serialized as strings, parsed at API boundary
- **No soft deletes:** No `IsDeleted` flags — hard deletes only
- **No cascade deletes:** FK relationships managed in application code
- **Enum-like strings:** Status fields use string constants, not DB enums
