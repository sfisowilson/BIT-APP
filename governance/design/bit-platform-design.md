# BIT Platform — Design Document

**Version:** 1.0
**Date:** 2026-07-23
**Source:** Derived from codebase analysis and `docs/DESIGN_DOCUMENT.md`

---

## 1. Subsystem Designs

### 1.1 Ingestion Subsystem

**Purpose:** Accept source video, validate, transcode to normalized format, detect scenes, extract metadata.

**States:** `Staging` → `Transcoding` → `SceneDetecting` → `Completed` | `Failed`

**Key Constraints:**
- Duration must not be modified from source (MReq 1)
- Strict campaign naming structure: `XX00XX00_XXXX XX00XX00_XXXX`
- Invalid/unsupported uploads rejected with clear error
- Chunked upload via `useChunkedUpload` → `POST /api/content/upload/*`

**Implementation:**
- Controller: `ContentController.cs`
- Service: `ContentService.cs` (includes `PipelineStages` state machine)
- Model: `ContentItem` + `SceneItem`
- Background jobs via Hangfire

### 1.2 Scene Analysis Subsystem

**Purpose:** Run CV models to detect candidate advertising surfaces per frame, estimate depth and orientation.

**Detectable Surface Types:**
- Billboards, TV/LED screens, walls, signage, posters
- Stadium perimeter LED boards, product packaging, product spaces

**Permanently Excluded (Brand Safety):**
- Human faces, children, emergency vehicles, government insignia, religious symbols/spaces

**AI Models:**
| Function | Current | Alternative |
|---|---|---|
| Object detection | YOLOv11 (ultralytics) | Detectron2 |
| Tracking | ByteTrack | — |
| Depth estimation | Heuristic (bounding box) | MiDaS / Depth Anything v2 |
| Orientation | Geometric solver from quad | — |

**Implementation:**
- Python FastAPI service at `detection-service/`
- Called by .NET backend, not directly by frontend
- Configurable thresholds: `confidence_threshold`, `iou_threshold` per request
- Model hot-swap on `model_size` change — no restart needed

### 1.3 Placement Recommendation Subsystem

**Purpose:** Score and rank candidate surfaces; enforce brand-safety; present for human review.

**Scoring Formula:**
$$Score = w_1 C_{confidence} + w_2 V_{visibility} + w_3 D_{duration} + w_4 S_{size} - w_5 O_{occlusion} - P_{brand\_conflict}$$

**Brand-Safety Pipeline (MReq 4):**
1. Check surface against permanent exclusion categories → auto-reject
2. Detect existing brands in scene (logo/text/category) → flag conflicts
3. Human approval mandatory → no output without approval

**Implementation:**
- Service: `BrandSafetyCheckService.cs` implements `IBrandSafetyCheckService`
- Controller: `BrandSafetyController.cs`
- Admin-configurable exclusion list via platform settings

### 1.4 Motion Tracking Subsystem

**Purpose:** Lock brand assets to surfaces across camera movement (pan, tilt, zoom, rotation).

**Tracking Methods (Planned):**
1. Primary: OpenCV planar tracker (homography-based)
2. Fallback: Point tracker (KLT / optical flow)
3. Post-track QA: Re-validate placement; flag drift/slip

### 1.5 Compositing & Rendering Subsystem

**Purpose:** Warp, relight, blur, shadow-match brand assets into video; render distributable output.

**Compositing Pipeline:**
```
Asset Image → Perspective Warp → Lighting Match → Motion Blur + Shadow + Grain → GPU Render
```

**Swappable Engines:**
```
ICompositingService
├── OpenCvCompositingService    (current: preview)
├── BasicCompositingService     (fallback)
└── [Future: Runway, SAM2+IC-Light, Proprietary]
```

**Export Presets:** Broadcast (ProRes), Streaming (H.264), Social (Vertical 9:16 optional)

**Implementation:**
- Controller: `CompositingController.cs`, `RendersController.cs`
- Service: `RenderService.cs`, `RenderJobService.cs` (Hangfire)
- Model: `RenderItem`

### 1.6 Campaign & Inventory Subsystem

**Purpose:** Manage advertisers, campaigns, creative assets, scheduling, regional targeting.

**Campaign Lifecycle:**
```
Draft → Active → Completed
  └────── Paused ──────┘
```

**Key Rules:**
- Campaign naming: strict structure `XX00XX00_XXXX XX00XX00_XXXX`
- Assets categorized per client, brand, dimension
- 30 brand categories for competitive separation
- Regional targeting: same content → different brand per market

