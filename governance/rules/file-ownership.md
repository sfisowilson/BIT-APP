# File Ownership & Traceability Map

**Version:** 1.0 | **Date:** 2026-07-23

> **⚠️ "If you change X, you MUST also change Y." This map prevents the #2 cause of bugs: partial implementations that miss cross-stack dependencies.**

---

## Change → Required Touch Points

### Adding a new database field

```
1. dotnet-api/Models/Models.cs          ← Add property to entity
2. dotnet-api/Migrations/               ← Create EF Core migration
3. dotnet-api/DTOs/*Dtos.cs             ← Add to relevant DTO (if exposed via API)
4. src/types.ts                         ← Add to TypeScript interface
5. governance/contracts/db-schema.md    ← Update schema snapshot
```

### Adding a new API endpoint

```
1. dotnet-api/DTOs/*Dtos.cs             ← Request + response DTOs
2. dotnet-api/Services/I*Service.cs     ← Interface method
3. dotnet-api/Services/*Service.cs      ← Implementation
4. dotnet-api/Controllers/*Controller.cs ← Controller action
5. dotnet-api/Program.cs                ← DI registration (if new service)
6. src/apiClient.ts                     ← Typed fetch function
7. src/types.ts                         ← TypeScript interfaces
8. governance/contracts/api-contract.md ← Update endpoint inventory
```

### Changing a React component's props

```
1. src/components/ComponentName.tsx     ← Update interface + destructuring
2. src/App.tsx                          ← Update usage site (parent passes props)
3. governance/contracts/component-contracts.md ← Update props documentation
```

### Adding a new React component

```
1. src/components/NewComponent.tsx      ← Create component file
2. src/App.tsx                          ← Import and compose into layout
3. src/types.ts                         ← Types needed by component (if new)
4. governance/contracts/component-contracts.md ← Document props
```

### Modifying the pipeline

```
1. dotnet-api/Services/ContentService.cs ← PipelineStages + TransitionStageAsync
2. dotnet-api/Controllers/ContentController.cs ← Pipeline endpoints
3. src/apiClient.ts                      ← Pipeline API functions
4. src/components/IngestionTab.tsx       ← Pipeline UI (buttons, progress)
5. src/types.ts                          ← ContentItem pipeline fields
6. governance/architecture/bit-platform-architecture.md ← Update state machine
7. governance/contracts/api-contract.md  ← Update endpoint signatures
```

### Adding an AI engine

```
1. dotnet-api/Services/NewEngineService.cs ← Implement existing interface
2. dotnet-api/Program.cs                   ← Add case to factory registration
3. (No DB change unless engine needs new settings)
4. (No frontend change — engines are admin-configurable)
```

### Changing the database schema

```
1. dotnet-api/Models/Models.cs          ← Entity definition
2. dotnet-api/Data/PostgresDbContext.cs  ← DbSet + relationship config (if new entity)
3. dotnet-api/Migrations/               ← `dotnet ef migrations add <Name>`
4. dotnet-api/DTOs/                     ← Update DTOs
5. dotnet-api/Repositories/             ← If new queries needed
6. src/types.ts                         ← TypeScript mirror
7. governance/contracts/db-schema.md    ← Update
8. governance/contracts/api-contract.md ← If endpoints affected
```

### Adding a new entity (full table)

```
1. dotnet-api/Models/Models.cs          ← Entity class
2. dotnet-api/Data/PostgresDbContext.cs  ← DbSet<T> + OnModelCreating config
3. dotnet-api/Migrations/               ← `dotnet ef migrations add <Name>`
4. dotnet-api/DTOs/NewEntityDtos.cs     ← Create DTO file
5. dotnet-api/Repositories/INewEntityRepository.cs ← Repository interface
6. dotnet-api/Repositories/NewEntityRepository.cs ← Repository implementation
7. dotnet-api/Services/INewEntityService.cs ← Service interface
8. dotnet-api/Services/NewEntityService.cs   ← Service implementation
9. dotnet-api/Controllers/NewEntitiesController.cs ← Controller
10. dotnet-api/Program.cs               ← DI registration
11. src/types.ts                        ← TypeScript interface
12. src/apiClient.ts                    ← API client functions
13. governance/contracts/db-schema.md   ← Update
14. governance/contracts/api-contract.md ← Update
```

---

## Critical "Never Miss" Dependencies

| If you touch... | You MUST also touch... | Reason |
|---|---|---|
| `Models.cs` | `Migrations/` | Schema change must be migratable |
| `Models.cs` | `src/types.ts` | Frontend types must mirror backend |
| `Controllers/` | `src/apiClient.ts` | Frontend needs to call new endpoints |
| `Controllers/` | `DTOs/` | Every endpoint uses DTOs |
| `Services/` | `Services/I*.cs` | Every service has an interface |
| `apiClient.ts` | `src/components/` | Components consume API functions |
| `types.ts` | `src/components/` | Components use TypeScript types |
| `Program.cs` DI | `Services/` + `Controllers/` | New services must be registered |
| `Controllers/` | `governance/contracts/api-contract.md` | **Contract maintenance** |
| `Models.cs` | `governance/contracts/db-schema.md` | **Contract maintenance** |
| Component props | `governance/contracts/component-contracts.md` | **Contract maintenance** |
| `ContentService.cs` | `governance/contracts/api-contract.md` (pipeline) | **Contract maintenance** |

---

## Cross-Stack Verification Checklist

Before marking any feature "done", verify every layer:

```
☐ Database:   Model changed → Migration created → DB updated
☐ Backend:    DTOs → Interface → Implementation → Controller → DI registered
☐ Frontend:   Types → API client → Component → Route (if new page)
☐ Tests:      Backend tests → Frontend tests → Pipeline tests (if applicable)
☐ Contracts:  API contract → DB schema → Component contracts (if applicable)
☐ Validation: Run governance/scripts/validate-contracts.ps1 — must pass
☐ Docs:       Architecture doc → Design doc → Source-of-truth (if applicable)
```
