# Plan: Interactive Placement — Click-to-Select & Draw-to-Place

**Status:** Planning | **Date:** 2026-07-28 | **Revised:** 2026-07-28 (pikaswaps audit + SAM3 video-rle integration)

---

## Summary

Add user-driven placement with two compositing paths selected by asset type:

| Path | Trigger | User action | Compositing engine | Latency (12s) | Pixel-perfect? |
|------|---------|-------------|-------------------|---------------|----------------|
| **Generative** | "Insert Product" | Click existing object | pikaswaps (text-driven AI swap) | ~5-8 min | No — AI reinterprets |
| **Planar** | "Place Signage" | Draw 4-corner quad | Classical homography warp | ~1-2 min | Yes — deterministic |

Both paths use **SAM3 video-rle** (`fal-ai/sam-3/video-rle`) for mask data with per-frame RLE + stable track_ids.

---

## Critical Constraints

### pikaswaps is text-driven, NOT mask-driven
pikaswaps takes `video_url` + `image_url` + `modify_region` (text) + `prompt` (text). It has **no mask_url or mask parameter**. SAM3 masks are NOT sent to pikaswaps — they are used only for: (1) UI preview overlay, (2) post-render drift-check. The `modify_region` text (Gemini-generated) tells pikaswaps what to replace.

### pikaswaps 5-second input limit
pikaswaps silently truncates videos >5s. The generative path MUST chunk scenes >5s into ≤5s segments with 0.25s overlap for splice blending. Partial chunk failure → retry that chunk only. All chunks fail → fall back to ffmpeg luma-mask (lazy SAM3 tracking triggered only on fallback).

### Generative ≠ pixel-perfect
pikaswaps is a diffusion model. It reinterprets asset content — fine for 3D products, unacceptable for logos/text. The planar path exists for exact-reproduction assets.

### SAM3 tracking is lazy (generative path only)
SAM3 full video tracking is NOT called in the generative path unless pikaswaps fails and ffmpeg fallback is needed. Normal path: SAM3 video-rle preview → pikaswaps (text-driven) → drift-check with SAM3 video-rle on output. SAM3 `apply_mask=true` video generation only occurs in the fallback code path.

### Planar path uses point tracking + SAM3 video-rle for occlusion
4 corner points tracked per frame (CoTracker3 / SAM3 point-track). SAM3 video-rle provides per-frame RLE masks for foreground occlusion (person crossing in front of signage).

---

## Architecture

```
EditorTab
    │
    ├── "Insert Product" mode ──────────────────────────────────────┐
    │   Click → SAM3 video-rle (point_prompt) → RLE mask → SVG     │
    │   AssetType = "Generative"                                     │
    │       ↓                                                        │
    │   Gemini: generate modify_region + prompt                      │
    │       ↓                                                        │
    │   Hangfire: GenerativeRenderJob                                │
    │       ├── Chunk source video ≤5s segments                     │
    │       ├── pikaswaps(video, image, modify_region, prompt)      │
    │       │   per chunk — AI locates region from text             │
    │       ├── Splice chunks → single video                        │
    │       ├── SAM3 video-rle on output → drift check (IoU)        │
    │       └── ffmpeg encode                                        │
    │       [Fallback: lazy SAM3 tracking → ffmpeg luma-mask]       │
    │                                                                │
    └── "Place Signage" mode ───────────────────────────────────────┐
        Draw 4-corner quad → live warp preview → quad overlay        │
        AssetType = "Planar"                                          │
            ↓                                                         │
        Hangfire: PlanarRenderJob                                     │
            ├── Point-track 4 corners (CoTracker3 / SAM3 pt-track)   │
            ├── SAM3 video-rle → foreground occlusion RLE masks      │
            ├── warpPerspective per frame                             │
            ├── Histogram relighting                                  │
            ├── Occlusion subtraction (signage behind people)        │
            └── ffmpeg encode                                         │
```

---

## Pre/Post Audit

### SAM3 Endpoints Used

