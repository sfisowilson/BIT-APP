using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers
{
    [ApiController]
    [Route("api/content")]
    [Authorize]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly PostgresDbContext _db;
        private readonly IEventLogService _eventLog;
        // Supported broadcast containers and codecs for validation
        private static readonly HashSet<string> SupportedContainers = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".webm" };
        private static readonly HashSet<string> SupportedVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
            { "h264", "h265", "hevc", "prores", "dnxhd", "dnxhr", "mpeg2video", "mpeg4", "vp9", "av1", "mjpeg", "mjp2" };

        public ContentController(IContentService contentService, IHostEnvironment env, IConfiguration config, PostgresDbContext db, IEventLogService eventLog)
        {
            _contentService = contentService;
            _eventLog = eventLog;
            _env = env;
            _config = config;
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResult<ContentItem>>> GetContent([FromQuery] ContentFilterParams filter)
        {
            var result = await _contentService.GetContentAsync(filter);
            return Ok(result);
        }

        /// <summary>MReq 1: Upload video file with codec validation, metadata extraction, and proxy generation.</summary>
        [HttpPost("upload")]
        [RequestSizeLimit(10_737_418_240)] // 10 GB default; also configurable via appsettings.json "UploadLimits:MaxVideoBytes"
        public async Task<IActionResult> UploadVideo(
            [FromForm] string title,
            [FromForm] string resolution,
            [FromForm] int frameRate,
            [FromForm] string duration,
            [FromForm] string sourceChannel,
            [FromForm] string? campaignId,
            IFormFile? file)
        {
            // Read configurable upload limit (default 10 GB for broadcast files)
            var maxBytes = _config.GetValue<long>("UploadLimits:MaxVideoBytes", 10_737_418_240);
            if (file != null && file.Length > maxBytes)
            {
                var maxGb = maxBytes / (1024.0 * 1024 * 1024);
                return BadRequest(new { error = $"File exceeds maximum upload size of {maxGb:F1} GB. Please use a smaller file or contact your administrator for direct ingest options." });
            }

            try
            {
                var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
                Directory.CreateDirectory(uploadsDir);

                string storageKey;
                string? savedFilePath = null;
                if (file != null && file.Length > 0)
                {
                    // Validate container format
                    var ext = Path.GetExtension(file.FileName);
                    if (!SupportedContainers.Contains(ext))
                    {
                        return BadRequest(new { error = $"Unsupported container format '{ext}'. Supported formats: {string.Join(", ", SupportedContainers)}" });
                    }

                    var safeName = $"{Guid.NewGuid():N}{ext}";
                    var filePath = Path.Combine(uploadsDir, safeName);
                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    storageKey = $"/api/content/file/{safeName}";
                    savedFilePath = filePath;

                    // Validate codec compatibility via FFprobe
                    try
                    {
                        var (codec, container) = ValidateVideoCodec(filePath);
                        if (!SupportedVideoCodecs.Contains(codec))
                        {
                            // Clean up invalid file
                            try { System.IO.File.Delete(filePath); } catch { }
                            return BadRequest(new { error = $"Unsupported video codec '{codec}'. Supported codecs: {string.Join(", ", SupportedVideoCodecs)}" });
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        // FFprobe not available — accept the file with a warning
                        System.Diagnostics.Debug.WriteLine($"Codec validation skipped: {ex.Message}");
                    }

                    // Generate H.264 proxy for web playback (async, fire-and-forget)
                    var proxyDir = Path.Combine(uploadsDir, "proxy");
                    Directory.CreateDirectory(proxyDir);
                    var proxyName = $"{Path.GetFileNameWithoutExtension(safeName)}_proxy.mp4";
                    var proxyPath = Path.Combine(proxyDir, proxyName);
                    // Skip if proxy already exists or is being generated
                    if (!System.IO.File.Exists(proxyPath) && !IsProxyGenerating(proxyPath))
                    {
                        MarkProxyGenerating(proxyPath);
                        BackgroundJob.Enqueue(() => GenerateProxyAsync(filePath, proxyPath));
                    }
                }
                else
                {
                    storageKey = $"s3://afrobotics-raw-ingest/{title.Replace(" ", "_").ToLower()}.mov";
                }

                // Extract real metadata from the uploaded file via FFprobe — the authority, never trust browser values
                var actualDuration = duration;
                var actualFps = frameRate;
                var actualResolution = resolution;
                var actualWidth = 1920;
                var actualHeight = 1080;

                if (savedFilePath != null)
                {
                    try
                    {
                        (actualDuration, actualFps, actualResolution, actualWidth, actualHeight) = ExtractVideoMetadata(savedFilePath);
                    }
                    catch (Exception ex)
                    {
                        await _eventLog.LogEventAsync("ContentUpload", "FFPROBE_METADATA_FAILED", "Error",
                            $"ffprobe metadata extraction failed for '{title}' ({Path.GetFileName(savedFilePath)}): " +
                            $"{ex.GetType().Name} — {ex.Message}. Falling back to browser-supplied/default values " +
                            $"({actualWidth}x{actualHeight} @ {actualFps}fps) — these are likely WRONG.");
                    }
                }

                var dto = new IngestVideoDto
                {
                    Title = title,
                    Resolution = actualResolution,
                    Width = actualWidth,
                    Height = actualHeight,
                    FrameRate = actualFps,
                    Duration = actualDuration,
                    SourceChannel = sourceChannel,
                    StorageKey = storageKey,
                    CampaignId = campaignId
                };

                var content = await _contentService.IngestVideoAsync(dto);
                return Ok(content);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Phase 2: Chunked / Resumable Upload for Large Broadcast Files
        // ═══════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, ChunkedUploadSession> UploadSessions = new();

        // ── Proxy generation deduplication ──
        private static readonly HashSet<string> GeneratingProxies = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsProxyGenerating(string proxyPath) {
            lock (GeneratingProxies) { return GeneratingProxies.Contains(proxyPath); }
        }

        private static void MarkProxyGenerating(string proxyPath) {
            lock (GeneratingProxies) { GeneratingProxies.Add(proxyPath); }
        }

        private static void ClearProxyGenerating(string proxyPath) {
            lock (GeneratingProxies) { GeneratingProxies.Remove(proxyPath); }
        }

        /// <summary>Initialize a chunked upload session. Returns an uploadId for subsequent chunk uploads.</summary>
        [HttpPost("upload/init")]
        public IActionResult InitChunkedUpload([FromBody] ChunkedUploadInitDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FileName))
                return BadRequest(new { error = "fileName is required." });

            var ext = Path.GetExtension(dto.FileName);
            if (!SupportedContainers.Contains(ext))
                return BadRequest(new { error = $"Unsupported format '{ext}'. Supported: {string.Join(", ", SupportedContainers)}" });

            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var session = new ChunkedUploadSession
            {
                UploadId = uploadId,
                FileName = dto.FileName,
                TotalChunks = dto.TotalChunks,
                ChunkSize = dto.ChunkSize,
                TotalSize = dto.TotalSize,
                CreatedAt = DateTime.UtcNow,
                UploadedChunks = new HashSet<int>()
            };

            lock (UploadSessions)
            {
                // Clean up stale sessions older than 24 hours
                var stale = UploadSessions.Where(kv => (DateTime.UtcNow - kv.Value.CreatedAt).TotalHours > 24)
                    .Select(kv => kv.Key).ToList();
                foreach (var key in stale) UploadSessions.Remove(key);

                UploadSessions[uploadId] = session;
            }

            var tempDir = Path.Combine(_env.ContentRootPath, "Uploads", "chunks", uploadId);
            Directory.CreateDirectory(tempDir);

            return Ok(new { uploadId, chunkSize = dto.ChunkSize });
        }

        /// <summary>Upload a single chunk. Chunks can arrive in any order.</summary>
        [HttpPost("upload/chunk")]
        [RequestSizeLimit(100_000_000)] // 100 MB per chunk
        public async Task<IActionResult> UploadChunk(
            [FromForm] string uploadId,
            [FromForm] int chunkIndex,
            [FromForm] int totalChunks,
            IFormFile chunk)
        {
            if (chunk == null || chunk.Length == 0)
                return BadRequest(new { error = "Chunk file is required." });

            ChunkedUploadSession? session;
            lock (UploadSessions)
            {
                if (!UploadSessions.TryGetValue(uploadId, out session))
                    return NotFound(new { error = "Upload session not found. It may have expired. Re-initialize the upload." });
            }

            var tempDir = Path.Combine(_env.ContentRootPath, "Uploads", "chunks", uploadId);
            Directory.CreateDirectory(tempDir);

            var chunkPath = Path.Combine(tempDir, $"chunk_{chunkIndex:D6}");
            await using (var stream = new FileStream(chunkPath, FileMode.Create))
            {
                await chunk.CopyToAsync(stream);
            }

            lock (UploadSessions)
            {
                session.UploadedChunks.Add(chunkIndex);
                session.LastActivity = DateTime.UtcNow;
            }

            return Ok(new { uploadId, chunkIndex, received = true, progress = session.UploadedChunks.Count * 100.0 / totalChunks });
        }

        /// <summary>Assemble all chunks into the final file, validate, generate proxy, and register.</summary>
        [HttpPost("upload/complete")]
        public async Task<IActionResult> CompleteChunkedUpload([FromBody] ChunkedUploadCompleteDto dto)
        {
            ChunkedUploadSession? session;
            lock (UploadSessions)
            {
                if (!UploadSessions.TryGetValue(dto.UploadId, out session))
                    return NotFound(new { error = "Upload session not found." });
            }

            var tempDir = Path.Combine(_env.ContentRootPath, "Uploads", "chunks", dto.UploadId);
            if (!Directory.Exists(tempDir))
                return BadRequest(new { error = "No chunks found for this session." });

            // Verify all chunks present
            var missingChunks = new List<int>();
            for (int i = 0; i < session.TotalChunks; i++)
            {
                if (!session.UploadedChunks.Contains(i))
                    missingChunks.Add(i);
            }

            if (missingChunks.Count > 0)
            {
                return BadRequest(new
                {
                    error = $"Missing {missingChunks.Count} chunk(s): [{string.Join(", ", missingChunks.Take(10))}]" +
                            (missingChunks.Count > 10 ? "..." : ""),
                    missingChunks = missingChunks.Take(20).ToList()
                });
            }

            // Assemble chunks into final file
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var ext = Path.GetExtension(session.FileName);
            var safeName = $"{dto.UploadId}{ext}";
            var finalPath = Path.Combine(uploadsDir, safeName);

            try
            {
                await using (var output = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    for (int i = 0; i < session.TotalChunks; i++)
                    {
                        var chunkPath = Path.Combine(tempDir, $"chunk_{i:D6}");
                        if (!System.IO.File.Exists(chunkPath)) continue;

                        var chunkBytes = await System.IO.File.ReadAllBytesAsync(chunkPath);
                        await output.WriteAsync(chunkBytes);
                    }
                }

                // Validate codec after assembly
                try
                {
                    var (codec, _) = ValidateVideoCodec(finalPath);
                    if (!SupportedVideoCodecs.Contains(codec))
                    {
                        try { System.IO.File.Delete(finalPath); } catch { }
                        return BadRequest(new { error = $"Video codec '{codec}' is not supported. Supported codecs: {string.Join(", ", SupportedVideoCodecs)}" });
                    }
                }
                catch (InvalidOperationException) { /* FFprobe not available — accept */ }

                // Generate proxy (skip if already exists or being generated)
                var proxyDir = Path.Combine(uploadsDir, "proxy");
                Directory.CreateDirectory(proxyDir);
                var proxyName = $"{Path.GetFileNameWithoutExtension(safeName)}_proxy.mp4";
                var proxyPath = Path.Combine(proxyDir, proxyName);
                if (!System.IO.File.Exists(proxyPath) && !IsProxyGenerating(proxyPath))
                {
                    MarkProxyGenerating(proxyPath);
                    BackgroundJob.Enqueue(() => GenerateProxyAsync(finalPath, proxyPath));
                }

                // Extract metadata
                var (actualDuration, actualFps, actualResolution, actualWidth, actualHeight) = ExtractVideoMetadata(finalPath);

                var ingestDto = new IngestVideoDto
                {
                    Title = dto.Title ?? Path.GetFileNameWithoutExtension(session.FileName),
                    Resolution = actualResolution,
                    Width = actualWidth,
                    Height = actualHeight,
                    FrameRate = actualFps,
                    Duration = actualDuration,
                    SourceChannel = dto.SourceChannel ?? "Direct Upload",
                    StorageKey = $"/api/content/file/{safeName}",
                    CampaignId = dto.CampaignId
                };

                var content = await _contentService.IngestVideoAsync(ingestDto);

                // Clean up chunk directory
                try { Directory.Delete(tempDir, true); } catch { }
                lock (UploadSessions) { UploadSessions.Remove(dto.UploadId); }

                return Ok(new { success = true, content, proxyGenerating = true });
            }
            catch (Exception ex)
            {
                // Clean up on failure
                try { if (System.IO.File.Exists(finalPath)) System.IO.File.Delete(finalPath); } catch { }
                return StatusCode(500, new { error = $"Assembly failed: {ex.Message}" });
            }
        }

        /// <summary>Check upload session status — which chunks have been received.</summary>
        [HttpGet("upload/status/{uploadId}")]
        public IActionResult GetChunkedUploadStatus(string uploadId)
        {
            ChunkedUploadSession? session;
            lock (UploadSessions)
            {
                if (!UploadSessions.TryGetValue(uploadId, out session))
                    return NotFound(new { error = "Upload session not found." });
            }

            return Ok(new
            {
                session.UploadId,
                session.FileName,
                session.TotalChunks,
                session.TotalSize,
                uploadedChunks = session.UploadedChunks.OrderBy(i => i).ToList(),
                progress = session.TotalChunks > 0
                    ? Math.Round(session.UploadedChunks.Count * 100.0 / session.TotalChunks, 1)
                    : 0,
                isComplete = session.UploadedChunks.Count >= session.TotalChunks
            });
        }

        /// <summary>Serve uploaded video files and thumbnails for playback with range support.</summary>
        [HttpGet("file/{*fileName}")]
        [AllowAnonymous]
        public IActionResult GetVideoFile(string fileName)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            // Normalise path separators and resolve the full path
            var cleanName = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var filePath = Path.GetFullPath(Path.Combine(uploadsDir, cleanName));

            // Prevent directory traversal
            if (!filePath.StartsWith(uploadsDir, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(filePath))
                return NotFound();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mxf" => "application/mxf",
                ".webm" => "video/webm",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            // No caching — files under tmp-renders/ are overwritten in place on every retry (same
            // renderId, same path), and an intermediary (e.g. the fal.ai-facing tunnel) caching a
            // prior response by URL would silently serve stale bytes to fal.ai on the next attempt.
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType, enableRangeProcessing: true);
        }

        /// <summary>Serve H.264 proxy files for web playback. Falls back to source if proxy not ready.</summary>
        [HttpGet("proxy/{contentId}")]
        [AllowAnonymous]
        public IActionResult GetProxyFile(string contentId)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var proxyDir = Path.Combine(uploadsDir, "proxy");

            // Search for a proxy file matching this content ID pattern
            if (Directory.Exists(proxyDir))
            {
                var candidates = Directory.GetFiles(proxyDir, "*_proxy.mp4");
                // Try to match by content ID in the filename (content IDs are like "v-01")
                foreach (var candidate in candidates)
                {
                    var fileName = Path.GetFileName(candidate);
                    if (fileName.Contains(contentId) || fileName.StartsWith(contentId))
                    {
                        var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return File(stream, "video/mp4", enableRangeProcessing: true);
                    }
                }
            }

            // Fallback: try source file
            var sourceCandidates = Directory.GetFiles(uploadsDir, $"{contentId}*")
                .Where(f => !f.Contains("_proxy"))
                .ToList();
            if (sourceCandidates.Count > 0)
            {
                var sourcePath = sourceCandidates[0];
                var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".mp4" => "video/mp4",
                    ".mov" => "video/quicktime",
                    ".mxf" => "application/mxf",
                    _ => "application/octet-stream"
                };
                var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, contentType, enableRangeProcessing: true);
            }

            return NotFound(new { error = "No video file or proxy found for this content." });
        }

        /// <summary>Check if a proxy file exists for a given content ID.</summary>
        [HttpGet("proxy-status/{contentId}")]
        [AllowAnonymous]
        public IActionResult GetProxyStatus(string contentId)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var proxyDir = Path.Combine(uploadsDir, "proxy");

            if (Directory.Exists(proxyDir))
            {
                var candidates = Directory.GetFiles(proxyDir, "*_proxy.mp4");
                foreach (var candidate in candidates)
                {
                    var fileName = Path.GetFileName(candidate);
                    if (fileName.Contains(contentId) || fileName.StartsWith(contentId))
                    {
                        var fileInfo = new FileInfo(candidate);
                        return Ok(new { ready = true, sizeBytes = fileInfo.Length, createdAt = fileInfo.CreationTimeUtc });
                    }
                }
            }

            return Ok(new { ready = false });
        }

        [HttpPost]
        public async Task<IActionResult> IngestVideo([FromBody] IngestVideoDto dto)
        {
            try
            {
                var content = await _contentService.IngestVideoAsync(dto);
                return Ok(content);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{contentId}/scenes")]
        public async Task<ActionResult<IEnumerable<SceneItem>>> GetScenes(string contentId)
        {
            var scenes = await _contentService.GetScenesAsync(contentId);
            return Ok(scenes);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteContent(string id)
        {
            try
            {
                var deleted = await _contentService.DeleteContentAsync(id);
                if (!deleted)
                {
                    return NotFound(new { error = "Content not found" });
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Pipeline Stage Transition Endpoints
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Transition a content item to a target pipeline stage with validation.
        /// Valid transitions: Staging→Transcoding, Transcoding→SceneDetecting, SceneDetecting→Completed,
        /// any→Failed, Failed→Staging (retry), Completed→SceneDetecting (re-detect).
        /// </summary>
        [HttpPost("{id}/transition")]
        public async Task<IActionResult> TransitionStage(string id, [FromBody] TransitionStageDto dto)
        {
            try
            {
                var content = await _contentService.TransitionStageAsync(id, dto.TargetStage, dto.ErrorMessage);
                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    message = $"Successfully transitioned '{content.Title}' to '{content.IngestionStatus}'."
                });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Reset content to Transcoding stage (re-run transcoding).</summary>
        [HttpPost("{id}/retranscode")]
        public async Task<IActionResult> Retranscode(string id)
        {
            try
            {
                var content = await _contentService.GetContentByIdAsync(id);
                if (content == null) return NotFound(new { error = "Content not found." });

                // Allow retranscode from Staging or Failed
                if (content.IngestionStatus != PipelineStages.Staging &&
                    content.IngestionStatus != PipelineStages.Failed &&
                    content.IngestionStatus != PipelineStages.Transcoding)
                {
                    // If already past Transcoding, reset to Staging first
                    content = await _contentService.TransitionStageAsync(id, PipelineStages.Staging);
                }

                content = await _contentService.TransitionStageAsync(id, PipelineStages.Transcoding);
                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    message = $"Transcoding restarted for '{content.Title}'."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Reset content to SceneDetecting stage and enqueue the Hangfire detection pipeline.</summary>
        [HttpPost("{id}/redetect-scenes")]
        public async Task<IActionResult> RedetectScenes(string id)
        {
            try
            {
                var content = await _contentService.GetContentByIdAsync(id);
                if (content == null) return NotFound(new { error = "Content not found." });

                var storageKey = content.StorageKey;
                if (string.IsNullOrEmpty(storageKey))
                    return BadRequest(new { error = "Scene detection requires a valid video storageKey." });

                // If Failed or past SceneDetecting, route through Staging→Transcoding first
                if (content.IngestionStatus == PipelineStages.Failed)
                {
                    content = await _contentService.TransitionStageAsync(id, PipelineStages.Staging);
                    content = await _contentService.TransitionStageAsync(id, PipelineStages.Transcoding);
                }
                else if (content.IngestionStatus == PipelineStages.Completed)
                {
                    // From Completed we can go directly to SceneDetecting
                }
                else if (content.IngestionStatus != PipelineStages.SceneDetecting)
                {
                    content = await _contentService.TransitionStageAsync(id, PipelineStages.Transcoding);
                }

                // Only transition if not already in SceneDetecting — pipeline rejects self-transitions
                if (content.IngestionStatus != PipelineStages.SceneDetecting)
                {
                    content = await _contentService.TransitionStageAsync(id, PipelineStages.SceneDetecting);
                }

                // Enqueue the Hangfire background job to actually run the detection pipeline
                var jobId = BackgroundJob.Enqueue<SceneDetectionJobService>(
                    s => s.RunDetectionPipeline(content.Id, content.Title, CancellationToken.None));

                content.DetectionJobId = jobId;
                content.DetectionProgress = 0;
                content.SceneDetectingStartedAt = DateTime.UtcNow;
                _db.ContentItems.Update(content);
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    jobId,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    message = $"Scene detection queued for '{content.Title}'. Poll GET /api/content/{id}/detection-status for progress."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Assembles one final video combining every scene: each scene's queued render
        /// (RenderItem.IsQueuedForFinal) if it has one, original footage otherwise. Poll
        /// GET /api/content/{id} (FinalAssemblyStatus/Progress) or listen for SignalR
        /// DetectionProgress with jobId "final-assembly" for live status.
        /// </summary>
        [HttpPost("{id}/final-assembly")]
        public async Task<IActionResult> StartFinalAssembly(string id)
        {
            var content = await _db.ContentItems.FindAsync(id);
            if (content == null) return NotFound(new { error = "Content not found." });

            if (content.FinalAssemblyStatus == "Processing")
                return BadRequest(new { error = "Final assembly is already in progress for this content." });

            var hasAnyScene = await _db.SceneItems.AnyAsync(s => s.ContentId == id);
            if (!hasAnyScene)
                return BadRequest(new { error = "This content has no scenes to assemble." });

            content.FinalAssemblyStatus = "Processing";
            content.FinalAssemblyProgress = 0;
            content.FinalAssemblyErrorMessage = null;
            await _db.SaveChangesAsync();

            BackgroundJob.Enqueue<FinalAssemblyJobService>(s => s.ProcessFinalAssemblyJob(id, CancellationToken.None));

            await _eventLog.LogEventAsync("RenderEngine", "FINAL_ASSEMBLY_QUEUED", "Info", $"Final assembly queued for content '{id}'.");

            return Ok(new { id, finalAssemblyStatus = content.FinalAssemblyStatus });
        }

        /// <summary>Serves the assembled final video (see ContentItem.FinalVideoStorageKey).</summary>
        [HttpGet("{id}/final-video")]
        [AllowAnonymous] // Allow direct video player stream, matching render download/preview routes
        public async Task<IActionResult> DownloadFinalVideo(string id)
        {
            var content = await _db.ContentItems.FindAsync(id);
            if (content == null) return NotFound(new { error = "Content not found." });

            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "renders", $"BIT_Final_{id}.mp4");
            if (!System.IO.File.Exists(localPath))
                return NotFound(new { error = "Final video not found on disk." });

            var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", $"BIT_Final_{content.Title}.mp4", enableRangeProcessing: true);
        }

        /// <summary>Delete all scenes (and child surfaces/ad-slots/approvals) for a content item.</summary>
        [HttpDelete("{contentId}/scenes")]
        public async Task<IActionResult> DeleteAllScenes(string contentId)
        {
            var content = await _db.ContentItems.FindAsync(contentId);
            if (content == null)
                return NotFound(new { error = "Content not found." });

            // Guard: block if any surfaces are approved (matches DeleteScene's single-scene guard)
            var sceneIds = await _db.SceneItems.Where(s => s.ContentId == contentId).Select(s => s.Id).ToListAsync();
            var hasApproved = await _db.SurfaceItems.AnyAsync(sf => sceneIds.Contains(sf.SceneId) && sf.Status == "Approved");
            if (hasApproved)
            {
                return BadRequest(new
                {
                    error = "Cannot delete scenes: approved surface(s) exist. " +
                            "Exclude or reject approved surfaces before deleting all scenes."
                });
            }

            try
            {
                await SceneDetectionJobService.DeleteExistingScenes(_db, contentId, CancellationToken.None);
                return Ok(new
                {
                    success = true,
                    contentId,
                    message = $"All scenes for '{content.Title}' deleted."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Mark content as Failed with an error message.</summary>
        [HttpPost("{id}/mark-failed")]
        public async Task<IActionResult> MarkFailed(string id, [FromBody] TransitionStageDto dto)
        {
            try
            {
                var content = await _contentService.TransitionStageAsync(
                    id, PipelineStages.Failed, dto.ErrorMessage ?? "Manual failure.");
                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    lastErrorMessage = content.LastErrorMessage
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Full reset — clear all pipeline progress back to Staging.</summary>
        [HttpPost("{id}/reset")]
        public async Task<IActionResult> ResetPipeline(string id)
        {
            try
            {
                var content = await _contentService.TransitionStageAsync(id, PipelineStages.Staging);
                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    message = $"Pipeline reset for '{content.Title}'. All stage data cleared."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Extract actual duration, frame rate, resolution, width, and height from a video file using FFprobe.
        /// Returns (duration "HH:MM:SS", fps, resolution "WxH", width, height).
        /// </summary>
        /// <summary>
        /// Extract video metadata via ffprobe. Delegates to the single-field helpers below
        /// rather than requesting all fields in one combined query — ffprobe's `-show_entries
        /// stream=a,b,c,d` does NOT preserve the requested field order in `-of csv` output; it
        /// returns fields in its own internal order. A prior version of this method assumed
        /// positional order matched the request and silently mis-mapped every value (duration
        /// read as width, width read as an unparseable fraction and defaulted to 1920, etc.) —
        /// corrupting every uploaded video's stored width/height/fps/duration with no error.
        /// </summary>
        private static (string duration, int fps, string resolution, int width, int height) ExtractVideoMetadata(string filePath)
        {
            var (width, height) = ExtractDimensionsOnly(filePath);
            var fps = ExtractFpsOnly(filePath);
            if (fps <= 0) fps = 25;
            var duration = ExtractDurationOnly(filePath) ?? "00:05:00";
            var resolution = $"{width}x{height}";

            return (duration, fps, resolution, width, height);
        }

        /// <summary>Extract just width/height from a video file.</summary>
        private static (int width, int height) ExtractDimensionsOnly(string filePath)
        {
            var ffprobePath = ResolveFfprobePath();
            var args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{filePath}\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffprobePath, args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            var parts = output.Split(',');
            int w = parts.Length > 0 && int.TryParse(parts[0], out var pw) ? pw : 1920;
            int h = parts.Length > 1 && int.TryParse(parts[1], out var ph) ? ph : 1080;
            return (w, h);
        }

        /// <summary>Extract just FPS from a video file.</summary>
        private static int ExtractFpsOnly(string filePath)
        {
            var ffprobePath = ResolveFfprobePath();
            var args = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of csv=p=0 \"{filePath}\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffprobePath, args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            if (output.Contains('/'))
            {
                var frac = output.Split('/');
                if (frac.Length == 2 && double.TryParse(frac[0], out var n) && double.TryParse(frac[1], out var d) && d > 0)
                    return (int)Math.Round(n / d);
            }
            return int.TryParse(output, out var f) ? f : 0;
        }

        /// <summary>Extract just duration from a video file.</summary>
        private static string? ExtractDurationOnly(string filePath)
        {
            var ffprobePath = ResolveFfprobePath();
            var args = $"-v error -select_streams v:0 -show_entries stream=duration -of csv=p=0 \"{filePath}\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffprobePath, args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            if (double.TryParse(output, out var secs) && secs > 0)
            {
                var ts = TimeSpan.FromSeconds(secs);
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            return null;
        }

        /// <summary>
        /// Validate the video codec of an uploaded file using FFprobe.
        /// Returns (codecName, containerFormat). Throws InvalidOperationException if FFprobe not available.
        /// </summary>
        private static (string codec, string container) ValidateVideoCodec(string filePath)
        {
            var ffprobePath = ResolveFfprobePath();

            var args = $"-v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 \"{filePath}\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffprobePath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var codec = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (string.IsNullOrEmpty(codec))
                codec = "unknown";

            return (codec.ToLowerInvariant(), Path.GetExtension(filePath).ToLowerInvariant());
        }

        /// <summary>
        /// Generate an H.264 1080p proxy (max 8 Mbps) for web playback.
        /// Runs asynchronously; does not block the upload response.
        /// </summary>
        public static async Task GenerateProxyAsync(string sourcePath, string proxyPath)
        {
            // Skip if proxy already exists or source is missing
            if (System.IO.File.Exists(proxyPath) || !System.IO.File.Exists(sourcePath))
            {
                ClearProxyGenerating(proxyPath);
                return;
            }

            try
            {
                var ffmpegPath = ResolveFfmpegPath();

                // Proxy settings: H.264, 1080p max, 8 Mbps, AAC audio 128k, keyframe every 2s
                var args = $"-y -i \"{sourcePath}\" " +
                           $"-vf \"scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease\" " +
                           $"-c:v libx264 -preset fast -crf 23 -maxrate 8M -bufsize 16M " +
                           $"-c:a aac -b:a 128k -ac 2 " +
                           $"-movflags +faststart -g 50 -keyint_min 25 " +
                           $"\"{proxyPath}\"";

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpegPath, args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();

                // Wait up to 30 minutes for proxy generation (large files take time)
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(30));

                System.Diagnostics.Debug.WriteLine(
                    process.ExitCode == 0
                        ? $"Proxy generated: {proxyPath}"
                        : $"Proxy generation failed (exit {process.ExitCode}): {sourcePath}");
            }
            catch (Exception ex)
            {
                // Proxy generation is best-effort — log and continue
                System.Diagnostics.Debug.WriteLine($"Proxy generation error: {ex.Message}");
            }
            finally
            {
                ClearProxyGenerating(proxyPath);
            }
        }

        /// <summary>Hangfire recurring job: clean up stale chunk upload directories older than 24h.</summary>
        [NonAction]
        public void CleanupChunkUploadDirectories()
        {
            var chunksDir = Path.Combine(_env.ContentRootPath, "Uploads", "chunks");
            if (!Directory.Exists(chunksDir)) return;

            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var dir in Directory.GetDirectories(chunksDir))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (info.CreationTimeUtc < cutoff)
                    {
                        Directory.Delete(dir, true);
                        System.Diagnostics.Debug.WriteLine($"Cleaned up stale chunk dir: {info.Name}");
                    }
                }
                catch { /* skip locked/in-use directories */ }
            }
        }

        /// <summary>Resolve the full path to ffprobe. Checks PATH and common install locations.</summary>
        private static string ResolveFfprobePath()
        {
            var candidates = new List<string>
            {
                "ffprobe", @"C:\ffmpeg\bin\ffprobe.exe",
                @"C:\ProgramData\chocolatey\bin\ffprobe.exe",
                @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
            };

            // WinGet install locations
            var wingetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages");
            if (Directory.Exists(wingetDir))
            {
                try
                {
                    var found = Directory.GetFiles(wingetDir, "ffprobe.exe", SearchOption.AllDirectories);
                    if (found.Length > 0) candidates.Add(found[0]);
                }
                catch { }
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    using var test = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = candidate, Arguments = "-version",
                            RedirectStandardOutput = true, RedirectStandardError = true,
                            UseShellExecute = false, CreateNoWindow = true
                        }
                    };
                    test.Start();
                    test.WaitForExit(2000);
                    if (test.ExitCode == 0) return candidate;
                }
                catch { }
            }

            throw new InvalidOperationException(
                "FFprobe (part of FFmpeg) is not installed. Install via: winget install ffmpeg");
        }

        /// <summary>Resolve the full path to ffmpeg.</summary>
        private static string ResolveFfmpegPath()
        {
            var candidates = new List<string>
            {
                "ffmpeg", @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            };

            var wingetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages");
            if (Directory.Exists(wingetDir))
            {
                try
                {
                    var found = Directory.GetFiles(wingetDir, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (found.Length > 0) candidates.Add(found[0]);
                }
                catch { }
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    using var test = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = candidate, Arguments = "-version",
                            RedirectStandardOutput = true, RedirectStandardError = true,
                            UseShellExecute = false, CreateNoWindow = true
                        }
                    };
                    test.Start();
                    test.WaitForExit(2000);
                    if (test.ExitCode == 0) return candidate;
                }
                catch { }
            }

            throw new InvalidOperationException(
                "FFmpeg is not installed. Install via: winget install ffmpeg");
        }
    }
}
