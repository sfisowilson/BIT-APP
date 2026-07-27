"""
Phase 2+3 Surface Detection Engine (v2).

Orchestrates the full open-vocabulary pipeline with multi-frame tracking:
    1. Multi-frame extraction with adaptive frame-skip (optical flow)
    2. Grounding DINO: zero-shot detection from text prompt → bounding boxes
    3. IoU-based tracking with re-identification across frames
    4. SAM: precise polygon masks for each tracked surface
    5. Depth Anything V2: per-surface depth from mask regions
    6. CLIP: brand-safety classification → auto-reject unsafe surfaces
    7. Return structured results with stable track IDs
"""

from __future__ import annotations

import logging
import time
from dataclasses import dataclass, field
from typing import Optional

import cv2
import numpy as np

from grounding_dino_detector import GroundingDinoDetector
from sam_segmenter import SamSegmenter
from depth_estimator import DepthEstimator
from brand_safety import BrandSafetyClassifier
from tracker import SurfaceTracker

logger = logging.getLogger("v2.engine")


@dataclass
class EngineV2Config:
    """Configuration for the v2 detection pipeline."""
    # ── Component toggles ──
    enable_grounding_dino: bool = True
    enable_sam: bool = True
    enable_depth: bool = True
    enable_brand_safety: bool = True
    enable_tracking: bool = True           # Phase 3: multi-frame tracking

    # ── Grounding DINO ──
    gd_model_variant: str = "base"         # base | tiny
    gd_box_threshold: float = 0.25
    gd_text_threshold: float = 0.20

    # ── Phase 3: Tracking ──
    track_iou_threshold: float = 0.30
    track_lost_buffer_frames: int = 30
    track_min_detection_frames: int = 3    # surfaces seen < N frames are filtered

    # ── Phase 3: Adaptive frame-skip ──
    adaptive_frame_skip: bool = True       # enable optical-flow-based frame skipping
    flow_motion_threshold: float = 2.5     # mean optical flow magnitude to trigger detection
    max_frame_skip: int = 15               # never skip more than N consecutive frames
    detection_interval: int = 10           # run Grounding DINO every N frames (tracking fills gaps)

    # ── Pipeline ──
    min_surface_confidence: float = 0.20
    device: str = "auto"                   # auto | cuda | cpu