| Endpoint | Path | Purpose | When called |
|----------|------|---------|-------------|
| `fal-ai/sam-3/video-rle` | Both | Preview mask (RLE + track_id), drift check, occlusion masks | Preview: on click. Drift: after pikaswaps. Occlusion: during planar render |
| `fal-ai/sam-3/video` | Generative fallback only | Generate masked video for ffmpeg luma-mask compositing | Only if pikaswaps fails (lazy) |

### pikaswaps Input Audit (source: docs/PIKASWAPS_API_REFERENCE.md)

| Parameter | Source | Example |
|-----------|--------|---------|
| `video_url` | Content video file (public URL) | `"https://ngrok.io/api/content/file/video.mp4"` |
| `image_url` | Brand asset file (public URL) | `"https://ngrok.io/api/assets/file/logo.png"` |
| `modify_region` | Gemini-generated from SurfaceType | `"the LED perimeter board on the soccer field"` |
| `prompt` | Gemini-generated from SurfaceType + Asset | `"replace with a Coca-Cola ad, photorealistic, matching stadium lighting"` |
| `negative_prompt` | Static or Gemini-generated | `"blurry, distorted, unrealistic, watermark"` |
| `seed` | Random or fixed for reproducibility | `42` |

### SAM3 video-rle Input Audit (source: docs/SAM3_VIDEO_RLE_API_REFERENCE.md)

**Preview (point click):**
| Parameter | Value |
|-----------|-------|
| `video_url` | Content video URL |
| `point_prompts` | `[{frame_index, x, y, label: 1}]` — single click point |
| `detection_threshold` | `0.5` (tighter for click) |
| `apply_mask` | `false` (want raw RLE data, not rendered video) |

**Drift Check (on pikaswaps output):**
| Parameter | Value |
|-----------|-------|
| `video_url` | pikaswaps output video URL |
| `prompt` | Same as original modify_region text |
| `detection_threshold` | `0.5` |
| `apply_mask` | `false` |

**Occlusion (planar path):**
| Parameter | Value |
|-----------|-------|
| `video_url` | Source video URL |
| `prompt` | `"person"` |
| `detection_threshold` | `0.3` (catch all people) |
| `apply_mask` | `false` (want RLE masks for subtraction) |

---

## Files to Create

| # | File | Purpose |
|---|------|---------|
| 1 | `dotnet-api/Services/RleDecoder.cs` | RLE mask → polygon conversion utility |
| 2 | `dotnet-api/DTOs/SegmentDtos.cs` | SegmentPreviewRequest/Response DTOs |
| 3 | `dotnet-api/Services/PikaswapsCompositingService.cs` | pikaswaps AI inpainting compositing engine |
| 4 | `dotnet-api/Services/VideoChunkingService.cs` | Split/splice video into ≤5s chunks with overlap |
| 5 | `dotnet-api/Services/PlanarWarpCompositingService.cs` | Planar homography compositing engine |
| 6 | `dotnet-api/Services/PointTrackingService.cs` | Per-frame corner tracking (CoTracker3/SAM3 point-track) |
| 7 | `src/components/SurfaceClickOverlay.tsx` | Dual-mode click-to-select + draw-quad overlay UI |
| 8 | `docs/PIKASWAPS_API_REFERENCE.md` | pikaswaps API reference documentation |

## Files to Modify

