using System.Globalization;
using System.Security.Claims;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Hubs;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

// Many services shell out to ffmpeg/ffprobe with decimal args built via string interpolation
// (e.g. "-ss {value:F3}"), which uses the current thread culture unless told otherwise. On a
// host whose locale uses ',' as the decimal separator (e.g. en-ZA), that produces invalid
// ffmpeg syntax ("2,000" instead of "2.000") and every such command silently fails. Force
// invariant culture process-wide — this is a backend API with no locale-dependent UI text.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// ── Configure Kestrel for large broadcast file uploads ──
var maxUploadBytes = builder.Configuration.GetValue<long>("UploadLimits:MaxVideoBytes", 10_737_418_240); // 10 GB default
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names (e.g., "fullName" instead of "FullName")
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Configure EF Core with PostgreSQL (MReq 25)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=afrobotics_bit;Username=postgres;Password=Password@1";
builder.Services.AddDbContext<PostgresDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        // Default (30s) is too short for batched saves of many large rows at once (e.g.
        // persisting SAM3 embeddings for every shot in a video with many camera cuts).
        npgsql.CommandTimeout(120)));

// Add CORS Policy for Vue.js Frontend client interaction
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendClient", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://*.run.app")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure JWT Authentication (MReq 8)
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "AFROBOTICS_BIT_SUPER_SECRET_SECURITY_KEY_2026_JWT";
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.MapInboundClaims = false; // preserve our claim types exactly as issued
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Email,
    };

    // Allow SignalR to send JWT via ?access_token= query string (WebSocket doesn't support custom headers)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
    };
});

// Register generic and specific repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICampaignRepository, CampaignRepository>();
builder.Services.AddScoped<IContentRepository, ContentRepository>();
builder.Services.AddScoped<ISurfaceRepository, SurfaceRepository>();
builder.Services.AddScoped<IRenderRepository, RenderRepository>();
builder.Services.AddScoped<IAlarmRepository, AlarmRepository>();
builder.Services.AddScoped<IAssetRepository, AssetRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();

// Register operational Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<IContentService, ContentService>();
builder.Services.AddScoped<ISurfaceService, SurfaceService>();
builder.Services.AddScoped<IRenderService, RenderService>();
builder.Services.AddScoped<IAlarmService, AlarmService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();

// ── Phase 1: Engine implementations & Factory ──
builder.Services.AddScoped<GeminiDetectionService>();
builder.Services.AddScoped<GeminiPlacementService>();
builder.Services.AddScoped<FalAiSam2Service>();
builder.Services.AddScoped<FalAiSam3Service>();
builder.Services.AddScoped<FalAiImageEmbedService>();
builder.Services.AddScoped<ShotClusteringService>();
builder.Services.AddScoped<ShotDetectionPipeline>();
builder.Services.AddScoped<ReplicateSurfaceDetectionService>();
builder.Services.AddScoped<GoogleVisionDetectionService>();
builder.Services.AddScoped<YoloSurfaceDetectionService>();
builder.Services.AddScoped<GroundingDinoDetectionService>();
builder.Services.AddScoped<GoogleVisionBrandAnalysisService>();
builder.Services.AddScoped<GeminiBrandAnalysisService>();
builder.Services.AddScoped<OpenCvCompositingService>();
builder.Services.AddScoped<PikaswapsCompositingService>();
builder.Services.AddScoped<PlanarWarpCompositingService>();
builder.Services.AddScoped<VideoChunkingService>();
builder.Services.AddScoped<KlingPromptEditService>();

// ── Phase 3: Surface tracking engine ──
builder.Services.AddScoped<Sam3TrackingService>();

builder.Services.AddScoped<IEngineFactory, EngineFactory>();
builder.Services.AddScoped<SurfaceDetectionPipeline>();

