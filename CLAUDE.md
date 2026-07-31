# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Mandatory first step

This repo has a governance system that is the enforced source of truth. **Before writing any code, read `governance/architecture/agent-quickstart.md`.** It links to the canonical contracts below — treat them as authoritative over your own inference:

| Need | Read |
|------|------|
| Endpoint signatures | `governance/contracts/api-contract.md` |
| Database schema | `governance/contracts/db-schema.md` |
| Component props | `governance/contracts/component-contracts.md` |
| Which file owns a concept | `governance/architecture/source-of-truth.md` |
| What files to touch for a given change | `governance/rules/file-ownership.md` |

If a concept isn't documented in these contracts, verify by reading the actual source file — don't assume it exists. `governance/rules/` also contains mandatory rules on testing, no-mock-code, commit discipline, and no-assumptions/no-temp-fixes; read `agent-workflow.md` and `hallucination-prevention.md` before making changes.

Full instructions also live in `AGENTS.md` and `copilot-instructions.md` (kept in sync with this file).

## Build, test, and run

```bash
# Frontend (Vite, port 3000; proxies /api and /hubs to the .NET API on 57220)
npm run dev              # dev server + HMR
npm run lint             # TypeScript check (tsc --noEmit) — there is no separate frontend test suite yet
npm run build            # production build

# Backend (.NET 8, port 57220)
dotnet run --project dotnet-api
dotnet test dotnet-api.Tests                                    # full suite
dotnet test dotnet-api.Tests --filter FullyQualifiedName~ClassName   # single test class/method

# Python detection service
cd detection-service && uvicorn main:app --reload
cd detection-service && python -m pytest

# Contract validation — run after any change to endpoints/schema/component props
governance/scripts/validate-contracts.ps1
```

## Architecture

BIT is an AI-powered platform that detects surfaces in video (billboards, screens, walls) and composites brand ad creative into them. Three tiers plus a governance layer:

```
src/                  React 19 + TypeScript frontend (Vite, Tailwind CSS 4)
dotnet-api/           .NET 8 / ASP.NET Core API (EF Core + PostgreSQL, Hangfire, SignalR, JWT)
detection-service/    Python FastAPI service (YOLOv11, OpenCV, ByteTrack)
governance/           Living architecture/contract docs — see above
```

### Backend layering

`Controller → I*Service → *Service → I*Repository → Repository<T> (EF Core) → PostgreSQL`

- Controllers are thin; business logic lives in services, data access in repositories.
- Every service has an interface (`IFooService` → `FooService`), registered via `builder.Services.AddScoped<...>` in `dotnet-api/Program.cs`.
- DTOs sit at every API boundary — EF entities (`dotnet-api/Models/Models.cs`) are never returned directly.
- Migrations are EF Core code-first, under `dotnet-api/Migrations/`.

### AI engines are swappable via a factory pattern

Detection, brand analysis, compositing, and tracking each have an interface (`ISurfaceDetectionService`, `IBrandAnalysisService`, `ICompositingService`, `ISurfaceTrackingService`) with multiple real concrete implementations (Gemini, Google Vision, YOLO, Replicate, GroundingDINO, OpenCV, Pikaswaps, PlanarWarp, SAM3). The active implementation per category is chosen at runtime by admin-configurable Platform Settings keys, resolved through `EngineFactory`/`IEngineFactory` in `dotnet-api/Program.cs`. There is no "basic"/no-op fallback engine — if a Platform Setting is missing or invalid, `EngineFactory` throws a clear configuration error rather than silently degrading:

```
engine_detection      → "yolo" | "replicate" | "google" | "gemini" | "grounding-dino"
engine_brand_analysis → "gemini" | "google"
engine_compositing    → "opencv" | "pikaswaps" | "planar-warp"
engine_tracking       → "sam3"
```

Adding a new engine: implement the existing interface, register it in `Program.cs`, add a factory case — no frontend or DB change needed unless it requires new settings.

### Content pipeline (state machine)

Defined in `dotnet-api/Services/ContentService.cs` (`PipelineStages`, `TransitionStageAsync`) and exposed via `ContentController.cs`:

```
Staging → Transcoding → SceneDetecting → Completed
  ↓          ↓              ↓
Failed ←── Failed ←────── Failed
```
Valid from `Staging`: `Transcoding`, `Failed`. Valid from `Failed`: `Staging` (retry). Valid from `Completed`: `SceneDetecting` (re-detect).

### Frontend

- `src/App.tsx` — routing (React Router, URL-derived state: `/c/:campaignId/:view`) and top-level handlers.
- `src/apiClient.ts` — the only place HTTP calls to the .NET API are made (JWT attach/refresh, error handling).
- `src/types.ts` — TypeScript interfaces mirroring backend DTOs.
- `src/components/` — feature tab/panel components (Ingestion, Editor, Renders, Admin, etc), each consuming `apiClient.ts` + `types.ts`.
- `src/hooks/useChunkedUpload.ts`, `usePaginatedData.ts`, `useIdleTimer.ts`, `useSignalR.ts` — shared stateful behavior; SignalR hub connections proxy through `/hubs`.
- Path alias `@/*` maps to the repo root (see `vite.config.ts` / `tsconfig.json`).

### Cross-stack conventions (apply everywhere)

- IDs are GUID strings (`Guid.NewGuid().ToString()`) — never integers.
- Timestamps are UTC (`DateTime.UtcNow`).
- JSON is camelCase on both frontend and backend.
- No mock/stub/fake/placeholder code, and no empty catch blocks or silently swallowed exceptions — see `governance/rules/no-mock-code.md` and `governance/rules/no-assumptions-no-temp-fixes.md`.

### Changing something that spans layers

`governance/rules/file-ownership.md` has the exhaustive touch-point list per change type (new DB field, new endpoint, new component, pipeline change, new AI engine, new entity). As a rule of thumb: a backend model/DTO change needs a matching migration and a `src/types.ts` update; a new controller endpoint needs a matching `src/apiClient.ts` function; any of these also need the relevant `governance/contracts/*.md` file updated and `governance/scripts/validate-contracts.ps1` passing before the change is considered done.
