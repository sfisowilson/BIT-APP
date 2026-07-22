using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// MReq 4: Checks every detected surface against the brand-safety exclusion list.
/// Surfaces matching an active BrandSafetyRule are auto-rejected.
/// Called between detection and scoring in the pipeline.
/// </summary>
public interface IBrandSafetyCheckService
{
    Task<BrandSafetyCheckResult> CheckAsync(string surfaceType, string boundaryCoordinatesJson, int sceneIndex);
}

public class BrandSafetyCheckResult
{
    public bool IsExcluded { get; set; }
    public string? ExclusionReason { get; set; }
}

public class BrandSafetyCheckService : IBrandSafetyCheckService
{
    private readonly PostgresDbContext _context;
    private readonly ILogger<BrandSafetyCheckService> _logger;

    private static readonly string[] PermanentExclusions =
    {
        "Human Faces", "Children", "Emergency Vehicles", "Government Insignia",
        "Religious Symbols", "Religious Spaces"
    };

    public BrandSafetyCheckService(PostgresDbContext context, ILogger<BrandSafetyCheckService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<BrandSafetyCheckResult> CheckAsync(string surfaceType, string boundaryCoordinatesJson, int sceneIndex)
    {
        // Load active exclusion rules from DB
        var activeRules = await _context.BrandSafetyRules
            .Where(r => r.IsActive)
            .Select(r => r.Category)
            .ToListAsync();

        // Merge with permanent hardcoded exclusions
        foreach (var perm in PermanentExclusions)
        {
            if (!activeRules.Contains(perm))
                activeRules.Add(perm);
        }

        // Check the surface type against exclusion categories
        foreach (var rule in activeRules)
        {
            var ruleLower = rule.ToLowerInvariant();
            var surfaceLower = surfaceType.ToLowerInvariant();

            if (surfaceLower.Contains(ruleLower) ||
                ruleLower.Contains("face") && surfaceLower.Contains("face") ||
                ruleLower.Contains("child") && surfaceLower.Contains("child") ||
                ruleLower.Contains("religious") && (surfaceLower.Contains("religious") || surfaceLower.Contains("church") || surfaceLower.Contains("temple")))
            {
                _logger.LogInformation("Brand-safety exclusion triggered: '{SurfaceType}' matched rule '{Rule}' (scene {Scene})",
                    surfaceType, rule, sceneIndex);

                return new BrandSafetyCheckResult
                {
                    IsExcluded = true,
                    ExclusionReason = $"Brand Safety: Excluded category '{rule}' detected on surface type '{surfaceType}'."
                };
            }
        }

        return new BrandSafetyCheckResult { IsExcluded = false };
    }
}
