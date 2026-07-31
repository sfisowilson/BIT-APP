---
name: bit-development
description: 'BIT (Brand Inserts Technology) platform development. Use when: working on the BIT portal, .NET API, Python detection service, React frontend, video ingestion pipeline, AI surface detection, compositing, campaign management, or any BIT feature. Covers architecture, patterns, conventions, and the full-stack workflow across React/TypeScript, C#/.NET, and Python/FastAPI.'
argument-hint: 'Describe the BIT feature or task'
user-invocable: true
---

# BIT Platform Development

## ⛔ GOVERNANCE — MANDATORY FIRST STEP

**Before taking ANY action on this project, you MUST consult the governance documents at `governance/`.**

### Quick Start (2-minute read)
→ **`governance/architecture/agent-quickstart.md`** — Minimum context every agent needs.

### Mandatory Workflow

1. **Read the quick-start card** — `governance/architecture/agent-quickstart.md`
2. **Prevent hallucinations** — Follow `governance/rules/hallucination-prevention.md` (10 rules)
3. **Verify prerequisites exist** — `governance/features/<name>.gherkin`, `governance/nfrs/<name>.md`, `governance/plans/<name>.md`
4. **Consult exact references** (never guess):
   - Endpoints → `governance/contracts/api-contract.md`
   - Database → `governance/contracts/db-schema.md`
   - Component props → `governance/contracts/component-contracts.md`
   - Which file owns what → `governance/architecture/source-of-truth.md`
   - What files to touch → `governance/rules/file-ownership.md`
5. **Follow all rules** in `governance/rules/`

### Governance Quick Reference

| # | Rule | File | Severity |
|---|---|---|---|
| H1-H10 | Hallucination prevention | `governance/rules/hallucination-prevention.md` | MANDATORY |
| 1 | No mock code — ever | `governance/rules/no-mock-code.md` | NON-NEGOTIABLE |
| 2 | Verify before acting | `governance/rules/verification.md` | MANDATORY |
| 3 | Always add unit tests | `governance/rules/testing.md` | MANDATORY |
| 4 | Prerequisites required | `governance/rules/prerequisites.md` | MANDATORY |
| 5 | Follow architecture patterns | `governance/rules/agent-workflow.md` | MANDATORY |
| 6 | Cross-stack completeness | `governance/rules/agent-workflow.md` | MANDATORY |
| 7 | File ownership traceability | `governance/rules/file-ownership.md` | MANDATORY |
| 8 | Contract freshness validation | `governance/rules/agent-workflow.md` (Rule 8) | MANDATORY |
| 9 | Commit & push after every feature | `governance/rules/commit-discipline.md` | MANDATORY |
| 10 | No assumptions, no temp fixes | `governance/rules/no-assumptions-no-temp-fixes.md` | MANDATORY |
| M1 | Contract maintenance protocol | `governance/rules/contract-maintenance.md` | MANDATORY |

---

## Project Overview

BIT (Brand Inserts Technology) is an AI-powered video inventory creation platform that inserts brand advertisements into existing video content. It transforms surfaces inside video — billboards, screens, walls, signage — into monetisable advertising inventory.

### Tech Stack

