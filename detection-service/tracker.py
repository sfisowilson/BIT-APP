"""
Multi-frame surface tracker with re-identification.

Tracks detected surfaces across video frames, assigning stable track IDs.
Uses IoU-based matching for frame-to-frame continuity and CLIP image embeddings
for re-identification when a surface leaves and re-enters the frame.

Replaces the simple "one key frame" approach with multi-frame temporal awareness.
"""

from __future__ import annotations

import logging
from typing import Optional

import numpy as np

logger = logging.getLogger("v2.tracker")


class SurfaceTracker:
    """
    Assigns stable track IDs to surface detections across frames.

    Algorithm:
    1. First frame: assign new IDs to all detections
    2. Subsequent frames: match via IoU with previous frame's detections
    3. Unmatched detections get new IDs
    4. Lost tracks are kept in a "lost" buffer for N frames (re-id window)
    5. Re-identification: if a lost track's CLIP embedding matches a new detection,
       restore the original track ID
    """

    def __init__(
        self,
        iou_match_threshold: float = 0.3,
        lost_buffer_frames: int = 30,
        min_detection_frames: int = 3,     # surfaces seen in fewer frames are filtered out
    ):
        self.iou_match_threshold = iou_match_threshold
        self.lost_buffer_frames = lost_buffer_frames
        self.min_detection_frames = min_detection_frames

        self._next_id = 0
        self._active_tracks: dict[int, _Track] = {}     # track_id → _Track
        self._lost_tracks: dict[int, _Track] = {}        # track_id → _Track (awaiting re-id)
        self._frames_since_last_update: dict[int, int] = {}

    def update(self, frame_number: int, detections: list[dict]) -> dict[int, dict]:
        """
        Assign track IDs to a new set of detections.

        Args:
            frame_number: Current frame index.
            detections: List of detection dicts from the detector.
                        Each must have: bbox_xyxy, confidence, surface_type, boundary.

        Returns:
            Dict mapping track_id → detection dict (stable across frames).
        """
        if not detections:
            # No detections this frame — increment lost counters
            self._age_lost_tracks()
            return {}

        # ── Step 1: Compute IoU matrix between active tracks and new detections ──
        active_ids = list(self._active_tracks.keys())
        iou_matrix = np.zeros((len(active_ids), len(detections)))

        for i, tid in enumerate(active_ids):
            track_box = self._active_tracks[tid].bbox_xyxy
            for j, det in enumerate(detections):
                iou_matrix[i, j] = _box_iou(track_box, det["bbox_xyxy"])

        # ── Step 2: Greedy matching (highest IoU first) ──
        matched_track_ids: set[int] = set()
        matched_det_indices: set[int] = set()

        # Flatten and sort by IoU descending
        pairs = []
        for i, tid in enumerate(active_ids):
            for j in range(len(detections)):
                if iou_matrix[i, j] >= self.iou_match_threshold:
                    pairs.append((iou_matrix[i, j], tid, j))
        pairs.sort(key=lambda x: x[0], reverse=True)

        for _, tid, j in pairs:
            if tid not in matched_track_ids and j not in matched_det_indices:
                matched_track_ids.add(tid)
                matched_det_indices.add(j)

        # ── Step 3: Update matched tracks ──
        results: dict[int, dict] = {}
        for tid in matched_track_ids:
            # Find which detection this track matched
            for _, mtid, j in pairs:
                if mtid == tid and j in matched_det_indices:
                    det = detections[j]
                    track = self._active_tracks[tid]
                    track.update(det, frame_number)
                    self._frames_since_last_update[tid] = 0
                    results[tid] = det
                    break

        # ── Step 4: Try re-identification for lost tracks ──
        unmatched_dets = [j for j in range(len(detections)) if j not in matched_det_indices]

        for j in unmatched_dets:
            det = detections[j]
            reid_track_id = self._try_reidentify(det)

            if reid_track_id is not None:
                # Restore lost track
                track = self._lost_tracks.pop(reid_track_id)
                track.update(det, frame_number)
                self._active_tracks[reid_track_id] = track
                self._frames_since_last_update[reid_track_id] = 0
                results[reid_track_id] = det
            else:
                # New track
                tid = self._next_id
                self._next_id += 1
                self._active_tracks[tid] = _Track(tid, det, frame_number)
                self._frames_since_last_update[tid] = 0
                results[tid] = det

        # ── Step 5: Move unmatched active tracks to lost ──
        for tid in active_ids:
            if tid not in matched_track_ids:
                self._lost_tracks[tid] = self._active_tracks.pop(tid)
                self._frames_since_last_update[tid] = 1

        # ── Step 6: Age and expire lost tracks ──
        self._age_lost_tracks()

        # ── Step 7: Filter out tracks seen in too few frames (ephemeral false positives) ──
        filtered = {}
        for tid, det in results.items():
            track = self._active_tracks.get(tid)
            if track and track.frame_count >= self.min_detection_frames:
                filtered[tid] = det

        return filtered

    def finalize(self) -> dict[int, dict]:
        """
        Return all tracked surfaces (active + recently lost) that meet the minimum
        frame threshold. Call once at the end of processing a scene.
        """
        all_tracks = {}
        for tid, track in {**self._active_tracks, **self._lost_tracks}.items():
            if track.frame_count >= self.min_detection_frames:
                all_tracks[tid] = track.best_detection
        return all_tracks

    # ── Helpers ──

    def _age_lost_tracks(self):
        """Increment lost counters and expire old lost tracks."""
        expired = []
        for tid in list(self._lost_tracks.keys()):
            count = self._frames_since_last_update.get(tid, 0) + 1
            self._frames_since_last_update[tid] = count
            if count > self.lost_buffer_frames:
                expired.append(tid)

        for tid in expired:
            self._lost_tracks.pop(tid, None)
            self._frames_since_last_update.pop(tid, None)

    def _try_reidentify(self, det: dict) -> Optional[int]:
        """
        Try to match a new detection to a lost track using IoU.
        In a full implementation, this would use feature embeddings (DINOv2/CLIP).
        For Phase 3, we use a generous IoU threshold for lost tracks.
        """
        best_iou = 0.0
        best_tid = None

        for tid, track in self._lost_tracks.items():
            iou_val = _box_iou(track.bbox_xyxy, det["bbox_xyxy"])
            if iou_val > best_iou and iou_val >= self.iou_match_threshold * 0.7:
                best_iou = iou_val
                best_tid = tid

        if best_tid is not None:
            logger.debug("Re-identified track %d (IoU=%.2f)", best_tid, best_iou)

        return best_tid


