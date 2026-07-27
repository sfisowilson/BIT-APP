# Commit Discipline Rule

**Version:** 1.0 | **Date:** 2026-07-23

---

## The Rule

**After every feature implementation, commit with a detailed message and push. Never leave uncommitted work at the end of a session.**

---

## Commit Message Format

Every commit message must follow this structure:

```
<type>(<scope>): <short summary>

<detailed description of what changed and why>

Governance:
- Features: governance/features/<name>.gherkin [created | updated]
- NFRs: governance/nfrs/<name>.md [created | updated]
- Plan: governance/plans/<name>.md [created | updated | completed]
- Contracts: [list any contract files updated]

Files changed:
- <file path> — <what changed>
- <file path> — <what changed>
```

### Type

| Type | When to use |
|---|---|
| `feat` | New feature or significant new capability |
| `fix` | Bug fix |
| `refactor` | Code restructure without functional change |
| `docs` | Documentation only (governance, README, comments) |
| `test` | Adding or updating tests |
| `chore` | Build, config, dependencies, maintenance |
| `pipeline` | Content pipeline stage changes |

### Scope

The affected subsystem: `api`, `frontend`, `detection`, `pipeline`, `governance`, `db`, `auth`, `campaigns`, `content`, `surfaces`, `renders`, `compositing`

### Examples

```
feat(content): add chunked upload resume capability

Added resume functionality to chunked upload so interrupted uploads
can continue from the last successful chunk. Added ChunkResumeToken
to ContentItem model and ResumeUpload endpoint.

Governance:
- Features: governance/features/chunked-upload-resume.gherkin — created
- NFRs: governance/nfrs/chunked-upload-resume.md — created
- Plan: governance/plans/chunked-upload-resume.md — completed
- Contracts: governance/contracts/api-contract.md — updated
- Contracts: governance/contracts/db-schema.md — updated

Files changed:
- dotnet-api/Models/Models.cs — added ChunkResumeToken field
- dotnet-api/Controllers/ContentController.cs — added ResumeUpload endpoint
- dotnet-api/DTOs/ContentDtos.cs — added ChunkResumeDto
- src/apiClient.ts — added resumeUpload function
- src/hooks/useChunkedUpload.ts — added resume logic
- dotnet-api.Tests/ContentServiceTests.cs — added resume tests
```

```
fix(pipeline): prevent double-transition race condition

Added concurrency check in TransitionStageAsync to prevent two
simultaneous requests from transitioning the same content item.

Files changed:
- dotnet-api/Services/ContentService.cs — added optimistic concurrency
```

---

## Commit Frequency

| Situation | When to commit |
|---|---|
| Feature complete (all layers) | Immediately — with full governance block |
| Bug fix applied | Immediately after fix + test |
| Contract/docs update | Immediately |
| Work in progress at end of day | Commit with `wip` prefix, push to feature branch |
| Mid-feature logical checkpoint | Commit with `wip` prefix |

---

## Push Rule

**After every commit on a shared branch, push immediately.** Never accumulate multiple unpushed commits.

- Feature branches: push after every commit
- `main`/`master`: push after every PR merge
- Never end a session with unpushed commits

---

## Pre-Commit Checklist

Before committing, verify:

```
☐ All changed files listed in commit message
☐ Governance contracts updated if endpoints/schema/props changed
☐ validate-contracts.ps1 passes (exit 0)
☐ Build passes (dotnet build, npx tsc --noEmit)
☐ Tests pass (dotnet test, npx vitest run)
☐ No leftover debug code, console.log, or commented-out blocks
☐ No mock/stub/fake code (see governance/rules/no-mock-code.md)
```

---

## What NOT to Commit

- `node_modules/`, `bin/`, `obj/`, `dist/`, `build/` (gitignored)
- `.env` files with secrets (gitignored)
- Uploaded media files in `Uploads/` (gitignored)
- Large binary files (>10 MB) — use Git LFS
- IDE-specific settings (`.vscode/` except shared configs)
- Debug logs, temporary scripts
