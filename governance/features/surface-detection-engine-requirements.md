# BIT Platform — Surface Detection & Placement Pipeline Requirements

**Version:** 1.2-draft  
**Date:** 2026-07-23  
**Status:** In Review  
**Scope:** Surface detection → adjustment → tracking → asset association → compositing → approval → render → reassembly  
**Author:** Sabelo Nkosi & Sfiso Dlamini

---

## 1. Core Principle

> **Every visible surface in a video frame is a potential advertising placement.**

The BIT platform monetises video by inserting brands onto surfaces. If the engine only finds 12 predefined surface types (billboards, TV screens, etc.), it leaves money on the table. An empty wall, a passing vehicle, a tabletop, a curtain, a calm body of water — all are inventory. The engine's job is to find them all and let the human approver decide.

---

## 2. Detection Requirements

### 2.1 Surface Discovery (Recall)

| # | Requirement | Priority |
|---|---|---|
| **R1** | The engine MUST detect any flat or semi-flat visible region that could hold a 2D image overlay, regardless of what object the surface belongs to. | **Critical** |
| **R2** | The engine MUST NOT be limited to a fixed taxonomy of surface types. It must use open-vocabulary or characteristic-based detection. | **Critical** |
| **R3** | Detection MUST work on diverse content: sports broadcasts, street scenes, indoor footage, news, talk shows, scripted drama, documentary. | **Critical** |
| **R4** | Surfaces as small as 3% of frame area SHOULD be detected (e.g. a phone screen held up in a crowd). | High |
| **R5** | Surfaces at extreme angles (near edge-on) SHOULD still be flagged, with lower viability scores. | Medium |
| **R6** | The engine SHOULD detect surfaces on moving objects (vehicles, people's clothing — NOT faces/heads). | Medium |

### 2.2 Surface Classification (Precision)

| # | Requirement | Priority |
|---|---|---|
| **R7** | Each detected surface MUST include a descriptive `surface_type` label (free text, not a fixed enum) — e.g. "red brick wall", "white van side panel", "glass office window". | **Critical** |
| **R8** | Each surface MUST include a `confidence_score` (0–1) reflecting how certain the engine is that this is a real surface. | **Critical** |
| **R9** | Each surface MUST include a `viability_score` (0–1) reflecting how suitable this surface is for ad placement. Must consider: size, visibility, angle, texture, lighting, occlusion, motion. | **Critical** |
| **R10** | False positives (detecting non-surfaces as surfaces) SHOULD be rare. Engines should favour missing a marginal surface over flooding results with noise. | High |

### 2.3 Spatial Accuracy (Boundaries)

| # | Requirement | Priority |
|---|---|---|
| **R11** | Each surface MUST include `boundary_coordinates` — at least 4 points forming a quadrilateral in pixel coordinates. | **Critical** |
| **R12** | Boundaries SHOULD follow the actual perspective of the surface (not axis-aligned bounding boxes). Polygons with 4–20 points are acceptable. | High |
| **R13** | Boundaries MUST be in the same coordinate space as the source frame (typically 1920×1080). | **Critical** |
| **R14** | Pixel-precise polygon masks are STRONGLY PREFERRED for compositing quality. The closer the boundary matches the actual surface edges, the better the final render. | High |

### 2.4 Depth & Orientation

| # | Requirement | Priority |
|---|---|---|
| **R15** | Each surface SHOULD include `estimated_depth` in metres (approximate, relative depth is acceptable). | Medium |
| **R16** | Each surface SHOULD include `orientation_vector` with yaw, pitch, roll in degrees. | Medium |
| **R17** | Depth and orientation MAY be estimated heuristically if a dedicated depth/pose model is unavailable. Approximate values are better than none. | Medium |

### 2.5 Temporal Consistency (Tracking)

| # | Requirement | Priority |
|---|---|---|
| **R18** | The same surface appearing across multiple frames SHOULD retain a stable `track_id`. | Medium |
| **R19** | Surfaces that leave and re-enter frame SHOULD be re-identified with their original `track_id` where possible. | Low |
| **R20** | Surfaces seen in fewer than 3 frames SHOULD be filtered out as ephemeral false positives. | Medium |

---

## 3. Brand Safety Requirements

| # | Requirement | Priority |
|---|---|---|
| **S1** | The following MUST be permanently excluded and never returned as surfaces: | **Critical** |
| | • Human faces, heads, or identifiable body parts | |
| | • Children or minors | |
| | • Emergency vehicles (ambulance, fire, police) | |
| | • Military vehicles, personnel, or weapons | |
| | • Religious symbols, places of worship, sacred objects | |
| | • Government buildings, official insignia, national flags | |
| | • Alcohol, tobacco, or drug-related branding/paraphernalia | |
| | • Gore, blood, violence, or explicit content | |
| **S2** | Excluded surfaces MUST include an `exclusion_reason` explaining why. | **Critical** |
| **S3** | Brand-safety filtering MUST happen automatically — human approval is mandatory but the engine should pre-filter. | **Critical** |
| **S4** | Existing brand logos in the scene SHOULD be flagged as potential brand conflicts (not auto-excluded, but flagged for human review). | Medium |

---

## 4. Output Contract

Every engine implementing `ISurfaceDetectionService` MUST return `List<SurfaceDetectionResult>` where each result contains:

| Field | Type | Required | Description |
|---|---|---|---|
| `SurfaceType` | `string` | **Yes** | Free-text description of the surface (not a fixed enum) |
| `BoundaryCoordinatesJson` | `string` (JSON) | **Yes** | Array of `{x, y}` points, at least 4 for a quadrilateral |
| `EstimatedDepth` | `double` | No | Approximate depth in metres (default: 5.0) |
| `OrientationVectorJson` | `string` (JSON) | No | `{yaw, pitch, roll}` in degrees (default: all 0) |
| `ConfidenceScore` | `double` | **Yes** | 0.0–1.0 detection confidence |
| `ViabilityScore` | `double` | **Yes** | 0.0–1.0 ad placement suitability |
| `ExclusionReason` | `string?` | No | Non-null if surface was rejected for brand safety |

---

## 5. Engine Evaluation Matrix

| Engine | R1 Open Vocab | R2 No Taxonomy | R3 Diverse Content | R11 Boundaries | R14 Polygons | S1 Brand Safety | GPU Required | Cost |
|---|---|---|---|---|---|---|---|---|---|
| **Gemini 2.0 Flash** | ✅ Excellent | ✅ Free-text types | ✅ General VLM | ✅ Native | ⚠️ Approximate | ✅ Built into prompt | ❌ Cloud | ~$0.0001/img |
| **Fal.ai SAM 2** | N/A (masks only) | N/A | N/A | ✅ From SAM2 | ✅ Pixel-perfect | N/A | ❌ Cloud GPU | ~$0.001/mask |
| **Replicate (GD+SAM2)** | ✅ Good | ⚠️ Prompt-limited | ✅ Good | ✅ From SAM2 | ✅ Pixel-perfect | ❌ Separate CLIP needed | ❌ Cloud | ~$0.005/img |
| **Fal.ai IC-Light** | N/A (relight only) | N/A | N/A | N/A | N/A | N/A | ❌ Cloud GPU | ~$0.002/img |
| **Grounding DINO local** | ✅ Good | ⚠️ Prompt-limited | ✅ Good | ❌ Bbox only | ❌ Needs SAM | ❌ Separate CLIP needed | ✅ 4-6GB | Free |
| **Google Vision** | ❌ Fixed taxonomy | ❌ Fixed labels | ⚠️ Limited | ✅ Normalized vertices | ❌ Bbox only | ❌ Not built in | ❌ Cloud | ~$0.0015/img |
| **YOLO (any size)** | ❌ 80 COCO classes | ❌ 2-6 classes usable | ❌ Sports/broadcast only | ❌ Axis-aligned | ❌ No | ❌ No brand safety at all | ✅ 2-4GB | Free |

**Legend:** ✅ = Meets requirement | ⚠️ = Partially meets | ❌ = Does not meet

---

## 6. Recommended Engine Strategy — Zero-GPU Architecture

### 6.1 Service Allocation

Every requirement is met by exactly one service. No GPU server needed anywhere.

| Pipeline Step | Requirement | Service | Compute | Unit Cost |
|---|---|---|---|---|
| **Scene Cut Detection** | — | FFmpeg `select='gt(scene,0.3)'` | Local CPU | $0.00 |
| **Surface Discovery + Safety** | R1–R10, S1–S3 | **Gemini 2.0 Flash API** | Google Cloud | ~$0.0001 / keyframe |
| **Polygon Masks** | R11–R14 | **Fal.ai SAM 2 API** (or Replicate SAM 2) | Serverless GPU | ~$0.001 / mask |
| **Per-Frame Tracking** | T1–T5 | **OpenCV Optical Flow** (OpenCVSharp4 / EmguCV in C#, or calcOpticalFlowPyrLK + ECC in Python) | Local CPU | $0.00 |
| **Perspective Compositing** | Q1–Q3 | **OpenCVSharp4** — GetPerspectiveTransform + WarpPerspective + LAB histogram match | Local CPU | $0.00 |
| **Video Reassembly** | M1–M8 | **FFmpeg** (CLI wrapper or FFMpegCore NuGet) — stream copy audio, re-encode video | Local CPU | $0.00 |

### 6.2 Swappable Services

| Capability | Primary | Fallback | When to Switch |
|---|---|---|---|
| Surface Detection | Gemini 2.0 Flash | Replicate Grounding DINO | Gemini quota exhausted or offline |
| Polygon Masks | Fal.ai SAM 2 | Replicate SAM 2 | Cost or latency preference |
| Compositing (basic) | OpenCVSharp4 (CPU) | — | Always |
| Compositing (premium) | Fal.ai IC-Light / ControlNet | — | High-value placements where flat warp looks fake (§Q6–Q8) |

### 6.3 Cost Estimate — Per Minute of Finished Video

| Step | Calls | Rate | Cost |
|---|---|---|---|
| Keyframe analysis (~30s scenes, 2 keyframes/sec) | 120 frames | $0.0001 / frame | **$0.012** |
| SAM 2 masking (5 approved surfaces per scene, ~2 scenes) | 10 masks | $0.001 / mask | **$0.010** |
| Tracking, compositing, FFmpeg render | CPU | $0.00 | **$0.00** |
| **Total** | | | **~$0.022 / minute** |

Well below the AC6 target of $0.50/hour ($0.0083/minute).

### 6.4 Why OpenCV on CPU for Tracking & Compositing

| Concern | Why CPU OpenCV Is Sufficient |
|---|---|
| Optical flow speed | calcOpticalFlowPyrLK on sparse corner points — ~1ms per frame at 1080p |
| Perspective warp speed | GetPerspectiveTransform + WarpPerspective — ~2ms per frame |
| Histogram matching | LAB colour space mean/variance transfer — <1ms per frame |
| Per-frame throughput | ~4ms CPU per frame at 1080p = 250 fps — faster than real-time |
| Multi-core scaling | FFmpeg + OpenCV both parallelize across cores naturally |
| ECC alignment | Enhanced Correlation Coefficient for sub-pixel surface alignment between frames — robust to lighting changes |

---

## 7. Acceptance Criteria

An engine is **production-ready** when:

1. **AC1**: Upload 5 diverse test videos (sports, street scene, indoor, news, drama). The engine finds surfaces in ALL of them — including at least one surface that is NOT a billboard, screen, or sign (e.g., a wall, a vehicle, a table).

2. **AC2**: For a frame containing both a person's face and an empty wall beside them, the engine returns the wall but NOT the face.

3. **AC3**: Surface boundaries visually align with actual surface edges (not grossly misaligned). A human reviewer says "yes, that boundary outline matches the surface."

4. **AC4**: Zero surfaces are returned with `ExclusionReason = null` that contain brand-safety violations (faces, weapons, etc.).

5. **AC5**: The pipeline completes within 5 minutes for a 30-second scene (cloud engines) or 2 minutes (local engines with GPU).

6. **AC6**: Cost does not exceed $0.50 per hour of processed video at the default engine tier.

---

## 8. Open Questions for Review

| # | Question | Suggested Answer |
|---|---|---|
| **Q1** | Should we auto-reject surfaces below a viability threshold, or always present them for human review? | Present all but sort by viability. Human decides. Some low-viability surfaces might be perfect for a specific brand. |
| **Q2** | Should we detect surfaces on people's clothing (t-shirts, jackets) or is that too risky? | Detect but mark as "Person Attire" with a lower default viability. Human must explicitly approve. Faces/heads still excluded. |
| **Q3** | How should we handle reflective surfaces (glass windows, mirrors, water)? | Flag with "Reflective Surface" type. Viability depends on whether the reflection is stable or moving. |
| **Q4** | What minimum surface size (as % of frame) is worth detecting? | 3% minimum. Below that, compositing quality degrades significantly. |
| **Q5** | Should the engine process every frame or sample key frames? | Key frames with optical-flow adaptive skip. Detection on ~10% of frames, tracking fills gaps. |
| **Q6** | Is a surface on a moving object (bus driving past) viable? | Yes — detect it. Viability depends on how long it stays in frame. Short appearances get low viability. |

---

## 9. Revision History

| Version | Date | Changes |
|---|---|---|
| 1.0-draft | 2026-07-23 | Initial draft. Captures core principle, 20 detection requirements, 4 brand safety rules, output contract, engine evaluation matrix, acceptance criteria, open questions. |
| 1.1-draft | 2026-07-23 | Expanded scope to full pipeline: surface adjustment, tracking, asset association, compositing quality, approval workflow, scene reassembly, quality constraints. |
| 1.2-draft | 2026-07-23 | Integrated zero-GPU strategy: Gemini for detection, Fal.ai for SAM 2 masks, OpenCV CPU for tracking + compositing, FFmpeg for reassembly. Added cost breakdown (~$0.022/min video). Added Fal.ai SAM 2 and IC-Light to evaluation matrix. Replaced generic pipeline diagrams with tool-specific architectures. |

---

## 10. Surface Adjustment & Editor

After AI detection, surfaces enter the **Scene QA / Editor** view where operators can refine them.

### 10.1 Current State

| Capability | Status | Detail |
|---|---|---|
| View detected surfaces | ✅ Built | `GET /api/scenes/{id}/surfaces` returns all surfaces for a scene |
| Surface thumbnails | ✅ Built | `GenerateSurfaceThumbnails` in SceneDetectionJobService creates thumbnail crops |
| Drag/resize boundary points | ❌ Not built | Frontend displays boundaries read-only |
| Add new surface manually | ❌ Not built | No UI for drawing a new surface polygon |
| Delete a surface | ❌ Not built | No UI for removing a false positive |
| Adjust depth/orientation | ❌ Not built | Values are write-only from the engine |
| Re-run detection on one scene | ⚠️ Partial | Full re-detect exists but is all-or-nothing |

### 10.2 Requirements

| # | Requirement | Priority |
|---|---|---|
| **E1** | The operator MUST be able to adjust boundary points of any detected surface by dragging vertices in the editor. | **Critical** |
| **E2** | The operator MUST be able to delete false-positive surfaces. | **Critical** |
| **E3** | The operator MUST be able to manually draw a new surface polygon on any frame. | High |
| **E4** | Adjusted boundaries MUST be persisted to `SurfaceItem.BoundaryCoordinatesJson`. | **Critical** |
| **E5** | The editor MUST show the surface boundary overlaid on the video frame at the correct playback position. | **Critical** |
| **E6** | The editor SHOULD show a live preview of how a brand asset would look on the surface (basic perspective warp). | Medium |

### 10.3 Data Flow

```
Detection Engine → SurfaceItem (Candidate) → Editor View → Operator adjusts →
  PUT /api/surfaces/{id} → SurfaceItem updated → Status stays "Candidate" until approved
```

---

## 11. Surface Tracking Across Frames

Once a surface is identified and adjusted on a key frame, it must be tracked through every frame of the scene for compositing.

### 11.1 Current State

| Capability | Status | Detail |
|---|---|---|
| Frame-to-frame tracking | ⚠️ Partial | YOLO v1 has ByteTrack for COCO objects. v2 tracker.py does IoU-based tracking across key frames. |
| Full-scene tracking | ❌ Not built | No engine tracks a surface through every single frame 1..N |
| Tracking data persisted | ❌ Not built | No per-frame boundary data stored |
| Re-identification | ⚠️ Partial | tracker.py has lost-track buffer, but no feature-embedding re-id |

### 11.2 Requirements

| # | Requirement | Priority |
|---|---|---|
| **T1** | A surface boundary on frame N MUST be computable for any frame in the scene range, not just key frames. | **Critical** |
| **T2** | Tracking MUST handle camera motion: pan, tilt, zoom, rotation. | **Critical** |
| **T3** | Tracking MUST handle surface occlusion — if the surface is partially blocked, the visible portion should still be tracked. | High |
| **T4** | Tracking drift MUST be detectable. If the tracked boundary has slipped off the actual surface, the system should flag it. | Medium |
| **T5** | Per-frame boundary data SHOULD be stored as a time series for the renderer. Format: `[{frame: N, boundary: [...]}, ...]`. | High |
| **T6** | The same surface across different scenes within the same content SHOULD retain a consistent identity. | Low |

### 11.3 Tracking Architecture — OpenCV Optical Flow (CPU)

**Tool:** OpenCVSharp4 / EmguCV (C#) or calcOpticalFlowPyrLK + ECC (Python).
**Cost:** $0.00 — runs on the same CPU as the .NET API.

```
Key Frame (Gemini surface detection + SAM 2 mask)
       │
       ▼
  Operator Adjustment (Editor) — final boundary polygon
       │
       ▼
  Extract sparse corner points from surface region (Shi-Tomasi / goodFeaturesToTrack)
       │
       ├─ Frame N:   calcOpticalFlowPyrLK → point displacements
       ├─             ECC (Enhanced Correlation Coefficient) → sub-pixel alignment
       ├─             findHomography → perspective transform matrix
       ├─             Apply homography to boundary quad → predicted boundary
       │
       ├─ Frame N+1: Repeat from previous frame's points
       ├─ ...
       └─ Frame N+K: Compare tracked boundary to re-detected boundary.
                      If drift > threshold (e.g. 15px) → flag for operator review.
       │
       ▼
  Per-frame boundary series → stored as JSON array for compositing:
  [{frame: 0, boundary: [[x,y],...]}, {frame: 1, ...}, ...]
```

**Why CPU is sufficient:** Sparse optical flow on ~50 corner points takes ~1ms/frame at 1080p. ECC alignment adds ~2ms. Total tracking overhead ~3ms/frame — well under real-time.

---

## 12. Asset-to-Surface Association

After surfaces are detected and adjusted, they become **Ad Slots** — inventory that can be filled with brand assets.

### 12.1 Current State

| Capability | Status | Detail |
|---|---|---|
| Surface approval creates AdSlot | ✅ Built | `SurfaceService.ApproveSurfaceAsync` creates an `AdSlotItem` with `SlotStatus = "Available"` |
| Campaign association | ✅ Built | `AdSlotItem.CampaignId` links to a campaign |
| Asset upload | ✅ Built | `AssetsController` + `CreativeAsset` model |
| Asset-to-slot assignment | ⚠️ Partial | AdSlot references CampaignId but not a specific AssetId |
| Competitive separation | ❌ Not built | No enforcement of 30 brand categories |

### 12.2 Requirements

| # | Requirement | Priority |
|---|---|---|
| **A1** | An approved surface MUST automatically become an AdSlot with status "Available". | **Critical** |
| **A2** | An advertiser MUST be able to assign a CreativeAsset to an available AdSlot. | **Critical** |
| **A3** | The system MUST prevent two competing brands (same BrandCategory) from appearing in the same scene. | High |
| **A4** | One surface in different regional markets MAY carry different brand assets (regional targeting). | Medium |
| **A5** | An AdSlot MUST retain its complete history: which asset was assigned, when, by whom, and the approval decision. | High |

### 12.3 Data Model (existing)

```
SurfaceItem (Status=Approved)
    └── AdSlotItem (SlotStatus=Available → Reserved → Rendering → Completed)
           ├── CampaignId → CampaignItem
           ├── AssetId → CreativeAsset (needs adding)
           └── ApprovalItem (Decision, ApproverEmail, Timestamp)
```

### 12.4 Gap: AssetId on AdSlotItem

Currently `AdSlotItem` has `CampaignId` but no `AssetId`. The renderer needs to know which specific asset image to composite onto the surface. **`AssetId` should be added to `AdSlotItem`.**

---

## 13. Compositing & Rendering — Quality Requirements

This is where the brand asset is actually inserted into the video. The quality of compositing determines whether the result looks like a real in-scene placement or a cheap Photoshop overlay.

### 13.1 Current State

| Capability | Status | Detail |
|---|---|---|
| Compositing preview | ✅ Built | `POST /api/compositing/preview` — returns a single composited frame |
| Swappable engines | ✅ Built | `ICompositingService` → `OpenCvCompositingService` (current), `BasicCompositingService` (fallback) |
| Render dispatch | ✅ Built | `POST /api/renders` → Hangfire job |
| Perspective warp | ⚠️ Basic | OpenCV warpPerspective from 4-point boundary |
| Lighting match | ❌ Not built | No histogram matching or relighting |
| Motion blur | ❌ Not built | No per-frame motion blur on inserted asset |
| Shadow casting | ❌ Not built | No drop shadow or ambient occlusion |
| Grain/noise match | ❌ Not built | No film grain matching |
| Multi-surface render | ❌ Not built | Renders one surface at a time |

### 13.2 Non-Negotiable Quality Constraints

The user stated these explicitly. They are **inviolable**:

| # | Constraint | Rationale |
|---|---|---|
| **C1** | The scene duration MUST NOT change. Inserting a brand does not add or remove frames. | This is advertising inventory — altering duration breaks downstream scheduling, ad servers, and broadcast clocks. |
| **C2** | The original scene lighting MUST NOT be altered. The inserted brand asset must match the scene's lighting, not the other way around. | Changing scene lighting is visually detectable and damages content integrity. |
| **C3** | The inserted brand asset MUST appear natural — as if it was always there. | The value proposition is invisible advertising. If viewers notice the insertion, the product fails. |
| **C4** | Non-surface pixels MUST remain pixel-identical to the source. Only the surface region is modified. | Preserves content creator's original work outside the ad placement area. |

### 13.3 Compositing Quality Requirements

| # | Requirement | Priority |
|---|---|---|
| **Q1** | The brand asset MUST be perspective-warped to match the surface's quadrilateral boundary exactly. | **Critical** |
| **Q2** | The asset MUST be colour-matched to the scene's white balance and colour temperature. | **Critical** |
| **Q3** | The asset MUST match the scene's brightness/luminance level — not brighter or darker than its surroundings. | **Critical** |
| **Q4** | If the surface is partially occluded (e.g. a person walks in front of a billboard), the asset MUST be occluded accordingly. | High |
| **Q5** | The asset SHOULD have motion blur matching the scene's camera movement on a per-frame basis. | High |
| **Q6** | The asset SHOULD have a subtle drop shadow or ambient occlusion where it meets the surface edges. | Medium |
| **Q7** | The asset SHOULD match the film grain / sensor noise pattern of the source footage. | Medium |
| **Q8** | For digital screens (TVs, phones), the asset SHOULD be composited with a slight screen-door or sub-pixel pattern for realism. | Low |
| **Q9** | The compositing engine MUST be swappable via `ICompositingService` — different engines for different quality tiers. | **Critical** |

### 13.4 Compositing Pipeline — OpenCV CPU + Optional AI Relighting

**Tool:** OpenCVSharp4 (C#) for standard placements. Fal.ai IC-Light / ControlNet for premium.
**Cost:** $0.00 per frame (standard) or ~$0.002 per frame (premium AI relighting).

```
Source Frame
    │
    ├─ Surface boundary (per-frame, tracked) → 4-corner quad
    │     │
    │     └─ Cv2.GetPerspectiveTransform(asset_corners, surface_quad)
    │        Cv2.WarpPerspective(asset, transform) → warped asset
    │
    ├─ Scene region analysis (surface neighborhood, LAB colour space)
    │     │
    │     ├─ Compute mean + variance of L, A, B channels in surface area
    │     ├─ Compute same for brand asset
    │     └─ Colour transfer: map asset LAB distribution → scene LAB distribution
    │        (Reinhard et al. colour transfer algorithm — <1ms CPU)
    │
    ├─ Motion blur (from optical flow vectors computed during tracking)
    │     │
    │     └─ Directional Gaussian blur matching per-pixel flow magnitude
    │
    ├─ SAM 2 mask as alpha channel
    │     │
    │     └─ Pixels outside mask → original frame (bit-identical, per C4)
    │        Pixels inside mask → alpha-blended warped asset
    │
    ├─ [Optional: Premium] Fal.ai IC-Light → photo-realistic shadows,
    │     specular highlights, ambient occlusion matching environment
    │
    └─ Alpha composite: asset blended onto original frame
          │
          ▼
    Composited Frame (only surface region modified — per C4)
```

**Per-frame CPU cost:** ~4ms total at 1080p (warp: 2ms + colour transfer: 1ms + blend: 1ms).
**Premium path:** Trigger only when viability > 0.8 and placement tier = "high-value".

---

## 14. Reviewer Approval Workflow

Every surface placement must be approved by a human before rendering.

### 14.1 Current State

| Capability | Status | Detail |
|---|---|---|
| Approve/reject surface | ✅ Built | `POST /api/surfaces/{id}/approve` with decision + reason |
| Audit trail | ✅ Built | `ApprovalItem` records decision, approver, timestamp |
| Approval list view | ✅ Built | `GET /api/approvals` with filtering |
| Side-by-side preview | ❌ Not built | Reviewer cannot see before/after comparison |
| Batch approval | ❌ Not built | Must approve one surface at a time |
| Approval required for render | ❌ Not built | Render can be triggered without approval |

### 14.2 Requirements

| # | Requirement | Priority |
|---|---|---|
| **V1** | The reviewer MUST see the original frame AND a compositing preview side-by-side before deciding. | **Critical** |
| **V2** | The reviewer MUST be able to approve or reject each surface placement individually. | **Critical** |
| **V3** | Rejection MUST require a reason (free text). | **Critical** |
| **V4** | Every decision MUST be permanently recorded in the audit trail. | **Critical** |
| **V5** | Rendering MUST be blocked until all surfaces in a scene are approved. | High |
| **V6** | The reviewer SHOULD be able to see surface metadata: confidence, viability, depth, surface type. | Medium |
| **V7** | The reviewer SHOULD be able to play the scene as video to see how the surface moves. | Medium |
| **V8** | Batch approval ("approve all in this scene") SHOULD be available for high-confidence surfaces. | Low |

### 14.3 Approval States

```
Candidate ──→ Operator adjusts boundary (Editor)
    │
    ▼
Pending ──→ Reviewer sees before/after preview
    │
    ├── Approved ──→ AdSlot created → eligible for render
    │
    └── Rejected ──→ Surface excluded with reason → audit trail
```

---

## 15. Scene Reassembly & Final Render

After all surfaces in all scenes are approved and assets assigned, the full video must be reassembled with all brand insertions applied.

### 15.1 Current State

| Capability | Status | Detail |
|---|---|---|
| Single-surface render | ⚠️ Partial | Render dispatch exists but render logic is basic |
| Multi-surface per scene | ❌ Not built | Each surface rendered independently |
| Scene concatenation | ❌ Not built | No assembly of rendered scenes back into full video |
| Audio passthrough | ❌ Not built | Audio handling not implemented |
| Export presets | ⚠️ Partial | `RenderItem.ExportPreset` field exists but not enforced |

### 15.2 Requirements

| # | Requirement | Priority |
|---|---|---|
| **M1** | The final render MUST process every frame of every scene, applying all approved brand insertions. | **Critical** |
| **M2** | Scenes MUST be concatenated in their original order with original timing — no gaps, no overlaps. | **Critical** |
| **M3** | The original audio track MUST pass through unmodified. | **Critical** |
| **M4** | The output duration MUST exactly match the input duration — per C1. | **Critical** |
| **M5** | Multiple surfaces within the same scene MUST be composited in correct depth order (closer surfaces render on top). | High |
| **M6** | The renderer MUST support multiple export presets: Broadcast (ProRes 422), Streaming (H.264), Social (H.264 vertical 9:16 crop). | Medium |
| **M7** | Render progress MUST be reported incrementally so the frontend can show a progress bar. | Medium |
| **M8** | Failed renders MUST be retryable without re-processing already-completed scenes. | Low |

### 15.3 Render Assembly Architecture — FFmpeg CPU

**Tool:** FFmpeg CLI (Process wrapper in C#) or FFMpegCore NuGet.
**Cost:** $0.00 — runs on the same CPU as the .NET API.

```
Source Video (full, original codec)
    │
    ├─ Decode to raw frames (yuv420p pixel format)
    │
    ├─ Scene 1 [frames 0-300]
    │     ├─ Surface A: composited per-frame → overlay on scene frames
    │     └─ Surface B: composited per-frame → overlay (depth-ordered: far first, near last)
    │     └─ Write Scene 1 output frames
    │
    ├─ Scene 2 [frames 301-600]
    │     └─ Surface C: composited per-frame → overlay
    │     └─ Write Scene 2 output frames
    │
    ├─ ... (all N scenes processed independently, parallelizable)
    │
    ├─ Encode rendered frames → intermediate MP4 (libx264 for H.264, or libx265)
    │
    └─ FFmpeg final assembly:
          ffmpeg -i rendered_visuals.mp4 -i original_input.mp4 \
                 -c:v copy -c:a copy -map 0:v:0 -map 1:a:0 final_output.mp4
          │
          ├─ Video stream: from rendered file (re-encoded with brand insertions)
          ├─ Audio stream: from original file (stream-copied, zero degradation)
          ├─ Duration: exact match to source (guaranteed by stream copy + same frame count)
          │
          └─ Export preset applied:
               Broadcast: ProRes 422 (mov)
               Streaming: H.264 (mp4)
               Social: H.264 vertical 9:16 crop (mp4)
```

**Key guarantees:**
- **M3 (unmodified audio):** `-c:a copy` copies the original audio bitstream — zero re-encoding, zero quality loss.
- **M4 (exact duration):** Same frame count in → same frame count out. Stream copy enforces duration integrity.

---

## 16. End-to-End Pipeline Summary

```
1. INGEST        Upload video → validate, transcode, normalize
                      │
2. SCENE DETECT   FFmpeg scene-cut detection → scene boundaries
                      │
3. SURFACE DETECT AI engine finds surfaces → Candidate list
   (this doc §2)      │
                      ▼
4. EDITOR         Operator adjusts boundaries, deletes false positives
   (this doc §10)     │
                      ▼
5. TRACK          Surface tracked through every frame of the scene
   (this doc §11)     │
                      ▼
6. ASSOCIATE      Asset assigned to AdSlot → campaign link
   (this doc §12)     │
                      ▼
7. APPROVE        Reviewer sees before/after → approves or rejects
   (this doc §14)     │
                      ▼
8. COMPOSITE      Per-frame compositing with perspective warp + lighting
   (this doc §13)     │
                      ▼
9. REASSEMBLE     Scenes concatenated with original audio → final file
   (this doc §15)     │
                      ▼
10. DELIVER       Export in selected preset → storage → distribution
```

---

## 17. Updated Revision History