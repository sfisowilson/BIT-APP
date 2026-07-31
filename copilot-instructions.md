# BIT Platform — Copilot Instructions

> **Primary instructions:** See `AGENTS.md` for build commands, conventions, anti-patterns, and skill links.

---

## 🛑 STOP. READ THIS FIRST.

**Before ANY action on this project, read `governance/architecture/agent-quickstart.md`.**

For specific tasks, consult the contracts:

| Need | File |
|------|------|
| Endpoint signatures | `governance/contracts/api-contract.md` |
| Database schema | `governance/contracts/db-schema.md` |
| Component props | `governance/contracts/component-contracts.md` |
| Which file owns what | `governance/architecture/source-of-truth.md` |
| What files to touch | `governance/rules/file-ownership.md` |

**If it's not in the contracts, it doesn't exist. Read, don't guess.**

All mandatory rules are in `governance/rules/` — especially `agent-workflow.md` and `hallucination-prevention.md`. Read them before writing code.

---

## ⛔ SELF-CHECK (perform before every response)

Before responding, verify silently:
1. Did I read the relevant source file or contract? (Not guess)
2. Did I use exact file paths?
3. Did I avoid inventing endpoints/fields/props?
4. Did I include tests in my plan?
5. Did I trace the full call chain (frontend→API→service→DB)?
6. Am I proposing real code, not mocks?

---

## Project Context

BIT is an AI-powered video inventory platform. Three tiers:
- **Frontend:** React 19, TypeScript ~5.8, Vite 6, Tailwind CSS 4 — `src/`
- **Backend:** .NET 8, ASP.NET Core, EF Core, PostgreSQL, Hangfire, JWT — `dotnet-api/`
- **AI:** Python FastAPI, YOLOv11, OpenCV, ByteTrack — `detection-service/`

### Key facts (memorize these)
- All IDs: GUID strings (`Guid.NewGuid().ToString()`), NOT integers
- All timestamps: UTC (`DateTime.UtcNow`)
- All JSON: camelCase (frontend AND backend)
- DTOs at every API boundary — never expose EF entities
- Every service has an interface: `I*Service` → `*Service`
- Controllers thin, business logic in services, data access in repositories
- No "basic"/no-op fallback engines — misconfiguration throws a clear error instead of silently degrading

### Pipeline states
```
Staging → Transcoding → SceneDetecting → Completed
  ↓          ↓              ↓
Failed ←── Failed ←────── Failed
```

### AI engine settings
```
engine_detection      → "yolo" | "replicate" | "google" | "gemini" | "grounding-dino"
engine_brand_analysis → "gemini" | "google"
engine_compositing    → "opencv" | "pikaswaps" | "planar-warp"
engine_tracking       → "sam3"
```

### URL routes
```
/                          → Landing
/c/:campaignId             → Dashboard
/c/:campaignId/assets      → Assets
/c/:campaignId/content     → Ingestion
/c/:campaignId/placements  → Editor
/c/:campaignId/renders     → Compositing
/admin                     → Admin Console
/telemetry                 → Telemetry
```

---

> **After every feature:** commit with semantic message and push. See `governance/rules/commit-discipline.md`. Run `governance/scripts/validate-contracts.ps1` to verify contract freshness.
