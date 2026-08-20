using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// Calls Fal.ai SAM 3 Image Embedding endpoint to generate visual embeddings
/// from video keyframes. Embeddings power shot-to-shot clustering for scene
/// boundary detection based on visual similarity.
///
/// Endpoint: POST https://queue.fal.run/fal-ai/sam-3/image/embed
/// Returns: base64-encoded float embedding vector
/// </summary>
public class FalAiImageEmbedService
{
    private readonly IPlatformSettingsService _settings;
    private readonly ILogger<FalAiImageEmbedService> _logger;
    private readonly IEventLogService _eventLog;
    private readonly HttpClient _http;

    private const string DefaultEndpoint = "https://queue.fal.run/fal-ai/sam-3/image/embed";
    private const string DefaultQueueBase = "https://queue.fal.run/fal-ai/sam-3";

    public FalAiImageEmbedService(IPlatformSettingsService settings, ILogger<FalAiImageEmbedService> logger, IEventLogService eventLog)
    {
        _settings = settings;
        _logger = logger;
        _eventLog = eventLog;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    /// <summary>
    /// Generate an embedding vector for a single image (keyframe).
    /// </summary>
    /// <param name="imageUrl">Publicly accessible URL of the keyframe image.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Float array embedding, or null on failure.</returns>
    public async Task<float[]?> EmbedAsync(string imageUrl, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync("falai_api_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("[ImageEmbed] falai_api_key not configured");
            return null;
        }

        var endpoint = await _settings.GetAsync("sam3_embed_endpoint", DefaultEndpoint);
        var queueBase = await _settings.GetAsync("sam3_embed_queue_base", DefaultQueueBase);

        try
        {
            var payload = new { image_url = imageUrl };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

            _logger.LogInformation("[ImageEmbed] Submitting embed for {Url}", imageUrl);

            // ── Step 1: Submit ──
            var submitResponse = await _http.PostAsync(endpoint, content, ct);
            var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);

            if (!submitResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("[ImageEmbed] HTTP {Status}: {Body}",
                    (int)submitResponse.StatusCode, Truncate(submitJson, 500));
                return null;
            }

            // ── Step 2: Parse response ──
            string? requestId = null;
            string? embeddingB64 = null;

            using var doc = JsonDocument.Parse(submitJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("request_id", out var rid))
                requestId = rid.GetString();

            if (root.TryGetProperty("embedding_b64", out var emb))
                embeddingB64 = emb.GetString();

            // ── Step 3: Poll if async (request_id returned but no embedding yet) ──
            if (embeddingB64 == null && !string.IsNullOrEmpty(requestId))
            {
                _logger.LogInformation("[ImageEmbed] Polling request {RequestId}", requestId);
                embeddingB64 = await PollForEmbeddingAsync(queueBase, requestId, ct);
            }

            if (string.IsNullOrEmpty(embeddingB64))
            {
                _logger.LogWarning("[ImageEmbed] No embedding returned for {Url}", imageUrl);
                return null;
            }

            return DecodeEmbedding(embeddingB64);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ImageEmbed] Failed for {Url}", imageUrl);
            return null;
        }
    }

    /// <summary>
    /// Batch-embed multiple keyframe images. Submits all in parallel, then polls
    /// pending requests together.
    /// </summary>
    /// <param name="contentId">Used only to attribute the DB event log entry if embeddings fail.</param>
    public async Task<Dictionary<int, float[]?>> EmbedBatchAsync(
        Dictionary<int, string> shotIndexToUrl,
        CancellationToken ct = default,
        Func<int, int, Task>? onProgress = null,
        string? contentId = null)
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<int, float[]?>();
        var total = shotIndexToUrl.Count;
        var completed = 0;
        var failed = 0;

        // Each embed can involve polling fal.ai's queue for up to 5 minutes, so running these
        // strictly sequentially (as before) made a shot-heavy video (dozens to hundreds of
        // shots) take tens of minutes just for embedding — before scene clustering or surface
        // detection even start. Bounded parallelism keeps this from overwhelming fal.ai's API
        // while cutting wall-clock time roughly by the concurrency factor.
        var concurrencyStr = await _settings.GetAsync("sam3_embed_concurrency", "6");
        var concurrency = int.TryParse(concurrencyStr, out var cVal) && cVal >= 1 ? cVal : 6;
        using var semaphore = new SemaphoreSlim(concurrency);

        async Task EmbedOneAsync(int index, string url)
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await EmbedAsync(url, ct);
                results[index] = result;
                if (result == null) Interlocked.Increment(ref failed);
            }
            finally
            {
                semaphore.Release();
            }

            var completedSoFar = Interlocked.Increment(ref completed);
            if (onProgress != null)
            {
                try
                {
                    await onProgress(completedSoFar, total);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ImageEmbed] onProgress callback failed at {Completed}/{Total}", completedSoFar, total);
                }
            }
        }

        await Task.WhenAll(shotIndexToUrl.Select(kvp => EmbedOneAsync(kvp.Key, kvp.Value)));

        // Failures are otherwise only visible in console logs — surface an aggregate summary
        // to the DB event log so a fully-broken embedding pass (e.g. an unreachable base URL)
        // is actually noticed instead of silently degrading scene clustering.
        if (failed > 0)
        {
            var severity = failed == total ? "Error" : "Warning";
            await _eventLog.LogEventAsync("SceneDetection", "EMBEDDING_FAILURES", severity,
                $"{failed}/{total} keyframe embeddings failed" +
                (contentId != null ? $" for content {contentId}" : "") +
                (failed == total ? " (all failed — check falai_api_key and sam3_video_base_url reachability)." : "."));
        }

        return new Dictionary<int, float[]?>(results);
    }

    // ── Polling ──

    private async Task<string?> PollForEmbeddingAsync(string queueBase, string requestId, CancellationToken ct)
    {
        var statusUrl = $"{queueBase}/requests/{requestId}/status";
        var resultUrl = $"{queueBase}/requests/{requestId}";

        for (int attempt = 0; attempt < 30; attempt++) // ~5 min max at 10s intervals
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Key {await _settings.GetAsync("falai_api_key")}");

            var statusResponse = await _http.GetAsync(statusUrl, ct);
            var statusJson = await statusResponse.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(statusJson);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

            if (status == "COMPLETED")
            {
                var resultResponse = await _http.GetAsync(resultUrl, ct);
                var resultJson = await resultResponse.Content.ReadAsStringAsync(ct);
                using var rDoc = JsonDocument.Parse(resultJson);
                var rRoot = rDoc.RootElement;
                if (rRoot.TryGetProperty("embedding_b64", out var emb))
                    return emb.GetString();
                return null;
            }

            if (status is "FAILED" or "CANCELLED")
            {
                _logger.LogWarning("[ImageEmbed] Request {RequestId} {Status}", requestId, status);
                return null;
            }
        }

        _logger.LogWarning("[ImageEmbed] Request {RequestId} timed out after 30 polls", requestId);
        return null;
    }

    // ── Decoding ──

    private static float[]? DecodeEmbedding(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
