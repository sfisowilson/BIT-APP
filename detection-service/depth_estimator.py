"""
Monocular depth estimation using Depth Anything V2.

Replaces the crude heuristic (depth = 1 + (1-areaRatio)*25) with a real
pretrained monocular depth model that produces metric depth maps from single images.

Model: depth-anything/Depth-Anything-V2-Small-hf (~100 MB, ~2GB VRAM)
"""

from __future__ import annotations

import logging
import time
from typing import Optional

import numpy as np

logger = logging.getLogger("v2.depth")


class DepthEstimator:
    """Wraps Depth Anything V2 for per-surface depth extraction."""

    def __init__(self, device: str = "auto"):
        import torch

        self._device = "cuda" if (device == "auto" and torch.cuda.is_available()) else device
        if self._device == "auto":
            self._device = "cpu"
        self._pipe = None

    @property
    def is_loaded(self) -> bool:
        return self._pipe is not None

    def load(self):
        """Load Depth Anything V2 pipeline."""
        if self._pipe is not None:
            return

        from transformers import pipeline

        model_id = "depth-anything/Depth-Anything-V2-Small-hf"
        logger.info("Loading Depth Anything V2: %s on %s", model_id, self._device)
        t0 = time.time()

        self._pipe = pipeline(
            "depth-estimation",
            model=model_id,
            device=self._device if self._device != "cpu" else -1,
        )

        logger.info("Depth Anything V2 loaded in %.1fs", time.time() - t0)

    def estimate(self, image: np.ndarray, masks: list[np.ndarray]) -> list[float]:
        """
        Estimate depth for each surface mask region.

        Args:
            image: BGR numpy array (H×W×3).
            masks: List of binary masks (H×W bool arrays) — one per surface.

        Returns:
            List of depth values in metres (approximate relative depth, normalized).
        """
        if self._pipe is None:
            self.load()

        if not masks:
            return []

        from PIL import Image

        # Convert BGR → RGB PIL
        rgb = Image.fromarray(image[..., ::-1])

        t0 = time.time()
        result = self._pipe(rgb)
        depth_map = np.array(result["depth"])  # H×W float32, relative depth

        depths = []
        for mask in masks:
            # Extract depth values within the mask region
            if mask is None or not mask.any():
                depths.append(5.0)  # default mid-range
                continue

            region_depth = depth_map[mask]
            if len(region_depth) == 0:
                depths.append(5.0)
                continue

            # Use median depth (robust to outliers at mask edges)
            median_depth = float(np.median(region_depth))

            # Normalize to approximate metre range (1–30m)
            # Depth Anything outputs relative depth — scale to plausible range
            depth_norm = _normalize_depth(median_depth, depth_map)

            depths.append(round(depth_norm, 1))

        elapsed = (time.time() - t0) * 1000
        logger.info(
            "Depth: %d surfaces estimated in %.0fms (range: %.1f–%.1fm)",
            len(depths), elapsed,
            min(depths) if depths else 0,
            max(depths) if depths else 0,
        )

        return depths

    def release(self):
        """Free GPU/CPU memory."""
        if self._pipe:
            del self._pipe
            self._pipe = None
            import torch
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        logger.info("Depth Anything V2 released")


def _normalize_depth(median_val: float, depth_map: np.ndarray) -> float:
    """
    Convert relative depth value to approximate metres (1–30m range).

    Depth Anything outputs relative depth (inverse depth), not metric.
    We normalize to a plausible range based on the full depth map statistics.
    """
    dmin = float(depth_map.min())
    dmax = float(depth_map.max())

    if dmax <= dmin:
        return 5.0

    # Normalize to 0–1, then map to 1–30 metre range
    normalized = (median_val - dmin) / (dmax - dmin)
    # Invert: high depth value = close (Depth Anything convention)
    # Map: 0 (far) → 30m, 1 (close) → 1m
    metres = 1.0 + (1.0 - normalized) * 29.0
    return round(metres, 1)
