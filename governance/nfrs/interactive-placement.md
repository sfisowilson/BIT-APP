# NFRs: Interactive Placement — Click-to-Select & Draw-to-Place

**Feature:** Interactive Placement with SAM3 video-rle + pikaswaps (text-driven) + Planar Homography
**Date:** 2026-07-28 | **Status:** Planning | **Revised:** 2026-07-28 (pikaswaps audit — text-driven, no mask input; SAM3 video-rle for RLE data)

---

## Performance

### Generative Path (pikaswaps — text-driven, no mask input)

- SAM3 video-rle preview call: target <15s for fal.ai queue submission + polling
- Gemini prompt generation (modify_region + prompt): <2s, non-blocking (runs during approval flow)
- pikaswaps compositing: target <3 min per 5s chunk; 12s scene = 3 chunks = ~3 min (parallel) or ~9 min (serial)
- SAM3 video-rle full tracking: NOT called in generative path unless ffmpeg fallback is needed (~2-3 min, lazy-executed only on pikaswaps failure)
- Chunking overhead: ffmpeg split + concat adds ~5s per chunk; negligible vs. pikaswaps processing
- Post-render drift check: +1 SAM3 video-rle call on output (~2-3 min); only on pikaswaps renders
- Splice + encode: ~30s for 12s clip
- **Total generative path latency (12s scene, pikaswaps success):** ~5-8 min (chunks + drift-check + splice; no SAM3 tracking)

### Planar Homography Path (classical CV)

- Quad tracking (CoTracker3 / SAM3 point-track): target <1 min for 12s/30fps clip
- SAM3 video-rle occlusion mask: ~30s per scene (returns per-frame RLE masks for foreground objects)
- Perspective warp per frame: ~2-5ms per frame (OpenCV `warpPerspective`); ~150ms total for 12s/30fps
- Relighting (histogram transfer): ~5ms per frame; ~180ms total for 12s/30fps
- Composite + encode: ~15s for 12s clip (ffmpeg)
- **Total planar path latency (12s): ~1-2 min** — significantly faster than generative path
- No duration limit — 60s clip processes in ~5-8 min (linear scaling)
- No chunking, no external generative API calls, no queue polling

### Shared

- Mask overlay rendering: <100ms to render SVG polygon from decoded RLE
- Video coordinate mapping: O(1) per click (scale factor computed once on metadata load)
- Quad draw-to-place: 60fps drag responsiveness (CSS transform, no React re-render)
- Live warp preview: 30fps via Canvas/WebGL, computed client-side from quad + asset
- SignalR progress at phase boundaries; polling fallback at 60s interval

---

## Security

- SAM3 and pikaswaps API keys stored in Platform Settings (DB), never exposed to frontend
- All new endpoints (`/api/surfaces/preview-segment`, `/api/renders/interactive`) behind `[Authorize]`
- Click coordinates validated: must be within video dimensions (0 ≤ x < width, 0 ≤ y < height)
- Quad coordinates validated: must form a valid convex quadrilateral before dispatch
- Frame index validated: must be within scene frame range
- pikaswaps input sanitized: asset file existence verified before API call
- No user-uploaded content sent to pikaswaps beyond brand assets (already vetted)

---

## Scalability

- `SurfaceItem.AssetType`: nvarchar(50), values "Planar" / "Generative" — negligible storage
- `SurfaceItem.Source`: nvarchar(50), default "AI" — negligible storage
- `RenderItem.CompositingEngine`: nvarchar(50), values "pikaswaps" / "PlanarWarp" / "ffmpeg-luma" / "ffmpeg-perspective"
- `RenderItem.QualityTier`: nvarchar(20), values "AI" / "Exact" / "Standard"
- Planar path: per-frame quad data stored as JSON array `[{frame, corners: [{x,y}×4]}]` — ~50KB for 12s/30fps clip
- Generative path: existing mask/RLE storage unchanged
- SAM3 API calls: 2 per generative placement (preview + tracking), 1 per planar placement (preview only)
- pikaswaps API calls: ⌈duration/4.75⌉ per generative render; 0 for planar renders
- Concurrent renders: Hangfire `DisableConcurrentExecution(timeoutInSeconds: 1800)` unchanged
- RLE decoding: O(w×h) per mask, server-side, single-threaded per request

---

## Data Integrity

### Shared
- All new EF Core migrations are additive (new columns with defaults, no data loss)
- Manual surfaces follow same lifecycle as AI surfaces (Candidate → Approved → Render)
- Deletion cascade unchanged: deleting content cleans up manual surfaces identically

### Generative Path
- Post-render drift check: Re-run SAM3 video-rle on pikaswaps output; compare per-frame RLE masks (by track_id) to original preview mask
- IoU < 0.85 on any frame → render marked "NeedsReview" (not "Finished")
- Admin can approve NeedsReview renders or trigger regeneration
- ffmpeg fallback renders skip drift check (no AI generation to verify)
- pikaswaps input uses text (modify_region + prompt), not masks — SAM3 data is for verification only