| # | File | Change |
|---|------|--------|
| 9 | `dotnet-api/Services/ISurfaceTrackingService.cs` | Add `PreviewSegmentAsync()` method signature |
| 10 | `dotnet-api/Services/Sam3TrackingService.cs` | Implement `PreviewSegmentAsync` + extract raw mask data from SAM3 response |
| 11 | `dotnet-api/Services/BasicTrackingService.cs` | Stub `PreviewSegmentAsync` returning null |
| 12 | `dotnet-api/Services/EngineFactory.cs` | Add `"pikaswaps"` and `"planar-warp"` compositing cases |
| 13 | `dotnet-api/Services/RenderJobService.cs` | Branch: GenerativeRenderJob + PlanarRenderJob based on AssetType |
| 14 | `dotnet-api/Controllers/SurfacesController.cs` | Add `POST /api/surfaces/preview-segment` |
| 15 | `dotnet-api/Controllers/RendersController.cs` | Add `POST /api/renders/interactive` dispatch (routes to correct job) |
| 16 | `dotnet-api/Program.cs` | Register PikaswapsCompositingService, PlanarWarpCompositingService, PointTrackingService, VideoChunkingService |
| 17 | `dotnet-api/Models/Models.cs` | Add `AssetType`, `Source`, `TrackingDataJson` to SurfaceItem; add `CompositingEngine`, `QualityTier` to RenderItem |
| 18 | `dotnet-api/DTOs/CompositingDtos.cs` | Add `CreateInteractiveRenderDto` with AssetType field |
| 19 | `src/apiClient.ts` | Add `previewSegment()`, `confirmInteractivePlacement()` |
| 20 | `src/types.ts` | Add `SegmentPreviewResponse`, `InteractiveRenderRequest`, `AssetType`, `QualityTier` |
| 21 | `src/components/EditorTab.tsx` | Integrate dual-mode click handler + quad overlay + mode toggle + approval flow + quality badges |
| 22 | `governance/contracts/api-contract.md` | Add new endpoints |
| 23 | `governance/contracts/component-contracts.md` | Add `SurfaceClickOverlay` props |

---

## Step-by-Step Implementation Order

### Step 1: Database & Models — New Fields
- Add `AssetType` (string, "Generative" default), `Source` (string, "AI" default), `TrackingDataJson` (string, nullable, MaxLength 100000) to `SurfaceItem`
- Add `CompositingEngine` (string, max 50), `QualityTier` (string, max 20) to `RenderItem`
- Create EF Core migration
- *No dependencies. Parallel with Step 2.*

### Step 2: RLE Decoder Utility
- Create `RleDecoder.cs`: `Decode(rle, width, height) → bool[,]` (Kaggle/COCO RLE order)
- `MaskToPolygon(bool[,]) → List<(int x, int y)>` (contour tracing)
- Unit test with known RLE strings
- *No dependencies. Parallel with Step 1.*

### Step 3: SAM3 Preview Endpoint
- Add `PreviewSegmentAsync(contentId, frameIndex, x, y, ct) → SegmentPreviewResult?` to `ISurfaceTrackingService`
- Implement in `Sam3TrackingService`: calls fal.ai with single point_prompts, parses `SAM3VideoObjectFrame[]`, extracts best mask by confidence, decodes RLE → polygon via RleDecoder
- Stub in `BasicTrackingService` (returns null)
- Create `SegmentDtos.cs`: `SegmentPreviewRequest`, `SegmentPreviewResponse`
- Add `POST /api/surfaces/preview-segment` to `SurfacesController`
- *Depends on: Step 2 (RleDecoder)*

### Step 4: pikaswaps Compositing Engine
- Research fal.ai `/pika/v2/pikaswaps` API schema → create `docs/PIKASWAPS_API_REFERENCE.md`
- Create `PikaswapsCompositingService : ICompositingService`
- Implement submit → poll → download pattern (same as Sam3TrackingService)
- Input: source video URL + SAM3 mask video URL + brand asset URL + text prompt
- Output: inpainted video file path
- Fallback: catch exceptions → log → return null (caller falls back to ffmpeg)
- Add `"pikaswaps"` case to `EngineFactory`
- Register in `Program.cs`
- *Can start in parallel with Step 3.*

### Step 5: Video Chunking Service
- Create `VideoChunkingService`:
  - `SplitIntoChunks(videoPath, maskPath, maxDuration=5s, overlap=0.25s) → List<Chunk>`
  - `SpliceChunks(chunks, outputPath)` via ffmpeg concat
  - `FillGap(chunks, gapIndex, outputPath)` — fills failed chunk with original frames
- Unit test with 4s, 5s, 12s, and 30s clips
- *Depends on: Step 4 (needs to know pikaswaps constraint)*

### Step 6: Planar Warp Compositing Engine
- Create `PlanarWarpCompositingService : ICompositingService`
- Per-frame pipeline:
  - `cv2.getPerspectiveTransform(assetCorners, frameQuad)` → homography
  - `cv2.warpPerspective(asset, homography, frameSize)` → warped asset
  - Histogram transfer from wall region → relit asset
  - Overlay on frame with occlusion mask subtraction
