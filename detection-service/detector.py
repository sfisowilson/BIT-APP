"""
Core YOLO detector with ByteTrack surface tracking.

Detects ad-placeable rectangular surfaces:
    - TV / monitor screens          (COCO class 62: tvmonitor)
    - Digital signage / phones      (COCO class 67: cell phone → extrapolated)
    - Billboards / posters          (custom or via large rectangular objects)
    - Wall banners / field boards   (large aspect-ratio rectangles)

ByteTrack assigns stable track IDs so the same surface in frame N and frame N+1
keeps the same identity — this is the "tracking" part.
"""

from __future__ import annotations

import time
import math
import logging
from typing import Optional
from dataclasses import dataclass

import cv2
import numpy as np
from ultralytics import YOLO

logger = logging.getLogger("yolo-service.detector")

# ── COCO classes we treat as candidate ad surfaces ──
# Expanded from just tvmonitor+cellphone to include classes that correlate
# with ad-placeable surfaces when combined with aspect-ratio heuristics.
# NOTE: COCO lacks "billboard", "stadium signage", "empty wall", "bus side" —
# those require Phase 2 Grounding DINO (zero-shot open-vocabulary detection).
SURFACE_COCO_IDS: dict[int, str] = {
    # Digital screens (high confidence)
    62: "tvmonitor",       # TV / monitor screen
    63: "laptop",           # laptop screen → digital signage proxy
    67: "cell phone",       # phone → small digital screen
    # Rectangular objects that may be posters/signage (lower confidence — needs filtering)
    73: "book",             # book → could be poster/magazine if large+wide aspect
    74: "clock",            # clock → often near public signage areas
    # Wide rectangular objects (strict aspect-ratio filtering required)
    76: "keyboard",         # keyboard → wide rectangle, could be signage in context
}

# Classes that are NEVER ad surfaces — permanently exclude regardless of detection
BRAND_SAFETY_EXCLUDE_IDS: set[int] = {
    0,    # person → faces/people must never have ads placed on them
}

# ── Target surface type mapping by aspect ratio ──
def classify_surface(cls_name: str, w: float, h: float) -> str:
    """Map YOLO class + bounding-box shape to BIT surface types."""
    aspect = w / h if h > 0 else 1.0

    # ── Digital screens ──
    if cls_name in ("tvmonitor", "laptop"):
        if aspect > 2.5:
            return "Stadium Perimeter LED Board"
        if aspect > 1.5:
            return "Digital Screen"
        return "TV Screen"

    if cls_name == "cell phone":
        return "Digital Screen"

    # ── Proxy classes (low confidence — only classified if aspect ratio looks plausible) ──
    if cls_name in ("book",):
        if aspect > 2.0:
            return "Poster / Print Ad"
        if aspect > 1.3:
            return "Signage Panel"
        return "Uncertain"

    if cls_name in ("clock",):
        return "Public Signage Area"

    if cls_name in ("keyboard",):
        if aspect > 2.5:
            return "Wall Banner"
        return "Uncertain"

    # ── Generic shape-based fallback ──
    if aspect > 3.0:
        return "Wall Banner"
    if aspect > 1.8:
        return "Billboard"
    if aspect < 0.6:
        return "Window Signage"
    return "Field Board"


def estimate_depth(box_w: int, box_h: int, frame_w: int, frame_h: int) -> float:
    """
    Heuristic depth from bounding-box size relative to frame.
    Larger box → closer surface. Returns metres (1–30m range).
    """
    area_ratio = (box_w * box_h) / (frame_w * frame_h)
    # Invert: small area = far away
    depth = 1.0 + (1.0 - area_ratio) * 25.0
    return round(min(max(depth, 1.0), 30.0), 1)


