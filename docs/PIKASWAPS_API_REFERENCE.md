# Pika Pikaswaps — API Reference

> **Source:** fal.run — Pika v2 Pikaswaps  
> **Purpose:** Swap out any object or region of a video with a new image or object. Define areas to replace with a text description.

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
  --url https://queue.fal.run/fal-ai/pika/v2/pikaswaps \
  --header "Authorization: Key $FAL_KEY" \
  --header "Content-Type: application/json" \
  --data '{
     "video_url": "https://v3.fal.media/files/monkey/vXi5n_oq0Qpnbs7Eb2k-b_output.mp4"
   }')

REQUEST_ID=$(echo "$response" | grep -o '"request_id": *"[^"]*"' | sed 's/"request_id": *//; s/"//g')
```

### Request Status

The command above will not return the final result, but the process status with its `request_id`. Use the **Queue API** commands specified below to check the status and get the final result.

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
  --url https://queue.fal.run/fal-ai/pika/v2/pikaswaps \
  --header "Authorization: Key $FAL_KEY" \
  --header "Content-Type: application/json" \
  --data '{
     "video_url": "https://v3.fal.media/files/monkey/vXi5n_oq0Qpnbs7Eb2k-b_output.mp4"
   }')

REQUEST_ID=$(echo "$response" | grep -o '"request_id": *"[^"]*"' | sed 's/"request_id": *//; s/"//g')
```

### Fetch Request Status

```bash
curl --request GET \
  --url https://queue.fal.run/fal-ai/pika/requests/$REQUEST_ID/status \
  --header "Authorization: Key $FAL_KEY"
```

### Get the Result

