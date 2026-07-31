# API Contract — Complete Endpoint Inventory

**Version:** 1.0 | **Date:** 2026-07-23 | **Source:** Verified from actual controllers

> **⚠️ This is the single source of truth for all API endpoints. If an endpoint is not listed here, it does NOT exist. Do not assume endpoints.**

---

## Conventions

- Base: `/api`
- Auth: `[Authorize]` unless noted `No` in Auth column
- JSON: camelCase everywhere
- Pagination: `PaginatedResult<T>` with `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasPreviousPage`, `hasNextPage`
- Error shape: `{ error: "message" }`

---

## Auth Controller — `api/auth`

| Method | Path | Auth | Request Body | Response |
|---|---|---|---|---|
| `POST` | `/api/auth/login` | No | `LoginRequestDto { email, password }` | `{ token, user: { id, fullName, email, role, accountStatus } }` |
| `POST` | `/api/auth/refresh` | No | `TokenRefreshDto { token }` | `{ token, user }` |
| `POST` | `/api/auth/validate` | No | `TokenRefreshDto { token }` | `{ valid: boolean }` |
| `POST` | `/api/auth/register` | No | Registration DTO | User + token |

---

## Campaigns Controller — `api/campaigns` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/campaigns` | Query: `CampaignFilterParams { page, pageSize, ... }` | `PaginatedResult<CampaignItem>` |
| `GET` | `/api/campaigns/{id}/assets` | — | `{ campaign: CampaignItem, assets: CreativeAsset[] }` |
| `GET` | `/api/campaigns/{id}/summary` | — | `{ hasApprovedPlacements: bool }` — lightweight pipeline-status signal (any `SurfaceItem` with `Status = "Approved"` for a scene under this campaign's content); used by the Campaign Dashboard's "Placements" pipeline step |
| `POST` | `/api/campaigns` | `CreateCampaignDto` | `CampaignItem` |
| `PUT` | `/api/campaigns/{id}` | `UpdateCampaignDto` | `CampaignItem` |
| `DELETE` | `/api/campaigns/{id}` | — | `{ success: true }` |

---

## Content Controller — `api/content` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/content` | Query: `ContentFilterParams { page, pageSize, ... }` | `PaginatedResult<ContentItem>` |
| `GET` | `/api/content/{id}` | — | `ContentItem` |
| `POST` | `/api/content/upload` | Form: `title`, `resolution`, `frameRate`, `duration`, `sourceChannel`, `campaignId?`, `file` | `ContentItem` |
| `POST` | `/api/content/{id}/transition` | `{ targetStage, errorMessage? }` | `{ success, id, ingestionStatus, message }` |
| `POST` | `/api/content/{id}/retranscode` | — | `{ success, id, ingestionStatus, message }` |
| `POST` | `/api/content/{id}/redetect-scenes` | — | `{ jobId, id, ingestionStatus, message }` — Enqueues Hangfire detection pipeline |
| `GET` | `/api/content/{contentId}/detection-status` | — | `{ contentId, progress (0-100), ingestionStatus, jobId, errorMessage?, completed, failed }` — Poll for detection job progress |
| `DELETE` | `/api/content/{contentId}/scenes` | — | `{ success, contentId, message }` — Delete all scenes (and child surfaces/ad-slots/approvals) for a content item. Blocks if any surfaces are Approved (400). |
| `POST` | `/api/content/{id}/mark-failed` | `{ targetStage: "Failed", errorMessage? }` | `{ success, id, ingestionStatus, lastErrorMessage? }` |
| `POST` | `/api/content/{id}/reset` | — | `{ success, id, ingestionStatus, message }` |
| `GET` | `/api/content/file/{*fileName}` | — | Binary file (video or image). Supports subdirectories (e.g., `thumbnails/`). MIME: mp4, mov, avi, mxf, webm, jpg, png, gif, webp |

---

## Scenes Controller — `api/` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/scenes` | Query filter | `PaginatedResult<SceneItem>` |
| `PUT` | `/api/scenes/{id}` | Body: scene fields | `SceneItem` |
| `GET` | `/api/scenes/{id}/surfaces` | — | `SurfaceItem[]` |
| `POST` | `/api/scenes/ai-modify` | `{ sceneId?, prompt?, videoTitle?, sceneIndex? }` | `{ data: { description, model } }` |
| `POST` | `/api/scenes/update` | `{ id, aiPrompt?, aiStatus?, aiOutputDescription?, aiModelUsed? }` | `{ success, id }` |
| `POST` | `/api/video/ai-split-analyze` | `{ contentId, videoTitle }` | `{ jobId, contentId, message }` — The primary detection entry point ("AI Split Analyze"). Enqueues `RunDetectionPipeline`: FFmpeg shot-cut detection → SAM3 keyframe embedding → clustering shots into scenes (a scene may span multiple cuts) → surface detection per clustered scene. |
| `DELETE` | `/api/scenes/{id}` | — | `{ success, id, message }` — Delete a single scene and all child surfaces/ad-slots/approvals. Blocks if any surface is Approved (400). |
| `GET` | `/api/scenes/{id}/clip` | — | `video/mp4` file download — FFmpeg-extracted clip of the scene's frame range from source video |
| `GET` | `/api/scenes/{sceneId}/shots` | — | `ShotDto[] { id, shotIndex, startFrame, endFrame, keyframeTimestamp, keyframeUrl }` — Shots (camera cuts) making up the scene, ordered by `shotIndex`. A scene can span multiple shots; empty array if the scene predates shot clustering. |

---

## Surfaces Controller — `api/` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/scenes/{sceneId}/surfaces` | — | `SurfaceItem[]` |
| `GET` | `/api/scenes/surfaces/batch` | Query: `sceneIds` (comma-separated) | `SurfaceItem[]` — surfaces for multiple scenes in one request |
| `POST` | `/api/surfaces/preview-segment` | `SegmentPreviewRequest { contentId, frameIndex, x, y }` | `SegmentPreviewResponse { maskPolygonJson, confidence, trackId, surfaceType, frameIndex, boundsXMin/YMin/XMax/YMax }` — SAM3 point-click preview for the interactive placement editor |
| `POST` | `/api/surfaces/from-click` | `CreateSurfaceFromClickRequest { contentId, frameIndex, maskPolygonJson, surfaceType? }` | `CreateSurfaceResponse { surfaceId, sceneId }` — Persists a SurfaceItem (`AssetType="Generative"`, `Source="Manual"`) from an interactive "Insert Product" click; scene resolved from `contentId`+`frameIndex`. Required before dispatching a Generative interactive render. |
| `POST` | `/api/surfaces/from-quad` | `CreateSurfaceFromQuadRequest { contentId, frameIndex, quadCornersJson, surfaceType? }` | `CreateSurfaceResponse { surfaceId, sceneId }` — Same as above for a "Place Signage" 4-corner quad (`AssetType="Planar"`). |
| `POST` | `/api/surfaces/{id}/approve` | `ApprovalDto` | Approval result — persists the (optionally operator-adjusted) boundary as the new seed. Actual tracking happens per-shot inside the render job (`ShotAwareTrackingService`), not at approval time. |

---

## Renders Controller — `api/renders` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/renders` | Query: `RenderFilterParams { page, pageSize, ... }` | `PaginatedResult<RenderItem>` |
| `POST` | `/api/renders/interactive` | `CreateInteractiveRenderDto { contentId, surfaceId, campaignId, assetId, assetType, exportPreset? }` | `202` → `RenderItem` — The interactive-placement dispatch endpoint. Routes to the Planar or Generative Hangfire job based on `assetType`. `surfaceId` must already exist — see `POST /api/surfaces/from-click`/`from-quad`. Used for both interactively-placed and AI-detected surfaces (the latter always dispatch with `assetType: "Generative"`). |
| `POST` | `/api/renders/prompt-preview` | `CreatePromptRenderDto { contentId, sceneId, campaignId, assetId, promptText, exportPreset? }` | `202` → `RenderItem` — The prompt-based dispatch endpoint ("AI Placement Assistant → Generate New"). No pre-existing surface required. `sceneId`'s duration must fall within `[3.0, 10.05]`s (else `400`) — Kling O1's real input constraints. Creates a `RenderItem(RenderMode="PromptEdit", SurfaceId=null)` and enqueues `ProcessPromptPreviewJob`, which generates a preview and stops at `RenderStatus = "PreviewReady"` (does not auto-finalize). |
| `POST` | `/api/renders/{id}/approve-splice` | — | `200` → `RenderItem` — Requires `RenderStatus == "PreviewReady"` (else `400`). Enqueues `ProcessPromptSpliceJob`, which splices the approved preview clip into the full source video in place of the target scene and finalizes to `RenderStatus = "Finished"`. |
| `POST` | `/api/renders/{id}/reject-prompt` | `RejectPromptRenderDto { reason? }` | `200` → `{ success: true }` — Requires `RenderStatus == "PreviewReady"` (else `400`). Sets `RenderStatus = "Rejected"`, no splice job enqueued, no final video produced. |
| `GET` | `/api/renders/{id}/status` | — | Render status |
| `POST` | `/api/renders/{id}/retry` | — | Re-enqueues a Failed render. For `RenderMode = "PromptEdit"` renders, re-enqueues `ProcessPromptPreviewJob` instead of the Interactive-mode retry path. |
| `GET` | `/api/renders/{id}/download` | — | `video/mp4` file download, `[AllowAnonymous]`. Serves `renders/BIT_Render_{id}.mp4`, falling back to a sample video if missing. |
| `GET` | `/api/renders/{id}/preview` | — | `video/mp4` file download, `[AllowAnonymous]`. Serves the not-yet-approved `renders/BIT_Preview_{id}.mp4` for the preview player; `404` if missing (no fallback). |

---

## Assets Controller — `api/assets` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/assets` | Query filter | `PaginatedResult<CreativeAsset>` |
| `POST` | `/api/assets` | Multipart form: `name, type, brandCategory, campaignId?, file` | `CreativeAsset` |
| `DELETE` | `/api/assets/{id}` | — | `{ success: true }` |

---

## Approvals Controller — `api/approvals` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/approvals` | Query: `ApprovalFilterParams { page, pageSize, adSlotId?, campaignId?, decision? }` | Paginated approvals |

---

## Invoices Controller — `api/campaigns` — `[Authorize]`

| Method | Path | Request | Response |
|---|---|---|---|
| `GET` | `/api/campaigns/{campaignId}/invoice` | — | `200` → `InvoiceSummaryDto` — `404` if campaign not found. Computed live (not persisted) as exposure seconds × viability multiplier + render processing costs: one line item per `Finished` render (`durationSeconds × viabilityScore × ZAR150/sec`), or a single flat "Campaign Setup & Surface Booking" (`ZAR1350`) placeholder line if no renders have finished yet; plus `ZAR250 × renderCount` processing fee and 15% VAT. |

---

## Operations Controllers (all `[Authorize]`)

| Controller | Route | Key Endpoints |
|---|---|---|
| **Alarms** | `api/alarms` | `GET /` list, `PUT /{id}` acknowledge |
| **Logs** | `api/logs` | `GET /` query event logs |
| **Stats** | `api/stats` | `GET /summary` → `StatsSummary` |
| **Users** | `api/users` | `GET /` list (Admin), `POST /` create (Admin), `PUT /{id}`, `DELETE /{id}` |
| **UserProfile** | `api/profile` | `GET /`, `PUT /` own profile |
| **AdminRoleRequests** | `api/admin/role-requests` | `GET /`, `POST /`, `POST /{id}/approve`, `POST /{id}/reject` |
| **AdminSettings** | `api/admin/settings` | `GET /`, `PUT /` platform settings |
| **BrandSafety** | `api/brand-safety` | `GET /`, `PUT /` exclusion list |
| **Notifications** | `api/notifications` | `GET /preferences`, `PUT /preferences` |
| **Usage** | `api/usage` | `GET /` usage records |
| **Attention** | `api/attention` | `GET /` attention feed |
| **Compositing** | `api/compositing` | `POST /preview` — Returns `CompositedFrame { imageBase64, contentType, engineUsed, processingMs }`. Overlays asset onto video frame at surface coordinates. |

---

## Frontend API Client Functions (`src/apiClient.ts`)

| Function | HTTP | Endpoint | Returns |
|---|---|---|---|
| `login(credentials)` | POST | `/api/auth/login` | `LoginResponse { token, user }` |
| `refreshToken()` | POST | `/api/auth/refresh` | `LoginResponse \| null` |
| `logout()` | — | — | void (clears localStorage) |
| `fetchWithAuth(url, opts)` | * | * | `Response` (JWT attached) |
| `fetchPublic(url, opts)` | * | * | `Response` (no auth) |
| `fetchJson<T>(url)` | GET | * | `Promise<T>` |
| `fetchPaginated<T>(url, params)` | GET | * | `{ items, totalCount, page, pageSize, totalPages, hasPreviousPage, hasNextPage }` |
| `transitionStage(contentId, targetStage, errorMessage?)` | POST | `/api/content/{id}/transition` | `{ success, id, ingestionStatus, message }` |
| `retranscode(contentId)` | POST | `/api/content/{id}/retranscode` | `{ success, id, ingestionStatus, message }` |
| `redetectScenes(contentId)` | POST | `/api/content/{id}/redetect-scenes` | `{ jobId, id, ingestionStatus, message }` — Enqueues Hangfire; poll `/api/content/{id}/detection-status` for completion |
| `markFailed(contentId, errorMessage?)` | POST | `/api/content/{id}/mark-failed` | `{ success, id, ingestionStatus, lastErrorMessage? }` |
| `resetPipeline(contentId)` | POST | `/api/content/{id}/reset` | `{ success, id, ingestionStatus, message }` |
| `fetchStatsSummary()` | GET | `/api/stats/summary` | `StatsSummary` |
| `fetchCampaignInvoice(campaignId)` | GET | `/api/campaigns/{id}/invoice` | `InvoiceSummary` |
| `buildQueryString(params)` | — | — | Query string helper |

---

## TypeScript Types Matching API Responses (`src/types.ts`)

| Type | Matching .NET Entity/DTO |
|---|---|
| `ContentItem` | `ContentItem` model |
| `SceneItem` | `SceneItem` model |
| `SurfaceItemResponse` | Raw API shape (JSON strings) |
| `SurfaceItem` | Parsed shape (deserialized coords/orientation) |
| `CampaignItem` | `CampaignItem` model |
| `CreativeAsset` | `CreativeAsset` model |
| `RenderItem` | `RenderItem` model |
| `EventLog` | `EventLog` model |
| `AlarmItem` | `AlarmItem` model |
| `User` | `User` model |
| `AuthResponse` | `LoginResponseDto` |
| `StatsSummary` | Stats aggregation DTO |
| `InvoiceSummary` | `InvoiceSummaryDto` |
| `InvoiceLineItem` | `InvoiceLineItemDto` |
| `BRAND_CATEGORIES` | 30 brand categories (constant array) |

---

## Pipeline Valid Transitions (from `ContentService.PipelineStages`)

```
Staging       → Transcoding, Failed
Transcoding   → SceneDetecting, Failed
SceneDetecting → Completed, Failed
Failed        → Staging (retry)
Completed     → SceneDetecting (re-detect only)
```

All other transitions are REJECTED.
