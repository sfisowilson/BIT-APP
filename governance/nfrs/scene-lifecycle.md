# NFRs: Scene & Surface Lifecycle Management

**Feature:** Scene & Surface Lifecycle Management
**Date:** 2026-07-23
**Status:** In Progress

---

## Performance

- Scene clip FFmpeg extraction must complete within 120 seconds (matching existing scene detection timeout)
- Bulk scene delete: single database transaction, no N+1 queries
- Cascade delete via EF Core: one SaveChangesAsync call for the full chain
- Scene clip: streaming file response (no buffering of entire clip in memory)

## Security

- All new endpoints behind `[Authorize]` (matching existing convention)
- Scene clip: validate StorageKey starts with `/api/content/file/` before accessing disk
- Scene clip: use `Path.GetFullPath` + `StartsWith` check to prevent directory traversal
- Scene clip: validate frame range is within video bounds before FFmpeg invocation
- No elevation of privilege — auth via existing JWT bearer token

## Scalability

- Bulk delete handles 200+ scenes in a single request
- Cascade delete via EF Core is a single database round-trip for the parent entity
- Scene clip uses FFmpeg subprocess (one at a time)
- No concurrent FFmpeg invocations from the same request

## Data Integrity

- Re-detect guard: approved surface check is atomic with deletion (same transaction)
- Cascade deletes: enforced at both EF Core level (OnModelCreating) and code level (manual cleanup)
- Orphan prevention: ContentService.DeleteContentAsync cleans children before parent
- EF Core cascade + manual cleanup = belt-and-suspenders approach

## Error Handling

- Re-detect guard: `InvalidOperationException` with approved surface count
- Single scene delete: 400 if approved surfaces exist, 404 if scene not found
- Bulk scene delete: 400 if any scene has approved surfaces, with identifying info
- Scene clip: 404 if scene/content/source file not found
- Scene clip: 400 if scene has no content reference
- Scene clip: 500 with clear message if FFmpeg fails
- Content delete: 404 if content not found
- All errors logged via IEventLogService

## Observability

- Re-detect guard block logged with contentId and approved surface count
- Scene deletion logged with sceneId and count of child entities removed
- Bulk scene deletion logged with contentId and scene count
- Scene clip generation logged with contentId, sceneId, frame range, and processing time
- FFmpeg stderr captured for clip generation failures

## Backward Compatibility

- Existing content items without scenes delete cleanly (no errors from empty child queries)
- Existing re-detect flow unchanged when no approved surfaces exist
- Existing `DeleteExistingScenes` method in SceneDetectionJobService preserved (guard added inside)
- No database migration needed — EF Core cascade config is metadata-only (no schema change)
- Existing API response shapes unchanged