**Implementation:**
- Controller: `CampaignsController.cs`, `AssetsController.cs`
- Service: `CampaignService.cs`, `AssetService.cs`
- Model: `CampaignItem`, `CreativeAsset`, `AdSlotItem`

### 1.7 Approval Workflow Subsystem

**Purpose:** Mandatory human-in-the-loop approval for every placement; permanent audit trail.

```
Placement Recommended → Present to Approver → Review → Approved/Rejected
                                                          ↓
                                              Audit trail recorded (ApprovalItem)
```

**Implementation:**
- Controller: `ApprovalsController.cs`
- Model: `ApprovalItem` (decision, rejection reason, approver, timestamp)

### 1.8 Analytics & BI Subsystem

**Metrics Tracked (MReq 19):**
- Content items ingested, analysed, rendered
- Ad slots created and assigned
- Estimated impressions and exposure duration
- Peak/average processing and render times
- Revenue vs. render cost

**Implementation:**
- Controller: `StatsController.cs`
- Frontend: `AnalyticsTab.tsx`

### 1.9 Operations Subsystem

**Purpose:** Event logging, alarms, usage tracking, admin console.

**Implementation:**
- Controllers: `LogsController.cs`, `AlarmsController.cs`, `UsageController.cs`, `AdminSettingsController.cs`
- Middleware: `UsageTrackingMiddleware.cs` (MReq 22)
- Models: `EventLog`, `AlarmItem`, `UsageRecord`
- Frontend: `TelemetryTab.tsx`, `AdminConsoleTab.tsx`

---

## 2. API Design

### 2.1 Conventions

| Convention | Value |
|---|---|
| Base URL | `/api` |
| Protocol | HTTPS |
| Auth | JWT Bearer in `Authorization` header |
| Content-Type | `application/json` |
| JSON Naming | camelCase |
| Controller Attribute Routing | `[HttpGet]`, `[HttpPost("{id}")]` |
| Pagination | `PaginatedResult<T>` with `Page`, `PageSize`, `TotalCount`, `Items` |

### 2.2 Endpoint Summary by Controller

| Controller | Key Endpoints |
|---|---|
| **Auth** | `POST /api/auth/login`, `POST /api/auth/register` |
| **Users** | `GET/POST /api/users`, `PUT/DELETE /api/users/{id}` |
| **UserProfile** | `GET/PUT /api/profile` |
| **AdminRoleRequests** | `GET/POST /api/admin/role-requests`, `POST .../approve`, `POST .../reject` |
| **AdminSettings** | `GET/PUT /api/admin/settings` |
| **Content** | `GET/POST /api/content`, `POST .../upload`, `POST .../{id}/transition`, `POST .../{id}/retranscode`, `POST .../{id}/redetect-scenes`, `POST .../{id}/mark-failed`, `POST .../{id}/reset` |
| **Scenes** | `GET /api/scenes`, `GET/PUT /api/scenes/{id}`, `GET .../{id}/surfaces` |
| **Surfaces** | `GET/POST /api/surfaces`, `PUT /api/surfaces/{id}` |
| **Campaigns** | `GET/POST /api/campaigns`, `DELETE /api/campaigns/{id}`, `GET .../{id}/assets` |
| **Assets** | `GET/POST /api/assets`, `DELETE /api/assets/{id}` |
| **Compositing** | `POST /api/compositing/preview` |
| **Renders** | `GET/POST /api/renders`, `GET .../{id}/status` |
| **Approvals** | `GET/POST /api/approvals` |
| **BrandSafety** | `GET/PUT /api/brand-safety` |
| **Alarms** | `GET /api/alarms`, `PUT /api/alarms/{id}` |
| **Logs** | `GET /api/logs` |
| **Notifications** | `GET/PUT /api/notifications/preferences` |
| **Stats** | `GET /api/stats/summary` |
| **Usage** | `GET /api/usage` |
| **Attention** | `GET /api/attention` |

### 2.3 Standard Response Envelope

```json
{
  "data": { ... },
  "error": null,
  "meta": { "page": 1, "pageSize": 20, "totalCount": 150 }
}
```

---

## 3. Frontend Design

### 3.1 URL-Driven State

