# Non-Functional Requirements: Full Video Pipeline

**Version:** 1.0  
**Date:** 2026-07-24  

## Performance Requirements
*   **Scene Detection:** Video scene cut detection must complete in under 5 seconds per minute of 1080p source video.
*   **Engine Resolution:** AI Engine resolution must execute asynchronously without blocking system threads or DI container startup.
*   **Video Stitching (Rejoining):** Scene rejoining using FFmpeg stream copy/concat must process at a minimum of 5x realtime speed.

## Content & Duration Integrity (MReq 1)
*   The final rejoined output video MUST match the exact duration, framerate, and audio track of the original source video down to the frame.

## Security & Approval Governance (MReq 11)
*   Render operations MUST enforce human approval (`SurfaceItem.Status == "Approved"`).
*   Download endpoints MUST require JWT authentication and validate user role authorizations.

## Accuracy & Precision
*   Invoice calculations must accurately reflect exposure seconds $\times$ viability multiplier + render processing costs with sub-cent precision.
