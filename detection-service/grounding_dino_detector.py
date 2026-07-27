"""
Zero-shot open-vocabulary surface detector using Grounding DINO.

Unlike YOLO (fixed 80 COCO classes), Grounding DINO accepts arbitrary text prompts
and detects ANY object matching those descriptions — no custom training needed.

This is the core fix for Phase 2: it finds "empty wall", "bus side", "stadium LED board",
"billboard", and other ad-placeable surfaces that have no COCO class.

Model: IDEA-Research/grounding-dino-base (~700 MB, ~6GB VRAM recommended)
Falls back to grounding-dino-tiny (~200 MB) if VRAM constrained.
"""

from __future__ import annotations

import logging
import time
from typing import Optional

import numpy as np
import torch
from PIL import Image

logger = logging.getLogger("v2.grounding-dino")

# ── Text prompt: what to look for ──
# Design principle: ANY flat or semi-flat visible surface is a candidate ad placement.
# We describe characteristics, not specific object types — the model decides what qualifies.
# Grounding DINO works best with shorter, comma-separated descriptive phrases.
SURFACE_TEXT_PROMPT = (
    "a flat rectangular surface . a smooth empty area . "
    "a visible wall or panel . a screen or display . "
    "a large flat side of an object . a planar region . "
    "a surface suitable for placing an image . "
    "a blank area on a vehicle or building . "
    "a sign or board . a poster or banner . "
    "a tabletop or counter surface . a floor or ground plane . "
    "a fabric panel or curtain . a door or window surface"
)

# Surfaces that should ALWAYS be excluded regardless of detection confidence
# These are checked post-detection via the brand-safety classifier in Phase 2.
# Grounding DINO itself has no exclusion mechanism — it finds whatever matches the prompt.

# ── Model registry ──
MODEL_VARIANTS = {
    "base": "IDEA-Research/grounding-dino-base",
    "tiny": "IDEA-Research/grounding-dino-tiny",
}


class GroundingDinoDetector:
    """Zero-shot detector that finds ad surfaces from text descriptions."""

    def __init__(
        self,
        model_variant: str = "base",
        box_threshold: float = 0.25,
        text_threshold: float = 0.20,
        device: str = "auto",
    ):
        self.model_variant = model_variant
        self.box_threshold = box_threshold
        self.text_threshold = text_threshold
        self._model = None
        self._processor = None
        self._device = device

    @property
    def is_loaded(self) -> bool:
        return self._model is not None

    def load(self):
        """Load Grounding DINO model. Call once before detection."""
        if self._model is not None:
            return

        model_name = MODEL_VARIANTS.get(self.model_variant, MODEL_VARIANTS["base"])

        if self._device == "auto":
            self._device = "cuda" if torch.cuda.is_available() else "cpu"

        logger.info("Loading Grounding DINO: %s on %s", model_name, self._device)

        from transformers import AutoProcessor, AutoModelForZeroShotObjectDetection

        t0 = time.time()
        self._processor = AutoProcessor.from_pretrained(model_name)
        self._model = AutoModelForZeroShotObjectDetection.from_pretrained(model_name)
        self._model.to(self._device)
        self._model.eval()

        logger.info("Grounding DINO loaded in %.1fs", time.time() - t0)

    def detect(self, image: np.ndarray) -> list[dict]:
        """
        Run zero-shot detection on a single frame.

        Args:
            image: BGR numpy array (H×W×3) as read by cv2.

        Returns:
            List of dicts with keys: surface_type, boundary (4-corner xyxy),
            confidence, bbox_xyxy.
        """
        if self._model is None:
            self.load()

        # Convert BGR (OpenCV) → RGB (PIL)
        rgb = cv2_to_pil(image)

        t0 = time.time()
        inputs = self._processor(
            images=rgb,
            text=SURFACE_TEXT_PROMPT,
            return_tensors="pt",
        ).to(self._device)

        with torch.no_grad():
            outputs = self._model(**inputs)

        # Post-process: convert logits → boxes + scores
        results = self._processor.post_process_grounded_object_detection(
            outputs,
            inputs.input_ids,
            box_threshold=self.box_threshold,
            text_threshold=self.text_threshold,
            target_sizes=[rgb.size[::-1]],  # (height, width)
        )[0]

        boxes = results["boxes"].cpu().numpy()       # (N, 4) xyxy format
        scores = results["scores"].cpu().numpy()      # (N,)
        labels = results["labels"]                     # list of strings

        surfaces = []
        for box, score, label in zip(boxes, scores, labels):
            x1, y1, x2, y2 = box.astype(int)
            bw, bh = x2 - x1, y2 - y1

            # Skip tiny or frame-filling detections
            h, w = image.shape[:2]
            if bw < 30 or bh < 20 or bw > w * 0.85 or bh > h * 0.85:
                continue

            surface_type = _map_label_to_surface_type(label)

            surfaces.append({
                "surface_type": surface_type,
                "boundary": [
                    {"x": int(x1), "y": int(y1)},
                    {"x": int(x2), "y": int(y1)},
                    {"x": int(x2), "y": int(y2)},
                    {"x": int(x1), "y": int(y2)},
                ],
                "confidence": round(float(score), 3),
                "label": label,
                "bbox_xyxy": (int(x1), int(y1), int(x2), int(y2)),
            })

        # Deduplicate overlapping detections (keep highest confidence)
        surfaces.sort(key=lambda s: s["confidence"], reverse=True)
        surfaces = _nms_surfaces(surfaces, iou_threshold=0.5)

        elapsed = (time.time() - t0) * 1000
        logger.info(
            "Grounding DINO: %d surfaces found in %.0fms (thresholds: box=%.2f text=%.2f)",
            len(surfaces), elapsed, self.box_threshold, self.text_threshold,
        )

        return surfaces

    def release(self):
        """Free GPU/CPU memory."""
        if self._model:
            del self._model
            self._model = None
        if self._processor:
            del self._processor
            self._processor = None
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        logger.info("Grounding DINO released")


