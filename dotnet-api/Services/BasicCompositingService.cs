using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Afrobotics.Bit.Api.Services
{
    /// <summary>
    /// Basic compositing service. Returns the asset image as a preview.
    /// No external dependencies — pure .NET.
    ///
    /// TO SWAP TO RUNWAY GEN-4:
    ///   1. Create RunwayCompositingService : ICompositingService
    ///   2. Call Runway API with original frame + surface mask + asset image
    ///   3. Change DI in Program.cs: AddScoped&lt;ICompositingService, RunwayCompositingService&gt;()
    /// </summary>
    public class BasicCompositingService : ICompositingService
    {
        private readonly PostgresDbContext _context;
        private readonly IHostEnvironment _env;

        public BasicCompositingService(PostgresDbContext context, IHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public async Task<CompositedFrame> CompositeAsync(CompositingRequest request)
        {
            var sw = Stopwatch.StartNew();

            var asset = await _context.CreativeAssets.FindAsync(request.AssetId);
            if (asset == null)
                throw new ArgumentException($"Asset {request.AssetId} not found.");

            // Load asset image as base64
            var base64 = await LoadAssetAsync(asset.StorageKey);

            // Parse coordinates for logging
            ParseCoords(request.BoundaryCoordinatesJson);

            sw.Stop();

            return new CompositedFrame
            {
                ImageBase64 = base64 ?? string.Empty,
                ContentType = base64 != null ? "image/png" : "text/plain",
                EngineUsed = "BasicCompositor",
                ProcessingMs = sw.ElapsedMilliseconds
            };
        }

        private async Task<string?> LoadAssetAsync(string storageKey)
        {
            if (string.IsNullOrEmpty(storageKey)) return null;
            if (!storageKey.StartsWith("/api/assets/file/")) return null;
            var fileName = storageKey.Replace("/api/assets/file/", "");
            var path = Path.Combine(_env.ContentRootPath, "Uploads", "assets", fileName);
            if (!File.Exists(path)) return null;
            return Convert.ToBase64String(await File.ReadAllBytesAsync(path));
        }

        private void ParseCoords(string json)
        {
            try { using var _ = JsonDocument.Parse(json); } catch { /* ignore */ }
        }
    }
}