- No external API — pure OpenCV/ffmpeg
- Add `"planar-warp"` case to `EngineFactory`
- Register in `Program.cs`
- *No dependencies. Parallel with Steps 4-5.*

### Step 7: Point Tracking Service
- Create `PointTrackingService`
- `TrackCornersAsync(videoPath, startFrame, endFrame, initialQuad) → List<FrameQuad>`
- Uses CoTracker3 API or SAM3 point-track mode (not segmentation mode)
- Detects lock-loss per corner; interpolates across temporary occlusion
- Returns per-frame `[{frame, corners: [{x,y}×4]}]`
- *Depends on: SAM3 point-track research. Can start in parallel.*

### Step 7b: Gemini Prompt Generation for pikaswaps
- Add `GeneratePikaswapsPrompt(SurfaceType, AssetName) → (modify_region, prompt)` to GeminiDetectionService
- modify_region: describes the object to replace (e.g. "the LED perimeter board")
- prompt: describes desired result (e.g. "replace with a Coca-Cola ad, photorealistic, matching stadium lighting")
- Stored on SurfaceItem or passed to render job
- *No dependencies. Parallel with Steps 3-7.*

### Step 8: Generative + Planar Render Jobs
- Add `ProcessGenerativeRenderJob(renderId, ct)` to `RenderJobService`
  - Gemini: generate modify_region + prompt from SurfaceType + asset name
  - Chunk source video ≤5s (if needed) → pikaswaps per chunk (video_url, image_url, modify_region, prompt) → splice chunks → SAM3 video-rle drift check → ffmpeg encode
  - Fallback: if any pikaswaps call fails → lazy SAM3 video tracking (generate mask from stored polygon via sam-3/video) → ffmpeg luma-mask compositing
- Add `ProcessPlanarRenderJob(renderId, ct)` to `RenderJobService`
  - Point-track 4 corners → SAM3 video-rle occlusion masks → per-frame warpPerspective → histogram relight → occlusion subtraction → ffmpeg encode
- Route based on `SurfaceItem.AssetType` in dispatch controller
- SignalR progress broadcasts at each phase
- Set `CompositingEngine` and `QualityTier` at completion
- *Depends on: Steps 4, 5, 6, 7, 7b*

### Step 9: Frontend — apiClient & types
- Add TypeScript: `SegmentPreviewRequest`, `SegmentPreviewResponse`, `InteractiveRenderRequest`
- Add TypeScript: `AssetType`, `QualityTier`, `CompositingEngine`
- Add API functions: `previewSegment(dto)`, `confirmInteractivePlacement(dto)`
- *No dependencies. Parallel with Steps 3-8.*

### Step 10: Frontend — SurfaceClickOverlay Component
- Create `src/components/SurfaceClickOverlay.tsx`
- Props: `videoRef`, `contentId`, `currentFrame`, `frameRate`, `assetType`, `assetUrl`, `onPlacementConfirmed`
- "Insert Product" mode: click → call `previewSegment` → render SVG polygon overlay
- "Place Signage" mode: 4-click quad placement → live Canvas/WebGL warp preview → draggable corners
- Coordinate scaling to native video resolution
- "Refine with AI" button (sends quad to SAM3 box_prompts)
- *Depends on: Step 9 (apiClient types)*

### Step 11: Frontend — Integrate into EditorTab
- Add mode toggle state: `interactionMode: 'product' | 'signage'`
- Render `SurfaceClickOverlay` as absolute-positioned layer over `<video>`
- Wire approval flow → `confirmInteractivePlacement` → SignalR progress updates
- Add quality tier badges to render cards (🟣 AI / 🟢 Exact / ⚪ Standard / ⚠️ NeedsReview)
- Add filter dropdown in Renders tab
- Toast notifications for errors
- *Depends on: Step 10 (SurfaceClickOverlay)*

### Step 12: Governance — Update Contracts
- Add new endpoints to `api-contract.md`
- Add `SurfaceClickOverlay` props to `component-contracts.md`
- *Depends on: Steps 1-11 complete*

