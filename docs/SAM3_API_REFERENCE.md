# SAM 3 Video Tracking — fal.ai API Reference

> **Model:** `fal-ai/sam-3/video`  
> **Sync endpoint:** `https://fal.run/fal-ai/sam-3/video` (blocks until done — not recommended)  
> **Queue endpoint:** `https://queue.fal.run/fal-ai/sam-3/video` (returns `request_id` immediately — **use this**)  
> **Queue status:** `https://queue.fal.run/fal-ai/sam-3/video/requests/{request_id}/status`  
> **Queue result:** `https://queue.fal.run/fal-ai/sam-3/video/requests/{request_id}`  
> **Purpose:** Segment and track objects across video frames using point, box, or text prompts.

---

## 1. Authentication

Set your fal.ai API key as an environment variable:

```bash
export FAL_KEY="YOUR_API_KEY"
```

Or configure it programmatically in your client:

```js
import { fal } from "@fal-ai/client";
fal.config({ credentials: "YOUR_FAL_KEY" });
```

> **⚠️ Never expose your `FAL_KEY` in client-side code.** Use a server-side proxy for browser/mobile apps.

---

## 2. Quick Start (Subscribe)

Blocking call — waits for the result and returns it:

```js
import { fal } from "@fal-ai/client";

const result = await fal.subscribe("fal-ai/sam-3/video", {
  input: { video_url: "https://example.com/video.mp4" },
  logs: true,
  onQueueUpdate: (update) => {
    if (update.status === "IN_PROGRESS") {
      update.logs.map((log) => log.message).forEach(console.log);
    }
  },
});
console.log(result.data);     // { video: { url: "..." } }
console.log(result.requestId); // "764cabcf-b745-4b3e-ae38-1200304cf45b"
```

---

## 3. Queue (Long-Running Requests)

### Submit

```js
const { request_id } = await fal.queue.submit("fal-ai/sam-3/video", {
  input: { video_url: "..." },
  webhookUrl: "https://optional.webhook.url/for/results",
});
```

### Check Status

```js
const status = await fal.queue.status("fal-ai/sam-3/video", {
  requestId: "764cabcf-b745-4b3e-ae38-1200304cf45b",
  logs: true,
});
```

### Fetch Result

```js
const result = await fal.queue.result("fal-ai/sam-3/video", {
  requestId: "764cabcf-b745-4b3e-ae38-1200304cf45b"
});
console.log(result.data);
```

---

## 4. Real-time (WebSockets)

```js
const connection = fal.realtime.connect("fal-ai/sam-3/video", {
  onResult: (result) => console.log(result),
  onError: (error) => console.error(error),
  tokenProvider: async (app) => {
    const response = await fetch("/api/fal/realtime-token", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ app }),
    });
    return response.text();
  },
  tokenExpirationSeconds: 10,
});

connection.send({ video_url: "..." });
```

---

## 5. Files

- **Data URI (base64):** Pass a base64-encoded file directly.
- **Hosted URL:** Pass a publicly accessible URL.
- **Upload:** Use `fal.storage.upload(file)` for automatic hosting.

```js
const file = new File(["Hello, World!"], "hello.txt", { type: "text/plain" });
const url = await fal.storage.upload(file);
```

---

## 6. Input Schema

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `video_url` | `string` | (required) | URL of the video to segment |
| `prompt` | `string` | `""` | Text prompt. Use commas for multiple objects (e.g. `"person, cloth"`) |
| `point_prompts` | `list<PointPrompt>` | `[]` | Point prompts with frame index |
| `box_prompts` | `list<BoxPrompt>` | `[]` | Box prompts with frame index |
| `apply_mask` | `boolean` | `true` | Overlay the mask on the output video |
| `video_output_type` | `enum` | `"X264 (.mp4)"` | `"X264 (.mp4)"` or `"VP9 (.webm)"` |
| `detection_threshold` | `float` | `0.5` | Confidence threshold (0.0–1.0). Lower = more detections, less precise |

> **⚠️ Constraint:** `point_prompts` and `box_prompts` cannot both be populated on the same frame. Send one as the populated array and the other as `[]`. Our implementation uses `box_prompts` (stronger spatial cue) with `point_prompts: []`.