# ── Helpers ──

def cv2_to_pil(bgr: np.ndarray) -> Image.Image:
    """Convert OpenCV BGR numpy array → RGB PIL Image."""
    import cv2
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    return Image.fromarray(rgb)


def _map_label_to_surface_type(label: str) -> str:
    """Map a Grounding DINO text label to a BIT surface type.
    With the open-vocabulary prompt, labels will be descriptive phrases.
    We classify based on keywords in the label."""
    lower = label.lower().strip()

    # Digital / electronic surfaces
    if any(w in lower for w in ("screen", "display", "monitor", "tv", "television", "led", "phone", "tablet", "laptop")):
        return "Digital Screen"
    if "scoreboard" in lower or "stadium" in lower or "arena" in lower:
        return "Stadium Display"

    # Outdoor / transit
    if any(w in lower for w in ("vehicle", "car", "bus", "truck", "train", "transit", "taxi")):
        return "Transit Ad Space"
    if any(w in lower for w in ("billboard", "hoarding", "outdoor")):
        return "Billboard"

    # Architectural
    if any(w in lower for w in ("wall", "facade", "building", "exterior", "brick", "concrete", "panel", "surface", "planar", "flat", "smooth", "empty", "blank", "plain")):
        return "Wall Surface"
    if any(w in lower for w in ("window", "glass", "door")):
        return "Window / Door Surface"
    if any(w in lower for w in ("floor", "ground", "pavement", "road", "path", "carpet")):
        return "Floor / Ground Surface"

    # Indoor / furniture
    if any(w in lower for w in ("table", "counter", "desk", "shelf", "surface of", "top of")):
        return "Table / Counter Surface"
    if any(w in lower for w in ("fabric", "curtain", "cloth", "banner", "flag", "tapestry")):
        return "Fabric Surface"

    # Signage / print
    if any(w in lower for w in ("sign", "poster", "banner", "board", "advertisement", "ad", "logo", "brand")):
        return "Signage / Poster"

    # Generic catch-all: the model found something it thinks is an ad surface
    return "Candidate Surface"


def _nms_surfaces(surfaces: list[dict], iou_threshold: float = 0.5) -> list[dict]:
    """Simple IoU-based deduplication for surface bounding boxes."""
    keep = []
    for s in surfaces:
        s_box = s["bbox_xyxy"]
        overlap = False
        for k in keep:
            if _box_iou(s_box, k["bbox_xyxy"]) > iou_threshold:
                overlap = True
                break
        if not overlap:
            keep.append(s)
    return keep


def _box_iou(a: tuple, b: tuple) -> float:
    """Intersection-over-Union for two xyxy boxes."""
    x1 = max(a[0], b[0])
    y1 = max(a[1], b[1])
    x2 = min(a[2], b[2])
    y2 = min(a[3], b[3])
    inter = max(0, x2 - x1) * max(0, y2 - y1)
    area_a = (a[2] - a[0]) * (a[3] - a[1])
    area_b = (b[2] - b[0]) * (b[3] - b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0
