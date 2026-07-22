using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// OpenCV-based compositing: perspective warp + alpha blend the brand asset onto the detected surface.
/// Activated when engine_compositing = "opencv".
/// Uses FFmpeg for frame extraction and OpenCV for homography warp.
/// Falls back to BasicCompositingService if OpenCV is unavailable.
/// </summary>
public class OpenCvCompositingService : ICompositingService
{
    private readonly PostgresDbContext _context;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OpenCvCompositingService> _logger;
    private readonly ICompositingService _fallback;

    public OpenCvCompositingService(PostgresDbContext context, IHostEnvironment env, ILogger<OpenCvCompositingService> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
        _fallback = new BasicCompositingService(context, env);
    }

    public async Task<CompositedFrame> CompositeAsync(CompositingRequest request)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var asset = await _context.CreativeAssets.FindAsync(request.AssetId);
            if (asset == null)
                throw new ArgumentException($"Asset {request.AssetId} not found.");

            // TODO: OpenCV homography pipeline
            // 1. Extract frame from source video using FFmpeg
            // 2. Parse boundary coordinates from request
            // 3. Compute homography matrix (source asset → target surface)
            // 4. Warp asset image with cv::warpPerspective
            // 5. Alpha blend onto target frame with cv::addWeighted
            // 6. Return composited frame as base64

            _logger.LogInformation("[OpenCV] Would warp asset {AssetId} onto surface coordinates for content {ContentId}",
                request.AssetId, request.ContentId);

            // Fall back to basic for now
            return await _fallback.CompositeAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenCV compositing failed — falling back to basic");
            sw.Stop();
            return new CompositedFrame
            {
                ImageBase64 = string.Empty,
                ContentType = "text/plain",
                EngineUsed = "OpenCvCompositor (fallback)",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }
    }
}
