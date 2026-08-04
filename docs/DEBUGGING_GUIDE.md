# BIT Platform — Debugging Guide

How to find out what actually happened when something goes wrong, especially "I placed an asset and nothing showed up in the video." This is a practical, log-first guide — it assumes you have Admin access to the running app.

---

## 1. Where the logs actually live

There is **one central log table**, `EventLogs` (backed by the `EventLog` model), written to by every backend service via `IEventLogService.LogEventAsync(module, eventCode, severity, description)`. Everything else (render status, error messages on cards) is a thin view over this same data or over the `RenderItems`/`ContentItems` rows themselves.

| Source | What it shows | How to reach it |
|---|---|---|
| **Telemetry tab** (sidebar → Platform → Telemetry) | Paginated, filterable view of `EventLogs` | In-app UI, no setup needed |
| **`GET /api/logs`** | Same data, raw JSON, full filter set | Authenticated `fetch` (see §3) |
| **`GET /api/usage/csv`** | Full `EventLogs` export (up to 10,000 rows) as CSV | See §3 — the Telemetry tab's "Download CSV" button is currently broken (a plain `<a href>` to an `[Authorize]`-protected route with no auth header attached, so it 401s); fetch it with a token instead |
| **Render's own `lastErrorMessage`** | The final exception message for a `Failed` render | Renders tab card / Placements screen "Render Status" card — **Admin-only, collapsed by default** |
| **Backend console (`dotnet run` stdout)** | Raw `ILogger` output — includes stack traces the EventLog description doesn't | Terminal running the API process only. Not persisted anywhere else. |
| **Browser DevTools → Console/Network** | Frontend fetch errors, SignalR connection state, actual HTTP response bodies | F12 in the browser |

**Start with the Telemetry tab or `/api/logs` for almost everything.** The backend console is only useful if you have the terminal open live and the failure just happened — it isn't persisted or queryable after the fact.

### A gap to know about

The Telemetry tab's Severity filter dropdown only offers **Info / Warning / Major / Critical** — but the actual code only ever logs `"Info"`, `"Warning"`, or `"Error"` for these events (confirmed by grepping every `LogEventAsync` call in `dotnet-api/Services/`). **"Error" isn't a selectable option in that dropdown**, so you can't isolate errors by severity in the UI. Use the search box (matches `EventCode`/`Description`) instead, or query the API directly with `severity=Error`, or grep the CSV export.

---

## 2. The three-tier system, in one line each

```
React frontend (Vite) → .NET API (ASP.NET Core, Hangfire jobs) → external AI APIs (Gemini, fal.ai SAM3, fal.ai Pikaswaps, fal.ai Kling O1)
```

Placing an asset never happens synchronously — it always goes through a Hangfire background job (`RenderJobService` / `FinalAssemblyJobService`), which is why the log trail is the only way to see what happened after the fact. The job writes progress via SignalR (what you see live as a progress bar) **and** writes durable `EventLog` rows at each phase (what you can look up later).

---

## 3. How to query logs directly

### Via the API (from anywhere with a valid session)

```
GET /api/logs?pageSize=100&severity=Error&search=<text>&module=<module>&dateFrom=<ISO8601>
```

`LogFilterParams`: `page`, `pageSize` (max 100), `sortBy`, `sortDescending`, `severity`, `module`, `dateFrom`, `dateTo`, `search`.

### Quickest way: from the browser console, while logged into the app

```js
(async () => {
  const token = localStorage.getItem('bit_token');
  const res = await fetch('/api/logs?pageSize=100&severity=Error', {
    headers: { Authorization: `Bearer ${token}` },
  });
  const data = await res.json();
  console.table(data.items.map(i => ({ t: i.timestamp, code: i.eventCode, module: i.module, desc: i.description.slice(0, 120) })));
})();
```

Narrow by time window (e.g. right after you clicked "Submit") by adding `&dateFrom=2026-08-02T10:00:00Z`, or by module (`&module=Pikaswaps`), or free-text search a specific render/content/surface ID (`&search=r-02e7db`).

### CSV export (the in-app button doesn't currently work)

