# NFRs: Scene Detection Pipeline Fix

**Feature:** Scene Detection Pipeline — Real AI Engine Integration
**Date:** 2026-07-23
**Status:** Implemented

---

## Performance

- FFmpeg scene detection must complete within 120 seconds (existing timeout)
- YOLO detection per scene must complete within 5 minutes (existing HttpClient timeout)
- Thumbnail generation is non-blocking — a single thumbnail failure must not delay or fail the pipeline
- Hangfire job retries: 2 attempts max (existing AutomaticRetry attribute)
- Progress reporting: at least 5 progress updates (5%, 15%, 40%, 50%, 87%, 93%, 100%)

## Security

- The file-serving endpoint must prevent directory traversal (Path.GetFullPath + StartsWith check)
- Thumbnail files are served from the same Uploads directory with the same access controls
- No elevation of privilege — auth is handled by existing [Authorize] / [AllowAnonymous] attributes

## Scalability

- Thumbnail generation uses FFmpeg subprocess — one per surface, sequential
- Thumbnail directory created once per pipeline run (Directory.CreateDirectory is idempotent)
- No concurrent thumbnail generation (sequential foreach loop)

## Error Handling

- YOLO service unreachable → Hangfire job fails with descriptive InvalidOperationException
- YOLO service HTTP error → Hangfire job fails with InvalidOperationException
- YOLO service timeout → Hangfire job fails with TimeoutException
- FFmpeg unavailable for scene detection → falls back to single-scene (whole video)
- FFmpeg unavailable for thumbnails → skips thumbnail, surface saved without image
- Pipeline transition validation → rejects invalid transitions with descriptive error
- Self-transition guard → skips transition when already in target state

## Observability

- All YOLO requests logged at Information level with contentId, sceneIndex, frame range
- Detection completion logged with surface count, frames processed, processing time
- Thumbnail failures logged via Debug.WriteLine (non-critical)
- Hangfire dashboard available at /hangfire for job monitoring
- Detection progress updated in ContentItem.DetectionProgress field

## Backward Compatibility

- Existing surfaces without PlacementImageUrl (null) are handled gracefully by frontend
- File-serving endpoint supports both flat files and subdirectory paths (thumbnails/)
- Pipeline transition rules unchanged — only added guard, no new transitions