def estimate_orientation(coords: np.ndarray) -> dict:
    """
    Estimate yaw/pitch/roll from the quadrilateral shape.
    Coords is 4×2 array of corner points.
    Returns rough angles in degrees.
    """
    if coords.shape[0] < 4:
        return {"yaw": 0, "pitch": 0, "roll": 0}

    # Sort points: top-left, top-right, bottom-right, bottom-left
    rect = coords.astype(np.float32)

    # Compute edge vectors
    top_edge = rect[1] - rect[0]
    right_edge = rect[2] - rect[1]
    left_edge = rect[3] - rect[0]

    # Yaw: horizontal tilt (left edge vs vertical)
    yaw = math.degrees(math.atan2(top_edge[1], top_edge[0] + 1e-6)) * 0.5

    # Pitch: vertical foreshortening (left vs right edge length ratio)
    left_len = np.linalg.norm(left_edge)
    right_len = np.linalg.norm(right_edge)
    pitch = math.degrees(math.atan2(right_len - left_len, right_len + left_len + 1e-6)) * 3

    # Roll: rotation around view axis (top edge angle)
    roll = math.degrees(math.atan2(top_edge[1], top_edge[0] + 1e-6))

    return {
        "yaw": round(yaw, 1),
        "pitch": round(pitch, 1),
        "roll": round(roll, 1),
    }


def compute_viability(conf: float, box_w: int, box_h: int, frame_w: int, frame_h: int) -> float:
    """
    Composite viability score for ad placement:
    - Confidence from YOLO
    - Size: too small = not viable, too large = probably foreground obstruction
    - Aspect ratio: very thin or very tall surfaces are hard to place on
    """
    area_ratio = (box_w * box_h) / (frame_w * frame_h)
    aspect = box_w / box_h if box_h > 0 else 1.0

    # Size score: optimal 5-40% of frame
    if 0.05 <= area_ratio <= 0.40:
        size_score = 1.0
    elif area_ratio < 0.05:
        size_score = area_ratio / 0.05  # linear ramp down
    else:
        size_score = max(0.0, 1.0 - (area_ratio - 0.40) / 0.30)

    # Aspect score: prefer 1.3–3.0 (landscape rectangles)
    if 1.3 <= aspect <= 3.0:
        aspect_score = 1.0
    elif aspect < 1.3:
        aspect_score = max(0.3, aspect / 1.3)
    else:
        aspect_score = max(0.3, 3.0 / aspect)

    viability = conf * 0.4 + size_score * 0.35 + aspect_score * 0.25
    return round(min(max(viability, 0.0), 1.0), 2)


def _is_plausible_surface(cls_name: str, xyxy: np.ndarray, frame_w: int, frame_h: int) -> bool:
    """
    Reject detections that are physically impossible as ad surfaces.
    Low-confidence proxy classes (book, clock, keyboard) face stricter checks.
    High-confidence classes (tvmonitor, cell phone, laptop) get a lighter touch.
    """
    x1, y1, x2, y2 = xyxy.astype(int)
    bw, bh = x2 - x1, y2 - y1
    aspect = bw / bh if bh > 0 else 1.0
    area_ratio = (bw * bh) / (frame_w * frame_h)

    # ── Universal sanity checks ──
    # Reject extremely thin slivers (poles, wires, edges)
    if aspect > 8.0 or aspect < 0.125:
        return False

    # Reject tiny specks (noise)
    if bw < 30 or bh < 20:
        return False

    # Reject frame-filling detections (usually false positives, not ad surfaces)
    if bw > frame_w * 0.85 or bh > frame_h * 0.85:
        return False

    # ── High-confidence digital-screen classes: always accept if above sanity checks ──
    if cls_name in ("tvmonitor", "cell phone", "laptop"):
        return True

    # ── Low-confidence proxy classes: stricter filtering ──
    # Must be reasonably sized (not a tiny book in someone's hand)
    if area_ratio < 0.02:
        return False

    # Must have landscape-ish aspect (books/clock/keyboard as ad proxies should be wide)
    if cls_name in ("keyboard", "book") and aspect < 1.2:
        return False

    # Clock faces are roughly square — don't accept very wide or very tall clocks
    if cls_name == "clock" and (aspect > 2.5 or aspect < 0.5):
        return False

    return True


