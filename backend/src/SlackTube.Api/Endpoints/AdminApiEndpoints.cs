using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Endpoints;

public sealed record CreateMappingDto(Guid SlackWorkspaceId, string SlackChannelId, Guid GoogleAccountId);

public sealed record UpdateSettingsDto(string? DefaultVisibility, int? TransferChunkSizeMb);

/// <summary>Create an OAuth client (Google Cloud project). The secret is write-only — never read back.</summary>
public sealed record CreateGoogleClientDto(string Label, string ClientId, string ClientSecret);

public sealed record UpdateGoogleClientDto(string? Label, string? Status);

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
            SlackWorkspaceService workspaces, GoogleOAuthService google, GoogleOAuthClientService clients,
            IQuotaService quota, CancellationToken ct) =>
        {
            var wsCount = await workspaces.CountActiveWorkspacesAsync(ct);
            var conn = await google.GetConnectionAsync(ct);
            var accountCount = await google.CountAccountsAsync(ct);
            // Quota is per OAuth client; aggregate across Active clients for the at-a-glance total.
            int used = 0, cap = 0, remainingUploads = 0, totalUploads = 0;
            foreach (var c in await clients.ListAsync(ct))
            {
                if (c.Status != GoogleOAuthClientService.StatusActive) continue;
                var qs = await quota.GetStatusAsync(c.Id);
                used += qs.UsedUnits; cap += qs.CapUnits;
                remainingUploads += qs.RemainingUploads; totalUploads += qs.TotalUploads;
            }
            return Results.Ok(new
            {
                slackConfigured = wsCount > 0,
                workspaceCount = wsCount,
                google = new { conn.Connected, conn.Scopes, conn.ConnectedAt, accountCount },
                quota = new { UsedUnits = used, CapUnits = cap, RemainingUploads = remainingUploads, TotalUploads = totalUploads },
            });
        });

        // ---- Settings (upload defaults) ------------------------------------------------
        admin.MapGet("/settings", async (ISettingsStore settings, CancellationToken ct) =>
        {
            var s = await settings.GetUploadSettingsAsync(ct);
            return Results.Ok(new { defaultVisibility = s.Visibility, transferChunkSizeMb = s.ChunkSizeMb });
        });

        admin.MapPatch("/settings", async (UpdateSettingsDto dto, ISettingsStore settings, CancellationToken ct) =>
        {
            var cur = await settings.GetUploadSettingsAsync(ct);
            var visibility = dto.DefaultVisibility is null
                ? cur.Visibility
                : YouTubeUploadService.NormalizeVisibility(dto.DefaultVisibility);
            var chunk = dto.TransferChunkSizeMb is null ? cur.ChunkSizeMb : Math.Clamp(dto.TransferChunkSizeMb.Value, 1, 1024);
            await settings.UpdateUploadSettingsAsync(visibility, chunk, ct);
            return Results.Ok(new { defaultVisibility = visibility, transferChunkSizeMb = chunk });
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

        // ---- Google OAuth clients (one per Cloud project; quota is enforced here) -------
        admin.MapGet("/google/clients", async (GoogleOAuthClientService clients, IQuotaService quota, CancellationToken ct) =>
        {
            var list = await clients.ListAsync(ct);
            var result = new List<object>(list.Count);
            foreach (var c in list)
            {
                var q = await quota.GetStatusAsync(c.Id);
                var accountCount = await clients.CountAccountsAsync(c.Id, ct);
                result.Add(new
                {
                    c.Id, c.Label, c.ClientId, c.Status, c.CreatedAt, c.UpdatedAt,
                    accountCount,
                    quota = new { q.UsedUnits, q.CapUnits, q.RemainingUploads, q.TotalUploads },
                });
            }
            return Results.Ok(result);
        });

        admin.MapPost("/google/clients", async (CreateGoogleClientDto dto, GoogleOAuthClientService clients, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ClientId) || string.IsNullOrWhiteSpace(dto.ClientSecret))
                return Results.BadRequest(new { error = "client_id_and_secret_required" });
            if (await clients.ClientIdExistsAsync(dto.ClientId, ct))
                return Results.Conflict(new { error = "client_id_exists" });
            try
            {
                var created = await clients.CreateAsync(dto.Label, dto.ClientId, dto.ClientSecret, ct);
                return Results.Ok(created); // never echoes the secret
            }
            catch (DbUpdateException) // lost a race against the unique ClientId index → 409, not 500
            {
                return Results.Conflict(new { error = "client_id_exists" });
            }
        });

        admin.MapPatch("/google/clients/{id:guid}", async (Guid id, UpdateGoogleClientDto dto, GoogleOAuthClientService clients, CancellationToken ct) =>
            await clients.UpdateAsync(id, dto.Label, dto.Status, ct) ? Results.NoContent() : Results.NotFound());

        admin.MapDelete("/google/clients/{id:guid}", async (Guid id, GoogleOAuthClientService clients, CancellationToken ct) =>
        {
            var (ok, error) = await clients.DeleteAsync(id, ct);
            if (ok) return Results.NoContent();
            return error == "not_found" ? Results.NotFound() : Results.Conflict(new { error });
        });

        // ---- Google accounts (multi-account) -------------------------------------------
        admin.MapGet("/google/accounts", async (GoogleOAuthService google, IQuotaService quota, CancellationToken ct) =>
        {
            var accounts = await google.ListAccountsAsync(ct);
            var result = new List<object>(accounts.Count);
            foreach (var a in accounts)
            {
                // Quota shown is the issuing CLIENT's shared daily cap (accounts on the same client share it).
                var q = await quota.GetStatusAsync(a.OAuthClientId);
                result.Add(new
                {
                    a.Id, a.Label, a.YouTubeChannelId, a.YouTubeChannelTitle, a.AvatarUrl, a.AccountEmail, a.Status, a.CreatedAt,
                    a.OAuthClientId, a.OAuthClientLabel,
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
            SlackWorkspaceService ws, GoogleOAuthService google, GoogleOAuthClientService clients,
            IJobService jobs, IQuotaService quota, IOptions<AppOptions> appOpt, CancellationToken ct) =>
        {
            var workspaceCount = await ws.CountActiveWorkspacesAsync(ct);
            var accountCount = await google.CountAccountsAsync(ct);
            // Quota is per OAuth client (project): used = sum over clients, cap = per-project default × clients.
            var capPer = appOpt.Value.YouTubeDailyQuotaUnits;
            var clientList = await clients.ListAsync(ct);
            var usedSum = 0;
            foreach (var c in clientList)
                usedSum += (await quota.GetStatusAsync(c.Id)).UsedUnits;
            var (uploadsToday, uploadsLast24h, errorsLast24h) = await jobs.GetDashboardCountsAsync(ct);
            return Results.Ok(new
            {
                workspaceCount,
                accountCount,
                clientCount = clientList.Count,
                uploadsToday,
                uploadsLast24h,
                errorsLast24h,
                quotaUsedUnits = usedSum,
                quotaCapUnits = capPer * clientList.Count,
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
