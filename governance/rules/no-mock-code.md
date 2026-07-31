# Rule: No Mock Code — Ever

**Status:** NON-NEGOTIABLE
**Applies to:** All code in this repository

---

## The Rule

**Never create, suggest, or add mock/stub/fake/dummy/placeholder code. Always implement real functionality.**

---

## What is Forbidden

1. **Hardcoded data arrays** pretending to be a database
2. **Stub services** returning canned/fake responses
3. **`// TODO: implement later`** placeholders in lieu of real logic
4. **`placeholder`/`FIXME`** values instead of proper implementation
5. **"Basic"/no-op fallback engines** — every `ISurfaceDetectionService`/`IBrandAnalysisService`/`ICompositingService`/`ISurfaceTrackingService` implementation must be real; a missing/invalid engine setting must make `EngineFactory` throw a clear error, not silently resolve to a no-op

---

## What is Required

- **Real API endpoints** with proper controllers, services, repositories, and DTOs
- **Real database queries** against PostgreSQL via EF Core
- **Real AI/ML inference** (YOLO, Gemini, etc.)
- **Real file I/O**, real HTTP calls, real authentication
- **Real error handling** — no swallowed exceptions, empty catch blocks, or temp fixes that hide errors. See `governance/rules/no-assumptions-no-temp-fixes.md` Part 2 for the full rule.

---

---

## Rationale

Mock code:
- Creates false confidence (tests pass but real system fails)
- Accumulates technical debt (mocks are never replaced)
- Hides integration issues until production
- Wastes time (writing good mocks is often harder than the real implementation)

Every line of mock code is a future production incident.
