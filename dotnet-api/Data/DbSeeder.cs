using System;
using System.Linq;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Data
{
    /// <summary>
    /// Development seed data for initial database population.
    /// Not for production use — replace with proper data migration in production.
    /// </summary>
    public static class DbSeeder
    {
        public static void SeedInitialRecords(PostgresDbContext context)
        {
            // ── Users ──────────────────────────────────────────────
            if (!context.Users.Any())
            {
                context.Users.AddRange(
                    new User
                    {
                        Id = "usr-01",
                        FullName = "Sabelo Nkosi",
                        Email = "admin@afrobotics.co.za",
                        PasswordHash = "admin123",
                        Role = "Admin",
                        AccountStatus = "Active",
                        LastLoginAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = "usr-02",
                        FullName = "Sfiso Dlamini",
                        Email = "loverboy.sfiso@gmail.com",
                        PasswordHash = "editor123",
                        Role = "Editor",
                        AccountStatus = "Active",
                        LastLoginAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = "usr-03",
                        FullName = "Thabo Ndlovu",
                        Email = "advertiser@afrobotics.co.za",
                        PasswordHash = "advertiser123",
                        Role = "Advertiser",
                        AccountStatus = "Active",
                        LastLoginAt = DateTime.UtcNow
                    }
                );
            }

            // ── ContentItems ───────────────────────────────────────
            if (!context.ContentItems.Any())
            {
                context.ContentItems.AddRange(
                    new ContentItem
                    {
                        Id = "v-01",
                        Title = "Orlando Pirates vs Kaizer Chiefs - SADC Derby Main Match",
                        Duration = "01:30:00",
                        Resolution = "1920x1080 (1080p)",
                        FrameRate = 50,
                        SourceChannel = "SuperSport Variety 4",
                        StorageKey = "s3://afrobotics-raw-ingest/derby_pirates_chiefs_2026.mxf",
                        IngestionStatus = "Completed",
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new ContentItem
                    {
                        Id = "v-02",
                        Title = "M1 Gauteng Highway Aerial Drone - Advertising Survey Route",
                        Duration = "00:03:15",
                        Resolution = "3840x2160 (4K)",
                        FrameRate = 60,
                        SourceChannel = "Direct Upload (Drone-04)",
                        StorageKey = "s3://afrobotics-raw-ingest/gauteng_highway_survey.mp4",
                        IngestionStatus = "Completed",
                        CreatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new ContentItem
                    {
                        Id = "v-03",
                        Title = "Staged Living Room Segment - OTT Interactive Screen Test",
                        Duration = "00:05:40",
                        Resolution = "1920x1080 (1080p)",
                        FrameRate = 25,
                        SourceChannel = "Studio Ingest Box A",
                        StorageKey = "s3://afrobotics-raw-ingest/living_room_ott_tests.mov",
                        IngestionStatus = "Staging",
                        CreatedAt = DateTime.UtcNow
                    }
                );
            }

            // ── SceneItems ─────────────────────────────────────────
            if (!context.SceneItems.Any())
            {
                context.SceneItems.AddRange(
                    new SceneItem { Id = "s-01", ContentId = "v-01", StartFrame = 0, EndFrame = 1500, SceneIndex = 1, DurationSeconds = 30, QaStatus = "Approved" },
                    new SceneItem { Id = "s-02", ContentId = "v-01", StartFrame = 1500, EndFrame = 4500, SceneIndex = 2, DurationSeconds = 60, QaStatus = "Approved" },
                    new SceneItem { Id = "s-03", ContentId = "v-01", StartFrame = 4500, EndFrame = 7500, SceneIndex = 3, DurationSeconds = 60, QaStatus = "Unchecked" },
                    new SceneItem { Id = "s-04", ContentId = "v-02", StartFrame = 0, EndFrame = 1800, SceneIndex = 1, DurationSeconds = 30, QaStatus = "Approved" },
                    new SceneItem { Id = "s-05", ContentId = "v-02", StartFrame = 1800, EndFrame = 5400, SceneIndex = 2, DurationSeconds = 60, QaStatus = "Approved" }
                );
            }

            // ── SurfaceItems ──────────────────────────────────────
            if (!context.SurfaceItems.Any())
            {
                context.SurfaceItems.AddRange(
                    new SurfaceItem
                    {
                        Id = "sf-01", SceneId = "s-01",
                        SurfaceType = "Stadium Perimeter LED Board",
                        BoundaryCoordinatesJson = "[{\"x\":102,\"y\":720},{\"x\":890,\"y\":720},{\"x\":895,\"y\":790},{\"x\":100,\"y\":790}]",
                        EstimatedDepth = 18.5,
                        OrientationVectorJson = "{\"yaw\":2,\"pitch\":-1,\"roll\":0}",
                        ConfidenceScore = 0.94, ViabilityScore = 0.88, Status = "Candidate"
                    },
                    new SurfaceItem
                    {
                        Id = "sf-02", SceneId = "s-01",
                        SurfaceType = "Spectator Face (Close-up)",
                        BoundaryCoordinatesJson = "[{\"x\":450,\"y\":210},{\"x\":510,\"y\":210},{\"x\":510,\"y\":280},{\"x\":450,\"y\":280}]",
                        EstimatedDepth = 4.2,
                        OrientationVectorJson = "{\"yaw\":45,\"pitch\":10,\"roll\":5}",
                        ConfidenceScore = 0.98, ViabilityScore = 0.0, Status = "Excluded",
                        ExclusionReason = "MReq 4 (Brand Safety Violation): Face detection filter permanently triggered."
                    },
                    new SurfaceItem
                    {
                        Id = "sf-03", SceneId = "s-02",
                        SurfaceType = "Mid-pitch Stadium 3D Grass Mat",
                        BoundaryCoordinatesJson = "[{\"x\":300,\"y\":550},{\"x\":980,\"y\":570},{\"x\":1100,\"y\":680},{\"x\":120,\"y\":640}]",
                        EstimatedDepth = 22.1,
                        OrientationVectorJson = "{\"yaw\":-5,\"pitch\":-22,\"roll\":2}",
                        ConfidenceScore = 0.89, ViabilityScore = 0.92, Status = "Approved"
                    },
                    new SurfaceItem
                    {
                        Id = "sf-04", SceneId = "s-02",
                        SurfaceType = "Pre-existing Coca-Cola Pitch Sign",
                        BoundaryCoordinatesJson = "[{\"x\":50,\"y\":520},{\"x\":180,\"y\":520},{\"x\":180,\"y\":560},{\"x\":50,\"y\":560}]",
                        EstimatedDepth = 28.0,
                        OrientationVectorJson = "{\"yaw\":-12,\"pitch\":-5,\"roll\":0}",
                        ConfidenceScore = 0.96, ViabilityScore = 0.15, Status = "Excluded",
                        ExclusionReason = "Competitive Separation: Active Coca-Cola billboard pre-detected in-scene."
                    },
                    new SurfaceItem
                    {
                        Id = "sf-05", SceneId = "s-04",
                        SurfaceType = "Highway Overhead Gantry Board",
                        BoundaryCoordinatesJson = "[{\"x\":400,\"y\":150},{\"x\":880,\"y\":170},{\"x\":880,\"y\":320},{\"x\":400,\"y\":300}]",
                        EstimatedDepth = 35.4,
                        OrientationVectorJson = "{\"yaw\":1,\"pitch\":5,\"roll\":-1}",
                        ConfidenceScore = 0.95, ViabilityScore = 0.96, Status = "Candidate"
                    }
                );
            }

            // ── CampaignItems ────────────────────────────────────
            if (!context.Campaigns.Any())
            {
                context.Campaigns.AddRange(
                    new CampaignItem
                    {
                        Id = "c-01", Name = "Coca-Cola SADC Winter Oasis",
                        NamingStructureCode = "UZ01EP12_COKE",
                        ScheduleStart = DateTime.UtcNow.AddDays(-30),
                        ScheduleEnd = DateTime.UtcNow.AddDays(60),
                        TargetRegion = "SADC (Zambia, Zimbabwe, SA)",
                        TotalBudget = 450000, Status = "Active",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    },
                    new CampaignItem
                    {
                        Id = "c-02", Name = "Nike AirMax Streetwear Launch",
                        NamingStructureCode = "UZ02EP04_NIKE",
                        ScheduleStart = DateTime.UtcNow.AddDays(-15),
                        ScheduleEnd = DateTime.UtcNow.AddDays(15),
                        TargetRegion = "Gauteng Metro",
                        TotalBudget = 280000, Status = "Active",
                        CreatedAt = DateTime.UtcNow.AddDays(-15)
                    },
                    new CampaignItem
                    {
                        Id = "c-03", Name = "Samsung Neo-QLED Showcase",
                        NamingStructureCode = "UZ05EP08_SAMS",
                        ScheduleStart = DateTime.UtcNow.AddDays(28),
                        ScheduleEnd = DateTime.UtcNow.AddDays(58),
                        TargetRegion = "Nationwide South Africa",
                        TotalBudget = 620000, Status = "Draft",
                        CreatedAt = DateTime.UtcNow
                    }
                );
            }

            // ── CreativeAssets ──────────────────────────────────
            if (!context.CreativeAssets.Any())
            {
                context.CreativeAssets.AddRange(
                    new CreativeAsset { Id = "as-01", Name = "Coke Classic Red Landscape Banner", Type = "Image", StorageKey = "s3://afrobotics-assets/coke_classic_red_banner.png", FileSize = "1.2 MB", Dimensions = "1920x540", BrandCategory = "Beverages (Non-Alcoholic)", CampaignId = "c-01" },
                    new CreativeAsset { Id = "as-02", Name = "Nike Swoosh High-Contrast White", Type = "Logo", StorageKey = "s3://afrobotics-assets/nike_swoosh_alpha.png", FileSize = "450 KB", Dimensions = "1024x1024", BrandCategory = "Apparel & Footwear", CampaignId = "c-02" },
                    new CreativeAsset { Id = "as-03", Name = "Samsung Neon Glow Video Overlay", Type = "Video", StorageKey = "s3://afrobotics-assets/samsung_glow_h264.mp4", FileSize = "18.4 MB", Dimensions = "1920x1080", BrandCategory = "Consumer Electronics", CampaignId = "c-03" }
                );
            }

            // ── RenderItems ─────────────────────────────────────
            if (!context.Renders.Any())
            {
                context.Renders.Add(
                    new RenderItem
                    {
                        Id = "r-01", ContentId = "v-01", SurfaceId = "sf-03",
                        CampaignId = "c-01", AssetId = "as-01",
                        ExportPreset = "Broadcast-ProRes",
                        StorageKey = "s3://afrobotics-finished-renders/rendered_derby_coke_final.mxf",
                        RenderStatus = "Finished", Progress = 100,
                        ProcessingDurationMs = 42500,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    }
                );
            }

            // ── EventLogs ───────────────────────────────────────
            if (!context.EventLogs.Any())
            {
                context.EventLogs.AddRange(
                    new EventLog { Id = "l-01", Timestamp = DateTime.UtcNow.AddHours(-2), EventCode = "AUTH_JWT_SUCCESS", Severity = "Info", Module = "IdentityGateway", User = "loverboy.sfiso@gmail.com", Description = "User logged in successfully from authorized workspace context." },
                    new EventLog { Id = "l-02", Timestamp = DateTime.UtcNow.AddHours(-1.5), EventCode = "INGEST_META_FFMPEG", Severity = "Info", Module = "IngestionService", User = "System", Description = "Extracted metadata stream for v-02: 4K (3840x2160) at 60fps." },
                    new EventLog { Id = "l-03", Timestamp = DateTime.UtcNow.AddHours(-1.2), EventCode = "AI_EXCLUSION_TRIGGERED", Severity = "Warning", Module = "BrandSafetyClassifier", User = "System", Description = "Exclusion triggered on Scene 1 spectator face overlay (MReq 4 violation)." },
                    new EventLog { Id = "l-04", Timestamp = DateTime.UtcNow.AddHours(-0.5), EventCode = "GPU_NODE_PRORES_EXPORT", Severity = "Info", Module = "CompositingEngine", User = "System", Description = "Render composite job completed successfully in 42.5 seconds on GPU Node #03." }
                );
            }

            // ── AlarmItems ──────────────────────────────────────
            if (!context.Alarms.Any())
            {
                context.Alarms.AddRange(
                    new AlarmItem { Id = "al-01", Timestamp = DateTime.UtcNow.AddHours(-12), Severity = "Minor", Source = "SMTP Gateway Relay", Description = "Delay detected in cellular SMS queue gateway fallback stream.", IsActive = false },
                    new AlarmItem { Id = "al-02", Timestamp = DateTime.UtcNow.AddSeconds(-5), Severity = "Critical", Source = "GPU Render Node #02", Description = "Critical hardware timeout: VRAM capacity exceeded under concurrent batch composite loading.", IsActive = true }
                );
            }

            context.SaveChanges();
        }
    }
}
