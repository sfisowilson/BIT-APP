# SAM 3 — Segment Video RLE API Reference

> **Source:** fal.run — SAM 3 Video RLE  
> **Purpose:** Segment any object in a video using text prompts, point prompts, or box prompts. Returns run-length encoded masks per frame with stable object tracking IDs.

---

## 1. Calling the API

### Setup your API Key

Set `FAL_KEY` as an environment variable in your runtime:

```bash
export FAL_KEY="YOUR_API_KEY"
```

### Submit a Request

The client API handles the API submit protocol. It will handle the request status updates and return the result when the request is completed.

```bash
response=$(curl --request POST \
  --url https://queue.fal.run/fal-ai/sam-3/video-rle \
  --header "Authorization: Key $FAL_KEY" \
  --header "Content-Type: application/json" \
  --data '{
     "video_url": "https://v3b.fal.media/files/b/elephant/NQdDxB0Ddfo82SPLbhYDp_bedroom.mp4"
   }')

REQUEST_ID=$(echo "$response" | grep -o '"request_id": *"[^"]*"' | sed 's/"request_id": *//; s/"//g')
```

### Request Status

The command above will not return the final result, but the process status with its `request_id`. Use the **Queue API** commands specified below to check the status and get the final result.

### Real-time via WebSockets

This model has a real-time mode via WebSockets, supported via the `fal.realtime` client.

---

## 2. Authentication

The API uses an API Key for authentication. It is recommended you set the `FAL_KEY` environment variable in your runtime when possible.

### API Key

**Protect your API Key:** When running code on the client-side (e.g. in a browser, mobile app or GUI applications), make sure to not expose your `FAL_KEY`. Instead, use a server-side proxy to make requests to the API.

---

## 3. Queue

### Long-running Requests

For long-running requests, such as training jobs or models with slower inference times, it is recommended to check the Queue status and rely on Webhooks instead of blocking while waiting for the result.

### Submit a Request

```bash
response=$(curl --request POST \
  --url https://queue.fal.run/fal-ai/sam-3/video-rle \
  --header "Authorization: Key $FAL_KEY" \
  --header "Content-Type: application/json" \
  --data '{
     "video_url": "https://v3b.fal.media/files/b/elephant/NQdDxB0Ddfo82SPLbhYDp_bedroom.mp4"
   }')

REQUEST_ID=$(echo "$response" | grep -o '"request_id": *"[^"]*"' | sed 's/"request_id": *//; s/"//g')
```

### Fetch Request Status

```bash
curl --request GET \
  --url https://queue.fal.run/fal-ai/sam-3/requests/$REQUEST_ID/status \
  --header "Authorization: Key $FAL_KEY"
```

### Get the Result

