# Plan: Scene & Surface Lifecycle Management

**Status:** In Progress
**Date:** 2026-07-23

---

## Summary

Fix 7 gaps in scene/surface lifecycle: re-detect safety, cascade deletes on video removal, granular scene delete (single + bulk), scene clip export, and frontend re-detect confirmation dialog.

---

## Entity Relationship Chain (current — no cascades)

```
ContentItem ──(ContentId)──→ SceneItem ──(SceneId)──→ SurfaceItem ──(SurfaceId)──→ AdSlotItem ──(AdSlotId)──→ ApprovalItem
                                                                     └──(SurfaceId)──→ RenderItem
                              ContentItem ──(ContentId)─────────────────────────────→ RenderItem
```

---

## Files Changed

### Backend (.NET)

| File | Change |
|---|---|
| `dotnet-api/Data/PostgresDbContext.cs` | Add EF Core relationship configs with `DeleteBehavior.Cascade` for the full chain |
| `dotnet-api/Services/SceneDetectionJobService.cs` | Guard `DeleteExistingScenes` against approved surfaces; promote to internal static for reuse |
| `dotnet-api/Services/ContentService.cs` | Add child entity cleanup in `DeleteContentAsync` |
| `dotnet-api/Controllers/ScenesController.cs` | Add `DELETE /api/scenes/{id}`, `GET /api/scenes/{id}/clip` |
| `dotnet-api/Controllers/ContentController.cs` | Add `DELETE /api/content/{contentId}/scenes` |

### Frontend (TypeScript/React)

| File | Change |
|---|---|
| `src/components/IngestionTab.tsx` | Add re-detect confirmation dialog (two-step pattern from AdminConsoleTab) |

### Governance

| File | Change |
|---|---|
| `governance/features/scene-lifecycle.gherkin` | Created (18 scenarios) |
| `governance/nfrs/scene-lifecycle.md` | Created |
| `governance/plans/scene-lifecycle.md` | This file |
| `governance/contracts/api-contract.md` | Add 3 new endpoints |

---

## Step-by-Step

### Phase 1: Backend

1. **EF Core cascade deletes** — `PostgresDbContext.OnModelCreating`: configure `HasMany/WithOne/OnDelete(Cascade)` for ContentItem→SceneItem→SurfaceItem→AdSlotItem→ApprovalItem and ContentItem→RenderItem, SurfaceItem→RenderItem

2. **Re-detect guard** — `SceneDetectionJobService.DeleteExistingScenes`: before deletion, query `SurfaceItems` for any with `Status == "Approved"` belonging to scenes of this content. If found, throw `InvalidOperationException`. Promote method to `internal static` for reuse.

3. **Orphan cleanup on video delete** — `ContentService.DeleteContentAsync`: before `_contentRepository.DeleteAsync(content)`, manually delete RenderItems (by ContentId and SurfaceIds), then call `SceneDetectionJobService.DeleteExistingScenes`. Belt-and-suspenders with EF cascade.

4. **Single scene delete** — `ScenesController`: `DELETE /api/scenes/{id}`. Load scene, check for approved surfaces (400 if found), delete surfaces/ad-slots/approvals/renders, delete scene, return 200.

5. **Bulk scene delete** — `ContentController`: `DELETE /api/content/{contentId}/scenes`. Check for approved surfaces across all scenes (400 if any), call `DeleteExistingScenes`, return 200 with count.

6. **Scene clip export** — `ScenesController`: `GET /api/scenes/{id}/clip`. Load scene + content, resolve source video path, FFmpeg extract frame range to temp MP4, return `FileStreamResult` with `video/mp4` content type and `Content-Disposition: attachment`.

### Phase 2: Frontend

7. **Re-detect confirmation** — `IngestionTab.tsx`: Add `reDetectConfirmId` state. On first click of "Re-detect Scenes", show confirmation instead of calling API. Second click confirms. Follows `AdminConsoleTab` two-step delete pattern.

### Phase 3: Documentation

8. **Update api-contract.md** — Add `DELETE /api/scenes/{id}`, `DELETE /api/content/{contentId}/scenes`, `GET /api/scenes/{id}/clip`.

---

## Verification

1. `dotnet build` — no compiler errors or warnings
2. `npx tsc --noEmit` — only pre-existing fetchPublic error
3. Delete video with scenes → all children removed from DB
4. Approve surface → re-detect → 400 / Hangfire job fails
5. `DELETE /api/scenes/{id}` approved surface → 400; unchecked → 200
6. `DELETE /api/content/{id}/scenes` → bulk removal
7. `GET /api/scenes/{id}/clip` → downloads MP4 segment
8. Frontend: Re-detect button → confirmation modal → confirm → API call

---

## Rollback

- Revert `PostgresDbContext.cs` cascade config changes (remove relationship definitions)
- Revert `SceneDetectionJobService.cs` guard (remove approved surface check)
- Revert `ContentService.cs` manual cleanup (remove child deletion code)
- Remove new endpoints from controllers
- Remove confirmation dialog from IngestionTab
- No database migration needed — all changes are code-only
