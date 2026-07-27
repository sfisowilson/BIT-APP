"""
Precise surface segmentation using Meta's Segment Anything Model (SAM).

Takes bounding boxes from Grounding DINO and produces pixel-precise polygon masks.
Replaces the current axis-aligned [[x1,y1],[x2,y1],[x2,y2],[x1,y2]] with the actual
perspective boundary of each surface — critical for realistic compositing.

Model: sam_vit_b_01ec64.pth (ViT-B, ~370 MB, ~4GB VRAM)
"""

from __future__ import annotations

import logging
import time
from typing import Optional

import cv2
import numpy as np

logger = logging.getLogger("v2.sam")

# SAM checkpoint download URL (Meta's official release)
SAM_CHECKPOINT_URL = "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_b_01ec64.pth"
SAM_MODEL_TYPE = "vit_b"


class SamSegmenter:
    """Wraps Meta's SAM for precise surface boundary extraction."""

    def __init__(self, checkpoint_path: str = "sam_vit_b_01ec64.pth", device: str = "auto"):
        import torch

        self.checkpoint_path = checkpoint_path
        self._device = "cuda" if (device == "auto" and torch.cuda.is_available()) else device
        if self._device == "auto":
            self._device = "cpu"
        self._predictor = None

    @property
    def is_loaded(self) -> bool:
        return self._predictor is not None

    def load(self):
        """Load SAM model. Downloads checkpoint on first use if not cached."""
        if self._predictor is not None:
            return

        import os
        from segment_anything import sam_model_registry, SamPredictor

        # Auto-download checkpoint if missing
        if not os.path.exists(self.checkpoint_path):
            logger.info("SAM checkpoint not found — downloading %s ...", SAM_CHECKPOINT_URL)
            self._download_checkpoint()

        logger.info("Loading SAM %s on %s from %s", SAM_MODEL_TYPE, self._device, self.checkpoint_path)
        t0 = time.time()

        sam = sam_model_registry[SAM_MODEL_TYPE](checkpoint=self.checkpoint_path)
        sam.to(device=self._device)
        self._predictor = SamPredictor(sam)

        logger.info("SAM loaded in %.1fs", time.time() - t0)

    def segment(self, image: np.ndarray, boxes_xyxy: list[tuple]) -> list[dict]:
        """
        Generate precise polygon masks for a list of bounding boxes.

        Args:
            image: BGR numpy array (H×W×3).
            boxes_xyxy: List of (x1, y1, x2, y2) integer tuples.

        Returns:
            List of dicts with keys: polygon (list of {x,y} points), area_px, mask.
        """
        if self._predictor is None:
            self.load()

        if not boxes_xyxy:
            return []

        # Convert boxes to SAM's expected format (numpy array)
        input_boxes = np.array(boxes_xyxy, dtype=np.float32)

        t0 = time.time()
        self._predictor.set_image(image)

        masks, scores, _ = self._predictor.predict(
            box=input_boxes,
            multimask_output=False,  # single best mask per box
        )

        results = []
        for i, (mask, score) in enumerate(zip(masks, scores)):
            # Extract polygon from binary mask
            polygon = _mask_to_polygon(mask)

            if polygon is None or len(polygon) < 4:
                continue  # mask too small or degenerate

            area_px = int(mask.sum())
            results.append({
                "polygon": polygon,
                "area_px": area_px,
                "mask_score": round(float(score), 3),
                "mask": mask,  # keep for depth extraction
            })

        elapsed = (time.time() - t0) * 1000
        logger.info(
            "SAM: %d/%d boxes segmented in %.0fms",
            len(results), len(boxes_xyxy), elapsed,
        )

        return results

    def release(self):
        """Free GPU/CPU memory."""
        if self._predictor:
            del self._predictor
            self._predictor = None
            import torch
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        logger.info("SAM released")

    def _download_checkpoint(self):
        """Download SAM checkpoint from Meta's CDN."""
        import urllib.request

        logger.info("Downloading SAM checkpoint (~370 MB) — this may take a few minutes ...")
        urllib.request.urlretrieve(SAM_CHECKPOINT_URL, self.checkpoint_path)
        logger.info("SAM checkpoint downloaded to %s", self.checkpoint_path)


# ── Helpers ──

def _mask_to_polygon(mask: np.ndarray, epsilon: float = 0.002) -> Optional[list[dict]]:
    """
    Convert a binary mask to a simplified polygon.

    Uses OpenCV's findContours + approxPolyDP to get the convex hull,
    then simplifies with the Douglas-Peucker algorithm.
    """
    # Ensure mask is uint8
    mask_u8 = (mask.astype(np.uint8) * 255)

    contours, _ = cv2.findContours(mask_u8, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if not contours:
        return None

    # Use the largest contour
    largest = max(contours, key=cv2.contourArea)

    # Simplify polygon
    peri = cv2.arcLength(largest, closed=True)
    approx = cv2.approxPolyDP(largest, epsilon * peri, closed=True)

    # Convert to list of {x, y} dicts
    points = approx.reshape(-1, 2)
    if len(points) < 4:
        return None

    return [{"x": int(p[0]), "y": int(p[1])} for p in points]
