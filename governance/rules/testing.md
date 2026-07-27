# Rule: Always Add Unit Tests

**Status:** MANDATORY
**Applies to:** All code changes

---

## The Rule

**Every code change must include corresponding unit tests. No exceptions.**

---

## Test Requirements by Layer

### .NET Backend (`dotnet-api.Tests/`)

| Component | Test File Pattern | What to Test |
|---|---|---|
| Services | `*ServiceTests.cs` | All public methods, business logic, edge cases |
| Controllers | `*ControllerTests.cs` | All endpoints: happy path + error responses |
| Repositories | `*RepositoryTests.cs` | Query logic, filtering, pagination |
| Pipeline | In `ContentServiceTests.cs` | All valid + invalid state transitions |

**Framework:** xUnit (already in use)
**Pattern:** Use the existing test project at `dotnet-api.Tests/`

### React Frontend (`src/` — test alongside components)

| Component | What to Test |
|---|---|
| Tab components | Rendering, user interactions, API call triggers |
| Shared components | All prop combinations, edge cases |
| Hooks | State transitions, side effects |
| apiClient | Request/response handling, error cases, token management |

### Python Detection Service (`detection-service/`)

| Module | What to Test |
|---|---|
| `detector.py` | Model loading, detection output format, threshold behavior |
| `main.py` | `/detect` endpoint: valid/invalid requests, `/health` endpoint |

**Framework:** pytest

---

## Minimum Coverage Rules

1. **Services:** Every public method must have at least one test
2. **Controllers:** Every endpoint must have:
   - Happy path test (200/201)
   - Validation error test (400)
   - Auth error test (401/403) where applicable
3. **Pipeline:** Every valid transition tested; every invalid transition confirmed rejected
4. **DTOs/Models:** Covered implicitly through service/controller tests

---

## What NOT to Test

- Framework internals (ASP.NET routing, EF Core query translation)
- Third-party library behavior
- Trivial getters/setters without logic
- Auto-mapper configurations (unless custom logic)

---

## Test Naming Convention

```
MethodName_Scenario_ExpectedBehavior
```

Examples:
- `LoginAsync_ValidCredentials_ReturnsJwtToken`
- `TransitionStageAsync_FromStagingToCompleted_ThrowsValidationError`
- `UploadContent_MissingTitle_ReturnsBadRequest`

---

## Running Tests

```bash
# .NET
cd dotnet-api && dotnet test

# Frontend
npx vitest run

# Python
cd detection-service && python -m pytest
```
