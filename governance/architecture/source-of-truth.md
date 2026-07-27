# Source of Truth Registry

**Version:** 1.0 | **Date:** 2026-07-23

> **⚠️ For every domain concept in BIT, there is exactly ONE file that is the definitive source of truth. When in doubt about any concept, consult its canonical file — not your memory. This eliminates the #1 cause of hallucinations.**

---

## Domain → Canonical File Map

### Data & Persistence

| Concept | Canonical File | Notes |
|---|---|---|
| **All entity definitions** | `dotnet-api/Models/Models.cs` | Single file. All 11 entities. |
| **Current DB schema** | `governance/contracts/db-schema.md` | Human-readable snapshot |
| **Migrations (schema history)** | `dotnet-api/Migrations/` | EF Core code-first |
| **DB context** | `dotnet-api/Data/PostgresDbContext.cs` | DbSets, relationships, config |
| **Seed data** | `dotnet-api/Data/DbSeeder.cs` | Development-only initial data |

### API

| Concept | Canonical File | Notes |
|---|---|---|
| **All endpoints (exact signatures)** | `governance/contracts/api-contract.md` | Verified from controllers |
| **Auth endpoints** | `dotnet-api/Controllers/AuthController.cs` | Login, refresh, validate, register |
| **Content endpoints** | `dotnet-api/Controllers/ContentController.cs` | Upload, pipeline transitions |
| **Campaign endpoints** | `dotnet-api/Controllers/CampaignsController.cs` | CRUD + asset association |
| **Scene endpoints** | `dotnet-api/Controllers/ScenesController.cs` | AI modify, split-analyze |
| **Surface endpoints** | `dotnet-api/Controllers/SurfacesController.cs` | List, approve |
| **Render endpoints** | `dotnet-api/Controllers/RendersController.cs` | List, dispatch |
| **All DTOs** | `dotnet-api/DTOs/` | 11 DTO files. Request/response shapes. |
| **Frontend API client** | `src/apiClient.ts` | All HTTP functions. JWT, error handling. |
| **Frontend types** | `src/types.ts` | TypeScript interfaces mirroring .NET DTOs |

### Pipeline

| Concept | Canonical File | Notes |
|---|---|---|
| **Pipeline state machine** | `dotnet-api/Services/ContentService.cs` | `PipelineStages` static class. All valid transitions. |
| **Stage transition logic** | `dotnet-api/Services/ContentService.cs` | `TransitionStageAsync()` method |
| **Pipeline endpoints** | `dotnet-api/Controllers/ContentController.cs` | transition, retranscode, redetect-scenes, mark-failed, reset |

### AI Engines

| Concept | Canonical File | Notes |
|---|---|---|
| **Engine registration (factory)** | `dotnet-api/Program.cs` | `engine_detection`, `engine_brand_analysis`, `engine_compositing` |
| **Detection interface** | `dotnet-api/Services/ISurfaceDetectionService.cs` | Contract |
| **Brand analysis interface** | `dotnet-api/Services/IBrandAnalysisService.cs` | Contract |
| **Compositing interface** | `dotnet-api/Services/ICompositingService.cs` | Contract |
| **YOLO detector** | `detection-service/detector.py` | `YoloSurfaceDetector` class |
| **YOLO API** | `detection-service/main.py` | FastAPI `/detect`, `/health` |

### Frontend

| Concept | Canonical File | Notes |
|---|---|---|
| **App root (routing, state)** | `src/App.tsx` | URL-derived state, all handlers |
| **All component interfaces** | `governance/contracts/component-contracts.md` | Verified props |
| **URL routes** | `src/App.tsx` (useMemo) + `src/components/CampaignSidebar.tsx` | `/c/:id/:view` pattern |
| **Auth state** | `src/apiClient.ts` (token mgmt) + `src/App.tsx` (login/logout) | |
| **Custom hooks** | `src/hooks/useChunkedUpload.ts`, `usePaginatedData.ts`, `useIdleTimer.ts` | |

### Configuration

| Concept | Canonical File | Notes |
|---|---|---|
| **.NET config** | `dotnet-api/appsettings.json` | Default config |
| **Environment overrides** | `dotnet-api/appsettings.Development.json`, `appsettings.Production.json` | |
| **Runtime settings (DB-backed)** | Via `PlatformSettingsService` → `PlatformSettings` table | Admin-configurable |
| **Vite config** | `vite.config.ts` | Build, proxy, Tailwind |
| **TypeScript config** | `tsconfig.json` | Strict mode, paths |

### Governance

| Concept | Canonical File | Notes |
|---|---|---|
| **All rules** | `governance/rules/` | agent-workflow, no-mock-code, verification, testing, prerequisites |
| **Architecture** | `governance/architecture/bit-platform-architecture.md` | Complete system reference |
| **Design** | `governance/design/bit-platform-design.md` | Subsystem designs |
| **API contract** | `governance/contracts/api-contract.md` | All endpoints |
| **DB schema** | `governance/contracts/db-schema.md` | All tables/columns |
| **Component contracts** | `governance/contracts/component-contracts.md` | All component props |
| **Skill file** | `.github/skills/bit-development/SKILL.md` | Agent workflow skill |
| **Copilot instructions** | `copilot-instructions.md` | Always-on guidance |

---

## Anti-Hallucination Protocol

When you need to know ANYTHING about the project:

1. **Identify the domain** from the table above
2. **Read the canonical file** — not a summary, not memory, the ACTUAL file
3. **If the concept is not in the canonical file, it does NOT exist**
4. **Never say "I think..." or "It probably..."** — read or ask

### Common Hallucination Traps

| Trap | Reality |
|---|---|
| "There's probably a DELETE endpoint for surfaces" | **Verify.** Read `SurfacesController.cs`. If not there, it doesn't exist. |
| "The User model probably has a PhoneNumber field" | **Verify.** Read `Models.cs`. If not there, it doesn't exist. |
| "I can add a new field to ContentItem" | **Verify.** Read `Models.cs` → add field → create migration. |
| "This component accepts an `onSave` prop" | **Verify.** Read `component-contracts.md` or the component file. |
