# Agent Quick-Start Card

**Version:** 1.0 | **Date:** 2026-07-23

> **Minimum context any agent needs before taking action on the BIT platform. Load this first, every time.**

---

## ⛔ STOP — Read These First (Mandatory, in order)

| # | Document | Why |
|---|---|---|
| 1 | `governance/rules/agent-workflow.md` | The rules you MUST follow |
| 2 | `governance/rules/hallucination-prevention.md` | How to NOT make things up |
| 3 | `governance/architecture/source-of-truth.md` | Which file owns each concept |

---

## 🗺️ Quick Reference

### I need to know about...

| Question | Answer Location |
|---|---|
| What endpoints exist? | `governance/contracts/api-contract.md` |
| What's in the database? | `governance/contracts/db-schema.md` |
| What props does component X take? | `governance/contracts/component-contracts.md` |
| What files do I need to touch? | `governance/rules/file-ownership.md` |
| How does the pipeline work? | `dotnet-api/Services/ContentService.cs` → `PipelineStages` |
| How are AI engines configured? | `dotnet-api/Program.cs` → AI Engine Registration section |
| What's the architecture? | `governance/architecture/bit-platform-architecture.md` |
| What's the subsystem design? | `governance/design/bit-platform-design.md` |

### I need to add/change...

| Task | Required Files (minimum) |
|---|---|
| New API endpoint | DTO → Service Interface → Service → Controller → apiClient.ts → types.ts |
| New DB field | Models.cs → Migration → DTO → types.ts → db-schema.md |
| New React component | Component file → App.tsx → component-contracts.md |
| New AI engine | Service (implement interface) → Program.cs (factory case) |
| Pipeline change | ContentService.cs → ContentController.cs → apiClient.ts → IngestionTab.tsx |

---

## 🔑 Key Facts (Memorize These)

- **All IDs are GUID strings** — not integers. `Guid.NewGuid().ToString()`
- **All timestamps are UTC** — `DateTime.UtcNow`
- **All JSON is camelCase** — frontend AND backend
- **DTOs at every API boundary** — never expose EF entities
- **Every service has an interface** — `IService` → `Service`
- **Controllers are thin** — business logic in services
- **No "basic"/no-op fallback engines** — misconfiguration throws, never silently degrades
- **No mock code — ever** — `governance/rules/no-mock-code.md`

### Pipeline States (exact strings)
```
Staging → Transcoding → SceneDetecting → Completed
  ↓          ↓              ↓
Failed ←── Failed ←────── Failed
```
Valid from Staging: `Transcoding`, `Failed`
Valid from Failed: `Staging` (retry only)
Valid from Completed: `SceneDetecting` (re-detect only)

### AI Engine Setting Keys
```
engine_detection      → "yolo" | "replicate" | "google" | "gemini" | "grounding-dino"
engine_brand_analysis → "gemini" | "google"
engine_compositing    → "opencv" | "pikaswaps" | "planar-warp"
engine_tracking       → "sam3"
```
No "basic" fallback exists — an unset or unrecognized value makes `EngineFactory` throw.

### URL Patterns (React Router)
```
/                          → Landing
/c/:campaignId             → Dashboard
/c/:campaignId/assets      → Assets
/c/:campaignId/content     → Ingestion
/c/:campaignId/placements  → Editor / Surface QA
/c/:campaignId/renders     → Compositing
/c/:campaignId/reports     → Analytics
/admin                     → Admin Console
/telemetry                 → System Telemetry
/analytics                 → BI Analytics
```

### Project Structure (top-level)
```
src/                  → React frontend (TypeScript)
dotnet-api/           → .NET 8 backend API (C#)
detection-service/    → Python FastAPI YOLO service
governance/           → ⛔ Living rules & references
docs/                 → Legacy design docs
```

---

## 🚨 Common Mistakes to Avoid

1. **Assuming an endpoint exists** → Always check `api-contract.md` first
2. **Adding fields to models without migrations** → Always `dotnet ef migrations add`
3. **Forgetting to register DI** → New services need `builder.Services.AddScoped` in `Program.cs`
4. **Missing frontend types** → Backend DTO changes need matching TypeScript interfaces
5. **Using integer IDs** → All IDs are GUID strings
6. **Non-UTC timestamps** → Always `DateTime.UtcNow`
7. **Exposing entities directly** → Always map through DTOs
8. **Creating mock code** → NON-NEGOTIABLE. Never.
9. **Working without prerequisites** → Need feature.gherkin + NFRs + plan
10. **Skipping tests** → Every change needs tests
11. **Swallowing exceptions or creating temp fixes** → No empty catch blocks, no silent null returns. See `governance/rules/no-assumptions-no-temp-fixes.md` Part 2.
12. **Assuming instead of asking when unsure** → If code verification is inconclusive, ask the developer. See `governance/rules/no-assumptions-no-temp-fixes.md` Part 1.
