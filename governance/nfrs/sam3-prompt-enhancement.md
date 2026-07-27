# NFRs: SAM3 Prompt Enhancement + Paid API Compositing

**Feature:** SAM3 Prompt Enhancement — Gemini-Generated Segmentation Descriptions
**Date:** 2026-07-27
**Status:** Implementing

---

## Performance

- Gemini prompt generation: `sam3_prompt` adds <50 tokens to response; no measurable latency increase
- SAM3 payload: 5 point_prompts + 1 box_prompt + text ~800 bytes; no impact on request time
- SAM3 tracking latency: unchanged — video processing dominates (~2-3 min for 12s clip)
- Luma mask compositing: single ffmpeg pass (3 inputs → 1 output), ~30s for 12s/25fps clip
- No per-frame extraction needed (unlike Phase 3 fallback which extracts PNGs)

## Security

- `sam3_prompt` is AI-generated text describing visual appearance; no PII or sensitive data
- No new network boundaries — Gemini ↔ SAM3 flow unchanged
- SAM3 prompt not used in any authorization decision

## Scalability

- `Sam3Prompt` column: `nvarchar(500)` — negligible storage impact
- One prompt per surface (max ~50 per video) — <25KB total
- SAM3 API call count unchanged (1 per surface per render)
- Luma mask compositing: O(1) ffmpeg calls regardless of video length

## Data Integrity

- `Sam3Prompt` is nullable — backward compatible with existing surfaces
- No data migration needed for existing rows (null is valid)
- Gemini prompt template change is backward compatible — old responses without `sam3_prompt` parse as null
- EF Core migration is additive only (new column, no data loss)

## Error Handling

- Gemini returns null/missing `sam3_prompt` → field stays null, no error
- Gemini prompt too long (>500 chars) → truncated at DB level via MaxLength
- SAM3 receives null prompt → `prompt` field excluded from JSON payload (C# null → absent in JSON)
- SAM3 422/error with new payload → logged via event log, falls back to empty tracking result
- ffmpeg luma mask failure → logged, render marked Failed

## Observability

- SAM3 payload logged at TRACKING_START: point count, prompt presence, threshold
- Event log: `USING_SAM3_VIDEO` → `RENDER_COMPLETED` with method "luma-mask compositing"
- ffmpeg stderr captured for compositing failures

## Backward Compatibility

- Existing surfaces without `Sam3Prompt` continue to work (null → prompt excluded)
- Non-Gemini detection engines unaffected (field stays null)
- Old render pipeline (Phase 3 per-frame compositing) preserved as fallback when SAM3 video unavailable
