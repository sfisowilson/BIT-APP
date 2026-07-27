# Agent Workflow Governance Rules

**Version:** 1.0
**Date:** 2026-07-23

---

## Rule 1: Fact-Based Planning (No Assumptions)

**Every plan must be based on verified facts, not assumptions.**

Before proposing or implementing any change:
1. **Read the relevant existing code** — controllers, services, models, DTOs, components
2. **Verify the current state** — Check what endpoints exist, what the database schema is, what the pipeline does
3. **Trace the full call chain** — Frontend → API Client → Controller → Service → Repository → DB
4. **Cite evidence** — Reference specific files, line numbers, or commit history in plans
5. **Never guess** — If you don't know something, read the code or ask; never assume
6. **When verification is inconclusive, ask the developer** — If the code is ambiguous, undocumented, or doesn't answer your question, escalate to a human. See `governance/rules/no-assumptions-no-temp-fixes.md` for the full escalation protocol.

**Violation examples:**
- "I assume there's an endpoint for..." → Stop. Check first.
- "The database probably has..." → Stop. Read the models.
- "This component likely..." → Stop. Read the component.

---

## Rule 2: Always Add Unit Tests

**No code change without unit tests.**

For every implementation:
1. **Backend (.NET):** Add tests in `dotnet-api.Tests/` following existing xUnit patterns
2. **Frontend (React):** Add component tests (Vitest + React Testing Library pattern)
3. **Python:** Add pytest tests in `detection-service/`
4. **Test the real thing** — No mock-only tests. Test actual service logic, repository queries, API responses
5. **Cover edge cases:** null inputs, boundary values, error paths, pipeline transitions

**Minimum coverage:**
- Services: test all public methods
- Controllers: test all endpoints (happy path + error cases)
- Pipeline: test all valid and invalid state transitions

---

## Rule 3: Prerequisites for Work

**Never work on something that does not have:**

1. **`feature.gherkin`** — A Gherkin feature file in `governance/features/` describing:
   - Feature name and description
   - User stories as scenarios (Given/When/Then)
   - Acceptance criteria

2. **NFRs** — Non-functional requirements in `governance/nfrs/` covering:
   - Performance expectations
   - Security requirements
   - Scalability considerations
   - Error handling expectations

3. **Plan** — An implementation plan in `governance/plans/` containing:
   - Files to create/modify (exhaustive list)
   - Step-by-step implementation order
   - Dependencies between steps
   - Testing strategy
   - Rollback considerations

**Gate check:** Before writing any code, verify all three exist. If not, create them first.

---

## Rule 4: Follow Architecture Patterns

**Always follow the established architecture patterns documented in `governance/architecture/`.**

Key patterns (non-exhaustive):
- Controller → Service (interface) → Repository (interface) → EF Core → PostgreSQL
- DTOs at every API boundary — never expose entities
- AI engines via factory pattern with swappable implementations
- Centralized API client (`apiClient.ts`) for all frontend HTTP calls
- URL-driven routing with React Router
- Pipeline state machine with guarded transitions

---

## Rule 5: Cross-Stack Completeness

**A feature is not complete until all layers are implemented.**

For any feature, ensure:
1. **Database:** Model changes + EF Core migration
2. **Backend:** DTOs → Service interface → Service implementation → Repository → Controller endpoint
3. **Frontend:** TypeScript types → API client function → React component → Route (if new page)
4. **Tests:** Unit tests at every layer
5. **Documentation:** Update relevant governance docs

---

## Rule 6: No Mock Code (Non-Negotiable)

See `governance/rules/no-mock-code.md` for the full rule. In summary:
- Never create stub/fake/dummy/placeholder code
- Always implement real functionality
- `server.ts` in-memory DB is legacy — never extend it
- No `Basic*Service` stubs beyond the existing admin-configurable fallbacks

---

## Rule 7: Governance Document Maintenance

**Governance documents are living artifacts — keep them updated.**

After any significant change:
1. Update `governance/architecture/` if architecture changed
2. Update `governance/design/` if subsystem design changed
3. Update `governance/contracts/` if endpoints, schema, or component props changed
4. Mark plans as complete in `governance/plans/`
5. Archive completed features in `governance/features/`

---

## Rule 8: Contract Freshness Validation

**Run the validation script before marking work as complete.**

```powershell
governance/scripts/validate-contracts.ps1
```

This checks that all governance contracts are newer than the source files they document. A failing validation means you forgot to update a contract — fix it before merging.

See `governance/rules/contract-maintenance.md` for the full maintenance protocol.

---

## Rule 9: Commit and Push After Every Feature

**After every feature implementation, commit with a detailed message and push immediately.**

1. Follow the commit message format in `governance/rules/commit-discipline.md`
2. Include a Governance block listing which governance files were created/updated
3. List every file changed with a brief description of what changed
4. Push immediately after commit — never end a session with unpushed work
5. Run the pre-commit checklist: contracts fresh, build passes, tests pass, no mock code

---

## Enforcement

These rules are enforced by the BIT Development skill (`.github/skills/bit-development/SKILL.md`). Before any code change, the skill must:
1. Verify prerequisites exist (feature.gherkin, NFRs, plan)
2. Read relevant architecture/design documents
3. Trace the full call chain
4. Plan tests before implementation
5. Reject work that violates these rules
6. Remind to run `validate-contracts.ps1` before completion
7. Remind to commit with detailed message and push after completion
