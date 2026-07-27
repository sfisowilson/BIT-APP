# NFRs: Placement Workbench Fix

**Feature:** Placement Workbench — Stable Video & Visible Surfaces
**Date:** 2026-07-23
**Status:** Implemented

---

## Performance

- Operational data poll interval: 10 seconds (was 5 seconds, increased to reduce load)
- Poll only fetches renders, logs, alarms — NOT content, campaigns, or assets
- Content/campaigns/asset data only refreshes on explicit user actions (upload, delete, approve)
- SVG rendering uses Tailwind `transition-all duration-200` for smooth polygon animations
- Thumbnail images use `loading="lazy"` to defer off-screen image loading
- Failed thumbnail images are hidden via `onError` handler (no broken image icons)

## Security

- No change to authentication — all placement workbench endpoints use existing [Authorize]
- Surface thumbnails served via same file endpoint with AllowAnonymous (public read access to Uploads)
- No new endpoints added — all changes are frontend-only

## Scalability

- SVG polygon rendering scales linearly with surface count (no virtualization needed for typical < 20 surfaces)
- Operational poll uses Promise.allSettled — individual endpoint failures don't block others
- No polling for content data eliminates unnecessary database load

## Error Handling

- Video metadata not loaded → seekToSurface waits for loadedmetadata event before seeking
- Invalid seek time (NaN, negative) → seekToSurface returns early without error
- Thumbnail image load failure → onError hides the img element, shows placeholder icon
- Missing boundary coordinates (< 3 points) → polygon rendering returns null (skipped)
- Operational poll failure → silent catch, no UI disruption

## Observability

- Console.log added for surface data loading: `[EditorTab] Loaded N surfaces for scene X`
- Debug SVG overlay badge always visible: "✓ N surfaces" or "✗ No surfaces loaded"
- Debug crosshair at video center helps verify SVG overlay positioning

## Backward Compatibility

- Surface cards without placementImageUrl render placeholder icon (not broken)
- Pre-existing fetchPublic error in App.tsx:357 is unrelated to these changes
- All EditorTab props unchanged — component interface fully backward compatible
