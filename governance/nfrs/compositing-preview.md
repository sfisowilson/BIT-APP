# NFRs: Compositing Preview

**Feature:** Compositing Preview — Asset Superimposed on Video Scene
**Date:** 2026-07-23
**Status:** Planned

---

## Performance

- Frame extraction via FFmpeg: must complete within 30 seconds
- Asset overlay via FFmpeg: must complete within 30 seconds
- Total compositing request: < 60 seconds (existing HttpClient timeout)
- Base64 encoding: handled in-memory, no disk I/O beyond FFmpeg temp files
- FFmpeg subprocess cleanup: temp files deleted after processing

## Security

- File paths resolved from storage keys — validated against Uploads directory (existing pattern)
- No shell injection: FFmpeg arguments use sanitized file paths (quoted)
- Directory traversal: resolved paths checked with StartsWith(Uploads) guard
- Auth: existing [Authorize] on CompositingController

## Scalability

- FFmpeg subprocess per request (sequential, no pooling required for previews)
- Temp files cleaned up in finally block
- In-memory base64 conversion for small asset images (< 10 MB typical)

## Error Handling

- Asset file not found → ArgumentException with asset ID
- Video file not found → InvalidOperationException with content ID
- FFmpeg process failure → InvalidOperationException with stderr output
- Invalid boundary coordinates → logged, default to center placement
- FFmpeg timeout → process killed, TimeoutException thrown

## Observability

- All compositing requests logged at Information level (contentId, assetId, surfaceId)
- FFmpeg stderr captured for debugging on failure
- Processing time tracked via Stopwatch and returned in CompositedFrame
- Engine used reported in response (OpenCvCompositor vs BasicCompositor)

## Backward Compatibility

- BasicCompositingService unchanged — still returns raw asset
- CompositedFrame response shape unchanged (ImageBase64, ContentType, EngineUsed, ProcessingMs)
- CompositingRequest unchanged
- Frontend unchanged — receives same response shape, displays base64 image
