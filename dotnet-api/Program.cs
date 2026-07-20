using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Repositories;
using Afrobotics.Bit.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
    options.UseNpgsql(connectionString));

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
var key = Encoding.ASCII.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
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

// Compositing pipeline — swap implementation to change AI engine
// Current:  BasicCompositingService (System.Drawing)
// Future:   builder.Services.AddScoped<ICompositingService, RunwayCompositingService>();
builder.Services.AddScoped<ICompositingService, BasicCompositingService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseCors("AllowFrontendClient");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
        // Tables may already exist from a previous EnsureCreated() call.
        // Log a warning and continue — the seed step below will still run.
        logger.LogWarning(ex, "Migration failed (tables may already exist). Continuing to seed step.");
    }

    // Step 2: seed initial data (only inserts if tables are empty)
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

app.Run();