Once the request is completed, you can fetch the result. See the [Output Schema](#output) for the expected result format.

```bash
curl --request GET \
  --url https://queue.fal.run/fal-ai/sam-3/requests/$REQUEST_ID \
  --header "Authorization: Key $FAL_KEY"
```

---

## 4. Files

Some attributes in the API accept file URLs as input. Whenever that's the case you can pass your own URL or a Base64 data URI.

### Data URI (base64)

You can pass a Base64 data URI as a file input. The API will handle the file decoding for you. Keep in mind that for large files, this alternative although convenient can impact the request performance.

### Hosted Files (URL)

You can also pass your own URLs as long as they are publicly accessible. Be aware that some hosts might block cross-site requests, rate-limit, or consider the request as a bot.

### Uploading Files

We provide a convenient file storage that allows you to upload files and use them in your requests. You can upload files using the client API and use the returned URL in your requests.

---

## 5. Schema

### Input

| Parameter | Type | Description |
|---|---|---|
| `video_url` | string | The URL of the video to be segmented |
| `mask_url` | string | The URL of the mask to be applied initially |
| `prompt` | string | Text prompt for segmentation. Use commas to track multiple objects (e.g., `'person, cloth'`). Default: `""` |
| `point_prompts` | list\<PointPrompt\> | List of point prompts with frame indices |
| `box_prompts` | list\<BoxPrompt\> | List of box prompts with optional `frame_index` |
| `apply_mask` | boolean | Apply the mask on the video |
| `boundingbox_zip` | boolean | Return per-frame bounding box overlays as a zip archive |
| `detection_threshold` | float | Detection confidence threshold (0.0-1.0). Lower = more detections but less precise. Defaults: 0.5 for existing, 0.7 for new objects. Try 0.2-0.3 if text prompts fail. Default: `0.5` |
| `frame_index` | integer | Frame index used for initial interaction when `mask_url` is provided |

**Example:**

```json
{
  "video_url": "https://v3b.fal.media/files/b/elephant/NQdDxB0Ddfo82SPLbhYDp_bedroom.mp4",
  "prompt": "person",
  "point_prompts": [],
  "box_prompts": [],
  "detection_threshold": 0.5
}
```

### Output

| Field | Type | Description |
|---|---|---|
| `video` | File | The segmented video |
| `boundingbox_frames_zip` | File | Zip file containing per-frame bounding box overlays |

**Example:**

```json
{
  "video": "https://fal.media/files/monkey/5BLHmbX3qxu5cD5gQzTqw_output.mp4"
}
```

---

## 6. Other Types

### SAM3ObjectMask

| Field | Type | Description |
|---|---|---|
| `track_id` | integer | Stable object/track id (`out_obj_ids`) for this mask |
| `rle` | string | Run-length encoding (Kaggle/COCO order) of the mask |

### SAM3VideoObjectFrame

| Field | Type | Description |
|---|---|---|
| `frame_index` | integer | 0-based frame index in the input video |
| `objects` | list\<SAM3ObjectMask\> | Per-object masks present in this frame (empty when none) |

### PointPrompt

| Field | Type | Description |
|---|---|---|
| `x` | integer | X Coordinate of the prompt |
| `y` | integer | Y Coordinate of the prompt |
| `label` | Enum | `1` for foreground, `0` for background |
| `object_id` | integer | Optional object identifier. Prompts sharing an object id refine the same object. When a text prompt is also given, the id selects which detected object the points refine |
| `frame_index` | integer | The frame index to interact with |

**Label Enum Values:** `0` (background), `1` (foreground)

### BoxPrompt

| Field | Type | Description |
|---|---|---|
| `x_min` | integer | X Min Coordinate of the box |
| `y_min` | integer | Y Min Coordinate of the box |
| `x_max` | integer | X Max Coordinate of the box |
| `y_max` | integer | Y Max Coordinate of the box |
| `object_id` | integer | Optional object identifier. Boxes sharing an object id refine the same object |
| `frame_index` | integer | The frame index to interact with |

### PointPromptBase

| Field | Type | Description |
|---|---|---|
| `x` | integer | X Coordinate of the prompt |
| `y` | integer | Y Coordinate of the prompt |
| `label` | Enum | `1` for foreground, `0` for background |
| `object_id` | integer | Optional object identifier. Prompts sharing an object id refine the same object. When a text prompt is also given, the id selects which detected object the points refine |

**Label Enum Values:** `0` (background), `1` (foreground)

### BoxPromptBase

| Field | Type | Description |
|---|---|---|
| `x_min` | integer | X Min Coordinate of the box |
| `y_min` | integer | Y Min Coordinate of the box |
| `x_max` | integer | X Max Coordinate of the box |
| `y_max` | integer | Y Max Coordinate of the box |
| `object_id` | integer | Optional object identifier. Boxes sharing an object id refine the same object |

### MaskMetadata

| Field | Type | Description |
|---|---|---|
| `index` | integer | Index of the mask inside the model output |
| `score` | float | Score for this mask |
| `box` | list\<float\> | Bounding box for the mask in normalized cxcywh coordinates |

---

## 7. SAM 3D Body Metadata Types

### SAM3DBodyMetadata

| Field | Type | Description |
|---|---|---|
| `num_people` | integer | Number of people detected |
| `people` | list\<SAM3DBodyPersonMetadata\> | Per-person metadata |
| `keypoint_names` | list\<string\> | Ordered names of the 70 MHR keypoints. Index `i` corresponds to index `i` in every person's `keypoints_2d` and `keypoints_3d` arrays. Sourced from facebookresearch/sam-3d-body `mhr70.py` |

### SAM3DBodyPersonMetadata

| Field | Type | Description |
|---|---|---|
| `person_id` | integer | Index of the person in the scene |
| `bbox` | list\<float\> | Bounding box `[x_min, y_min, x_max, y_max]` |
| `focal_length` | float | Estimated focal length |
| `pred_cam_t` | list\<float\> | Predicted camera translation `[tx, ty, tz]` |
| `keypoints_2d` | list\<list\<float\>\> | 2D keypoints `[[x, y], ...]` — 70 MHR body keypoints in image coordinates |
| `keypoints_3d` | list\<list\<float\>\> | 3D keypoints `[[x, y, z], ...]` — 70 MHR body keypoints in camera space |
| `shape_params` | list\<float\> | MHR identity (β) shape parameters |
| `body_pose_params` | list\<list\<float\>\> | Per-joint body pose parameters (axis-angle form) |
| `hand_pose_params` | list\<list\<float\>\> | Per-joint hand pose parameters (axis-angle form) |
| `global_rot` | list\<void\> | Global root rotation produced by MHR |
| `pred_global_rots` | list\<void\> | Per-joint global rotations (world-space), typically `[N_joints, 3, 3]` rotation matrices |
| `scale_params` | list\<float\> | MHR scale parameters (isotropic or per-axis) |
| `expr_params` | list\<float\> | MHR facial-expression parameters |
| `pred_joint_coords` | list\<list\<float\>\> | Skeleton joint positions in world space `[[x, y, z], ...]` |
| `mhr_model_params` | list\<void\> | Packed MHR parameter vector (concatenated shape/pose/expression/scale) |
| `pred_pose_raw` | list\<void\> | Raw pose transforms produced by the MHR decoder (pre-FK) |

### SAM3DBodyAlignmentInfo

| Field | Type | Description |
|---|---|---|
| `person_id` | integer | Index of the person |
| `scale_factor` | float | Scale factor applied for alignment |
| `translation` | list\<float\> | Translation `[tx, ty, tz]` |
| `focal_length` | float | Focal length used |
| `target_points_count` | integer | Number of target points for alignment |
| `cropped_vertices_count` | integer | Number of cropped vertices |

### SAM3DObjectMetadata

| Field | Type | Description |
|---|---|---|
| `object_index` | integer | Index of the object in the scene |
| `scale` | list\<list\<float\>\> | Scale factors `[sx, sy, sz]` |
| `rotation` | list\<list\<float\>\> | Rotation quaternion `[x, y, z, w]` |
| `translation` | list\<list\<float\>\> | Translation `[tx, ty, tz]` |
| `camera_pose` | list\<list\<float\>\> | Camera pose matrix |

---

## 8. Shared Types

### Image

| Field | Type | Description |
|---|---|---|
| `url` | string | The URL where the file can be downloaded from |
| `content_type` | string | The mime type of the file |
| `file_name` | string | The name of the file. Auto-generated if not provided |
| `file_size` | integer | The size of the file in bytes |
| `width` | integer | The width of the image in pixels |
| `height` | integer | The height of the image in pixels |

### File

| Field | Type | Description |
|---|---|---|
| `url` | string | The URL where the file can be downloaded from |
| `content_type` | string | The mime type of the file |
| `file_name` | string | The name of the file. Auto-generated if not provided |
| `file_size` | integer | The size of the file in bytes |

---

## BIT-APP Integration Notes

- **Relevance:** SAM 3 Video RLE can serve as the AI detection/surface engine within BIT's swappable engine architecture. It replaces or augments the existing YOLOv11 detection pipeline with SAM 3's superior video object segmentation and tracking.
- **Engine Key:** Consider registering as `engine_detection` → `sam-3-video-rle` variant.
- **Key Advantages over YOLOv11:**
  - Text-promptable segmentation (no per-class training needed)
  - Stable object tracking via `track_id` across frames
  - Run-length encoded (RLE) masks — compact and efficient
  - 3D body mesh reconstruction (MHR model) for people
  - Point and box prompt support for interactive refinement
- **Queue Pattern:** Uses fal.run's queue-based async processing — aligns with BIT's existing Hangfire job queue pattern.
- **Authentication:** Requires `FAL_KEY` environment variable / platform setting.
- **Real-time Mode:** WebSocket support via `fal.realtime` client — could enable live preview in BIT's frontend.
- **Related Docs:** See `docs/SAM3_API_REFERENCE.md` for the existing SAM 3 reference.
