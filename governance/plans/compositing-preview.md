# Plan: Compositing Preview — Real Scene Compositing

**Status:** Planned
**Date:** 2026-07-23

---

## Summary

Implement real compositing in `OpenCvCompositingService` using FFmpeg to extract the video frame and overlay the brand asset at the surface location. Currently both Basic and OpenCV services return the raw asset image only — no scene compositing.

---

## Approach

Use FFmpeg (already a project dependency) for frame extraction and image overlay:

1. **Extract frame**: `ffmpeg -i video.mp4 -vf "select=eq(n\,{frameNumber})" -vframes 1 frame.png`
2. **Overlay asset**: `ffmpeg -i frame.png -i asset.png -filter_complex "[1:v]scale={w}:{h}[scaled];[0:v][scaled]overlay={x}:{y}" output.png`
3. **Return base64**: Convert output PNG to base64 string

This avoids adding new NuGet packages (OpenCvSharp, SkiaSharp) and uses the same FFmpeg pattern already established in `SceneDetectionJobService`.

The perspective warp (design spec) is deferred — this implementation does rectilinear overlay at the bounding box position, which is a good first step and useful for preview.

---

## Files to Modify

| File | Change |
|---|---|
| `dotnet-api/Services/OpenCvCompositingService.cs` | Replace TODO skeleton with FFmpeg-based frame extraction + asset overlay. Remove fallback to BasicCompositingService on success path. |
| `dotnet-api/appsettings.json` | Change `Engine.Compositing` from `"basic"` to `"opencv"` |
| `dotnet-api/appsettings.Development.json` | Verify compositing engine setting |
| `dotnet-api/appsettings.Production.json` | Change `Engine.Compositing` to `"opencv"` |

## Files NOT Modified

| File | Reason |
|---|---|
| `BasicCompositingService.cs` | Kept as admin-configurable fallback — unchanged |
| `CompositingController.cs` | No API change needed |
| `CompositingDtos.cs` | Request/response unchanged |
| Frontend (`App.tsx`, `EditorTab.tsx`) | Response shape unchanged — displays base64 image as before |
| `governance/contracts/api-contract.md` | Endpoint signature unchanged |

---

## Verification

1. Stop running .NET API
2. Rebuild: `dotnet build`
3. Restart API
4. Upload video + asset → run detection → place asset on surface
5. Click "🎬 Composite Preview"
6. Verify the preview image shows the VIDEO SCENE with the ASSET OVERLAID at the surface location
7. Verify `engineUsed: "OpenCvCompositor"` in the response
8. Test with missing asset file → verify error response
9. Test with missing video file → verify error response
10. Run `governance/scripts/validate-contracts.ps1`

---

## Rollback

- Revert `Engine.Compositing` to `"basic"` in appsettings files
- Revert `OpenCvCompositingService.cs` to restore fallback-only behavior