@dataclass
class YoloSurfaceDetector:
    """Holds the YOLO model and runs detection + tracking on video frames."""

    model_size: str = "large"        # nano | small | medium | large | xlarge — large is the production default
    conf_threshold: float = 0.35
    iou_threshold: float = 0.45
    model: Optional[YOLO] = None

    MODEL_MAP = {
        "nano":   "yolo11n.pt",
        "small":  "yolo11s.pt",
        "medium": "yolo11m.pt",
        "large":  "yolo11l.pt",
        "xlarge": "yolo11x.pt",
    }

    def __post_init__(self):
        self._load_model()

    def _load_model(self):
        model_name = self.MODEL_MAP.get(self.model_size, "yolo11n.pt")
        logger.info("Loading YOLO model: %s", model_name)
        self.model = YOLO(model_name)
        logger.info("Model loaded successfully")

    def detect(
        self,
        video_path: str,
        content_id: str,
        scene_index: int,
        start_frame: int,
        end_frame: int,
        tracked: bool = True,
        frame_skip: int = 1,      # process every frame by default for best temporal coverage
    ) -> dict:
        """Run detection + tracking on the specified frame range."""
        if self.model is None:
            self._load_model()

        t0 = time.time()

        cap = cv2.VideoCapture(video_path)
        if not cap.isOpened():
            raise FileNotFoundError(f"Cannot open video: {video_path}")

        frame_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        frame_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        fps = cap.get(cv2.CAP_PROP_FPS)

        # Seek to start frame
        cap.set(cv2.CAP_PROP_POS_FRAMES, start_frame)

        frames_processed, all_surfaces = self._process_frame_range(
            cap, start_frame, end_frame, frame_w, frame_h, tracked, frame_skip
        )

        cap.release()

        surfaces = self._finalize_surfaces(all_surfaces)

        elapsed_ms = (time.time() - t0) * 1000
        model_name = self.MODEL_MAP.get(self.model_size, "yolo11n.pt")

        logger.info(
            "Detection complete: content=%s scene=%d frames=%d surfaces=%d time=%.0fms",
            content_id, scene_index, frames_processed, len(surfaces), elapsed_ms,
        )

        return {
            "content_id": content_id,
            "scene_index": scene_index,
            "surfaces": surfaces,
            "frames_processed": frames_processed,
            "model_used": model_name,
            "processing_time_ms": round(elapsed_ms, 1),
        }

    def detect_batch(
        self,
        video_path: str,
        content_id: str,
        scenes: list[dict],
        tracked: bool = True,
        frame_skip: int = 3,
    ) -> list[dict]:
        """
        Batch detection: opens the video ONCE, processes all scene ranges sequentially,
        and returns results for every scene. Eliminates per-scene video I/O overhead.

        scenes: list of {"scene_index": int, "start_frame": int, "end_frame": int}
        """
        if self.model is None:
            self._load_model()

        t0 = time.time()

        cap = cv2.VideoCapture(video_path)
        if not cap.isOpened():
            raise FileNotFoundError(f"Cannot open video: {video_path}")

        frame_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        frame_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        model_name = self.MODEL_MAP.get(self.model_size, "yolo11n.pt")

        all_results: list[dict] = []

        for scene in scenes:
            scene_idx = scene["scene_index"]
            start_frame = scene["start_frame"]
            end_frame = scene["end_frame"]

            logger.info(
                "Batch scene %d/%d: frames %d-%d",
                scene_idx, len(scenes), start_frame, end_frame,
            )

            # Seek to scene start
            cap.set(cv2.CAP_PROP_POS_FRAMES, start_frame)

            frames_processed, all_surfaces = self._process_frame_range(
                cap, start_frame, end_frame, frame_w, frame_h, tracked, frame_skip
            )

            surfaces = self._finalize_surfaces(all_surfaces)

            all_results.append({
                "content_id": content_id,
                "scene_index": scene_idx,
                "surfaces": surfaces,
                "frames_processed": frames_processed,
                "model_used": model_name,
                "processing_time_ms": 0,  # filled below
            })

        cap.release()

        total_ms = (time.time() - t0) * 1000
        # Distribute timing proportionally across scenes
        total_frames = sum(r["frames_processed"] for r in all_results) or 1
        for r in all_results:
            r["processing_time_ms"] = round(total_ms * r["frames_processed"] / total_frames, 1)

        logger.info(
            "Batch detection complete: content=%s scenes=%d total_time=%.0fms",
            content_id, len(scenes), total_ms,
        )

        return all_results

    def _process_frame_range(
        self,
        cap: cv2.VideoCapture,
        start_frame: int,
        end_frame: int,
        frame_w: int,
        frame_h: int,
        tracked: bool,
        frame_skip: int,
    ) -> tuple[int, dict[int, dict]]:
        """Process a range of frames, returning (frames_processed, aggregated_surfaces)."""
        frames_processed = 0
        all_surfaces: dict[int, dict] = {}

        frame_idx = start_frame
        while frame_idx <= end_frame:
            ret, frame = cap.read()
            if not ret:
                break

            # Run YOLO with ByteTrack if tracking enabled
            if tracked:
                results = self.model.track(
                    frame,
                    persist=True,
                    conf=self.conf_threshold,
                    iou=self.iou_threshold,
                    verbose=False,
                    tracker="bytetrack.yaml",
                )
            else:
                results = self.model(
                    frame,
                    conf=self.conf_threshold,
                    iou=self.iou_threshold,
                    verbose=False,
                )

            frames_processed += 1

            if results[0].boxes is not None:
                boxes = results[0].boxes
                for i in range(len(boxes)):
                    cls_id = int(boxes.cls[i].item())
                    cls_name = results[0].names.get(cls_id, "unknown")

                    # ── Brand-safety: permanently exclude forbidden classes ──
                    if cls_id in BRAND_SAFETY_EXCLUDE_IDS:
                        continue

                    # ── Only accept surface-relevant COCO classes ──
                    if cls_id not in SURFACE_COCO_IDS:
                        continue

                    # ── Plausibility check: is this actually a surface candidate? ──
                    # Low-confidence proxy classes (book, clock, keyboard) need stronger evidence
                    if not _is_plausible_surface(cls_name, xyxy, frame_w, frame_h):
                        continue

                    conf = float(boxes.conf[i].item())
                    xyxy = boxes.xyxy[i].cpu().numpy()
                    x1, y1, x2, y2 = xyxy.astype(int)
                    bw, bh = x2 - x1, y2 - y1

                    # Skip very small or edge-hugging detections
                    if bw < 40 or bh < 30:
                        continue
                    if bw > frame_w * 0.85 or bh > frame_h * 0.85:
                        continue

                    track_id = None
                    if tracked and boxes.id is not None:
                        track_id = int(boxes.id[i].item())

                    surface_type = classify_surface(cls_name, bw, bh)
                    depth = estimate_depth(bw, bh, frame_w, frame_h)
                    coords_arr = np.array([[x1, y1], [x2, y1], [x2, y2], [x1, y2]])
                    orientation = estimate_orientation(coords_arr)
                    viability = compute_viability(conf, bw, bh, frame_w, frame_h)

                    surface_data = {
                        "surface_type": surface_type,
                        "boundary": [{"x": x1, "y": y1}, {"x": x2, "y": y1}, {"x": x2, "y": y2}, {"x": x1, "y": y2}],
                        "depth": depth,
                        "orientation": orientation,
                        "confidence": round(conf, 2),
                        "viability": viability,
                        "track_id": track_id,
                        "frame_count": 1,
                    }

                    key = track_id if track_id is not None else -(i + 1)
                    if key in all_surfaces:
                        # Average with previous detections for stability
                        prev = all_surfaces[key]
                        w = 1.0 / (prev["frame_count"] + 1)
                        prev["boundary"] = lerp_coords(prev["boundary"], surface_data["boundary"], w)
                        prev["confidence"] = round(max(prev["confidence"], surface_data["confidence"]), 2)
                        prev["viability"] = round((prev["viability"] * prev["frame_count"] + viability) / (prev["frame_count"] + 1), 2)
                        prev["frame_count"] += 1
                    else:
                        all_surfaces[key] = surface_data

            frame_idx += 1
            # Skip frames for speed (configurable via frame_skip)
            for _ in range(frame_skip - 1):
                if frame_idx <= end_frame:
                    cap.grab()
                    frame_idx += 1

        return frames_processed, all_surfaces

    def _finalize_surfaces(self, all_surfaces: dict[int, dict]) -> list[dict]:
        """Convert aggregated surface data to the standard response format."""
        surfaces = []
        for key, s in all_surfaces.items():
            # Exclude surfaces seen in too few frames (likely false positives)
            if s["frame_count"] < 2:
                continue

            exclusion = None
            if s["viability"] < 0.25:
                exclusion = f"Low viability score ({s['viability']:.2f})"
            elif s["confidence"] < 0.35:
                exclusion = f"Low detection confidence ({s['confidence']:.2f})"

            surfaces.append({
                "surface_type": s["surface_type"],
                "boundary_coordinates": s["boundary"],
                "estimated_depth": s["depth"],
                "orientation_vector": s["orientation"],
                "confidence_score": s["confidence"],
                "viability_score": s["viability"],
                "exclusion_reason": exclusion,
                "track_id": s["track_id"],
            })

        # Sort by viability (best first), deduplicate overlapping
        surfaces.sort(key=lambda x: x["viability_score"], reverse=True)
        surfaces = deduplicate_overlapping(surfaces)
        return surfaces

    def release(self):
        """Release model resources."""
        if self.model:
            del self.model
            self.model = None
            logger.info("Model released")


