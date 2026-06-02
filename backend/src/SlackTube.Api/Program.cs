using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Endpoints;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Secrets;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- options -------------------------------------------------------------------------
builder.Services.Configure<SlackOptions>(builder.Configuration.GetSection(SlackOptions.Section));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.Section));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
builder.Services.Configure<TokenEncryptionOptions>(builder.Configuration.GetSection(TokenEncryptionOptions.Section));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));

var pgConn = builder.Configuration.GetConnectionString("Postgres")
             ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
var redisConn = builder.Configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

// ---- data ----------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pgConn));

// ---- redis (lazy connect so the app still boots if Redis is momentarily unavailable) --
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var cfg = ConfigurationOptions.Parse(redisConn);
    cfg.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(cfg);
});

// ---- hangfire (PostgreSQL storage) ---------------------------------------------------
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(pgConn)));
builder.Services.AddHangfireServer(o => o.WorkerCount = 2);

// ---- typed HTTP client ---------------------------------------------------------------
builder.Services.AddHttpClient<SlackClient>();

// ---- singletons (stateless / Redis / in-memory) --------------------------------------
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
builder.Services.AddSingleton<IDedupService, DedupService>();
builder.Services.AddSingleton<ICancellationFlags, CancellationFlags>();
builder.Services.AddSingleton<IQuotaService, QuotaService>();
builder.Services.AddSingleton<IProgressTracker, ProgressTracker>();
builder.Services.AddSingleton<SlackSignatureVerifier>();
builder.Services.AddSingleton<SlackTemplateParser>();
builder.Services.AddSingleton<GoogleCredentialFactory>();
builder.Services.AddSingleton<DriveDownloadService>();
builder.Services.AddSingleton<YouTubeUploadService>();
builder.Services.AddSingleton<ISlackStatusService, SlackStatusService>();

// ---- scoped (touch the DbContext) ----------------------------------------------------
builder.Services.AddScoped<ISettingsStore, SettingsStore>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<SlackIngestService>();
builder.Services.AddScoped<UploadJobHandler>();

// ---- CORS (dev convenience; the Next.js BFF calls server-side and wouldn't need it) ---
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true)));

var app = builder.Build();
app.UseCors();

// ---- apply EF migrations on startup --------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapGet("/", () => Results.Ok(new { service = "SlackTube", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapSlackEndpoints();
app.MapGoogleOAuthEndpoints();
app.MapAdminApiEndpoints();

// Hangfire dashboard — local-request-only by default; secure with an auth filter for remote use.
app.MapHangfireDashboard("/hangfire");

app.Run();