Once the request is completed, you can fetch the result. See the [Output Schema](#output) for the expected result format.

```bash
curl --request GET \
  --url https://queue.fal.run/fal-ai/pika/requests/$REQUEST_ID \
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
| `video_url` | string | URL of the input video |
| `image_url` | string | URL of the image to swap with |
| `modify_region` | string | Plaintext description of the object/region to modify |
| `prompt` | string | Text prompt describing the modification |
| `negative_prompt` | string | Negative prompt to guide the model |
| `seed` | integer | The seed for the random number generator |

**Example:**

```json
{
  "video_url": "https://v3.fal.media/files/monkey/vXi5n_oq0Qpnbs7Eb2k-b_output.mp4",
  "image_url": "https://fal.media/files/lion/2-ckrSp9r067aApfxXIrh_80a8a57bec50432e9918c87ae35004ed.jpg",
  "modify_region": "the cookie jars",
  "prompt": "Replace the background with a jelly jar"
}
```

### Output

| Field | Type | Description |
|---|---|---|
| `video` | File | The generated video with swapped regions |

**Example:**

```json
{
  "video": {
    "url": "https://v3.fal.media/files/koala/fGsPStNbAYW55sfinbDEL_output.mp4"
  }
}
```

---

## 6. Other Pika API Types

### CollectionToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `images` | list\<PikaImage\> | List of images to use for video generation |
| `prompt` | string | Text prompt |
| `seed` | integer | The seed for the random number generator |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `""` |
| `aspect_ratio` | AspectRatioEnum | The aspect ratio of the generated video. Default: `"16:9"` |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |
| `duration` | integer | The duration of the generated video in seconds. Default: `5` |
| `ingredients_mode` | IngredientsModeEnum | Mode for integrating multiple images. Default: `"creative"` |

**Enum Values:**

- `aspect_ratio`: `16:9`, `9:16`, `1:1`, `4:5`, `5:4`, `3:2`, `2:3`
- `resolution`: `720p`, `1080p`
- `ingredients_mode`: `creative`, `precise`

### Pika22ImageToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `image_url` | string | URL of the image to use as the first frame |
| `prompt` | string | Text prompt |
| `seed` | integer | The seed for the random number generator |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `""` |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |
| `duration` | DurationEnum | The duration of the generated video in seconds. Default: `"5"` |

**Enum Values:**

- `resolution`: `720p`, `1080p`
- `duration`: `5`, `10`

### PikaffectsRequest

| Parameter | Type | Description |
|---|---|---|
| `image_url` | string | URL of the input image |
| `pikaffect` | PikaffectEnum | The Pikaffect to apply |
| `prompt` | string | Text prompt to guide the effect |
| `negative_prompt` | string | Negative prompt to guide the model |
| `seed` | integer | The seed for the random number generator |

**Pikaffect Enum Values:** `Cake-ify`, `Crumble`, `Crush`, `Decapitate`, `Deflate`, `Dissolve`, `Explode`, `Eye-pop`, `Inflate`, `Levitate`, `Melt`, `Peel`, `Poke`, `Squish`, `Ta-da`, `Tear`

### Pika22PikascenesRequest

| Parameter | Type | Description |
|---|---|---|
| `image_urls` | list\<string\> | URLs of images to combine into a video |
| `prompt` | string | Text prompt describing the desired video |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `"ugly, bad, terrible"` |
| `seed` | integer | The seed for the random number generator |
| `aspect_ratio` | AspectRatioEnum | The aspect ratio of the generated video. Default: `"16:9"` |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"1080p"` |
| `duration` | DurationEnum | The duration of the generated video in seconds. Default: `"5"` |
| `ingredients_mode` | IngredientsModeEnum | Mode for integrating multiple images. Default: `"precise"` |

**Enum Values:**

- `aspect_ratio`: `16:9`, `9:16`, `1:1`, `4:5`, `5:4`, `3:2`, `2:3`
- `resolution`: `720p`, `1080p`
- `duration`: `5`, `10`
- `ingredients_mode`: `precise`, `creative`

### Pika22TextToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `prompt` | string | Text prompt |
| `seed` | integer | The seed for the random number generator |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `"ugly, bad, terrible"` |
| `aspect_ratio` | AspectRatioEnum | The aspect ratio of the generated video. Default: `"16:9"` |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |
| `duration` | DurationEnum | The duration of the generated video in seconds. Default: `"5"` |

**Enum Values:**

- `aspect_ratio`: `16:9`, `9:16`, `1:1`, `4:5`, `5:4`, `3:2`, `2:3`
- `resolution`: `1080p`, `720p`
- `duration`: `5`, `10`

### Pika25KeyframesToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `image_urls` | list\<string\> | URLs of keyframe images (2-5 images) to create transitions between |
| `transitions` | list\<KeyframeTransition\> | Configuration for each transition. Length must be `len(image_urls) - 1`. Total duration of all transitions must not exceed 25 seconds. If not provided, uses default 5-second transitions with the global prompt. |
| `prompt` | string | Default prompt for all transitions. Individual transition prompts override this. |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `""` |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |
| `seed` | integer | The seed for the random number generator |

**Enum Values:**

- `resolution`: `480p`, `720p`, `1080p`

### Pika22KeyframesToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `image_urls` | list\<string\> | URLs of keyframe images (2-5 images) to create transitions between |
| `transitions` | list\<KeyframeTransition\> | Configuration for each transition. Length must be `len(image_urls) - 1`. Total duration of all transitions must not exceed 25 seconds. |
| `prompt` | string | Default prompt for all transitions |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `""` |
| `seed` | integer | The seed for the random number generator |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |

**Enum Values:**

- `resolution`: `720p`, `1080p`

### PikadditionsRequest

| Parameter | Type | Description |
|---|---|---|
| `video_url` | string | URL of the input video |
| `image_url` | string | URL of the image to add |
| `prompt` | string | Text prompt describing what to add |
| `negative_prompt` | string | Negative prompt to guide the model |
| `seed` | integer | The seed for the random number generator |

### Pika25ImageToVideoRequest

| Parameter | Type | Description |
|---|---|---|
| `image_url` | string | URL of the image to use as the first frame |
| `prompt` | string | Text prompt |
| `resolution` | ResolutionEnum | The resolution of the generated video. Default: `"720p"` |
| `duration` | DurationEnum | The duration of the generated video in seconds. Default: `"5"` |
| `negative_prompt` | string | A negative prompt to guide the model. Default: `"ugly, bad, terrible"` |
| `seed` | integer | The seed for the random number generator |

**Enum Values:**

- `resolution`: `480p`, `720p`, `1080p`
- `duration`: `5`, `10`

---

### Shared Types

#### KeyframeTransition

| Parameter | Type | Description |
|---|---|---|
| `duration` | integer | Duration of this transition in seconds. Default: `5` |
| `prompt` | string | Specific prompt for this transition. Overrides the global prompt if provided. |

#### File

| Parameter | Type | Description |
|---|---|---|
| `url` | string | The URL where the file can be downloaded from |
| `content_type` | string | The mime type of the file |
| `file_name` | string | The name of the file. Auto-generated if not provided |
| `file_size` | integer | The size of the file in bytes |

#### PikaImage

| Parameter | Type | Description |
|---|---|---|
| `image_url` | string | URL of the image |

---

## BIT-APP Integration Notes

- **Relevance:** Pikaswaps can be used as an AI compositing engine within BIT's swappable engine architecture (factory pattern via Platform Settings).
- **Engine Key:** Consider registering as `engine_compositing` → `pika-pikaswaps` variant.
- **Queue Pattern:** Uses fal.run's queue-based async processing — aligns with BIT's existing Hangfire job queue pattern.
- **Authentication:** Requires `FAL_KEY` environment variable / platform setting.
