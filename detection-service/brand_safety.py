"""
Brand-safety classifier using CLIP for zero-shot image classification.

Checks each detected surface region against exclusion categories:
- Human faces, children → PERMANENTLY EXCLUDED
- Emergency vehicles, police, military → PERMANENTLY EXCLUDED
- Religious symbols, government insignia → PERMANENTLY EXCLUDED
- Weapons, alcohol, tobacco branding → PERMANENTLY EXCLUDED

Also classifies whether the region is a valid ad surface at all.

Model: openai/clip-vit-base-patch32 (~350 MB, ~2GB VRAM)
"""

from __future__ import annotations

import logging
import time
from typing import Optional

import numpy as np

logger = logging.getLogger("v2.brand-safety")

# ── Safety classification categories ──
# Each is a (label, is_safe) pair. is_safe=False means REJECT the surface.
SAFETY_CATEGORIES = [
    # ── UNSAFE (auto-reject) ──
    ("a human face", False),
    ("a child or minor", False),
    ("an emergency vehicle like ambulance or fire truck", False),
    ("a police car or police officer", False),
    ("a military vehicle or soldier", False),
    ("a religious symbol, cross, crescent, or temple", False),
    ("a government building or official insignia", False),
    ("a weapon, gun, or firearm", False),
    ("alcohol branding, beer logo, or liquor advertisement", False),
    ("tobacco or cigarette branding", False),
    ("explicit or adult content", False),
    ("blood, gore, or violence", False),
    # ── SAFE (ad-placeable surfaces) ──
    ("a billboard or advertisement space", True),
    ("a television screen or monitor", True),
    ("an empty wall or blank surface", True),
    ("a stadium scoreboard or LED display", True),
    ("a bus or vehicle exterior", True),
    ("a poster, banner, or signage", True),
    ("a product display shelf or kiosk", True),
    ("a building facade or wall", True),
    ("a blank rectangular area suitable for advertising", True),
]

# Text templates for CLIP zero-shot classification
TEMPLATES = [
    "this is {}",
    "a photo of {}",
    "an image showing {}",
]


class BrandSafetyClassifier:
    """CLIP-based zero-shot brand safety checker."""

    def __init__(self, device: str = "auto"):
        import torch

        self._device = "cuda" if (device == "auto" and torch.cuda.is_available()) else device
        if self._device == "auto":
            self._device = "cpu"
        self._model = None
        self._processor = None
        self._labels = None
        self._is_safe = None
        self._text_features = None

    @property
    def is_loaded(self) -> bool:
        return self._model is not None

    def load(self):
        """Load CLIP model and precompute text embeddings for safety categories."""
        if self._model is not None:
            return

        from transformers import CLIPProcessor, CLIPModel
        import torch
        from PIL import Image

        model_id = "openai/clip-vit-base-patch32"
        logger.info("Loading CLIP for brand safety: %s on %s", model_id, self._device)
        t0 = time.time()

        self._model = CLIPModel.from_pretrained(model_id).to(self._device)
        self._processor = CLIPProcessor.from_pretrained(model_id)
        self._model.eval()

        # Precompute text embeddings for all safety categories
        self._labels = [cat[0] for cat in SAFETY_CATEGORIES]
        self._is_safe = [cat[1] for cat in SAFETY_CATEGORIES]

        # Use prompt templates for better zero-shot performance
        text_inputs = []
        for label in self._labels:
            for template in TEMPLATES:
                text_inputs.append(template.format(label))

        with torch.no_grad():
            inputs = self._processor(
                text=text_inputs,
                return_tensors="pt",
                padding=True,
                truncation=True,
            ).to(self._device)
            text_embeds = self._model.get_text_features(**inputs)
            # Average embeddings across templates
            text_embeds = text_embeds.reshape(len(self._labels), len(TEMPLATES), -1)
            self._text_features = text_embeds.mean(dim=1)
            self._text_features = self._text_features / self._text_features.norm(dim=-1, keepdim=True)

        logger.info(
            "CLIP brand safety loaded in %.1fs (%d categories)",
            time.time() - t0, len(self._labels),
        )

    def classify(self, image: np.ndarray, region_bbox: tuple) -> dict:
        """
        Classify a surface region for brand safety.

        Args:
            image: Full frame BGR numpy array (H×W×3).
            region_bbox: (x1, y1, x2, y2) of the surface.

        Returns:
            dict with keys:
                - is_safe: bool — True if surface passes brand safety
                - exclusion_reason: str | None — reason if rejected
                - top_label: str — closest matching category
                - top_score: float — confidence of top match
        """
        if self._model is None:
            self.load()

        from PIL import Image
        import torch

        x1, y1, x2, y2 = region_bbox
        # Crop the region with 20% padding for context
        h, w = image.shape[:2]
        pad_x = int((x2 - x1) * 0.2)
        pad_y = int((y2 - y1) * 0.2)
        cx1 = max(0, x1 - pad_x)
        cy1 = max(0, y1 - pad_y)
        cx2 = min(w, x2 + pad_x)
        cy2 = min(h, y2 + pad_y)

        crop = image[cy1:cy2, cx1:cx2]
        if crop.size == 0:
            return {"is_safe": False, "exclusion_reason": "Empty crop region", "top_label": "none", "top_score": 0.0}

        # Convert BGR → RGB PIL
        crop_rgb = Image.fromarray(crop[..., ::-1])

        t0 = time.time()
        inputs = self._processor(images=crop_rgb, return_tensors="pt").to(self._device)

        with torch.no_grad():
            image_embeds = self._model.get_image_features(**inputs)
            image_embeds = image_embeds / image_embeds.norm(dim=-1, keepdim=True)

            # Cosine similarity with precomputed text features
            similarity = (image_embeds @ self._text_features.T).squeeze(0)
            scores = similarity.softmax(dim=0).cpu().numpy()

        best_idx = int(scores.argmax())
        best_label = self._labels[best_idx]
        best_score = float(scores[best_idx])
        safe = self._is_safe[best_idx]

        elapsed = (time.time() - t0) * 1000

        result = {
            "is_safe": safe,
            "exclusion_reason": None if safe else f"Brand safety: {best_label}",
            "top_label": best_label,
            "top_score": round(best_score, 3),
        }

        logger.debug(
            "Brand safety: %s → %s (score=%.3f, safe=%s, %.0fms)",
            region_bbox, best_label, best_score, safe, elapsed,
        )

        return result

    def release(self):
        """Free GPU/CPU memory."""
        if self._model:
            del self._model
            self._model = None
        if self._processor:
            del self._processor
            self._processor = None
        self._text_features = None
        import torch
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        logger.info("CLIP brand safety released")