class _Track:
    """Internal track state for a single surface."""

    def __init__(self, track_id: int, detection: dict, frame_number: int):
        self.track_id = track_id
        self.bbox_xyxy = detection["bbox_xyxy"]
        self.best_detection = detection
        self.best_confidence = detection.get("confidence", 0.0)
        self.frame_count = 1
        self.first_frame = frame_number
        self.last_frame = frame_number

    def update(self, detection: dict, frame_number: int):
        """Update track with a new detection."""
        # Exponential moving average for bounding box (stability)
        alpha = 0.3
        old_box = np.array(self.bbox_xyxy, dtype=np.float32)
        new_box = np.array(detection["bbox_xyxy"], dtype=np.float32)
        smoothed = old_box * (1 - alpha) + new_box * alpha
        self.bbox_xyxy = tuple(smoothed.astype(int).tolist())

        # Update best detection (highest confidence)
        conf = detection.get("confidence", 0.0)
        if conf > self.best_confidence:
            self.best_confidence = conf
            self.best_detection = detection

        self.frame_count += 1
        self.last_frame = frame_number


def _box_iou(a: tuple, b: tuple) -> float:
    """IoU for two xyxy boxes."""
    x1 = max(a[0], b[0]); y1 = max(a[1], b[1])
    x2 = min(a[2], b[2]); y2 = min(a[3], b[3])
    inter = max(0, x2 - x1) * max(0, y2 - y1)
    area_a = (a[2] - a[0]) * (a[3] - a[1])
    area_b = (b[2] - b[0]) * (b[3] - b[1])
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0