| URL Pattern | View |
|---|---|
| `/` | Landing page |
| `/c/:campaignId` | Campaign dashboard |
| `/c/:campaignId/assets` | Asset library |
| `/c/:campaignId/content` | Content ingestion |
| `/c/:campaignId/placements` | Scene editor / surface QA |
| `/c/:campaignId/renders` | Compositing & renders |
| `/c/:campaignId/reports` | Analytics & reports |
| `/admin` | Admin console |
| `/telemetry` | System telemetry |

### 3.2 Component Composition Pattern

```
<App>
├── Login Screen (unauthenticated)
└── Authenticated App
    ├── Top Nav (CampaignSelector, user menu, AttentionBell)
    ├── CampaignSidebar (when campaign selected)
    └── Main Content
        ├── CampaignDashboard
        ├── CampaignsTab (assets)
        ├── IngestionTab (content upload/pipeline)
        ├── EditorTab (scene QA / surface review)
        ├── ComposerTab (renders)
        ├── AnalyticsTab
        ├── TelemetryTab
        └── AdminConsoleTab
            ├── SettingsPanel
            ├── BrandSafetyPanel
            ├── RoleRequestsPanel
            └── NotificationPreferencesPanel
```

### 3.3 State Management

| Type | Mechanism |
|---|---|
| Server State | Fetch on mount via `apiClient.ts`; no global cache |
| Auth State | JWT in `localStorage`; user session in memory |
| URL State | Campaign ID + view in URL path (single source of truth) |
| Form State | Local `useState` per component |

### 3.4 Data Flow

```
Component → apiClient.ts (fetchWithAuth) → .NET Controller → Service → Repository → EF Core → PostgreSQL
                                                                                    ↓
                                                                         Python Detection Service
                                                                         (called by .NET service, not frontend)
```

---

## 4. Database Design

### 4.1 Schema Conventions

- All primary keys: `string Id = Guid.NewGuid().ToString()`
- All timestamps: `DateTime.UtcNow`
- JSON fields stored as strings: `BoundaryCoordinatesJson`, `OrientationVectorJson`
- String lengths: `[MaxLength(N)]` annotations
- Foreign keys: `[ForeignKey]` + nullable reference
- JSON naming: camelCase via `PropertyNamingPolicy = CamelCase`

### 4.2 Migrations

- EF Core code-first migrations in `dotnet-api/Migrations/`
- Auto-applied on startup: `context.Database.Migrate()`
- Seeding in development only: `DbSeeder.SeedInitialRecords()`

---

## 5. Security Design

### 5.1 Auth Flow

```
POST /api/auth/login { email, password }
  → Validate credentials (bcrypt verify)
  → Generate JWT (HMAC-SHA256, includes role claims)
  → Return { token, user }

All subsequent requests:
  → Authorization: Bearer <token>
  → JWT middleware validates, sets ClaimsPrincipal
  → [Authorize] attribute checks authentication
  → [Authorize(Roles = "Admin")] for role-gated endpoints
```

### 5.2 Roles & Permissions

| Role | Permissions |
|---|---|
| **Admin** | Full system access: users, config, exclusions, all data |
| **Editor / Approver** | Review scenes, approve/reject placements, manage content QA |
| **Advertiser** | Create campaigns, upload assets, view own campaigns and reports |

---

## 6. Error Handling Design

### 6.1 Backend

- `ExceptionHandlingMiddleware` catches unhandled exceptions
- Returns structured errors: `{ error: "message" }`
- Development: `UseDeveloperExceptionPage()` (detailed)
- Production: `ExceptionHandlingMiddleware` (friendly, no stack traces)
- Pipeline failures recorded in `ContentItem.LastErrorMessage` / `LastErrorAt`

### 6.2 Frontend

- `apiClient.ts` intercepts at network level:
  - Network failures → "Unable to connect to server"
  - 500-range → "Something went wrong"
  - 400-range → returned as-is for caller to extract `data.error`
- Never leaks raw error details to UI

---

## 7. Key Design Decisions

1. **GUID string IDs** — All entities use string GUIDs, not auto-increment integers
2. **JSON-in-string pattern** — Complex types (coordinates, orientation) serialized as JSON strings in DB columns; parsed at API boundary via `parseSurfaceItem()`
3. **No soft deletes** — Hard deletes used (no `IsDeleted` flags)
4. **Configurable AI engines** — Runtime-swappable via platform settings; restart required
5. **Chunked uploads** — Large files split into chunks; assembled server-side
6. **CamelCase JSON everywhere** — Both backend serialization and frontend types
7. **Hangfire for all background work** — Single background job infrastructure (PostgreSQL-backed)
