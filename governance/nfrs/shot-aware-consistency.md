# NFRs: Shot-Aware Cross-Cut Consistency

**Feature:** Shot-aware tracking/compositing for interactive placement (extends `governance/nfrs/interactive-placement.md`)
**Date:** 2026-07-28 | **Status:** Implemented

---

## Performance

- Shot-aware tracking issues one `fal-ai/sam-3/video-rle` call per shot (not one per scene) — for an N-shot scene this is N sequential SAM3 calls instead of 1. Each call is bounded to that shot's own frame range, so total SAM3 wall-clock time is comparable to the pre-shot-aware single full-scene call; it is not N× slower in practice since a scene's total frame count is unchanged, just partitioned.
- Re-anchor calls (subsequent shots) use a text prompt, skipping the box/point-prompt frame-extraction step — marginally faster to submit than the seed shot's call.
- Generative-path per-shot chunking (`SplitByShotBoundariesAsync`) issues one pikaswaps call per shot ≤4.75s, or multiple for a shot exceeding that limit — same total pikaswaps call count as the prior fixed-duration chunker for an equivalent total scene duration.
- Drift-check now runs one SAM3 re-detection per shot segment (sampled at that segment's first tracked frame) instead of the prior single-call full-output check — bounded, does not scale with frame count.

## Correctness / Consistency

- **Within-scene, multi-shot consistency is the target guarantee**: a placement's tracked position/mask must be independently re-established in every shot of its scene, not carried over pixel-for-pixel from the previous shot (a hard cut invalidates the previous shot's screen coordinates).
- **Cross-scene consistency is explicitly out of scope**: the same physical surface reappearing in a separate, non-contiguous scene later in the video is not linked or tracked as "the same placement" — this would require a cross-scene surface-identity mechanism that does not exist in this codebase.
- **Backward compatibility**: `TrackingDataJson` parsers (`ShotAwareTrackingService`, `RenderJobService.ParsePlanarShotSegments`/`ParseGenerativeShotSegments`) must accept both the new `{shotSegments:[...]}` shape and the legacy flat frame-array shape, treating the latter as a single synthetic segment. No migration/backfill of existing surfaces is required or performed.
- **Contiguity assumption**: shot membership of a frame is derived from `ShotItem.StartFrame/EndFrame`, which must be a subset of absolute frames within the owning `SceneItem`'s range — this is guaranteed by `ShotClusteringService`'s own contiguity invariant (validated, logged on violation).

## Failure Semantics (per-shot, amends `interactive-placement.md`)

| Outcome | `SurfaceItem.TrackingStatus` | Render result |
|---|---|---|
| Every shot tracked or successfully re-anchored | `Tracked` | `Finished` |
| Seed shot tracked; ≥1 later shot has no detection above threshold | `PartialCoverage` | `NeedsReview` (source video passes through for that shot's frames — not silently degraded, not failed) |
| Seed shot itself has no detection above threshold | `LockLost` | `Failed` — nothing to render |
| Every shot (including seed) has no detection | `LockLost` | `Failed` |

- A shot marked `Skipped` must have an empty `frames` array in its segment — downstream render jobs must not attempt to composite frames for a `Skipped` segment; they must pass the source video through for that shot's time range unmodified.
- The Generative path's drift-check (IoU of a re-detected bounding box against the pre-composite tracked mask, sampled at each segment's seed frame) is advisory: IoU < 0.85 on any sampled shot sets the render to `NeedsReview`, never `Failed` — a drift-check failure does not discard otherwise-successful compositing work.

## Security / Data Integrity

- `POST /api/surfaces/from-click` / `from-quad` resolve the owning scene server-side from `contentId` + `frameIndex` (range query against `SceneItems`) — the client cannot specify an arbitrary `sceneId`, preventing a surface from being attached to a scene it wasn't actually placed in.
- Interactively-created surfaces are persisted with `Status="Approved"` immediately (no separate human approval gate) — the click/draw action itself is the approval, consistent with the existing interactive-placement gherkin's "Approve & Render" flow. This is unchanged from `interactive-placement.md`'s original design, not introduced by this plan.

## Error Handling

- `ShotAwareTrackingService` never throws for a per-shot detection failure — it downgrades that shot to `Skipped`/`LockLost` status and continues. The only thrown exceptions originate from the calling render job when `OverallStatus == "LockLost"` (nothing to render at all).
- ffmpeg argument construction throughout this codebase must use `CultureInfo.InvariantCulture` for decimal formatting — `Program.cs` now forces invariant culture process-wide; this was found to be broken (host locale `en-ZA` uses ',' as the decimal separator, corrupting every `-ss`/`-t` argument built via `{value:F3}` interpolation) while building this feature's tests, and fixed at the process level rather than at each call site.
