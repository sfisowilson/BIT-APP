"""
BIT Surface Detection Service (FastAPI)

Two detection engines available:
  v1 (YOLO + ByteTrack):    COCO-pretrained object detection with tracking
  v2 (Grounding DINO+SAM+Depth+CLIP):  Open-vocabulary zero-shot detection
                                        with precise segmentation, real depth,
                                        and brand-safety classification.

Usage:
    uvicorn main:app --host 0.0.0.0 --port 8001
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import Optional
import logging

from detector import YoloSurfaceDetector

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("detection-service")

app = FastAPI(title="BIT Surface Detection", version="2.0.0")

# ── Global detectors (lazy-loaded) ──
detector: Optional[YoloSurfaceDetector] = None       # v1 YOLO
engine_v2 = None                                      # v2 pipeline (loaded on first v2 request)
v2_config = None                                      # v2 config (set on first v2 request)

# ── Request/Response models ──

class DetectionRequest(BaseModel):
    content_id: str
    scene_index: int
    start_frame: int
    end_frame: int
    video_path: str = ""              # path or URL to the video file
    model_size: str = "large"          # nano | small | medium | large | xlarge — large is production default
    confidence_threshold: float = 0.35
    iou_threshold: float = 0.45
    tracked: bool = True              # enable ByteTrack for frame-to-frame ID consistency
    frame_skip: int = 1               # process every frame by default (1=100% of frames)

class BatchDetectionRequest(BaseModel):
    content_id: str
    video_path: str = ""
    scenes: list[dict]                # [{"scene_index": 1, "start_frame": 0, "end_frame": 100}, ...]
    model_size: str = "large"
    confidence_threshold: float = 0.35
    iou_threshold: float = 0.45
    tracked: bool = True
    frame_skip: int = 1

class SurfaceResult(BaseModel):
    surface_type: str                 # e.g. "TV Screen", "Billboard", "Digital Signage"
    boundary_coordinates: list[dict]  # [{x, y}, {x, y}, {x, y}, {x, y}]
    estimated_depth: float            # metres (heuristic from bounding box size)
    orientation_vector: dict          # {yaw, pitch, roll} — estimated from quad shape
    confidence_score: float           # 0-1 YOLO confidence
    viability_score: float            # 0-1 composite: confidence × size × aspect-ratio fit
    exclusion_reason: Optional[str] = None
    track_id: Optional[int] = None    # ByteTrack ID, consistent across frames

class DetectionResponse(BaseModel):
    content_id: str
    scene_index: int
    surfaces: list[SurfaceResult]
    frames_processed: int
    model_used: str
    processing_time_ms: float


# ── Health check ──
@app.get("/health")
def health():
    return {"status": "ok", "model_loaded": detector is not None and detector.model is not None}


# ── Main detection endpoint ──
@app.post("/detect", response_model=DetectionResponse)
def detect_surfaces(req: DetectionRequest):
    global detector

    # Auto-switch model if the configured model_size changed — no restart needed
    if detector is not None and detector.model_size != req.model_size:
        logger.info("Model size changed from %s to %s — reloading", detector.model_size, req.model_size)
        detector.release()
        detector = None

    if detector is None:
        detector = YoloSurfaceDetector(
            model_size=req.model_size,
            conf_threshold=req.confidence_threshold,
            iou_threshold=req.iou_threshold,
        )

    # Thresholds are per-request (no restart needed)
    detector.conf_threshold = req.confidence_threshold
    detector.iou_threshold = req.iou_threshold

    try:
        result = detector.detect(
            video_path=req.video_path,
            content_id=req.content_id,
            scene_index=req.scene_index,
            start_frame=req.start_frame,
            end_frame=req.end_frame,
            tracked=req.tracked,
            frame_skip=req.frame_skip,
        )
        return result
    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        logger.exception("Detection failed")
        raise HTTPException(status_code=500, detail=str(e))


# ── Batch detection endpoint (opens video once, processes all scenes) ──
@app.post("/detect-batch", response_model=list[DetectionResponse])
def detect_surfaces_batch(req: BatchDetectionRequest):
    global detector

    # Auto-switch model if the configured model_size changed
    if detector is not None and detector.model_size != req.model_size:
        logger.info("Model size changed from %s to %s — reloading", detector.model_size, req.model_size)
        detector.release()
        detector = None

    if detector is None:
        detector = YoloSurfaceDetector(
            model_size=req.model_size,
            conf_threshold=req.confidence_threshold,
            iou_threshold=req.iou_threshold,
        )

    detector.conf_threshold = req.confidence_threshold
    detector.iou_threshold = req.iou_threshold

    try:
        results = detector.detect_batch(
            video_path=req.video_path,
            content_id=req.content_id,
            scenes=req.scenes,
            tracked=req.tracked,
            frame_skip=req.frame_skip,
        )
        return results
    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        logger.exception("Batch detection failed")
        raise HTTPException(status_code=500, detail=str(e))


# ── v2 Request Models ──

class DetectionRequestV2(BaseModel):
    content_id: str
    scene_index: int
    start_frame: int
    end_frame: int
    video_path: str = ""
    gd_model_variant: str = "base"        # base | tiny
    gd_box_threshold: float = 0.25
    gd_text_threshold: float = 0.20
    enable_sam: bool = True
    enable_depth: bool = True
    enable_brand_safety: bool = True
    # ── Phase 3 options ──
    enable_tracking: bool = True
    adaptive_frame_skip: bool = True
    detection_interval: int = 10          # run GD every N frames
    flow_motion_threshold: float = 2.5
    track_min_detection_frames: int = 3

class BatchDetectionRequestV2(BaseModel):
    content_id: str
    video_path: str = ""
    scenes: list[dict]
    gd_model_variant: str = "base"
    gd_box_threshold: float = 0.25
    gd_text_threshold: float = 0.20
    enable_sam: bool = True
    enable_depth: bool = True
    enable_brand_safety: bool = True
    # ── Phase 3 options ──
    enable_tracking: bool = True
    adaptive_frame_skip: bool = True
    detection_interval: int = 10
    flow_motion_threshold: float = 2.5
    track_min_detection_frames: int = 3

class DetectionResponseV2(BaseModel):
    content_id: str
    scene_index: int
    surfaces: list[SurfaceResult]
    frames_processed: int
    model_used: str
    processing_time_ms: float
    engine_version: str = "v2"
    error: Optional[str] = None


# ── v2 Health check (includes both engines) ──
@app.get("/health")
def health():
    v1_loaded = detector is not None and detector.model is not None
    v2_loaded = engine_v2 is not None
    return {
        "status": "ok",
        "v1_loaded": v1_loaded,
        "v2_loaded": v2_loaded,
        "v1_model": detector.MODEL_MAP.get(detector.model_size, "unknown") if v1_loaded else None,
    }


# ── v2 Detection endpoint ──
@app.post("/detect-v2", response_model=DetectionResponseV2)
def detect_surfaces_v2(req: DetectionRequestV2):
    global engine_v2, v2_config

    try:
        from engine_v2 import SurfaceDetectionEngineV2, EngineV2Config

        # Create or reconfigure engine
        new_config = EngineV2Config(
            gd_model_variant=req.gd_model_variant,
            gd_box_threshold=req.gd_box_threshold,
            gd_text_threshold=req.gd_text_threshold,
            enable_sam=req.enable_sam,
            enable_depth=req.enable_depth,
            enable_brand_safety=req.enable_brand_safety,
            # Phase 3
            enable_tracking=req.enable_tracking,
            adaptive_frame_skip=req.adaptive_frame_skip,
            detection_interval=req.detection_interval,
            flow_motion_threshold=req.flow_motion_threshold,
            track_min_detection_frames=req.track_min_detection_frames,
        )

        if engine_v2 is None or v2_config != new_config:
            if engine_v2:
                engine_v2.release()
            engine_v2 = SurfaceDetectionEngineV2(config=new_config)
            engine_v2.load()
            v2_config = new_config

        result = engine_v2.detect(
            video_path=req.video_path,
            content_id=req.content_id,
            scene_index=req.scene_index,
            start_frame=req.start_frame,
            end_frame=req.end_frame,
        )
        return result

    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except ImportError as e:
        raise HTTPException(
            status_code=501,
            detail=f"v2 engine dependencies not installed: {e}. "
                   "Run: pip install transformers segment-anything torch pillow",
        )
    except Exception as e:
        logger.exception("v2 detection failed")
        raise HTTPException(status_code=500, detail=str(e))


# ── v2 Batch detection endpoint ──
@app.post("/detect-batch-v2", response_model=list[DetectionResponseV2])
def detect_surfaces_batch_v2(req: BatchDetectionRequestV2):
    global engine_v2, v2_config

    try:
        from engine_v2 import SurfaceDetectionEngineV2, EngineV2Config

        new_config = EngineV2Config(
            gd_model_variant=req.gd_model_variant,
            gd_box_threshold=req.gd_box_threshold,
            gd_text_threshold=req.gd_text_threshold,
            enable_sam=req.enable_sam,
            enable_depth=req.enable_depth,
            enable_brand_safety=req.enable_brand_safety,
            # Phase 3
            enable_tracking=req.enable_tracking,
            adaptive_frame_skip=req.adaptive_frame_skip,
            detection_interval=req.detection_interval,
            flow_motion_threshold=req.flow_motion_threshold,
            track_min_detection_frames=req.track_min_detection_frames,
        )

        if engine_v2 is None or v2_config != new_config:
            if engine_v2:
                engine_v2.release()
            engine_v2 = SurfaceDetectionEngineV2(config=new_config)
            engine_v2.load()
            v2_config = new_config

        results = engine_v2.detect_batch(
            video_path=req.video_path,
            content_id=req.content_id,
            scenes=req.scenes,
        )
        return results

    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except ImportError as e:
        raise HTTPException(
            status_code=501,
            detail=f"v2 engine dependencies not installed: {e}. "
                   "Run: pip install transformers segment-anything torch pillow",
        )
    except Exception as e:
        logger.exception("v2 batch detection failed")
        raise HTTPException(status_code=500, detail=str(e))


# ── Shutdown ──
@app.on_event("shutdown")
def shutdown():
    global detector, engine_v2
    if detector:
        detector.release()
        detector = None
    if engine_v2:
        engine_v2.release()
        engine_v2 = None
