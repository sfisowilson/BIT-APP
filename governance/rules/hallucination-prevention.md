# Hallucination Prevention Rules

**Version:** 1.0 | **Date:** 2026-07-23

> **These rules are specifically designed to eliminate AI hallucination when working with the BIT codebase. Follow them strictly.**

---

## Rule H1: Never Guess — Always Verify

**The single biggest cause of hallucinations is guessing instead of reading.**

| Instead of... | Do this... |
|---|---|
| "I think there's an endpoint for X" | Read the controller file. Check `api-contract.md`. |
| "The model probably has field Y" | Read `Models.cs`. Check `db-schema.md`. |
| "This component likely accepts prop Z" | Read the component file. Check `component-contracts.md`. |
| "The pipeline should allow transition A→B" | Read `ContentService.PipelineStages`. |
| "I assume the DB uses auto-increment IDs" | Read `Models.cs`. All PKs are GUID strings. |

---

## Rule H2: Use the Source of Truth Registry

**Before any statement about the codebase, consult `governance/architecture/source-of-truth.md`.**

This file maps every domain concept to its canonical file. If you need to know about:
- Endpoints → `api-contract.md`
- Database schema → `db-schema.md`
- Component props → `component-contracts.md`
- Pipeline transitions → `ContentService.cs`
- AI engines → `Program.cs`

**If a concept is not in its canonical file, IT DOES NOT EXIST.**

---

## Rule H3: Never Invent Endpoints

**The `api-contract.md` is the exhaustive list. If an endpoint is not there, do not use it.**

Common hallucinated endpoints that DO NOT EXIST:
- `DELETE /api/surfaces/{id}` — NOT REAL. Verify before using.
- `PUT /api/content/{id}` — NOT REAL. Content is updated via pipeline transitions.
- `GET /api/users/me` — NOT REAL. Use `GET /api/profile`.
- `POST /api/approvals` — NOT REAL. Approvals are created via surface approval.

---

## Rule H4: Never Invent Database Fields

**The `db-schema.md` is the exhaustive list. If a field is not there, it does NOT exist.**

Common hallucinated fields that DO NOT EXIST:
- `User.PhoneNumber` — NOT REAL
- `ContentItem.ThumbnailUrl` — NOT REAL
- `CampaignItem.Description` — NOT REAL
- `SceneItem.ThumbnailUrl` — NOT REAL

---

## Rule H5: Never Invent Component Props

**The `component-contracts.md` is the exhaustive list. If a prop is not there, verify in the component file.**

Never assume a component accepts an `onSave`, `onDelete`, `data`, `loading`, or `error` prop. Always check the interface.

---

## Rule H6: Read Before You Write

**Before creating or modifying any file, read at least 50 lines of:**
1. The file itself (if it exists)
2. A similar file in the same layer (e.g., another controller for a new controller)
3. The canonical reference document for that domain

---

## Rule H7: Count Exactly

**Never use approximate numbers. Use exact counts from verified sources.**

| Don't say... | Say... |
|---|---|
| "There are about 20 controllers" | "There are 19 controllers (verified from `Controllers/` directory)" |
| "It has several services" | "It has 22 service implementations (verified)" |
| "The DB has around 12 tables" | "The DB has 13 tables (verified from `Models.cs`)" |

---

## Rule H8: Use Exact File Paths

**Always reference files with their full relative path from the workspace root.**

| Don't say... | Say... |
|---|---|
| "the auth controller" | "`dotnet-api/Controllers/AuthController.cs`" |
| "the types file" | "`src/types.ts`" |
| "the pipeline service" | "`dotnet-api/Services/ContentService.cs`" |

---

## Rule H9: When in Doubt, Run a Search

**If you're uncertain about ANYTHING, use the tools before speaking:**

1. `grep_search` — Find exact strings, symbols, patterns
2. `file_search` — Find files by glob pattern
3. `list_dir` — List directory contents
4. `read_file` — Read specific file sections
5. `vscode_listCodeUsages` — Find all references to a symbol

---

## Rule H10: The "Does Not Exist" Default

**When an agent asks about something not in the canonical references, the answer is always: "That does not exist in the current codebase. Would you like me to check?"**

Never say "I think so" or "probably." Default to "does not exist" until proven otherwise.
