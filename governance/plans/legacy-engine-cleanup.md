# Plan: Remove Legacy/Basic Engines and Dead Render Paths

**Status:** Implemented | **Date:** 2026-07-29

---

## Summary

Removed all "basic"/no-op fallback engines and the legacy (pre-interactive-placement) detection and render code paths they supported, now that real engines (Replicate/Gemini/YOLO/GroundingDINO detection, Gemini/Google brand analysis, OpenCV/Pikaswaps/PlanarWarp compositing, SAM3 tracking) are configured and the shot-aware interactive placement pipeline (`governance/plans/shot-aware-consistency.md`) is the only render path. Also removed `ComposerTab.tsx` (already dead/unrouted) and cleaned up Admin Settings UI to match.

## What was removed

1. **`Basic*Service` stubs** — `BasicSurfaceDetectionService`, `BasicBrandAnalysisService`, `BasicCompositingService`, `BasicTrackingService`. `EngineFactory`'s switch statements now throw `InvalidOperationException` with an actionable message when an engine setting is missing/unrecognized, instead of silently resolving to a no-op. `OpenCvCompositingService`'s internal `BasicCompositingService` fallback-on-error was removed too (failures now propagate).
2. **Legacy scenes-only detection path** — `SceneDetectionJobService.RunScenesOnlyPipeline`, `SurfaceDetectionPipeline.RunScenesOnlyAsync` (+ its private `DetectScenes`/`BuildSceneCuts`/`DeleteExistingScenes` helpers), `POST /api/video/detect-scenes`, `detectScenesOnly()` in `apiClient.ts`. This was the shot-unaware, 1:1 FFmpeg-cut-as-scene path with no surface detection. "AI Split Analyze" in the UI now calls the real pipeline (`aiSplitAnalyze()` → `POST /api/video/ai-split-analyze` → `RunDetectionPipeline`: shot detection → clustering → surfaces).
3. **Legacy full-scene render path** — `RenderJobService.ProcessRenderJob` (+ its private `BuildPerspectiveArgsAsync`/`GetImageSizeAsync` helpers), `SurfaceTrackingJobService`, `ISurfaceTrackingService.TrackAsync` (+ `FrameBoundary`, and `Sam3TrackingService`'s now-dead `PollForResultAsync`/`ParseSeedBoundary`/`Sam3ResultResponse`/`Sam3File`), `POST /api/renders` (legacy dispatch), `CreateRenderDto`, `SurfaceItem.TrackedBoundariesJson` (migration `RemoveTrackedBoundariesJson` drops the column). `SurfacesController.ApproveSurface` no longer enqueues a tracking job on approval — tracking now happens per-shot inside the render job itself (`ShotAwareTrackingService`). `RenderService.RetryRenderAsync` now re-enqueues `ProcessPlanarRenderJob` or `ProcessGenerativeRenderJob` based on the render's own `CompositingEngine`, since those are the only jobs left.
4. **AI-detected surfaces migrated to the interactive render path.** The "Submit Placement" flow in `EditorTab` (`handleSubmitPlacement`, for surfaces detected by Gemini/Replicate/YOLO/etc., as opposed to interactively clicked/drawn) now calls `confirmInteractivePlacement` → `POST /api/renders/interactive` with `assetType: "Generative"` (the implicit default for every AI-detected surface — detection never sets `AssetType: "Planar"`) instead of the removed legacy endpoint. This is the only behavioral change with product-facing effect: AI-detected surfaces now render through `ShotAwareTrackingService` + pikaswaps like interactive Generative placements, rather than the old full-scene SAM3-luma-mask path.
5. **`server.ts`** — confirmed already absent from the repo (removed in an earlier session); only stale doc references remained and were cleaned up (`AGENTS.md`, `CLAUDE.md`, `copilot-instructions.md`, `governance/architecture/agent-quickstart.md`, `governance/rules/agent-workflow.md`, `governance/rules/no-mock-code.md`, `.github/skills/bit-development/SKILL.md`, `.github/skills/bit-requirements/SKILL.md`).
6. **`ComposerTab.tsx`** — confirmed dead (never imported/rendered from `App.tsx`); its only frontend caller, `handleQueueRender`, and the `composerCampaignId`/`composerAssetId`/`composerPreset` state it read, were dead too and removed alongside it.

## UI cleanup (Admin Settings)

`SettingsPanel.tsx`'s engine dropdowns previously offered a "Basic" option for every category (which would now just throw) and were missing real options that already existed on the backend:
- Removed all "Basic — ..." `<option>`s; default select values now match `EngineFactory`'s real defaults (replicate/gemini/opencv/sam3).
- Added the missing `pikaswaps`/`planar-warp` options to the Compositing Engine dropdown (previously only listed `opencv`), with a note that interactive placements route to Planar Warp/Pikaswaps automatically regardless of this setting (it only affects the legacy single-frame `/api/compositing/preview`).
- Added a `sam3_video_base_url` ("Public Video Base URL") field — previously had no UI at all; had to be set via a raw API call during earlier debugging of the shot-aware tracking feature. Removed the now-dead `sam3_tracking_endpoint` field (only `Sam3TrackingService.TrackAsync`, now deleted, ever read it) and its `PostgresDbContext`/`appsettings.json` mapping.
- Corrected the Surface Tracking Engine description, which claimed tracking is "triggered automatically when a surface is approved" — no longer true; it now runs inside the render job.

## Critical files
- `dotnet-api/Services/EngineFactory.cs` — new throw-on-misconfiguration behavior
- `dotnet-api/Services/RenderJobService.cs`, `RenderService.cs`, `Controllers/RendersController.cs` — render path
- `dotnet-api/Services/SceneDetectionJobService.cs`, `SurfaceDetectionPipeline.cs`, `Controllers/ScenesController.cs` — detection path
- `src/App.tsx`, `src/apiClient.ts` — `handleSubmitPlacement` redirect, `aiSplitAnalyze()` addition, dead composer state removal
- `src/components/SettingsPanel.tsx` — engine dropdown cleanup

## Verification
- `dotnet build` — 0 warnings, 0 errors (verified after every removal step)
- `dotnet test dotnet-api.Tests` — 51/51 passing (54 minus the 3 deleted `BasicSurfaceDetectionServiceTests`)
- `npm run lint` (tsc --noEmit) — clean except one pre-existing, unrelated `fetchPublic` error in `App.tsx` that predates this session
- Migration `RemoveTrackedBoundariesJson` applied to the dev database