class SurfaceDetectionEngineV2:
    """
    Phase 2 detection pipeline.

    Usage:
        engine = SurfaceDetectionEngineV2()
        engine.load()                    # explicitly load all models
        results = engine.detect(video_path, content_id, scene_index, 0, 300)
        engine.release()                 # free GPU memory
    """

    def __init__(self, config: EngineV2Config | None = None):
        self.config = config or EngineV2Config()
        self._gd: Optional[GroundingDinoDetector] = None
        self._sam: Optional[SamSegmenter] = None
        self._depth: Optional[DepthEstimator] = None
        self._safety: Optional[BrandSafetyClassifier] = None

    # ── Lifecycle ──

    def load(self):
        """Load all enabled models. Call once before detection."""
        logger.info("Loading v2 engine components ...")
        t0 = time.time()

        if self.config.enable_grounding_dino:
            self._gd = GroundingDinoDetector(
                model_variant=self.config.gd_model_variant,
                box_threshold=self.config.gd_box_threshold,
                text_threshold=self.config.gd_text_threshold,
                device=self.config.device,
            )
            self._gd.load()

        if self.config.enable_sam:
            self._sam = SamSegmenter(device=self.config.device)
            self._sam.load()

        if self.config.enable_depth:
            self._depth = DepthEstimator(device=self.config.device)
            self._depth.load()

        if self.config.enable_brand_safety:
            self._safety = BrandSafetyClassifier(device=self.config.device)
            self._safety.load()

        logger.info("v2 engine fully loaded in %.1fs", time.time() - t0)

    def release(self):
        """Release all loaded models and free GPU memory."""
        for component in [self._gd, self._sam, self._depth, self._safety]:
            if component and component.is_loaded:
                component.release()
        self._gd = None
        self._sam = None
        self._depth = None
        self._safety = None
        logger.info("v2 engine released")

    # ── Detection ──

    def detect(
        self,
        video_path: str,
        content_id: str,
        scene_index: int,
        start_frame: int,
        end_frame: int,
    ) -> dict:
        """
        Phase 3: Multi-frame detection with tracking and adaptive frame-skip.

        Processing loop:
          1. Open video, seek to start_frame
          2. For each frame in [start_frame, end_frame]:
             a. Compute optical flow vs last detection frame
             b. If motion > threshold OR interval exceeded → run Grounding DINO
             c. Otherwise → reuse previous detections (tracking fills gaps)
             d. SurfaceTracker assigns stable IDs across frames
          3. On key frames, run SAM + Depth + Brand Safety for precise geometry
          4. Finalize: return all tracked surfaces with stable IDs
        """
        t0 = time.time()
        tracker = SurfaceTracker(
            iou_match_threshold=self.config.track_iou_threshold,
            lost_buffer_frames=self.config.track_lost_buffer_frames,
            min_detection_frames=self.config.track_min_detection_frames,
        ) if self.config.enable_tracking else None

        cap = cv2.VideoCapture(video_path)
        if not cap.isOpened():
            raise FileNotFoundError(f"Cannot open video: {video_path}")

        frame_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        frame_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

        # Seek to start
        cap.set(cv2.CAP_PROP_POS_FRAMES, start_frame)

        frames_processed = 0
        detections_since_last_run = 0
        last_detection_frame = None        # for optical flow comparison
        last_frame_gray = None
        tracked_frame_detections: dict[int, dict] = {}  # track_id → latest detection
        key_frame_for_geometry = None      # best frame for SAM/Depth/BrandSafety
        key_frame_number = 0

        frame_idx = start_frame
        while frame_idx <= end_frame:
            ret, frame = cap.read()
            if not ret:
                break

            frames_processed += 1
            frame_gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)

            # ── Adaptive frame-skip: should we run detection on this frame? ──
            run_detection = False

            if not self.config.adaptive_frame_skip:
                # Simple fixed-interval mode
                run_detection = (detections_since_last_run >= self.config.detection_interval)
            elif last_frame_gray is None:
                # First frame — always run detection
                run_detection = True
            elif detections_since_last_run >= self.config.max_frame_skip:
                # Safety: don't skip too many frames
                run_detection = True
            else:
                # Compute optical flow magnitude
                flow_mag = _compute_flow_magnitude(last_frame_gray, frame_gray)
                if flow_mag >= self.config.flow_motion_threshold:
                    run_detection = True

            # ── Run Grounding DINO on this frame ──
            if run_detection and self._gd:
                detections = self._gd.detect(frame)
                detections_since_last_run = 0
                last_detection_frame = frame
                last_frame_gray = frame_gray.copy()
                key_frame_for_geometry = frame      # use most recent detection frame for geometry
                key_frame_number = frame_idx

                # Feed into tracker for stable IDs
                if tracker:
                    tracked_frame_detections = tracker.update(frame_idx, detections)
                else:
                    tracked_frame_detections = {
                        i: d for i, d in enumerate(detections)
                    }
            else:
                detections_since_last_run += 1
                # Tracker still updates (ages tracks, handles re-id attempts)
                if tracker:
                    tracked_frame_detections = tracker.update(frame_idx, [])

            frame_idx += 1

        cap.release()

        # ── Finalize: get all tracked surfaces ──
        if tracker:
            all_tracked = tracker.finalize()
        else:
            all_tracked = tracked_frame_detections

        if not all_tracked or key_frame_for_geometry is None:
            return self._empty_result(content_id, scene_index, frames_processed, t0)

        # ── SAM + Depth + Brand Safety on key frame ──
        tracked_list = list(all_tracked.values())
        boxes_xyxy = [d.get("bbox_xyxy", (0, 0, 0, 0)) for d in tracked_list]
        track_ids = list(all_tracked.keys())

        # SAM segmentation (on the best key frame)
        if self._sam:
            sam_results = self._sam.segment(key_frame_for_geometry, boxes_xyxy)
        else:
            sam_results = [None] * len(boxes_xyxy)

        # Depth estimation
        masks_for_depth = [r["mask"] if r else None for r in sam_results]
        if self._depth:
            depths = self._depth.estimate(key_frame_for_geometry, masks_for_depth)
        else:
            depths = [5.0] * len(boxes_xyxy)

        # ── Assemble final results ──
        surfaces = []
        for i, det in enumerate(tracked_list):
            track_id = track_ids[i] if i < len(track_ids) else None

            # Polygon from SAM, fall back to bounding box
            if sam_results and sam_results[i] and sam_results[i].get("polygon"):
                boundary = sam_results[i]["polygon"]
            else:
                boundary = det.get("boundary", [
                    {"x": 0, "y": 0}, {"x": 0, "y": 0}, {"x": 0, "y": 0}, {"x": 0, "y": 0}
                ])

            depth = depths[i] if i < len(depths) else 5.0

            # Brand safety
            exclusion_reason = None
            viability = det.get("confidence", 0.5)

            if self._safety and det.get("bbox_xyxy"):
                safety = self._safety.classify(key_frame_for_geometry, det["bbox_xyxy"])
                if not safety["is_safe"]:
                    exclusion_reason = safety["exclusion_reason"]
                    viability = min(viability, 0.15)
                else:
                    viability = min(1.0, viability * 1.2)

            # Orientation from polygon
            coords = np.array([[p["x"], p["y"]] for p in boundary[:4]], dtype=np.float32)
            orientation = _estimate_orientation(coords, frame_w, frame_h)

            surfaces.append({
                "surface_type": det.get("surface_type", "Detected Surface"),
                "boundary_coordinates": boundary,
                "estimated_depth": depth,
                "orientation_vector": orientation,
                "confidence_score": det.get("confidence", 0.5),
                "viability_score": round(min(max(viability, 0.0), 1.0), 2),
                "exclusion_reason": exclusion_reason,
                "track_id": track_id,  # Phase 3: stable across frames
            })

        surfaces.sort(key=lambda s: s["viability_score"], reverse=True)

        elapsed_ms = (time.time() - t0) * 1000
        logger.info(
            "v2+3 detection: content=%s scene=%d surfaces=%d frames=%d time=%.0fms",
            content_id, scene_index, len(surfaces), frames_processed, elapsed_ms,
        )

        return {
            "content_id": content_id,
            "scene_index": scene_index,
            "surfaces": surfaces,
            "frames_processed": frames_processed,
            "model_used": f"gd-{self.config.gd_model_variant}+sam+depth-v2+clip+track",
            "processing_time_ms": round(elapsed_ms, 1),
            "engine_version": "v2",
        }

    def detect_batch(
        self,
        video_path: str,
        content_id: str,
        scenes: list[dict],
    ) -> list[dict]:
        """Batch detection: process all scenes for a content item."""
        results = []
        for scene in scenes:
            try:
                result = self.detect(
                    video_path=video_path,
                    content_id=content_id,
                    scene_index=scene["scene_index"],
                    start_frame=scene["start_frame"],
                    end_frame=scene["end_frame"],
                )
                results.append(result)
            except Exception as e:
                logger.error("v2 batch failed for scene %d: %s", scene.get("scene_index"), e)
                results.append(self._empty_result(
                    content_id, scene.get("scene_index", 0), 0,
                    time.time(), error=str(e),
                ))
        return results

    # ── Helpers ──

    def _empty_result(
        self, content_id: str, scene_index: int, frames: int,
        t_start: float, error: str | None = None,
    ) -> dict:
        elapsed = (time.time() - t_start) * 1000
        return {
            "content_id": content_id,
            "scene_index": scene_index,
            "surfaces": [],
            "frames_processed": frames,
            "model_used": f"grounding-dino-{self.config.gd_model_variant}+sam+depth-v2",
            "processing_time_ms": round(elapsed, 1),
            "engine_version": "v2",
            "error": error,
        }


