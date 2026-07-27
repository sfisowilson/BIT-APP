# Plan: Placement Workbench Fix

**Status:** Completed
**Date:** 2026-07-23

---

## Summary

Fix the Placement Workbench (EditorTab) to stop the video reload loop, make surface polygons visible, and add surface thumbnail support.

---

## Root Cause Analysis

### Bug 1: Video reload loop
```
fetchAllData() every 5s → contentList update → hasCompletedVideos conditional
flips → video player unmounts → remounts → video reloads from network → repeat
```
Broken at two points: (a) polling updated all data including contentList, (b) hasCompletedVideos conditional unmounted the player.

### Bug 2: SVG overlay misaligned
SVG used `preserveAspectRatio="none"` (stretches) while video used `object-contain` (letterboxes). Polygon coordinates in pixel space didn't match displayed video area.

### Bug 3: Surface click didn't seek
`seekToSurface` checked `seekTime < vid.duration` but `vid.duration` is NaN before metadata loads. Seek silently failed.

### Bug 4: Surfaces barely visible
Polygons had `fillOpacity: 0.3`, `strokeDasharray: "8 4"`, and `strokeWidth: 2.5` — nearly invisible against video backgrounds.

---

## Files Changed

| File | Change |
|---|---|
| `src/App.tsx` | Added `fetchOperationalData()` — lightweight poll for renders/logs/alarms only, 10s interval. Replaced 5s `fetchAllData()` poll. Added surface data console.log tracing. Changed `handleSurfaceDecision` to use `fetchOperationalData()` instead of `fetchAllData()`. |
| `src/components/EditorTab.tsx` | Removed `{hasCompletedVideos && (` wrapper (prevents unmount). Changed SVG `preserveAspectRatio` "none" → "xMidYMid meet". Fixed `seekToSurface` (waits for loadedmetadata, pauses after seek). Added surface thumbnails to cards + detail panel. Improved polygon visibility (brighter fill, solid strokes, outer glow ring, pulse animation). Added debug overlay badge + crosshair. Added coordinate validation (min 3 points). |
| `src/types.ts` | No changes needed — `placementImageUrl` already existed in `SurfaceItem` and `parseSurfaceItem`. |

---

## Verification

1. Open Placement Workbench with a video that has detected surfaces
2. Verify video does NOT reload/flash (wait 30+ seconds — no polling-induced refresh)
3. Verify colored polygons visible on video (look for pulse animation on non-selected surfaces)
4. Click a surface in the "Detected Surfaces" list → video should seek and pause at the frame
5. Verify surface detail panel shows thumbnail if available
6. Check browser console for `[EditorTab] Loaded N surfaces` messages
7. `npx tsc --noEmit` — only pre-existing fetchPublic error

---

## Rollback

- Restore `fetchAllData()` in the 5s polling interval (revert App.tsx polling section)
- Restore `{hasCompletedVideos && (` wrapper in EditorTab (revert to git history)
- Restore original `preserveAspectRatio="none"` on SVG
- Restore original `seekToSurface` implementation
- All changes are frontend-only — no database migration needed
