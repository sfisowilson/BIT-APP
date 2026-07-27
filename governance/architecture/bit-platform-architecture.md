# BIT Platform — Architecture Document

**Version:** 1.0
**Date:** 2026-07-23
**Source:** Derived from codebase analysis and `docs/DESIGN_DOCUMENT.md`

---

## 1. System Architecture Overview

BIT (Brand Inserts Technology) is a **three-tier, multi-service platform** for AI-powered video inventory creation. It transforms surfaces inside video content into monetisable advertising inventory.

### 1.1 High-Level Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                     CLIENT LAYER (Browser)                        │
│  React 19 SPA  ·  Vite 6  ·  TypeScript 5.8  ·  Tailwind CSS 4  │
│  Motion (Framer)  ·  Lucide React  ·  React Router DOM 7         │
└──────────────────────────┬───────────────────────────────────────┘
                           │ HTTPS / JWT Bearer
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     API GATEWAY LAYER                             │
│  .NET 8 ASP.NET Core Web API  ·  C# 12                           │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌───────────┐ │
│  │19       │ │Services │ │Repos    │ │DTOs     │ │Middleware │ │
│  │Control- │ │(22 impl)│ │(8 impl) │ │(11 files)│ │(2 impl)  │ │
│  │lers     │ │         │ │         │ │         │ │           │ │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └───────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
┌─────────────────┐ ┌──────────┐ ┌──────────────────────┐
│   DATA LAYER    │ │  HANGFIRE│ │   AI/ML SERVICES     │
│  PostgreSQL 16  │ │  Back-   │ │  Python FastAPI      │
│  EF Core 8      │ │  ground  │ │  YOLOv11 + ByteTrack │
│  Npgsql         │ │  Jobs    │ │  OpenCV              │
└─────────────────┘ └──────────┘ └──────────────────────┘
```

### 1.2 Architectural Style

| Pattern | Where Applied |
|---|---|
| **Layered Architecture** | Controllers → Services → Repositories → EF Core → PostgreSQL |
| **Repository Pattern** | Generic `IRepository<T>` + specialized per aggregate |
| **Strategy Pattern** | AI engines: `ISurfaceDetectionService`, `IBrandAnalysisService`, `ICompositingService` |
| **Factory Pattern** | Engine registration in `Program.cs` via `IPlatformSettingsService` |
| **State Machine** | Content pipeline: Staging → Transcoding → SceneDetecting → Completed/Failed |
| **SPA + API Proxy** | Vite dev proxy `/api/*` → .NET backend; same-origin in production |
| **DTO Pattern** | Every API boundary uses DTOs — EF entities never exposed |

---

## 2. Technology Stack (Verified from Codebase)

### 2.1 Frontend

| Technology | Version | Usage |
|---|---|---|
| React | 19.0 | UI framework (Hooks, Context) |
| TypeScript | ~5.8 | Type-safe development |
| Vite | 6.2 | Build tool & dev server |
| Tailwind CSS | 4.1 | Utility-first CSS |
| React Router DOM | 7.18 | Client-side routing, URL-driven state |
| Motion (Framer Motion) | 12.23 | Page transitions, animations |
| Lucide React | 0.546 | Icon library |
| @google/genai | 2.4 | Gemini AI SDK (client-side) |

### 2.2 Backend (.NET API)

| Technology | Version | Usage |
|---|---|---|
| .NET / ASP.NET Core | 8.0 | Web API framework |
| C# | 12 | Programming language |
| Entity Framework Core | 8.0 | ORM |
| Npgsql | 8.0 | PostgreSQL provider |
| PostgreSQL | 16+ | Primary database |
| Hangfire | (latest) | Background job processing |
| JWT Bearer Auth | 8.0 | Stateless authentication |
| BCrypt | (via BCrypt.Net) | Password hashing |

### 2.3 AI/ML Detection Service

| Technology | Usage |
|---|---|
| Python 3 | Runtime |
| FastAPI | REST API framework |
| Ultralytics YOLOv11 | Object detection model |
| OpenCV | Image processing |
| ByteTrack | Multi-object tracking |
| NumPy | Numerical computation |

---

## 3. Component & Module Inventory

### 3.1 .NET API Controllers (19 controllers)

| Controller | File | Purpose |
|---|---|---|
| `AuthController` | `AuthController.cs` | Login, register, JWT issuance |
| `UsersController` | `UsersController.cs` | User CRUD (admin) |
| `UserProfileController` | `UserProfileController.cs` | Own profile management |
| `AdminRoleRequestsController` | `AdminRoleRequestsController.cs` | Role elevation requests |
| `AdminSettingsController` | `AdminSettingsController.cs` | Platform settings management |
| `ContentController` | `ContentController.cs` | Video ingestion, pipeline transitions |
| `ScenesController` | `ScenesController.cs` | Scene management, AI prompts |
| `SurfacesController` | `SurfacesController.cs` | Surface detection results |
| `CampaignsController` | `CampaignsController.cs` | Campaign CRUD |
| `AssetsController` | `AssetsController.cs` | Creative asset management |
| `CompositingController` | `CompositingController.cs` | Compositing preview |
| `RendersController` | `RendersController.cs` | Render job management |
| `ApprovalsController` | `ApprovalsController.cs` | Human approval workflow |
| `BrandSafetyController` | `BrandSafetyController.cs` | Brand safety exclusion list |
| `AlarmsController` | `AlarmsController.cs` | System alarms |
| `LogsController` | `LogsController.cs` | Event log querying |
| `NotificationsController` | `NotificationsController.cs` | Email/SMS notification prefs |
| `StatsController` | `StatsController.cs` | Dashboard statistics |
| `UsageController` | `UsageController.cs` | API usage tracking |
| `AttentionController` | `AttentionController.cs` | User attention/notification feed |

### 3.2 .NET Services (22 implementations)

| Service | Interface | Purpose |
|---|---|---|
| `AuthService` | `IAuthService` | Authentication, JWT generation |
| `UserService` | `IUserService` | User management |
| `CampaignService` | `ICampaignService` | Campaign business logic |
| `ContentService` | `IContentService` | Content ingestion & pipeline state machine |
| `SurfaceService` | `ISurfaceService` | Surface data management |
| `AssetService` | `IAssetService` | Creative asset management |
| `RenderService` | `IRenderService` | Render job management |
| `RenderJobService` | (concrete) | Hangfire render job execution |
| `SceneDetectionJobService` | (concrete) | Hangfire scene detection execution |
| `AlarmService` | `IAlarmService` | Alarm lifecycle |
| `LogService` | `ILogService` | Event log management |
| `EventLogService` | `IEventLogService` | Automatic event emission |
| `EmailService` | `IEmailService` | Email notifications |
| `SmsService` | `ISmsService` | SMS notifications |
| `PlatformSettingsService` | `IPlatformSettingsService` | DB-backed runtime settings |
| `BrandSafetyCheckService` | `IBrandSafetyCheckService` | Brand safety enforcement |
| `YoloSurfaceDetectionService` | `ISurfaceDetectionService` | YOLO-based surface detection |
| `ReplicateSurfaceDetectionService` | `ISurfaceDetectionService` | Replicate API surface detection |
| `GoogleVisionDetectionService` | `ISurfaceDetectionService` | Google Vision surface detection |
| `BasicSurfaceDetectionService` | `ISurfaceDetectionService` | Fallback (no external API) |
| `GeminiBrandAnalysisService` | `IBrandAnalysisService` | Gemini brand analysis |
| `GoogleVisionBrandAnalysisService` | `IBrandAnalysisService` | Google Vision brand analysis |
| `BasicBrandAnalysisService` | `IBrandAnalysisService` | Fallback brand analysis |
| `OpenCvCompositingService` | `ICompositingService` | OpenCV compositing |
| `BasicCompositingService` | `ICompositingService` | Fallback compositing |

### 3.3 AI Engine Swappability (Factory Pattern)

Registered in `Program.cs` — admin-configurable via Platform Settings at runtime:

```
engine_detection  → "yolo" | "replicate" | "google" | "basic"
engine_brand_analysis → "gemini" | "google" | "basic"
engine_compositing → "opencv" | "basic"
```

Engine changes require app restart. Defaults to `"basic"` if setting is missing.

### 3.4 React Frontend Components (20 components)

| Component | File | Purpose |
|---|---|---|
| `App` | `App.tsx` | Root: routing, auth state, tab composition |
| `CampaignsTab` | `CampaignsTab.tsx` | Campaign list & management |
| `IngestionTab` | `IngestionTab.tsx` | Video upload & pipeline monitoring |
| `EditorTab` | `EditorTab.tsx` | Scene QA & surface review |
| `ComposerTab` | `ComposerTab.tsx` | Compositing & render submission |
| `TelemetryTab` | `TelemetryTab.tsx` | System logs & alarms |
| `AdminConsoleTab` | `AdminConsoleTab.tsx` | User management, settings, role requests |
| `AnalyticsTab` | `AnalyticsTab.tsx` | BI dashboard & reports |
| `CampaignDashboard` | `CampaignDashboard.tsx` | Campaign overview |
| `CampaignSidebar` | `CampaignSidebar.tsx` | Campaign-scoped navigation |
| `CampaignSelector` | `CampaignSelector.tsx` | Campaign picker dropdown |
| `BrandSafetyPanel` | `BrandSafetyPanel.tsx` | Brand safety exclusion management |
| `SettingsPanel` | `SettingsPanel.tsx` | Platform settings editor |
| `RoleRequestsPanel` | `RoleRequestsPanel.tsx` | Role elevation approval |
| `PipelineProgress` | `PipelineProgress.tsx` | Content pipeline status visualization |
| `AttentionBell` | `AttentionBell.tsx` | Notification bell with badge |
| `NotificationPreferencesPanel` | `NotificationPreferencesPanel.tsx` | Notification mute toggles |
| `FilterableSelect` | `FilterableSelect.tsx` | Reusable searchable dropdown |
| `Pagination` | `Pagination.tsx` | Reusable pagination controls |
| `BitLogo` | `BitLogo.tsx` | Brand logo component |
| `NotFoundPage` | `NotFoundPage.tsx` | 404 page |

### 3.5 Frontend Hooks (3 custom hooks)

| Hook | File | Purpose |
|---|---|---|
| `useChunkedUpload` | `hooks/useChunkedUpload.ts` | Large file chunked upload with progress |
| `usePaginatedData` | `hooks/usePaginatedData.ts` | Cursor/offset pagination state |
| `useIdleTimer` | `hooks/useIdleTimer.ts` | Session auto-logout on inactivity |

### 3.6 Python Detection Service

| File | Purpose |
|---|---|
| `main.py` | FastAPI app: `/detect` and `/health` endpoints |
| `detector.py` | `YoloSurfaceDetector` class with ByteTrack |
| `requirements.txt` | Dependencies: fastapi, ultralytics, opencv-python, numpy |
| `yolo11n.pt` | YOLOv11 nano model weights |

---

## 4. Data Architecture

### 4.1 Entity Model (11 entities)

| Entity | Table | Key Relationships |
|---|---|---|
| `User` | Users | — |
| `RoleRequest` | RoleRequests | FK → User |
| `ContentItem` | ContentItems | Parent of SceneItems; optional FK → CampaignItem |
| `SceneItem` | SceneItems | FK → ContentItem; parent of SurfaceItems |
| `SurfaceItem` | SurfaceItems | FK → SceneItem; parent of AdSlotItems |
| `AdSlotItem` | AdSlotItems | FK → SurfaceItem, CampaignItem |
| `CampaignItem` | CampaignItems | Parent of CreativeAssets, AdSlots, Renders |
| `CreativeAsset` | CreativeAssets | FK → CampaignItem |
| `ApprovalItem` | ApprovalItems | FK → AdSlotItem, CampaignItem, User |
| `RenderItem` | RenderItems | FK → ContentItem, SurfaceItem, CampaignItem, CreativeAsset |
| `EventLog` | EventLogs | — |
| `AlarmItem` | AlarmItems | — |
| `UsageRecord` | UsageRecords | FK → User |

### 4.2 Database

- **Engine:** PostgreSQL 16+
- **Provider:** Npgsql 8.0 via EF Core 8.0
- **Migrations:** EF Core code-first with automatic migration on startup
- **Seeding:** `DbSeeder.SeedInitialRecords()` in development only
- **Background Jobs:** Hangfire with PostgreSQL storage

---

## 5. Pipeline State Machine

```
                 ┌──────────┐
                 │  Staging │
                 └────┬─────┘
                      │
            ┌─────────┼─────────┐
            ▼         │         ▼
     ┌────────────┐   │   ┌──────────┐
     │Transcoding │   │   │  Failed  │
     └─────┬──────┘   │   └────┬─────┘
           │          │        │ (retry)
           ▼          │        │
   ┌──────────────┐   │   ┌────┴─────┐
   │SceneDetecting│   │   │  Staging │
   └──────┬───────┘   │   └──────────┘
          │           │
    ┌─────┼─────┐     │
    ▼     │     ▼     │
┌────────┐│ ┌──────┐  │
│Completed│ │Failed│  │
└────────┘│ └──┬───┘  │
          │    │      │
          └────┴──────┘
(re-detect only from Completed)
```

**Valid transitions** (enforced by `ContentService.PipelineStages`):
- Staging → Transcoding, Failed
- Transcoding → SceneDetecting, Failed
- SceneDetecting → Completed, Failed
- Failed → Staging (retry)
- Completed → SceneDetecting (re-detect only)

**Endpoint:** `POST /api/content/{id}/transition` with full validation and timestamp tracking.

---

## 6. Security Architecture

| Measure | Implementation |
|---|---|
| Transport | HTTPS enforced |
| Authentication | JWT Bearer tokens (HMAC-SHA256) |
| Password Storage | bcrypt hashing |
| Authorization | Role-based: Admin, Editor, Advertiser |
| CORS | Whitelist: `localhost:3000`, `*.run.app` |
| Input Validation | ASP.NET model validation |
| SQL Injection Prevention | EF Core parameterized queries |
| Session Management | JWT expiry + `useIdleTimer` auto-logout |
| Audit Trail | UsageRecord + EventLog + ApprovalItem |

---

## 7. Integration Points

| Integration | Direction | Protocol |
|---|---|---|
| Frontend → .NET API | React → ASP.NET | HTTPS/REST + JWT |
| .NET API → Python Detection | ASP.NET → FastAPI | HTTP (internal) |
| .NET API → PostgreSQL | EF Core → Npgsql | TCP |
| .NET API → Hangfire | In-process | Direct |
| .NET API → Email (SMTP) | SendGrid/Mailgun | SMTP |
| .NET API → SMS | Twilio/Africa's Talking | REST |
| Frontend → Gemini AI | @google/genai | gRPC/REST |

---

## 8. Key Architectural Decisions (Verified)

1. **DTOs at every API boundary** — Controllers never expose EF entities directly
2. **Interface-abstracted AI engines** — Swappable without re-architecting
3. **Thin controllers** — Business logic in services, data access in repositories
4. **URL-driven routing** — Campaign ID and view encoded in URL path for shareable links
5. **Centralized API client** — All HTTP calls through `apiClient.ts` with JWT handling
6. **Chunked uploads** — Large video files via `useChunkedUpload` hook
7. **Pipeline as state machine** — Guarded transitions with automatic timestamp tracking
8. **Hangfire for background jobs** — Transcoding, scene detection, rendering
9. **Automatic migrations on startup** — EF Core `context.Database.Migrate()`
10. **All timestamps UTC** — `DateTime.UtcNow` everywhere