# ── Helpers ──

def _compute_flow_magnitude(prev_gray: np.ndarray, curr_gray: np.ndarray) -> float:
    """
    Compute mean optical flow magnitude between two grayscale frames.
    Used for adaptive frame-skip: high motion → run detection, low motion → skip.

    Uses Farneback dense optical flow (fast, GPU-accelerated via OpenCV).
    Returns mean magnitude in pixels.
    """
    # Downsample by 4x for speed (optical flow at full res is overkill for skip decisions)
    h, w = prev_gray.shape
    small_prev = cv2.resize(prev_gray, (w // 4, h // 4))
    small_curr = cv2.resize(curr_gray, (w // 4, h // 4))

    flow = cv2.calcOpticalFlowFarneback(
        small_prev, small_curr, None,
        pyr_scale=0.5, levels=3, winsize=15,
        iterations=3, poly_n=5, poly_sigma=1.2,
        flags=0,
    )

    mag = np.sqrt(flow[..., 0] ** 2 + flow[..., 1] ** 2)
    return float(mag.mean())


def _estimate_orientation(coords: np.ndarray, frame_w: int, frame_h: int) -> dict:
    """Geometric orientation estimation from polygon corners (fallback until Phase 3 pose model)."""
    import math

    if coords.shape[0] < 4:
        return {"yaw": 0, "pitch": 0, "roll": 0}

    top_edge = coords[1] - coords[0]
    right_edge = coords[2] - coords[1]
    left_edge = coords[3] - coords[0]

    yaw = math.degrees(math.atan2(top_edge[1], top_edge[0] + 1e-6)) * 0.5
    left_len = float(np.linalg.norm(left_edge))
    right_len = float(np.linalg.norm(right_edge))
    pitch = math.degrees(math.atan2(right_len - left_len, right_len + left_len + 1e-6)) * 3
    roll = math.degrees(math.atan2(top_edge[1], top_edge[0] + 1e-6))

    return {"yaw": round(yaw, 1), "pitch": round(pitch, 1), "roll": round(roll, 1)}
