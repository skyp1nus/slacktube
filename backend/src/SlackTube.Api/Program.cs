using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
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
builder.Services.AddSingleton<IApiUsageService, ApiUsageService>();
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
builder.Services.AddScoped<ChannelMappingService>();
builder.Services.AddScoped<GoogleOAuthClientService>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<SlackWorkspaceService>();
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

// ---- seed the env OAuth client + backfill legacy accounts (backward compat) -----------
// YouTube OAuth clients are now UI-managed (one per Cloud project). For a deploy that still
// configures the single Google__ env client, seed it as "Default (env)" when none exist, then
// bind every pre-existing account to it (their refresh tokens were issued by that client). Once
// UI clients exist, GoogleOptions is just an inert seed/fallback.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
    var googleOpt = scope.ServiceProvider.GetRequiredService<IOptions<GoogleOptions>>().Value;
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.GoogleClientSeed");

    var hasEnvCreds = !string.IsNullOrWhiteSpace(googleOpt.ClientId) && !string.IsNullOrWhiteSpace(googleOpt.ClientSecret);

    // The env client is the issuer of EVERY pre-existing account, so it's the only client we may
    // safely backfill orphans onto. Reuse an already-seeded one (matched by client id) or seed it
    // when the table is still empty (the first boot on this version).
    GoogleOAuthClient? envClient = null;
    if (hasEnvCreds)
    {
        envClient = db.GoogleOAuthClients.FirstOrDefault(c => c.ClientId == googleOpt.ClientId);
        if (envClient is null && !db.GoogleOAuthClients.Any())
        {
            var now = DateTimeOffset.UtcNow;
            envClient = new GoogleOAuthClient
            {
                Label = "Default (env)",
                ClientId = googleOpt.ClientId,
                EncryptedClientSecret = protector.Protect(googleOpt.ClientSecret),
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.GoogleOAuthClients.Add(envClient);
            db.SaveChanges();
            log.LogInformation("Seeded default OAuth client {Id} from Google__ env config", envClient.Id);
        }
    }

    var orphans = db.GoogleAccounts.Where(a => a.OAuthClientId == null).ToList();
    if (orphans.Count > 0)
    {
        // Bind ONLY to the env client (the real issuer) — never to an arbitrary UI client, which
        // could not refresh these tokens (hard constraint: token ↔ issuing client is permanent).
        if (envClient is not null)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var a in orphans) { a.OAuthClientId = envClient.Id; a.UpdatedAt = now; }
            db.SaveChanges();
            log.LogInformation("Backfilled {Count} Google account(s) onto env OAuth client {Client}", orphans.Count, envClient.Id);
        }
        else
        {
            log.LogWarning("{Count} Google account(s) have no OAuth client — set Google__ClientId/Secret to their original (issuing) client to migrate them, or reconnect the accounts.", orphans.Count);
        }
    }
}

// ---- recover jobs interrupted by a restart -------------------------------------------
// Queued/Downloading happen BEFORE the YouTube upload → safe to resume from scratch (re-enqueue).
// Uploading/Processing are the point of no return → never re-upload; fail with a verify note
// (unless the video id was already saved, in which case the upload finished → mark Done).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var backgroundJobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.JobRecovery");

    var interrupted = db.Jobs.Where(j =>
        j.State == JobState.Queued || j.State == JobState.Downloading ||
        j.State == JobState.Uploading || j.State == JobState.Processing).ToList();

    int resumed = 0, failed = 0, finalized = 0;
    var now = DateTimeOffset.UtcNow;
    foreach (var job in interrupted)
    {
        if (job.YouTubeVideoId is not null)
        {
            job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Done, At = now, Note = "recovered: upload had completed" });
            job.State = JobState.Done; job.UpdatedAt = now; finalized++;
        }
        else if (job.State is JobState.Uploading or JobState.Processing)
        {
            job.ErrorMessage = "Interrupted after the YouTube upload started — verify in YouTube Studio; the bot won’t re-upload.";
            job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Failed, At = now, Note = "recovered: interrupted past point of no return" });
            job.State = JobState.Failed; job.UpdatedAt = now; failed++;
        }
        else // Queued or Downloading — resume from scratch
        {
            if (job.State == JobState.Downloading)
            {
                job.History.Add(new JobStateHistory { JobId = job.Id, FromState = job.State, ToState = JobState.Queued, At = now, Note = "recovered: re-queued after restart" });
                job.State = JobState.Queued; job.UpdatedAt = now;
            }
            backgroundJobs.Enqueue<UploadJobHandler>(h => h.RunAsync(job.Id, CancellationToken.None));
            resumed++;
        }
    }
    if (interrupted.Count > 0)
    {
        db.SaveChanges();
        log.LogInformation("Job recovery: {Resumed} re-queued, {Failed} failed (point of no return), {Finalized} finalized", resumed, failed, finalized);
    }
}

// ---- recurring: re-render channel statuses so >24h recent items expire without new activity ----
using (var scope = app.Services.CreateScope())
{
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurring.AddOrUpdate<ISlackStatusService>(
        "slack-status-refresh",
        s => s.RefreshAllInPlaceAsync(CancellationToken.None),
        "*/30 * * * *"); // every 30 min (UTC); an item crossing 24h drops within ≤30 min
}

app.MapGet("/", () => Results.Ok(new { service = "SlackTube", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapSlackEndpoints();
app.MapGoogleOAuthEndpoints();
app.MapAdminApiEndpoints();

// Hangfire dashboard — local-request-only by default; secure with an auth filter for remote use.
app.MapHangfireDashboard("/hangfire");

app.Run();
