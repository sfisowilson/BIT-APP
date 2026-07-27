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
| `TrackedBoundariesJson` | string(100000) | No | — | Per-frame tracking data from SAM3 |
| `Sam3Prompt` | string(500) | No | — | Gemini-generated visual description for SAM3 segmentation |

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
| `SurfaceId` | string | Yes | — | FK → SurfaceItems |
| `CampaignId` | string | Yes | — | FK → CampaignItems |
| `AssetId` | string | Yes | — | FK → CreativeAssets |
| `ExportPreset` | string | Yes | `"Web-Ready MP4"` | |
| `StorageKey` | string | Yes | — | |
| `RenderStatus` | string | Yes | `"Queued"` | Queued, Processing, Finished, Failed |
| `Progress` | int | Yes | `0` | 0-100 |
| `ProcessingDurationMs` | int | Yes | `0` | |
| `CreatedAt` | DateTime | Yes | `UtcNow` | |

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