// AI Engine Resolvers — cached engine keys avoid per-request DB calls
builder.Services.AddScoped<ISurfaceDetectionService>(sp => sp.GetRequiredService<IEngineFactory>().GetSurfaceDetectionEngine());
builder.Services.AddScoped<IBrandAnalysisService>(sp => sp.GetRequiredService<IEngineFactory>().GetBrandAnalysisEngine());
builder.Services.AddScoped<ICompositingService>(sp => sp.GetRequiredService<IEngineFactory>().GetCompositingEngine());
builder.Services.AddScoped<ISurfaceTrackingService>(sp => sp.GetRequiredService<IEngineFactory>().GetTrackingEngine());

// Shot-aware tracking: tracks a placement across every shot/cut within its scene, standardizing
// both Planar and Generative paths on fal-ai/sam-3/video-rle via the resolved ISurfaceTrackingService.
builder.Services.AddScoped<IShotAwareTrackingService, ShotAwareTrackingService>();

// Brand-safety check pipeline (MReq 4)
builder.Services.AddScoped<IBrandSafetyCheckService, BrandSafetyCheckService>();

// AI placement assistant
builder.Services.AddScoped<IAiPlacementService, GeminiPlacementService>();

// Event logging (MReq 20) — emits events from pipeline stages automatically
builder.Services.AddScoped<IEventLogService, EventLogService>();

// Email notifications (MReq 12, 15) — falls back to console logging if SMTP not configured
builder.Services.AddScoped<IEmailService, EmailService>();

// SMS notifications (MReq 12) — stub, replace with Twilio/Africa's Talking in production
builder.Services.AddScoped<ISmsService, SmsService>();

// Platform settings (MReq 18) — DB-backed with appsettings.json fallback
builder.Services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();

// Hangfire job services
builder.Services.AddScoped<RenderJobService>();
builder.Services.AddScoped<SceneDetectionJobService>();

// ── SignalR — real-time push for pipeline progress, eliminating polling ──
builder.Services.AddSignalR();

// ── Hangfire — background job processing with PostgreSQL storage ──
var hangfireConnString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=afrobotics_bit;Username=postgres;Password=Password@1";
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(hangfireConnString)));
builder.Services.AddHangfireServer();

var app = builder.Build();

// Show detailed errors in dev, friendly messages in production
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseMiddleware<Afrobotics.Bit.Api.Middleware.ExceptionHandlingMiddleware>();
}

app.UseCors("AllowFrontendClient");

app.UseAuthentication();
app.UseAuthorization();

// MReq 22: Track all authenticated API requests
app.UseMiddleware<Afrobotics.Bit.Api.Middleware.UsageTrackingMiddleware>();

app.MapControllers();

// ── SignalR hub — real-time pipeline progress push ──
app.MapHub<BitHub>("/hubs/bit");

// ── Hangfire dashboard (admin-only) ──
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Afrobotics.Bit.Api.HangfireDashboardAuthFilter() }
});

// ── Recurring jobs ──
RecurringJob.AddOrUpdate<Afrobotics.Bit.Api.Controllers.ContentController>("cleanup-chunk-temp",
    c => c.CleanupChunkUploadDirectories(), Cron.Daily);

RecurringJob.AddOrUpdate<Afrobotics.Bit.Api.Controllers.UsageController>("archive-usage-records",
    c => c.ArchiveUsageRecords(), Cron.Weekly);

// Reaps Hangfire jobs orphaned by a non-graceful server restart (stuck "Processing" forever
// otherwise) — see JobsController.ReapOrphanedJobsAsync for details.
RecurringJob.AddOrUpdate<Afrobotics.Bit.Api.Controllers.JobsController>("reap-orphaned-jobs",
    c => c.ReapOrphanedJobsAsync(), "*/2 * * * *");

// Apply EF Core migrations on startup & seed initial data if database is empty
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<PostgresDbContext>();

    // Step 1: apply pending EF Core migrations
    try
    {
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        // Columns or tables may already exist from previous runs.
        logger.LogWarning(ex, "Migration warning (objects may already exist). Continuing.");
    }

    // Step 2: seed initial data (development only — skips in production)
    if (app.Environment.IsDevelopment())
    {
        try
        {
            DbSeeder.SeedInitialRecords(context);
            logger.LogInformation("Database seeding completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during database seeding.");
        }
    }
}

app.Run();