# ── Helpers ──

def lerp_coords(prev: list[dict], curr: list[dict], weight: float) -> list[dict]:
    """Linearly interpolate boundary coordinates for temporal smoothing."""
    result = []
    for p, c in zip(prev, curr):
        result.append({
            "x": round(p["x"] * (1 - weight) + c["x"] * weight),
            "y": round(p["y"] * (1 - weight) + c["y"] * weight),
        })
    return result


def iou(b1: list[dict], b2: list[dict]) -> float:
    """Compute Intersection-over-Union of two axis-aligned bounding boxes."""
    x1 = max(b1[0]["x"], b2[0]["x"])
    y1 = max(b1[0]["y"], b2[0]["y"])
    x2 = min(b1[2]["x"], b2[2]["x"])
    y2 = min(b1[2]["y"], b2[2]["y"])
    inter = max(0, x2 - x1) * max(0, y2 - y1)
    area1 = (b1[2]["x"] - b1[0]["x"]) * (b1[2]["y"] - b1[0]["y"])
    area2 = (b2[2]["x"] - b2[0]["x"]) * (b2[2]["y"] - b2[0]["y"])
    union = area1 + area2 - inter
    return inter / union if union > 0 else 0.0


def deduplicate_overlapping(surfaces: list[dict], iou_threshold: float = 0.5) -> list[dict]:
    """Remove highly overlapping surface detections, keeping highest viability."""
    keep = []
    for s in surfaces:
        if not any(iou(s["boundary_coordinates"], k["boundary_coordinates"]) > iou_threshold for k in keep):
            keep.append(s)
    return keep