### Step 13: Testing
- `RleDecoderTests`: known RLE → polygon conversion
- `Sam3TrackingServiceTests`: PreviewSegmentAsync with mock fal.ai response
- `PikaswapsCompositingServiceTests`: CompositeAsync with mock pikaswaps response
- `PlanarWarpCompositingServiceTests`: per-frame warp correctness, relighting, occlusion
- `PointTrackingServiceTests`: corner tracking, lock-loss detection, interpolation
- `VideoChunkingServiceTests`: split/splice for 4s, 5s, 12s, 30s clips
- `SurfaceClickOverlay.test.tsx`: coordinate mapping, mode switching, quad interaction
- Integration: click → preview → approve → render → quality badge correct

### Step 14: Validation
- `governance/scripts/validate-contracts.ps1` — must exit 0
- `dotnet test dotnet-api.Tests` — all tests pass
- `npm run lint` (tsc --noEmit) — no new errors

---

## Dependency Graph

```
Step 1 (DB fields)  ──────────────────────────────────────────────────────────┐
Step 2 (RleDecoder) ──→ Step 3 (Preview endpoint) ───────────────────────────┤
Step 4 (pikaswaps)  ──→ Step 5 (Chunking) ──────────────────────────────────┤
Step 6 (PlanarWarp) ─────────────────────────────────────────────────────────┤
Step 7 (PointTrack) ─────────────────────────────────────────────────────────┤
                         ↓                                                    │
                    Step 8 (Render jobs — Generative + Planar) ─────────────┤
Step 9 (apiClient) ──→ Step 10 (Overlay component) ──→ Step 11 (EditorTab) ─┘
                                                                              ↓
                                                                      Step 12 (Contracts)
                                                                              ↓
                                                                      Step 13 (Testing)
                                                                              ↓
                                                                      Step 14 (Validation)
```

Steps 1, 2, 4, 6, 7, 9 can run in parallel.

---

## Verification

1. Click billboard in "Insert Product" mode → SAM3 returns mask → SVG overlay renders correctly
2. Place 4-corner quad in "Place Signage" mode → live warp preview shows correct perspective
3. Generative: approve → Gemini prompt → chunks → pikaswaps(text-driven) → drift-check passes → 🟣 "AI Enhanced"
4. Generative: drift check fails → render marked "NeedsReview" → ⚠️ badge visible
5. Generative: pikaswaps fails → lazy SAM3 tracking → ffmpeg fallback → ⚪ "Standard"
6. Planar: approve → point-track → SAM3 video-rle occlusion → warp → relight → 🟢 "Pixel Perfect"
7. Square PNG on angled wall → pixel-identical logo, no AI reinterpretation
8. 60s planar scene → renders in <5 min, no chunking needed
9. Toggle between modes → cursor and tooltip update correctly
10. `dotnet test` — all new tests pass
11. `governance/scripts/validate-contracts.ps1` exits 0

---

## Rollback

- Remove `PikaswapsCompositingService` + `PlanarWarpCompositingService` registrations from `Program.cs` → falls back to existing engines
- Remove new endpoints from controllers → no impact on existing API surface
- Revert `EditorTab.tsx` to remove `SurfaceClickOverlay` → existing placement workflow unchanged
- Remove new columns from `SurfaceItem`/`RenderItem` → defaults = no data loss
- All changes are additive — no existing data or workflows modified

---

## Open Research Items

1. ✅ **pikaswaps API schema** — Documented in `docs/PIKASWAPS_API_REFERENCE.md`. Confirmed: text-driven (modify_region + prompt), no mask input, 5s duration limit.
2. ✅ **SAM3 video-rle API** — Documented in `docs/SAM3_VIDEO_RLE_API_REFERENCE.md`. Confirmed: per-frame RLE masks with stable track_ids, point_prompts + box_prompts supported, mask_url parameter for initial mask. Use for preview, drift-check, occlusion.
3. **CoTracker3 vs. SAM3 point-track** — Evaluate both for corner tracking accuracy and latency before Step 7.
4. **Gemini prompt quality for modify_region** — Test whether Gemini-generated text descriptions reliably guide pikaswaps to the correct region.
5. **Live warp preview** — Client-side Canvas 2D `setTransform` or WebGL texture mapping for 30fps quad preview.
