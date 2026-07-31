# BIT Platform — Agent Instructions

BIT is an AI-powered video ad insertion platform. Full-stack: React 19/TypeScript frontend, .NET 8/C# API, Python FastAPI/YOLOv11 detection service, PostgreSQL + Hangfire + SignalR.

---

## 🛑 Mandatory First Step

**Before ANY action**, read `governance/architecture/agent-quickstart.md`. Then consult the relevant contract:

| Need | Read |
|------|------|
| Endpoint signatures | `governance/contracts/api-contract.md` |
| Database schema | `governance/contracts/db-schema.md` |
| Component props | `governance/contracts/component-contracts.md` |
| Which file owns what | `governance/architecture/source-of-truth.md` |
| What files to touch | `governance/rules/file-ownership.md` |

**If it's not in these contracts, it doesn't exist. Read, don't guess.**

All governance rules live at `governance/rules/` — read `agent-workflow.md` and `hallucination-prevention.md` before writing any code.

---

## Build, Test & Run

```powershell
# Frontend (Vite, port 3000)
npm run dev              # Dev server + HMR
npm run lint             # TypeScript check (tsc --noEmit)
npm run build            # Production build

# Backend (.NET 8, port 57220)
dotnet run --project dotnet-api
dotnet test dotnet-api.Tests

# Python detection service
cd detection-service && uvicorn main:app --reload
pytest detection-service/

# Contract validation (run after every change)
governance/scripts/validate-contracts.ps1
```

---

## Non-Obvious Conventions

- **All IDs are GUID strings** — `Guid.NewGuid().ToString()`, never integers
- **All timestamps are UTC** — `DateTime.UtcNow`
- **JSON is always camelCase** — frontend AND backend
- **DTOs at every API boundary** — never expose EF entities directly
- **Every service has an interface** — `I*Service` → `*Service`, registered in `Program.cs`
- **Controllers are thin** — business logic in services, data access in repositories
- **AI engines are swappable** — factory pattern via Platform Settings (`engine_detection`, `engine_brand_analysis`, `engine_compositing`)

---

## Critical Anti-Patterns

- **NO mock/stub/fake/placeholder code** — real implementations only
- **No "basic"/no-op fallback engines** — every AI engine must be a real, working implementation; misconfiguration should throw a clear error, not silently degrade
- **Never guess endpoints, DB columns, or component props** — read the contracts
- **No silent exception swallowing** — no empty catch blocks, no `// FIXME` placeholders

---

## Skills

Load these for detailed workflows:
- **bit-development** — Architecture, patterns, cross-stack conventions, full API-surface-component traceability
- **bit-requirements** — MReqs, tech stack details, governance rules reference, ways of working
