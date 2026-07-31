using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// MReq 18: Reads platform settings from DB, falling back to appsettings.json defaults.
/// Writes settings to DB so they survive restarts and are editable from the admin UI.
/// </summary>
public interface IPlatformSettingsService
{
    Task<string> GetAsync(string key, string fallback = "");
    Task<int> GetIntAsync(string key, int fallback = 0);
    Task<double> GetDoubleAsync(string key, double fallback = 0.0);
    Task<bool> GetBoolAsync(string key, bool fallback = false);
    Task<Dictionary<string, string>> GetAllAsync();
    Task SetAsync(string key, string value, string? description = null);
}

public class PlatformSettingsService : IPlatformSettingsService
{
    private readonly PostgresDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformSettingsService> _logger;

    // Map setting keys to their appsettings.json fallback paths
    private static readonly Dictionary<string, string> AppsettingsFallbacks = new()
    {
        ["smtp_host"]      = "Smtp:Host",
        ["smtp_port"]      = "Smtp:Port",
        ["smtp_user"]      = "Smtp:User",
        ["smtp_password"]  = "Smtp:Password",
        ["smtp_from_email"]= "Smtp:FromEmail",
        ["upload_max_video_bytes"]  = "UploadLimits:MaxVideoBytes",
        ["upload_max_asset_bytes"]  = "UploadLimits:MaxAssetBytes",
        ["proxy_enabled"]           = "ProxySettings:Enabled",
        ["proxy_max_width"]         = "ProxySettings:MaxWidth",
        ["proxy_max_height"]        = "ProxySettings:MaxHeight",
        ["proxy_video_bitrate"]     = "ProxySettings:VideoBitrate",
        ["jwt_expiry_hours"]        = "Jwt:ExpiryHours",
        ["jwt_refresh_window_hours"]= "Jwt:RefreshWindowHours",
        ["fps_min"]                 = "Pipeline:FpsMin",
        ["fps_max"]                 = "Pipeline:FpsMax",
        ["scene_detect_threshold"]  = "Pipeline:SceneDetectThreshold",
        ["fallback_scene_secs"]     = "Pipeline:FallbackSceneSeconds",
        ["idle_timeout_minutes"]    = "Session:IdleTimeoutMinutes",
        ["idle_countdown_seconds"]  = "Session:IdleCountdownSeconds",
        ["support_email"]           = "Support:Email",
        // ── AI Engine configuration ──
        ["engine_detection"]         = "Engine:Detection",
        ["engine_brand_analysis"]    = "Engine:BrandAnalysis",
        ["engine_compositing"]       = "Engine:Compositing",
        ["replicate_api_key"]        = "Engine:ReplicateApiKey",
        ["google_vision_api_key"]    = "Engine:GoogleVisionApiKey",
        ["gemini_api_key"]           = "Engine:GeminiApiKey",
        ["falai_api_key"]            = "Engine:FalAiApiKey",
        // ── Replicate cloud API settings ──
        ["replicate_gd_model"]       = "Engine:ReplicateGdModel",      // deprecated — kept for rollback
        ["replicate_sam_model"]      = "Engine:ReplicateSamModel",     // deprecated — kept for rollback
        ["replicate_sam3_model"]     = "Engine:ReplicateSam3Model",
        ["replicate_sam3_version"]   = "Engine:ReplicateSam3Version",    // version hash — pin this to avoid silent upstream changes
        ["replicate_box_threshold"]  = "Engine:ReplicateBoxThreshold",
        ["replicate_text_threshold"] = "Engine:ReplicateTextThreshold",
        // ── Gemini model selection ──
        ["gemini_model"]             = "Engine:GeminiModel",
        ["gemini_timeout_seconds"]   = "Engine:GeminiTimeoutSeconds",  // per-request HTTP timeout; generous default to survive rate-limit backoff
        ["yolo_service_url"]         = "Engine:YoloServiceUrl",
        // ── Fal.ai SAM 3 ──
        ["falai_sam3_endpoint"]      = "Engine:Sam3Endpoint",
        ["yolo_model_size"]          = "Engine:YoloModelSize",
        ["yolo_confidence"]          = "Engine:YoloConfidence",
        ["yolo_iou"]                 = "Engine:YoloIou",
        ["yolo_frame_skip"]         = "Engine:YoloFrameSkip",
        // ── Phase 2: Grounding DINO v2 engine ──
        ["gd_model_variant"]         = "Engine:GdModelVariant",
        ["gd_box_threshold"]         = "Engine:GdBoxThreshold",
        ["gd_text_threshold"]        = "Engine:GdTextThreshold",
        ["gd_enable_sam"]            = "Engine:GdEnableSam",
        ["gd_enable_depth"]          = "Engine:GdEnableDepth",
        ["gd_enable_brand_safety"]   = "Engine:GdEnableBrandSafety",
        // ── Phase 3: Tracking & adaptive frame-skip ──
        ["gd_enable_tracking"]       = "Engine:GdEnableTracking",
        ["gd_adaptive_frame_skip"]   = "Engine:GdAdaptiveFrameSkip",
        ["gd_detection_interval"]    = "Engine:GdDetectionInterval",
        ["gd_flow_motion_threshold"] = "Engine:GdFlowMotionThreshold",
        ["gd_track_min_frames"]      = "Engine:GdTrackMinFrames",
        // ── Phase 3: Surface tracking ──
        ["engine_tracking"]           = "Engine:Tracking",
    };

    public PlatformSettingsService(PostgresDbContext context, IConfiguration config, ILogger<PlatformSettingsService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetAsync(string key, string fallback = "")
    {
        try
        {
            var setting = await _context.PlatformSettings.FindAsync(key);
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                return setting.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read platform setting '{Key}' from DB", key);
        }

        // Fallback: appsettings.json
        if (AppsettingsFallbacks.TryGetValue(key, out var configPath))
        {
            var configValue = _config[configPath];
            if (!string.IsNullOrEmpty(configValue))
                return configValue;
        }

        return fallback;
    }

    public async Task<int> GetIntAsync(string key, int fallback = 0)
    {
        var val = await GetAsync(key);
        return int.TryParse(val, out var result) ? result : fallback;
    }

    public async Task<double> GetDoubleAsync(string key, double fallback = 0.0)
    {
        var val = await GetAsync(key);
        return double.TryParse(val,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result) ? result : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback = false)
    {
        var val = await GetAsync(key);
        return bool.TryParse(val, out var result) ? result : fallback;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        var result = new Dictionary<string, string>();

        // Load from DB
        try
        {
            var dbSettings = await _context.PlatformSettings.ToListAsync();
            foreach (var s in dbSettings)
            {
                // Skip internal/comment keys
                if (s.Key.StartsWith("_")) continue;
                result[s.Key] = s.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load platform settings from DB");
        }

        // Fill gaps from appsettings.json
        foreach (var (key, configPath) in AppsettingsFallbacks)
        {
            if (!result.ContainsKey(key) && !key.StartsWith("_"))
            {
                var configValue = _config[configPath];
                if (!string.IsNullOrEmpty(configValue))
                    result[key] = configValue;
            }
        }

        return result;
    }

    public async Task SetAsync(string key, string value, string? description = null)
    {
        try
        {
            var setting = await _context.PlatformSettings.FindAsync(key);
            if (setting == null)
            {
                setting = new PlatformSetting
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.PlatformSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                if (description != null) setting.Description = description;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Platform setting '{Key}' updated to '{Value}'", key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist platform setting '{Key}'", key);
            throw;
        }
    }
}
