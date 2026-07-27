# Plan: Scene Detection Pipeline Fix

**Status:** Completed
**Date:** 2026-07-23

---

## Summary

Fix the scene detection pipeline to use real AI engines (YOLO) instead of random mock data, add surface thumbnails, and fix pipeline transition edge cases.

---

## Files Changed

### Backend (.NET)

| File | Change |
|---|---|
| `dotnet-api/Services/SceneDetectionJobService.cs` | Removed mock surface fallback, silent exception catch, and GenerateFallbackScenes. Added CreateSingleScene, GenerateSurfaceThumbnails, and CoordDto helper. Fixed FFmpeg threshold (0.3→0.4) and timestamps.Count condition ("> 1" → "> 0"). |
| `dotnet-api/Services/BasicSurfaceDetectionService.cs` | Replaced random mock data generation with InvalidOperationException that instructs operator to configure a real engine. |
| `dotnet-api/Controllers/ContentController.cs` | RedetectScenes endpoint now enqueues Hangfire job (was only transitioning stage). Added PostgresDbContext injection, System.Threading import. Added self-transition guard (skip if already SceneDetecting). Updated file-serving endpoint to support subdirectories and image MIME types. |
| `dotnet-api/appsettings.json` | Changed Engine.Detection from "basic" to "yolo". |
| `dotnet-api/appsettings.Production.json` | Changed Engine.Detection from "basic" to "yolo". |

### Frontend (TypeScript/React)

| File | Change |
|---|---|
| `src/App.tsx` | Added fetchOperationalData() for lightweight polling (renders/logs/alarms only, 10s interval). Original fetchAllData() still used for full startup refresh. Added console.log tracing for surface data loading. **Rewrote handleRedetectScenes** — single enqueue + polling pattern, no longer calls handleAiSplitAnalyze (was causing double Hangfire jobs). |
| `src/apiClient.ts` | Updated redetectScenes() return type from `{ success, ... }` to `{ jobId, id, ingestionStatus, message }` to match backend response. |
| `src/components/EditorTab.tsx` | Removed hasCompletedVideos conditional wrapper that unmounted video player. Fixed SVG preserveAspectRatio "none"→"xMidYMid meet". Fixed seekToSurface (waits for metadata, pauses after seek). Added surface thumbnails to cards and detail panel. Improved polygon visibility (brighter fill, solid strokes, outer glow). Added debug overlay badge. |

### Governance

| File | Change |
|---|---|
| `copilot-instructions.md` | Added mandatory STOP gate requiring agents to read governance files before any action. |
| `governance/contracts/api-contract.md` | Updated `redetect-scenes` response shape (`{ jobId, id, ingestionStatus, message }`). Updated `content/file` endpoint to support subdirectories and image MIME types. Updated `redetectScenes` API client entry. |
| `governance/features/scene-detection-pipeline-fix.gherkin` | Created retroactively (8 scenarios including single-enqueue verification). |
| `governance/nfrs/scene-detection-pipeline-fix.md` | Created retroactively. |
| `governance/plans/scene-detection-pipeline-fix.md` | This file. |
| `dotnet-api.Tests/BasicSurfaceDetectionServiceTests.cs` | 3 tests: throws instead of mock, error message contains engine options, never returns data. |
| `dotnet-api.Tests/SceneDetectionJobServiceTests.cs` | 7 tests: valid/invalid transitions, stage definitions, self-transition rejection. |

### Bug Fix: Double Hangfire Enqueue on Re-detect

**Root cause**: `handleRedetectScenes` called both `redetectScenes()` (enqueues job via `/api/content/{id}/redetect-scenes`) AND `handleAiSplitAnalyze()` (enqueues job via `/api/video/ai-split-analyze`). Two Hangfire jobs competed for the same content item.

**Fix**: `handleRedetectScenes` now calls only `redetectScenes()`, then polls `/api/content/{id}/detection-status` for completion (2s interval, 10min timeout). On completion, refreshes scenes and selects the first scene.

---

## Verification

1. Stop running .NET API (PID locks build output)
2. Start Python YOLO service: `cd detection-service && uvicorn main:app --host 0.0.0.0 --port 8001`
3. Rebuild .NET: `dotnet build`
4. Restart .NET API
5. Upload a test video → run scene detection → verify real YOLO surfaces with thumbnails
6. Run `governance/scripts/validate-contracts.ps1` — contracts should be fresh (or note staleness)
7. Frontend: `npx tsc --noEmit` — only pre-existing fetchPublic error

---

## Rollback

- Revert `appsettings.json` Engine.Detection to "basic" (restores BasicSurfaceDetectionService which now throws)
- Revert `SceneDetectionJobService.cs` to restore mock fallbacks (available in git history)
- No database migration needed — all changes are code-only