### Example Input (box prompt)

```json
{
  "video_url": "https://example.com/video.mp4",
  "prompt": "person",
  "point_prompts": [
    { "frame_index": 278, "x": 935, "y": 385, "label": 1 }
  ],
  "box_prompts": [],
  "apply_mask": true,
  "video_output_type": "X264 (.mp4)",
  "detection_threshold": 0.5
}
```

---

## 7. Output Schema

| Field | Type | Description |
|-------|------|-------------|
| `video` | `File` | The segmented video (MP4) |
| `boundingbox_frames_zip` | `File` | ZIP of per-frame bounding box overlays (may be `null` on free tier) |

### Example Output

```json
{
  "video": {
    "url": "https://fal.media/files/monkey/5BLHmbX3qxu5cD5gQzTqw_output.mp4",
    "content_type": "video/mp4",
    "file_name": "output.mp4",
    "file_size": 1234567
  }
}
```

---

## 8. Data Types

### PointPrompt

| Field | Type | Description |
|-------|------|-------------|
| `x` | `integer` | X coordinate (pixels) |
| `y` | `integer` | Y coordinate (pixels) |
| `label` | `enum` | `1` = foreground, `0` = background |
| `object_id` | `integer` | Optional. Prompts sharing an ID refine the same object |
| `frame_index` | `integer` | **Required.** 0-based frame index to apply the prompt on |

### BoxPrompt

| Field | Type | Description |
|-------|------|-------------|
| `x_min` | `integer` | Left edge (pixels) |
| `y_min` | `integer` | Top edge (pixels) |
| `x_max` | `integer` | Right edge (pixels) |
| `y_max` | `integer` | Bottom edge (pixels) |
| `object_id` | `integer` | Optional. Boxes sharing an ID refine the same object |
| `frame_index` | `integer` | **Required.** 0-based frame index |

### File

| Field | Type | Description |
|-------|------|-------------|
| `url` | `string` | Download URL |
| `content_type` | `string` | MIME type |
| `file_name` | `string` | Auto-generated file name |
| `file_size` | `integer` | Size in bytes |

### Image (extends File)

| Field | Type | Description |
|-------|------|-------------|
| `width` | `integer` | Width in pixels |
| `height` | `integer` | Height in pixels |

### SAM3VideoObjectFrame

| Field | Type | Description |
|-------|------|-------------|
| `frame_index` | `integer` | 0-based frame index |
| `objects` | `list<SAM3ObjectMask>` | Per-object masks in this frame |

### SAM3ObjectMask

| Field | Type | Description |
|-------|------|-------------|
| `track_id` | `integer` | Stable object/track ID |
| `rle` | `string` | Run-length encoding of the mask (Kaggle/COCO order) |

### MaskMetadata

| Field | Type | Description |
|-------|------|-------------|
| `index` | `integer` | Mask index in model output |
| `score` | `float` | Confidence score |
| `box` | `list<float>` | Bounding box in normalized cxcywh format |

---

## 9. BIT Integration Notes

### Current Payload (from `Sam3TrackingService.cs`)

```csharp
var payload = new
{
    video_url = videoUrl,
    point_prompts = new[]
    {
        new
        {
            frame_index = detectedFrame,
            x = centerX,
            y = centerY,
            label = 1        // foreground
        }
    },
    apply_mask = false,       // We want per-frame tracking data, not a pre-masked video
};
```

### Key Requirements

1. **`frame_index` is mandatory** on every prompt — SAM3 needs to know which frame the point/box belongs to
2. **Video must be publicly accessible** via URL (ngrok tunnel for local dev)
3. **Point coordinates are pixel-space** (not normalized), relative to the video dimensions
4. **`apply_mask: false`** returns tracking data without baking the mask into the video (though free tier may still only return the video)
5. **Free tier limitation:** `boundingbox_frames_zip` may be `null`; fall back to using the segmented video directly

### BIT Pipeline Flow

```
Surface detected (Gemini)
  → Seed boundary at detectedAtFrame
  → SAM3 tracks across scene frame range
  → SAM3 returns segmented video (mask applied to tracked region)
  → ffmpeg overlays brand asset centered on SAM3 video
  → Final render MP4
```
