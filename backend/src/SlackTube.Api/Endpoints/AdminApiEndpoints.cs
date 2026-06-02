using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Endpoints;

public sealed record SetSlackDto(string BotToken, string SigningSecret);
public sealed record SetChannelDto(string ChannelId);

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
            ISettingsStore settings, GoogleOAuthService oauth, IQuotaService quota, CancellationToken ct) =>
        {
            var slack = await settings.GetSlackAsync(ct);
            var channel = await settings.GetListeningChannelAsync(ct);
            var google = await oauth.GetConnectionAsync(ct);
            var q = await quota.GetStatusAsync();
            return Results.Ok(new
            {
                slackConfigured = slack.IsConfigured,
                listeningChannelId = channel,
                google = new { google.Connected, google.Scopes, google.ConnectedAt },
                quota = new { q.UsedUnits, q.CapUnits, q.RemainingUploads, q.TotalUploads },
            });
        });

        admin.MapPost("/slack", async (SetSlackDto dto, ISettingsStore settings, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.BotToken) || string.IsNullOrWhiteSpace(dto.SigningSecret))
                return Results.BadRequest("Both botToken and signingSecret are required.");
            await settings.SetSlackCredentialsAsync(dto.BotToken.Trim(), dto.SigningSecret.Trim(), ct);
            return Results.Ok(new { saved = true });
        });

        admin.MapGet("/channels", async (SlackClient slack, CancellationToken ct) =>
            Results.Ok(await slack.ListChannelsAsync(ct)));

        admin.MapPost("/channel", async (
            SetChannelDto dto, ISettingsStore settings, ISlackStatusService status, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ChannelId))
                return Results.BadRequest("channelId is required.");
            await settings.SetListeningChannelAsync(dto.ChannelId.Trim(), ct);
            await status.RefreshQueueAsync(ct); // (re)post the status message in the chosen channel
            return Results.Ok(new { saved = true });
        });

        admin.MapGet("/jobs", async (IJobService jobs, CancellationToken ct) =>
        {
            var history = await jobs.GetHistoryAsync(50, ct);
            return Results.Ok(history.Select(j => new
            {
                j.Id,
                fileName = j.OriginalFileName ?? j.Title,
                state = j.State.ToString(),
                j.YouTubeUrl,
                error = j.ErrorMessage,
                tags = j.Tags,
                j.CreatedAt,
                j.UpdatedAt,
            }));
        });
    }
}