The Telemetry tab's "Download CSV" is a plain `<a href="/api/usage/csv">` — but that route is `[Authorize]`-protected and a plain link click sends no auth header, so it 401s. Fetch it with the token and trigger the download manually instead:

```js
(async () => {
  const token = localStorage.getItem('bit_token');
  const res = await fetch('/api/usage/csv', { headers: { Authorization: `Bearer ${token}` } });
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = 'event-logs.csv'; a.click();
  URL.revokeObjectURL(url);
})();
```

### For a specific render/content/surface

There's no dedicated "logs for this render" endpoint — search by ID instead. Every event description for the render/tracking/compositing pipeline includes the relevant `RenderItem.Id`, `ContentItem.Id`, or `SurfaceItem.Id`, so `search=<that id>` reliably finds its whole trail in chronological order (`GET /api/logs` sorts newest-first by default — read bottom-to-top, or pass `sortDescending=false`).

---

## 4. Event code reference

### Module: `RenderEngine` (the render lifecycle itself)

| Event code | Meaning |
|---|---|
| `INTERACTIVE_RENDER_QUEUED` | A click/quad-placed or AI-detected surface render was dispatched |
| `PROMPT_PREVIEW_QUEUED` / `_COMPLETE` / `_FAILED` | "AI Placement Assistant → Generate New" preview generation (Kling O1) |
| `PROMPT_SPLICE_QUEUED` / `_COMPLETE` / `_FAILED` | Approved PromptEdit preview being spliced into the full source video |
| `PROMPT_REJECTED` | User declined a PromptEdit preview |
| `GENERATIVE_RENDER_COMPLETE` / `_FAILED` | Interactive/Pikaswaps render job finished (check `driftIoU` in the description — see §6) |
| `PLANAR_RENDER_COMPLETE` / `_FAILED` | Planar-warp (signage) render job finished |
| `TRACKING_LOCK_LOST` | SAM3 lost the surface in its seed shot — logged as a **warning**, not fatal (Pikaswaps doesn't need the tracked mask, only text) |
| `GEMINI_PROMPT_COMPLETE` | Gemini generated the `modify_region`/`prompt` text handed to Pikaswaps |
| `RENDER_RETRY_QUEUED` | User clicked Retry |
| `RENDER_DELETED` | Render row + output files removed |
| `RENDER_QUEUED_FOR_FINAL` / `RENDER_UNQUEUED_FOR_FINAL` | A render was marked/unmarked as the chosen one for its scene in the combined final video |
| `FINAL_ASSEMBLY_QUEUED` / `_COMPLETE` / `_FAILED` / `_FALLBACK` | Combined-video assembly job (`_FALLBACK` = a specific scene's queued render wasn't usable at assembly time, so original footage was used for that scene instead) |

### Module: `SAM3` (shot-aware surface tracking, feeds both compositing paths)

| Event code | Meaning |
|---|---|
| `RLE_SEGMENT_START` / `_COMPLETE` | A tracking call for one shot started/finished. **`_COMPLETE`'s description says "Segmented N frames, M total object-masks" — if M is 0, that shot/box found nothing.** |
| `RLE_REQUEST_PAYLOAD` | The exact JSON sent to fal.ai (`box_prompts`/`point_prompts`/`prompt` text, `detection_threshold`) — the single most useful line for diagnosing *why* nothing was found |
| `RLE_HTTP_ERROR` | fal.ai rejected the submit call outright (check the embedded HTTP status + body) |
| `RLE_NO_FRAMES` | Call succeeded but returned nothing at all |
| `RLE_POLL_ERROR` / `RLE_RESULT_ERROR` | Queue polling/result-fetch problem — HTTP status is in the description |

### Module: `Pikaswaps` (video compositing)

| Event code | Meaning |
|---|---|
| `COMPOSITE_START` | Submitting one shot/chunk for compositing — description has `video=`, `image=`, `modify_region=`, `prompt=` |
| `SUBMITTED` | fal.ai accepted the job — description has the raw submit response (`request_id`, `status_url`, `response_url`) |
| `POLLING_START` / `POLLING_COMPLETE` | Status polling lifecycle |
| `POLL_ERROR` | Non-200 on a status check. **If this repeats forever without ever reaching `POLLING_COMPLETE`, the poll is hitting the wrong URL or the request truly never finishes — see §6.** |
| `RESULT_ERROR` | Fetching the final result failed — the embedded HTTP status/body is the real reason (`413` = file too large, `500` with `downstream_service_error` = fal.ai couldn't fetch one of our files, etc.) |
| `NO_VIDEO` | End state: compositing produced nothing for this shot. The render still finishes (a shot with `NO_VIDEO` falls back to unmodified source footage for that shot only), so a `Finished`/`NeedsReview` render can still have **zero actual branding** if every shot hit this. |

### Module: `KlingPromptEdit` (PromptEdit / "Generate New" mode)

Same event-code shape as Pikaswaps (`SUBMITTED`, `POLLING_START/COMPLETE`, `POLL_ERROR`, `RESULT_ERROR`, `NO_VIDEO`) — same debugging approach applies.

---

## 5. `RenderStatus` meanings (don't assume "Finished" = "asset visible")

| Status | What it actually means |
|---|---|
| `Queued` | Row created, Hangfire job not yet picked up |
| `Processing` | Job running — check `Progress` (0–100) for where |
| `PreviewReady` | PromptEdit only — a preview clip exists, awaiting approval (nothing final yet) |
| `Finished` | Job completed and the drift-check (Interactive path) passed, or PromptEdit splice succeeded |
| **`NeedsReview`** | **Job completed, but either tracking only had partial shot coverage, or the drift-check IoU was below 0.85 — the composite may be off-target or missing on some shots. Not a failure, but worth actually watching the output before trusting it.** |
| `Failed` | Job threw — check `lastErrorMessage` (Admin-only) and the event log trail |
| `Rejected` | PromptEdit preview declined by the user |

**The trap:** even `Finished` doesn't guarantee the asset is visible. If every shot in a render hit Pikaswaps `NO_VIDEO` (e.g. because an external dependency was down — see §6), the job still completes successfully and reports `Finished`/`NeedsReview` because the code deliberately falls back to unmodified source footage per-shot rather than failing the whole render. The output video plays fine; it just doesn't have the brand asset in it. **Always spot-check the actual output** (the inline player on the Placements screen's Render Status card, or Download) rather than trusting status alone when something feels off.

---

## 6. Walkthrough: "I placed an asset and it's not showing up"

Work through these in order — each step either finds the answer or rules out a category.

### Step 1 — Find the render and its actual status

Placements screen → select the scene/surface → **Render Status** card, or Renders tab (each card now shows content/scene/surface/asset context and a "View in Placements" link back). Note the exact `renderId` (`r-xxxxxxxx`) and status.

- **`Failed`** → read `lastErrorMessage` on the card (Admin-only). Usually enough on its own.
- **`Finished` / `NeedsReview` but the output looks wrong or unbranded** → go to Step 2. This is the case that *looks* like success but isn't.
- **Stuck on `Processing` for a long time** → go to Step 3.

### Step 2 — Pull the full event trail for that render

```js
(async () => {
  const token = localStorage.getItem('bit_token');
  const res = await fetch(`/api/logs?pageSize=100&search=r-02e7db&sortDescending=false`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  const data = await res.json();
  data.items.forEach(i => console.log(i.timestamp, i.eventCode, i.severity, '—', i.description.slice(0, 300)));
})();
```

(swap `r-02e7db` for your render's ID prefix — a partial match against the description is fine)

Read it top-to-bottom in time order. What you're looking for:

- **A `TRACKING_LOCK_LOST` warning, followed by shots still completing successfully.** This is expected and non-fatal — Pikaswaps doesn't need the tracked mask. Don't chase this as the cause.
- **Every `RLE_SEGMENT_COMPLETE` says "0 total object-masks."** SAM3 genuinely couldn't find the surface anywhere. Check the paired `RLE_REQUEST_PAYLOAD` — a box prompt with no accompanying text, or a box covering most of the frame around a low-texture surface (a plain wall, a flat table), is a known failure pattern: SAM3 can find the same surface fine via text description but fails on box-only prompts for low-texture regions.
- **`COMPOSITE_START` for every shot, but every one followed by `NO_VIDEO`.** The composite is being attempted but never lands. Look at the `RESULT_ERROR` immediately before each `NO_VIDEO` — the embedded HTTP status tells you why:
  - **`413`** → the input video/image file exceeds fal.ai's 8MB limit for that endpoint.
  - **`500` with `downstream_service_error` mentioning a fetch failure on our own URL** (`sam3.mizo.co.za` or similar) → **fal.ai couldn't reach our server to download the file.** This is almost always the cloudflared tunnel being down, not a code bug — see §7.
  - **`405`** on every `POLL_ERROR`, never reaching `POLLING_COMPLETE` → the status-poll URL is wrong (should already be fixed, but if you see this, the code went back to a hardcoded queue URL instead of using the `status_url` fal.ai actually returned in the `SUBMITTED` response).
- **No `COMPOSITE_START` at all for some shots.** Tracking/prompt generation itself failed before compositing was even attempted — look earlier in the trail (`SAM3`/`GEMINI_PROMPT_COMPLETE` events) for what stopped it.

### Step 3 — Stuck `Processing`

Check whether the API process is even still running (`RenderJobService`/`FinalAssemblyJobService` are Hangfire background jobs — if the API restarted mid-job, the job just stops silently, no `Failed` transition, no final log entry). If the backend was restarted (common during active development), retry the render.

### Step 4 — Check the drift-check score for a `NeedsReview` result

The `GENERATIVE_RENDER_COMPLETE` event's description includes `driftIoU=`. Below `0.85` triggers `NeedsReview`. A very low score (well under 0.5) usually means the composite landed on the wrong region entirely, not just imprecisely — worth a visual check before deciding whether to keep or retry it.

---

## 7. Common root causes that aren't bugs in the placement logic itself

These show up as render failures but are actually external/environmental:

| Symptom | Real cause | How to confirm |
|---|---|---|
| Every Pikaswaps/SAM3 call fails with `500 downstream_service_error` fetching our own file, or a direct `curl` to the tunnel hostname returns `530` | The **cloudflared tunnel** that lets fal.ai reach the local dev server is down | `curl -s -o /dev/null -w "%{http_code}\n" https://<tunnel-host>/` — `530` means the tunnel client isn't connected to Cloudflare's edge, even if `cloudflared.exe` is technically still running |
| `NO_API_KEY` event | `falai_api_key` or `gemini_api_key` platform setting isn't configured | Admin Console → Platform Settings |
| Every call for one engine type fails identically across unrelated renders | Wrong `engine_detection` / `engine_compositing` / `engine_tracking` platform setting, or a stale/rotated API key | Admin Console → Platform Settings; cross-check against `docs/PIKASWAPS_API_REFERENCE.md` / `docs/SAM3_VIDEO_RLE_API_REFERENCE.md` for the current expected request shape |
| A render that predates a backend fix still exhibits the old bug | The fix only affects *new* renders — existing `RenderItem` rows don't get retroactively repaired | Retry the render (creates a fresh attempt using current code) rather than assuming the fix didn't work |

---

## 8. Quick reference

```
# All errors in the last hour
GET /api/logs?severity=Error&dateFrom=<1 hour ago ISO8601>&pageSize=100

# Everything for one render/content/surface
GET /api/logs?search=<id>&sortDescending=false&pageSize=100

# Full offline export for grepping
GET /api/usage/csv
```

- `RenderStatus: Finished`/`NeedsReview` ≠ "the asset is definitely visible" — every shot can silently fall back to unmodified footage.
- `TRACKING_LOCK_LOST` alone is not the cause of a failed render — Pikaswaps doesn't consume the tracked mask.
- `413` / `downstream_service_error` / repeated `POLL_ERROR` in the Pikaswaps or SAM3 trail almost always point at file size, tunnel connectivity, or URL construction — not the placement logic.
- When in doubt, pull the full event trail for the render (§3/§6) before assuming anything about the cause.
