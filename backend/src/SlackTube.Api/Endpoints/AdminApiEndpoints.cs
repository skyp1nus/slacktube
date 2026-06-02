using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Endpoints;

public sealed record CreateMappingDto(Guid SlackWorkspaceId, string SlackChannelId, Guid GoogleAccountId);

/// <summary>
/// Admin API consumed by the Next.js BFF server-side. Guarded by an <c>X-Admin-Token</c> header
/// (the web layer holds the key and attaches it; the browser never sees it).
/// </summary>
public static class AdminApiEndpoints
{
    public static void MapAdminApiEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var opts = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value;
            var provided = ctx.HttpContext.Request.Headers["X-Admin-Token"].ToString();
            if (string.IsNullOrEmpty(opts.EffectiveApiKey) || provided != opts.EffectiveApiKey)
                return Results.Unauthorized();
            return await next(ctx);
        });

        admin.MapGet("/status", async (
            SlackWorkspaceService workspaces, GoogleOAuthService google, IQuotaService quota, CancellationToken ct) =>
        {
            var wsCount = await workspaces.CountActiveWorkspacesAsync(ct);
            var conn = await google.GetConnectionAsync(ct);
            var accountCount = await google.CountAccountsAsync(ct);
            var defaultAccount = await google.GetDefaultAccountIdAsync(ct);
            var q = await quota.GetStatusAsync(defaultAccount);
            return Results.Ok(new
            {
                slackConfigured = wsCount > 0,
                workspaceCount = wsCount,
                google = new { conn.Connected, conn.Scopes, conn.ConnectedAt, accountCount },
                quota = new { q.UsedUnits, q.CapUnits, q.RemainingUploads, q.TotalUploads },
            });
        });

        // ---- Slack workspaces (OAuth-installed) ----------------------------------------
        admin.MapGet("/slack/workspaces", async (SlackWorkspaceService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListWorkspacesAsync(ct)));

        admin.MapGet("/slack/workspaces/{id:guid}/channels", async (Guid id, SlackWorkspaceService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListChannelsAsync(id, ct)));

        admin.MapPost("/slack/workspaces/{id:guid}/refresh-channels", async (Guid id, SlackWorkspaceService ws, CancellationToken ct) =>
        {
            var channels = await ws.RefreshChannelsAsync(id, ct);
            return channels is null ? Results.NotFound() : Results.Ok(channels);
        });

        admin.MapDelete("/slack/workspaces/{id:guid}", async (Guid id, SlackWorkspaceService ws, CancellationToken ct) =>
            await ws.DeleteWorkspaceAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---- Google accounts (multi-account) -------------------------------------------
        admin.MapGet("/google/accounts", async (GoogleOAuthService google, IQuotaService quota, CancellationToken ct) =>
        {
            var accounts = await google.ListAccountsAsync(ct);
            var result = new List<object>(accounts.Count);
            foreach (var a in accounts)
            {
                var q = await quota.GetStatusAsync(a.Id);
                result.Add(new
                {
                    a.Id, a.Label, a.YouTubeChannelId, a.YouTubeChannelTitle, a.AccountEmail, a.Status, a.CreatedAt,
                    quota = new { q.UsedUnits, q.CapUnits, q.RemainingUploads, q.TotalUploads },
                });
            }
            return Results.Ok(result);
        });

        admin.MapDelete("/google/accounts/{id:guid}", async (
            Guid id, GoogleOAuthService google, ChannelMappingService mappings, CancellationToken ct) =>
        {
            if (await mappings.IsAccountMappedAsync(id, ct))
                return Results.Conflict(new { error = "account_mapped" });
            return await google.DeleteAccountAsync(id, ct) ? Results.NoContent() : Results.NotFound();
        });

        // ---- Mapping (channel → account) -----------------------------------------------
        admin.MapGet("/slack/channels", async (SlackWorkspaceService ws, CancellationToken ct) =>
            Results.Ok(await ws.ListAllMemberChannelsAsync(ct)));

        admin.MapGet("/mappings", async (ChannelMappingService mappings, CancellationToken ct) =>
            Results.Ok(await mappings.ListAsync(ct)));

        admin.MapPost("/mappings", async (CreateMappingDto dto, ChannelMappingService mappings, ISlackStatusService status, CancellationToken ct) =>
        {
            var (ok, error) = await mappings.CreateAsync(dto.SlackWorkspaceId, dto.SlackChannelId, dto.GoogleAccountId, ct);
            if (!ok) return Results.Conflict(new { error });
            await status.RefreshQueueAsync(ct); // post the status message in the newly mapped channel
            return Results.Ok(new { created = true });
        });

        admin.MapDelete("/mappings/{id:guid}", async (Guid id, ChannelMappingService mappings, CancellationToken ct) =>
            await mappings.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---- Dashboard KPIs --------------------------------------------------------------
        admin.MapGet("/dashboard", async (
            SlackWorkspaceService ws, GoogleOAuthService google, IJobService jobs, IQuotaService quota,
            IOptions<AppOptions> appOpt, CancellationToken ct) =>
        {
            var workspaceCount = await ws.CountActiveWorkspacesAsync(ct);
            var accounts = await google.ListAccountsAsync(ct);
            var capPer = appOpt.Value.YouTubeDailyQuotaUnits;
            var usedSum = 0;
            foreach (var a in accounts)
                usedSum += (await quota.GetStatusAsync(a.Id)).UsedUnits;
            var (uploadsToday, uploadsLast24h, errorsLast24h) = await jobs.GetDashboardCountsAsync(ct);
            return Results.Ok(new
            {
                workspaceCount,
                accountCount = accounts.Count,
                uploadsToday,
                uploadsLast24h,
                errorsLast24h,
                quotaUsedUnits = usedSum,
                quotaCapUnits = capPer * accounts.Count,
            });
        });

        // ---- Job history (filtered + paginated) -----------------------------------------
        admin.MapGet("/jobs", async (
            IJobService jobs, string? status, int? page, int? pageSize, CancellationToken ct) =>
        {
            JobState? state = Enum.TryParse<JobState>(status, ignoreCase: true, out var s) ? s : null;
            var pageNum = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var (items, total) = await jobs.GetHistoryPagedAsync(state, pageNum, size, ct);
            return Results.Ok(new
            {
                total,
                items = items.Select(j => new
                {
                    j.Id,
                    fileName = j.OriginalFileName ?? j.Title,
                    state = j.State.ToString(),
                    j.YouTubeUrl,
                    error = j.ErrorMessage,
                    tags = j.Tags,
                    j.GoogleAccountId,
                    j.CreatedAt,
                    j.UpdatedAt,
                }),
            });
        });
    }
}
