# BIT Platform — Copilot Instructions

---

## 🛑 STOP. READ THIS FIRST. DO NOT SKIP.

**Before ANY action on this project — before reading a file, before writing code, before answering a question — you MUST read these three files IN ORDER:**

| # | File | Load with |
|---|---|---|
| 1 | `governance/architecture/agent-quickstart.md` | `read_file` |
| 2 | `governance/rules/hallucination-prevention.md` | `read_file` |
| 3 | `governance/rules/agent-workflow.md` | `read_file` |

**If you have not read all three, STOP and read them now. Do not proceed until you have.**

**Then**, for any specific task, consult:
- Need endpoint signatures? → `governance/contracts/api-contract.md`
- Need database schema? → `governance/contracts/db-schema.md`
- Need component props? → `governance/contracts/component-contracts.md`
- Need to know what files to touch? → `governance/rules/file-ownership.md`
- Need to know which file owns a concept? → `governance/architecture/source-of-truth.md`

**These governance files are the SINGLE SOURCE OF TRUTH. If it's not in them, it does NOT exist. Do not guess. Do not assume. Read.**

---

## ⛔ RULES YOU CANNOT IGNORE

These rules are inline — you are reading them right now. You cannot skip them. Follow them for every response.

### R1: NEVER GUESS — VERIFY OR SAY YOU DON'T KNOW

| Instead of... | Do this... |
|---|---|
| "I think there's an endpoint for..." | Read the controller file or `governance/contracts/api-contract.md` |
| "The model probably has field..." | Read `dotnet-api/Models/Models.cs` or `governance/contracts/db-schema.md` |
| "This component likely accepts prop..." | Read the component `.tsx` file or `governance/contracts/component-contracts.md` |
| "The pipeline should allow..." | Read `dotnet-api/Services/ContentService.cs` → `PipelineStages` |

**If you don't know something, use tools to find out. Never fabricate.**

### R2: NEVER INVENT ENDPOINTS, FIELDS, OR PROPS

If it's not in the contract files, **IT DOES NOT EXIST**. Default answer: "That doesn't exist in this codebase." The contracts at `governance/contracts/` are the exhaustive reference. Use them.

### R3: READ BEFORE YOU WRITE

Before creating or modifying any file, read at least 50 lines of:
1. The file itself (if it exists)
2. A similar file in the same layer
3. The relevant contract in `governance/contracts/`

### R4: USE EXACT FILE PATHS — NEVER VAGUE REFERENCES

Always reference files with full relative paths: `dotnet-api/Controllers/AuthController.cs`, not "the auth controller".

### R5: NO MOCK CODE — EVER

Never create stub/fake/dummy/placeholder code. No hardcoded data arrays. No `// TODO: implement later`. No new `Basic*Service` classes. `server.ts` is legacy — never extend it. Real implementations only.

### R6: ALWAYS ADD UNIT TESTS

Every code change includes tests. Backend: xUnit in `dotnet-api.Tests/`. Frontend: Vitest. Python: pytest. Services: test all public methods. Controllers: test all endpoints (happy + error). Pipeline: test all transitions.

### R7: VERIFY PREREQUISITES BEFORE CODING

Before writing code, check: does `governance/features/<name>.gherkin` exist? `governance/nfrs/<name>.md`? `governance/plans/<name>.md`? If missing, create them FIRST.

### R8: CROSS-STACK COMPLETENESS

A feature touches ALL layers. If you change a model → add migration → update DTOs → update TypeScript types → update API client → update component → update contracts → add tests. Never implement in isolation. See `governance/rules/file-ownership.md` for the full traceability map.

### R9: COMMIT AND PUSH AFTER EVERY FEATURE

Format: `type(scope): summary` + detailed description + Governance block listing changed files. Push immediately. See `governance/rules/commit-discipline.md`.

### R10: RUN VALIDATION AFTER CHANGES

```powershell
governance/scripts/validate-contracts.ps1
```
Exit 0 = contracts fresh. Exit 1 = you forgot to update a contract. Fix before committing.

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
- `server.ts` is LEGACY scaffolding — never extend it

### Pipeline states
```
Staging → Transcoding → SceneDetecting → Completed
  ↓          ↓              ↓
Failed ←── Failed ←────── Failed
```

### AI engine settings
```
engine_detection      → "yolo" | "replicate" | "google" | "basic"
engine_brand_analysis → "gemini" | "google" | "basic"
engine_compositing    → "opencv" | "basic"
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

## Governance Reference Files (consult for details)

| Need | File |
|---|---|
| Exact endpoints | `governance/contracts/api-contract.md` |
| Exact DB schema | `governance/contracts/db-schema.md` |
| Exact component props | `governance/contracts/component-contracts.md` |
| Which file owns what | `governance/architecture/source-of-truth.md` |
| What files to touch | `governance/rules/file-ownership.md` |
| Full architecture | `governance/architecture/bit-platform-architecture.md` |
| Subsystem design | `governance/design/bit-platform-design.md` |
| Agent quick-start | `governance/architecture/agent-quickstart.md` |
| All rules | `governance/rules/` |
| Skill file | `.github/skills/bit-development/SKILL.md` |
- **After every feature: commit with detailed message (see governance/rules/commit-discipline.md) and push**
