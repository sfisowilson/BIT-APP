# Plan: SAM3 Prompt Enhancement + Paid API Compositing

**Status:** Implementing | **Date:** 2026-07-27 | **API Tier:** Paid

---

## Summary

Two problems solved: (1) SAM3 tracking fails with single center-point + no text prompt → black output. (2) Compositing centers asset on SAM3 video instead of using it as a mask.

**Fix:** Gemini generates `sam3_prompt` descriptions. SAM3 receives text prompt + 4 corners + box + lowered threshold. Compositing uses SAM3 video as per-pixel luma mask via ffmpeg `alphamerge`.

---

## Pre/Post Audit (docs/SAM3_API_REFERENCE.md as source of truth)

| Field | Before | After | Correct per docs? |
|-------|--------|-------|-------------------|
| `video_url` | ✅ | ✅ | ✅ |
| `prompt` | ❌ Missing | ✅ Gemini-generated | ✅ `string` |
| `point_prompts` | ⚠️ Single center | ✅ 4 corners + center, all `object_id:0` | ✅ `list<PointPrompt>` |
| `box_prompts` | ❌ Missing | ✅ Bbox, `object_id:0` | ✅ `list<BoxPrompt>` |
| `apply_mask` | `true` | `true` | ✅ Used as luma mask |
| `detection_threshold` | ❌ Default 0.5 | `0.3` explicit | ✅ Lower for walls |
| `video_output_type` | ❌ Default | `"X264 (.mp4)"` explicit | ✅ |
| `object_id` | ❌ Missing | `0` on all prompts | ✅ Groups as single object |

---

## Files Changed

| # | File | Change |
|---|------|--------|
| 1 | `dotnet-api/Services/GeminiDetectionService.cs` | Prompt: add `sam3_prompt`. Model: add `Sam3Prompt`. Map in 2 sites. |
| 2 | `dotnet-api/Services/ISurfaceDetectionService.cs` | DTO: add `Sam3Prompt` |
| 3 | `dotnet-api/Models/Models.cs` | Entity: add `Sam3Prompt` (MaxLength 500) |
| 4 | `dotnet-api/Services/SurfaceDetectionPipeline.cs` | Map `Sam3Prompt` in 2 sites |
| 5 | `dotnet-api/Controllers/ScenesController.cs` | Map `Sam3Prompt` |
| 6 | `dotnet-api/Services/ISurfaceTrackingService.cs` | Add `string? sam3Prompt` param |
| 7 | `dotnet-api/Services/Sam3TrackingService.cs` | Payload: 5 points + box + prompt + threshold |
| 8 | `dotnet-api/Services/BasicTrackingService.cs` | Signature match |
| 9 | `dotnet-api/Services/SurfaceTrackingJobService.cs` | Pass `surface.Sam3Prompt` |
| 10 | `dotnet-api/Services/RenderJobService.cs` | Luma mask compositing via ffmpeg `alphamerge` |
| 11 | `governance/features/sam3-prompt-enhancement.gherkin` | 8 scenarios |
| 12 | `governance/nfrs/sam3-prompt-enhancement.md` | Created |
| 13 | `governance/plans/sam3-prompt-enhancement.md` | This file |

---

## Compositing Pipeline

```
Input 0: original source video
Input 1: SAM3 masked video (apply_mask=true → tracked=bright, bg=dark)
Input 2: brand asset PNG

ffmpeg filter_complex:
  [1:v] format=gray, geq=r='if(gt(lum(X,Y),10),255,0)'  → binary mask
  [2:v] scale=W:H, format=rgba                          → scaled asset RGBA
  [asset_rgba][mask] alphamerge                          → asset with mask as alpha
  [0:v][asset_masked] overlay=0:0, format=yuv420p       → final output

Result: Asset visible ONLY where SAM3 tracked. Original video shows through elsewhere.
```

---

## Verification

1. `dotnet build` — 0 errors ✅
2. `governance/rules/agent-workflow.md` — followed (verified code before changing, cited line numbers)
3. `governance/rules/prerequisites.md` — feature.gherkin + NFRs + plan all created
4. Gemini returns `sam3_prompt` in response (verify via event log)
5. `SurfaceItem.Sam3Prompt` persisted in DB post-detection
6. SAM3 payload logs show: 5 points + 1 box + prompt + threshold=0.3
7. SAM3 returns non-black video (>100KB)
8. Render output: original scene + asset on tracked surface only
9. `npx tsc --noEmit` — only pre-existing errors
