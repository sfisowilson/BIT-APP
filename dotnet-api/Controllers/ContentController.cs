using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
        // Supported broadcast containers and codecs for validation
        private static readonly HashSet<string> SupportedContainers = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".webm" };
        private static readonly HashSet<string> SupportedVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
            { "h264", "h265", "hevc", "prores", "dnxhd", "dnxhr", "mpeg2video", "mpeg4", "vp9", "av1", "mjpeg", "mjp2" };

        public ContentController(IContentService contentService, IHostEnvironment env, IConfiguration config)
        {
            _contentService = contentService;
            _env = env;
            _config = config;
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
                    BackgroundJob.Enqueue(() => GenerateProxyAsync(filePath, proxyPath));
                }
                else
                {
                    storageKey = $"s3://afrobotics-raw-ingest/{title.Replace(" ", "_").ToLower()}.mov";
                }

                // Extract real metadata from the uploaded file via FFprobe
                var actualDuration = duration;
                var actualFps = frameRate;
                var actualResolution = resolution;

                if (savedFilePath != null)
                {
                    try
                    {
                        (actualDuration, actualFps, actualResolution) = ExtractVideoMetadata(savedFilePath);
                    }
                    catch
                    {
                        // FFprobe not available — use browser-provided values
                    }
                }

                var dto = new IngestVideoDto
                {
                    Title = title,
                    Resolution = actualResolution,
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

                // Generate proxy
                var proxyDir = Path.Combine(uploadsDir, "proxy");
                Directory.CreateDirectory(proxyDir);
                var proxyName = $"{Path.GetFileNameWithoutExtension(safeName)}_proxy.mp4";
                var proxyPath = Path.Combine(proxyDir, proxyName);
                BackgroundJob.Enqueue(() => GenerateProxyAsync(finalPath, proxyPath));

                // Extract metadata
                var (actualDuration, actualFps, actualResolution) = ExtractVideoMetadata(finalPath);

                var ingestDto = new IngestVideoDto
                {
                    Title = dto.Title ?? Path.GetFileNameWithoutExtension(session.FileName),
                    Resolution = actualResolution,
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

        /// <summary>Serve uploaded video files for playback with range support.</summary>
        [HttpGet("file/{fileName}")]
        [AllowAnonymous]
        public IActionResult GetVideoFile(string fileName)
        {
            var uploadsDir = Path.Combine(_env.ContentRootPath, "Uploads");
            var filePath = Path.Combine(uploadsDir, fileName);

            // Prevent directory traversal
            if (!filePath.StartsWith(uploadsDir) || !System.IO.File.Exists(filePath))
                return NotFound();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ext switch
            {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mxf" => "application/mxf",
                ".webm" => "video/webm",
                _ => "application/octet-stream"
            };

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

        /// <summary>Reset content to SceneDetecting stage (re-run scene detection).</summary>
        [HttpPost("{id}/redetect-scenes")]
        public async Task<IActionResult> RedetectScenes(string id)
        {
            try
            {
                var content = await _contentService.GetContentByIdAsync(id);
                if (content == null) return NotFound(new { error = "Content not found." });

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

                content = await _contentService.TransitionStageAsync(id, PipelineStages.SceneDetecting);
                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    ingestionStatus = content.IngestionStatus,
                    message = $"Scene detection restarted for '{content.Title}'."
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
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
        /// Extract actual duration, frame rate, and resolution from a video file using FFprobe.
        /// Returns (duration "HH:MM:SS", fps, resolution "WxH").
        /// </summary>
        private static (string duration, int fps, string resolution) ExtractVideoMetadata(string filePath)
        {
            var ffprobePath = ResolveFfprobePath();

            var args = $"-v error -select_streams v:0 -show_entries stream=duration,r_frame_rate,width,height -of csv=p=0 \"{filePath}\"";
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
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // Output format: duration,fps,width,height  e.g. "125.360000,25/1,1920,1080"
            var parts = output.Split(',');
            var durationSeconds = parts.Length > 0 && double.TryParse(parts[0], out var d) ? d : 300;
            var fpsStr = parts.Length > 1 ? parts[1] : "25/1";
            var width = parts.Length > 2 && int.TryParse(parts[2], out var w) ? w : 1920;
            var height = parts.Length > 3 && int.TryParse(parts[3], out var h) ? h : 1080;

            // Parse FPS as a fraction (e.g. "25/1" or "30000/1001")
            var fps = 25;
            if (fpsStr.Contains('/'))
            {
                var frac = fpsStr.Split('/');
                if (frac.Length == 2 && double.TryParse(frac[0], out var num) && double.TryParse(frac[1], out var den) && den > 0)
                    fps = (int)Math.Round(num / den);
            }
            else
            {
                int.TryParse(fpsStr, out fps);
            }
            if (fps <= 0) fps = 25;

            // Format duration as HH:MM:SS
            var ts = TimeSpan.FromSeconds(durationSeconds);
            var durationFormatted = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            var resolution = $"{width}x{height}";
            if (width >= 3840) resolution = "3840x2160 (4K)";
            else if (width >= 1920) resolution = "1920x1080 (1080p)";
            else if (width >= 1280) resolution = "1280x720 (720p)";

            return (durationFormatted, fps, resolution);
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
