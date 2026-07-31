# Plan: Shot-Aware Cross-Cut Consistency for Interactive Placement

**Status:** Implemented | **Date:** 2026-07-28 | **Builds on:** `governance/plans/interactive-placement.md`

---

## Summary

A `SceneItem` is not one camera cut — `ShotDetectionPipeline` + `ShotClusteringService` group temporally-contiguous, visually-similar shots into a single scene (cosine similarity ≥ 0.85 on SAM3 keyframe embeddings). Before this plan, nothing downstream of clustering was shot-aware: surface detection sampled a single frame across the whole scene, and both interactive-placement render jobs tracked/composited continuously across `scene.StartFrame..EndFrame` as if it were one uncut shot. A placement would be positioned correctly in at most one shot and wrong (or absent) in every other shot of a multi-shot scene.

This plan makes a placed brand asset track and composite **consistently across every shot within its scene**, for both the Planar (homography) and Generative (pikaswaps) paths, and fixes the broken `surfaceId: ''` link that previously prevented the interactive flow from completing end-to-end.

**Scope:** consistency applies *within one clustered scene, across its shots only* — not across separate, non-contiguous scenes elsewhere in the video (out of scope; would require a cross-scene surface-identity mechanism that doesn't exist).

---

## Key Design Decisions

1. **`SurfaceItem.TrackingDataJson` is shot-segmented, not a flat frame array.** Shot membership of any frame is derivable from `ShotItem.StartFrame/EndFrame` (absolute, same numbering as `SceneItem`) — no new FK needed. Shape: `{shotSegments:[{shotId,shotIndex,startFrame,endFrame,status,trackId,confidence,frames:[...]}]}`. The parser falls back to the legacy flat-array shape for surfaces tracked before this plan — no breaking change. One additive column: `SurfaceItem.TrackingStatus` (cheap top-level summary).

2. **Both paths standardize on `fal-ai/sam-3/video-rle`, called once per shot** (`ShotAwareTrackingService`, backed by `Sam3TrackingService.SegmentVideoRleAsync` — the previously-dead video-rle response models are now wired up). The seed shot is tracked continuously via a box prompt; every subsequent shot is re-anchored with a **text** prompt (Gemini-generated surface description via `GenerateSurfaceDescriptionAsync`), since a cut changes the surface's screen position but not its semantic identity. A shot with no detection above threshold is marked `Skipped` — the source video passes through unmodified for that shot's frames rather than failing the whole render.
   - **Planar:** decode each frame's RLE → polygon (`RleDecoder`) → fit a 4-corner quad via `MinAreaRectFitter` (pure C# rotating calipers — no OpenCV dependency, matching this codebase's existing convention).
   - **Generative:** store the raw RLE per frame/shot; reused for the drift-check and as a luma-mask fallback.

3. **Overall tracking status:** `Tracked` (every shot succeeded) | `PartialCoverage` (some shots skipped, render completes as `NeedsReview`) | `LockLost` (seed shot failed, or every shot skipped — render Failed). This amends `governance/nfrs/interactive-placement.md`'s original "any lock-loss → always Failed" rule to be per-shot.

4. **The broken `surfaceId: ''` link is fixed as a prerequisite** (`POST /api/surfaces/from-click`/`from-quad`), independent of shot-awareness — needed before anything downstream could be exercised end-to-end.

5. **Both render jobs reassemble via `VideoChunkingService.SpliceChunksAsync`**, looping per shot segment and passing skipped shots through unmodified. `VideoChunkingService.SplitByShotBoundariesAsync` (new) ensures Generative-path chunk boundaries never straddle a cut — a shot exceeding pikaswaps' 4.75s limit is sub-split only within itself.

---

## Implementation Summary (all steps landed)

| # | Change | Files |
|---|---|---|
| 1 | `POST /api/surfaces/from-click`/`from-quad` — persist a SurfaceItem from an interactive click/quad, resolving the scene from `contentId`+`frameIndex` | `SurfaceService.cs`, `SurfacesController.cs`, `CreateSurfaceFromClickDtos.cs`, `EditorTab.tsx`, `apiClient.ts`, `types.ts` |
| 2 | `GET /api/scenes/{sceneId}/shots` + shot-boundary badge in the editor | `ScenesController.cs`, `ShotDtos.cs`, `apiClient.ts`, `types.ts`, `SurfaceClickOverlay.tsx`, `EditorTab.tsx` |
| 3 | Activated `fal-ai/sam-3/video-rle` in `Sam3TrackingService` (`SegmentVideoRleAsync`); fixed `PreviewSegmentAsync` to call video-rle instead of the wrong `sam-3/image` endpoint | `Sam3TrackingService.cs`, `ISurfaceTrackingService.cs` |
| 4 | `MinAreaRectFitter` — pure C# rotating-calipers min-area-rect | `MinAreaRectFitter.cs` |
| 5 | `ShotAwareTrackingService` — the core shot-loop + re-anchor logic | `ShotAwareTrackingService.cs` |
| 6 | Deleted `PointTrackingService` (unreferenced static-quad stub) | — |
| 7 | `VideoChunkingService.SplitByShotBoundariesAsync` | `VideoChunkingService.cs` |
| 8 | `GeminiDetectionService.GenerateSurfaceDescriptionAsync` — SAM3 re-anchor text prompts | `GeminiDetectionService.cs` |
| 9 | `RenderJobService.ProcessPlanarRenderJob` rewired: shot-aware tracking → per-shot extract/composite/relight → splice | `RenderJobService.cs` |
| 10 | `RenderJobService.ProcessGenerativeRenderJob` rewired: shot-aware chunking, drift-check implemented (was a TODO), fixed a latent bug where chunks were written to the OS temp dir instead of `Uploads/` (pikaswaps couldn't fetch them by URL) | `RenderJobService.cs` |
| 11 | `SurfaceItem.TrackingStatus` + migration `AddSurfaceTrackingStatus` | `Models.cs`, migration |
| 12 | This plan + NFR + gherkin; amended `interactive-placement.md` NFR; updated `db-schema.md`, `api-contract.md`, `component-contracts.md`, `bit-platform-architecture.md` | `governance/**` |
| 13 | Tests: `ShotAwareTrackingServiceTests`, `MinAreaRectFitterTests`, `ShotClusteringServiceTests`, `VideoChunkingServiceTests`, `SurfaceServiceTests` additions | `dotnet-api.Tests/**` |

**Also fixed in passing (blocking, not shot-awareness-specific):** the app's default thread culture was left as the host locale, which on any comma-decimal locale (e.g. `en-ZA`) silently corrupted every ffmpeg argument built via `{value:F3}` string interpolation. Fixed by forcing `CultureInfo.InvariantCulture` process-wide in `Program.cs`.

---

## Critical Files
- `dotnet-api/Services/ShotAwareTrackingService.cs` — the core new capability
- `dotnet-api/Services/RenderJobService.cs` — both render jobs' shot-loop rewrite
- `dotnet-api/Services/Sam3TrackingService.cs` — shared `SegmentVideoRleAsync` foundation
- `dotnet-api/Services/ShotClusteringService.cs` / `ShotDetectionPipeline.cs` — the existing shot/scene data this builds on
- `governance/plans/interactive-placement.md` — the plan this extends

## Verification
- `dotnet test dotnet-api.Tests` — all pass (see `governance/rules/testing.md` for coverage rules)
- Manual: place a Planar asset on a surface in a scene with 3+ shots (via `SceneDetectionJobService.RunDetectionPipeline`), Approve & Render, confirm the render re-anchors at each cut and passes through unmodified where re-detection fails