| Layer | Technology |
|-------|-----------|
| **Frontend** | React 19, TypeScript ~5.8, Vite 6, Tailwind CSS 4, Motion (framer-motion), Lucide React, React Router DOM 7 |
| **Backend API** | .NET (C#), ASP.NET Core, Entity Framework Core, PostgreSQL, Hangfire (background jobs), JWT auth |
| **AI/ML Service** | Python 3, FastAPI, YOLOv11 (ultralytics), OpenCV, ByteTrack |
| **Dev Server** | Express (Node.js) — proxies `/api/*` to .NET backend |

### Project Structure

```
BIT-APP/
├── src/                        # React frontend
│   ├── App.tsx                 # Main app with routing & state
│   ├── apiClient.ts            # Centralized API client (JWT, fetch wrappers)
│   ├── types.ts                # TypeScript interfaces & parsers
│   ├── main.tsx                # Entry point
│   ├── components/             # 20 feature components (tabs, panels, selectors)
│   └── hooks/                  # useChunkedUpload, useIdleTimer, usePaginatedData
├── dotnet-api/                 # .NET backend API
│   ├── Program.cs              # Service registration, DI, JWT config, AI engine setup
│   ├── Controllers/            # 16 API controllers
│   ├── Services/               # 22 service implementations + interfaces
│   ├── Models/Models.cs        # EF Core entity models
│   ├── DTOs/                   # Request/response DTOs
│   ├── Repositories/           # Repository pattern with interfaces
│   ├── Data/                   # DbContext + seeder
│   ├── Middleware/              # Exception handling, usage tracking
│   └── Migrations/             # EF Core migrations
├── detection-service/          # Python YOLO surface detection microservice
│   ├── main.py                 # FastAPI app with /detect and /health endpoints
│   ├── detector.py             # YoloSurfaceDetector with ByteTrack
│   ├── requirements.txt        # fastapi, ultralytics, opencv-python, numpy
│   └── yolo11n.pt              # YOLOv11 nano model weights
├── vite.config.ts              # Vite config with Tailwind plugin & API proxy (proxies /api directly to the .NET backend)
├── docs/                       # DESIGN_DOCUMENT.md, PRESENTATION_GUIDE.md
├── governance/                 # ⛔ Living governance rules (MUST consult first)
│   ├── README.md               # Governance overview
│   ├── rules/                  # Mandatory rules for all agents
│   ├── architecture/           # Architecture reference documents
│   ├── design/                 # Subsystem design documents
│   ├── plans/                  # Implementation plans (one per feature)
│   ├── features/               # Gherkin feature files
│   ├── nfrs/                   # Non-functional requirements
│   ├── contracts/              # API contracts
│   └── templates/              # Document templates
└── .github/                    # Agents, skills, requirements
```

---

## ⛔ CRITICAL RULE: Never Use Mock Code

**This is the most important rule in this project. Never, under any circumstances, create or add mock/fake/dummy/stub code.**

### What this means:

- **Never add hardcoded data** to any file. There is no in-memory scaffolding in this project — the .NET API + PostgreSQL is the only backend.
- **Never create stub services** that return fake data. Every service must have a real implementation.
- **Never use `placeholder`, `TODO`, `FIXME` as a substitute for real logic.** Implement it properly.
- **Never add "basic"/no-op fallback engines.** Every `ISurfaceDetectionService`/`IBrandAnalysisService`/`ICompositingService`/`ISurfaceTrackingService` implementation must be a real, working engine. If a Platform Setting picks an unconfigured/unknown engine, `EngineFactory` throws a clear configuration error — it does not silently degrade to a no-op.
- **Always use the real API.** Frontend code must call the .NET backend via `apiClient.ts`. Python services must run real YOLO inference. .NET services must use real database queries and real external API calls.

### What to do instead:

- **Implement the real thing.** If a feature needs an API endpoint, implement the controller, service, repository, and DTO properly.
- **Use the existing proxy.** The Vite dev server already proxies `/api/*` to the .NET backend. Use `USE_REAL_API=true` to enable the Express proxy.
- **Test with real data.** Seed the database via `DbSeeder.cs` or use actual test video files.

---

## Architecture Patterns

### 1. Clean Architecture (Backend)

Every feature follows this layered approach:

```
Controller → Service (interface) → Repository (interface) → EF Core → PostgreSQL
     ↕
   DTOs (request/response shapes, never expose entities directly)
```

**Rules:**
- Controllers are thin — they validate input, call services, map to DTOs, return responses.
- Services contain business logic and orchestration.
- Repositories handle data access only.
- Always define interfaces (`IService`, `IRepository`) and register in `Program.cs` via DI.
- Use `[Authorize]` on controllers; role checks with `[Authorize(Roles = "Admin")]`.

### 2. AI Engine Swappability

AI engines are admin-configurable at runtime via Platform Settings. Each engine implements a common interface:

```
ISurfaceDetectionService  → Yolo / Replicate / GoogleVision / Gemini / GroundingDino
IBrandAnalysisService     → Gemini / GoogleVision
ICompositingService       → OpenCv / Pikaswaps / PlanarWarp
ISurfaceTrackingService   → Sam3
```

Registered in `Program.cs` with a factory pattern that reads `engine_*` settings. **Always register new engines through this pattern — never hardcode engine selection.** There is no "basic"/no-op fallback — an unconfigured or unrecognized engine setting makes `EngineFactory` throw a clear error instead of silently degrading.

### 3. Content Pipeline (State Machine)

```
Staging → Transcoding → SceneDetecting → Completed
   ↓          ↓              ↓               ↓
 Failed ←── Failed ←────── Failed ←────────── (re-detect only)
```

Defined in `ContentService.PipelineStages`. Use `TransitionStageAsync()` for all stage changes. Timestamps are automatically managed.

### 4. Frontend Patterns

- **Centralized API client:** All HTTP calls go through `apiClient.ts`. It handles JWT tokens, error normalization, and auth state.
- **URL-driven navigation:** The app uses React Router with URL patterns (`/c/:campaignId/:view`) for shareable links.
- **Component composition:** Tab components (`CampaignsTab`, `IngestionTab`, etc.) are composed in `App.tsx`, not nested in routers.
- **Type safety:** TypeScript interfaces in `types.ts` mirror the .NET DTOs. Use `parseSurfaceItem()` pattern for deserializing JSON-string fields from the API.
- **Custom hooks:** `useChunkedUpload` (large file uploads), `usePaginatedData` (cursor/offset pagination), `useIdleTimer` (session timeout).

### 5. Python Detection Service

- **FastAPI** microservice, called by the .NET backend (not directly by the frontend).
- **YOLOv11** with **ByteTrack** for frame-to-frame surface identity tracking.
- **Per-request thresholds:** `confidence_threshold` and `iou_threshold` are configurable per detection request — no restart needed.
- **Model hot-swap:** Changing `model_size` in the request auto-reloads the model.
- Always return `SurfaceResult` with: `surface_type`, `boundary_coordinates`, `estimated_depth`, `orientation_vector`, `confidence_score`, `viability_score`, `track_id`.

---

## Good Ways of Working

### Before Writing Any Code

1. **Read governance rules first.** Start with `governance/rules/agent-workflow.md` — this is mandatory.
2. **Verify prerequisites.** Check that `governance/features/<name>.gherkin`, `governance/nfrs/<name>.md`, and `governance/plans/<name>.md` exist. If not, create them BEFORE coding.
3. **Consult architecture.** Read `governance/architecture/bit-platform-architecture.md` to understand the current system.
4. **Consult design.** Read `governance/design/bit-platform-design.md` for subsystem details.
5. **Read the existing pattern first.** Find a similar controller/service/component and follow its structure exactly.
6. **Check cross-stack impact.** A frontend change often needs a corresponding DTO, controller endpoint, and service method. Trace the full call chain.
7. **Use the design document.** `docs/DESIGN_DOCUMENT.md` defines the architecture, MReq references, and subsystem boundaries.

### While Writing Code

4. **Follow naming conventions:**
   - C#: PascalCase for public members, camelCase for private. Async methods end with `Async`.
   - TypeScript: PascalCase for components/interfaces, camelCase for functions/variables. Files use PascalCase for components.
   - Python: snake_case for functions/variables, PascalCase for classes. Type hints on all function signatures.
5. **DTOs for every API boundary.** Never return EF Core entities directly from controllers.
6. **Error handling:** Use `ExceptionHandlingMiddleware` for unhandled exceptions. Return structured errors: `{ error: "message" }`. Never swallow exceptions or create temp fixes — see `governance/rules/no-assumptions-no-temp-fixes.md` Part 2.
7. **Migrations:** Every model change needs an EF Core migration. Use `dotnet ef migrations add <Name>`.

### After Writing Code

8. **Run the build.** `dotnet build` for .NET, `npm run lint` (tsc --noEmit) for frontend.
9. **Verify the pipeline.** If you modified pipeline stages, verify all valid transitions still work.
10. **Update the repo memory** (`/memories/repo/`) with any new conventions or patterns discovered.

### Key Conventions

| Convention | Pattern |
|-----------|---------|
| API routes | `api/[resource]` — plural, lowercase |
| Controller actions | `[HttpGet]`, `[HttpPost("{id}")]` — attribute routing |
| Auth | `[Authorize]` on all controllers, JWT Bearer tokens |
| Pagination | `PaginatedResult<T>` with `Page`, `PageSize`, `TotalCount`, `Items` |
| JSON naming | camelCase everywhere (frontend + backend) |
| File uploads | Chunked via `useChunkedUpload` hook → `/api/content/upload/*` |
| Brand safety | MReq 4 — permanent exclusion list, never silently reduced |
| Background jobs | Hangfire for transcoding, scene detection, rendering |
| Timestamps | Always UTC (`DateTime.UtcNow`) |

---

## Subsystem Quick Reference

### Adding a new API endpoint

1. Define DTOs in `dotnet-api/DTOs/` (request + response)
2. Add service interface method in `dotnet-api/Services/I*Service.cs`
3. Implement in `dotnet-api/Services/*Service.cs`
4. Add controller action in `dotnet-api/Controllers/*Controller.cs`
5. Register DI in `Program.cs` if new service
6. Add typed fetch function in `src/apiClient.ts`
7. Add TypeScript interfaces in `src/types.ts`
8. Use in React component

### Adding a new AI engine

1. Create service class implementing the interface (e.g., `ISurfaceDetectionService`)
2. Add a new `case` in the factory registration in `Program.cs`
3. Admin can switch via Platform Settings → `engine_detection` (app restart required)

### Adding a database migration

```bash
cd dotnet-api
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### Running the detection service

```bash
cd detection-service
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8001
```