### Planar Path
- Per-frame quad coordinates are deterministic — no drift, no regeneration risk
- Pixel-perfect guarantee: warp is a mathematical transform, content never reinterpreted
- Tracking lock-loss: if ≥1 corner loses lock, render is Failed (not degraded)
- Tracking recovery after temporary occlusion: interpolate missing frames from bracketing frames
- SAM3 video-rle provides per-frame RLE masks for foreground occlusion (person crossing in front)

### Engine Provenance
- Every RenderItem records `CompositingEngine` (exact engine used) and `QualityTier`
- Users cannot unknowingly receive ffmpeg output when they expected pikaswaps or planar warp
- UI surfaces quality tier badge on every render card

---

## Error Handling

### Shared
- SAM3 preview no mask → "No distinct surface found — try 'Place Signage' mode instead"
- SAM3 preview timeout (30s) → error toast with retry button
- Asset file missing at render time → render marked Failed, error logged
- Video file inaccessible (ngrok tunnel down) → preview returns 400, render marked Failed

### Generative Path
- pikaswaps 4xx/5xx → logged, falls back to ffmpeg luma-mask compositing
- pikaswaps fallback triggers lazy SAM3 video tracking (mask video generated from stored polygon only if needed)
- Partial chunk failure: retry failed chunk up to 3 times; if still failing, stitch surrounding chunks, fill gap with original video frames, mark render with warning
- All chunks fail: full fallback to ffmpeg luma-mask compositing
- Drift check failure → render marked "NeedsReview", not silently passed

### Planar Path
- Quad tracking loses lock on ≥1 corner → render Failed, log frame + corner index
- Tracking recovers after temporary occlusion → interpolate missing frames from bracketing frames
- Asset has wrong aspect ratio → warn user during placement, suggest cropping
- Asset missing alpha channel → auto-add white background with warning
- Foreground occlusion mask missing → render without occlusion (signage always visible, even behind objects)
- Video resolution too low for clean warp → warn (target: ≥720p recommended)

---

## Observability

### Shared
- Event log codes: SAM3_PREVIEW_START, SAM3_PREVIEW_COMPLETE, SAM3_PREVIEW_FAILED
- SAM3 preview payload logged: frame index, click/quad coordinates, threshold
- Render progress via SignalR at every phase boundary

### Generative Path
- Event log codes: GEMINI_PROMPT_START, GEMINI_PROMPT_COMPLETE, CHUNKING_START, CHUNKING_COMPLETE, PIKASWAPS_START, PIKASWAPS_CHUNK_COMPLETE, PIKASWAPS_FALLBACK, DRIFT_CHECK_START, DRIFT_CHECK_PASS, DRIFT_CHECK_FAIL, SPLICE_START, SPLICE_COMPLETE
- SignalR phases: "Generating prompt" → "Chunking (N segments)" → "Compositing (pikaswaps chunk N/M)" → "QA drift-check" → "Splicing" → "Encoding"
- pikaswaps payload logged: video_url, image_url, modify_region (truncated), prompt (truncated)

### Planar Path
- Event log codes: QUAD_TRACK_START, QUAD_TRACK_COMPLETE, QUAD_TRACK_LOCK_LOST, SAM3_OCCLUSION_START, SAM3_OCCLUSION_COMPLETE, PLANAR_WARP_COMPLETE, RELIGHT_COMPLETE, PLANAR_COMPOSITE_COMPLETE
- SignalR phases: "Tracking corners" → "Detecting occlusion" → "Warping" → "Relighting" → "Compositing" → "Encoding"

---

## Backward Compatibility

- Existing AI-detected surfaces unaffected (Source = "AI", AssetType defaults to "Generative")
- `BasicTrackingService.PreviewSegmentAsync` returns null — no-op
- Existing renders use current ffmpeg path unless engine is explicitly switched
- No breaking API changes — all new endpoints are additive
- `SurfaceItem.TrackingDataJson` format is backward compatible: existing mask data unchanged; quad data is new shape for new surfaces only

---

## UI/UX

### Insert Product Mode (Generative)
- Cursor: crosshair with tooltip "Click an object to select it"
- Mask overlay: #3B82F6 at 30% fill, 2px solid stroke, outer glow
- Loading: skeleton overlay on video during SAM3 preview call
- Error: toast notification with retry button

### Place Signage Mode (Planar)
- Cursor: crosshair with tooltip "Click 4 corners: top-left → top-right → bottom-right → bottom-left"
- After 4th click: live perspective preview of asset warped into quad (30fps via Canvas/WebGL)
- Corner handles: 8px circle with crosshair, draggable
- Corner badges: small numbered circles (1-4) during placement
- "Refine with AI" button: optional SAM3 edge-snapping refinement

### Mode Toggle
- Segmented button control (Insert Product | Place Signage) above the video player
- Switching modes clears any active overlay

### Render Quality Badges
- 🟣 "AI Enhanced" for pikaswaps renders (QualityTier = "AI")
- 🟢 "Pixel Perfect" for planar warp renders (QualityTier = "Exact")
- ⚪ "Standard" for ffmpeg fallback renders (QualityTier = "Standard")
- Tooltip on ⚪: "AI enhancement was unavailable. Standard compositing used."
- ⚠️ badge on "NeedsReview" renders (drift check failed)
- Filter in Renders tab: "AI Enhanced" / "Pixel Perfect" / "Standard" / "Needs Review" / "All"
