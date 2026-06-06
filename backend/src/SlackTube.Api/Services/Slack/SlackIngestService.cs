using Hangfire;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;

namespace SlackTube.Api.Services.Slack;

/// <summary>A plain Slack message we may turn into an upload job (passed through Hangfire).</summary>
public sealed record SlackMessageRef(string EventId, string ChannelId, string UserId, string Ts, string Text);

/// <summary>
/// Stage 1 of the pipeline (runs as a Hangfire job so the HTTP endpoint can ACK in &lt;3s and the
/// work is durable). Parses the template, validates against Drive, then either asks for
/// confirmation (missing description/tags) or enqueues the upload job (stage 2).
/// </summary>
public sealed class SlackIngestService(
    SlackTemplateParser parser,
    GoogleOAuthService oauth,
    DriveDownloadService drive,
    IJobService jobs,
    ChannelMappingService mappings,
    SlackClient slack,
    SlackWorkspaceService workspaces,
    ISlackStatusService status,
    IBackgroundJobClient backgroundJobs,
    ILogger<SlackIngestService> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task ProcessMessageAsync(SlackMessageRef msg, CancellationToken ct)
    {
        // Idempotent across Hangfire retries / Slack redeliveries.
        if (await jobs.ExistsForEventAsync(msg.EventId, ct)) return;

        // Only mapped channels — resolve the target Google account from the mapping.
        var mapping = await mappings.GetByChannelAsync(msg.ChannelId, ct);
        if (mapping is null) return;

        var parsed = parser.Parse(msg.Text);
        if (!parsed.IsUploadTemplate) return; // not an UPLOAD command — ignore silently

        if (!parsed.HasVideo)
        {
            await ReplyAsync(msg, ":x: No Google Drive video link found. Add a `Video:` line with a Drive link.", ct);
            return;
        }

        var creds = await oauth.GetAccountCredsAsync(mapping.GoogleAccountId, ct);
        if (creds is null)
        {
            await ReplyAsync(msg, ":warning: The mapped Google account is unavailable — reconnect it in the admin panel.", ct);
            return;
        }

        // Verify the Drive file is reachable + grab its name up front (no YouTube quota cost).
        DriveFileInfo info;
        try
        {
            info = await drive.GetInfoAsync(drive.BuildService(creds.ClientId, creds.ClientSecret, creds.RefreshToken), parsed.DriveFileId!, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Drive metadata fetch failed for {FileId}", parsed.DriveFileId);
            await ReplyAsync(msg, ":x: Couldn’t access that Drive file — make sure it’s shared with the connected Google account.", ct);
            return;
        }

        if (info.IsGoogleNative)
        {
            await ReplyAsync(msg, ":x: That Drive file is a Google-native doc, not a video file.", ct);
            return;
        }

        var requiresConfirm = !parsed.HasDescription || !parsed.HasTags;
        var job = await jobs.CreateAsync(new NewJob(
            msg.EventId, msg.ChannelId, msg.UserId, msg.Ts,
            parsed.DriveFileId!, info.Name, Path.GetFileNameWithoutExtension(info.Name),
            parsed.Description, parsed.Tags, requiresConfirm, mapping.GoogleAccountId), ct);

        if (parsed.Warnings.Count > 0)
            await ReplyAsync(msg, ":warning: " + string.Join("\n", parsed.Warnings), ct);

        if (requiresConfirm)
        {
            var missing = (noDesc: !parsed.HasDescription, noTags: !parsed.HasTags) switch
            {
                (true, true) => "description or tags",
                (true, false) => "description",
                _ => "tags",
            };
            var (text, blocks) = SlackBlocks.Confirm(job.Id, info.Name, missing);
            var confirmToken = await workspaces.GetBotTokenForChannelAsync(msg.ChannelId, ct);
            if (confirmToken is not null)
                await slack.PostMessageAsync(confirmToken, msg.ChannelId, text, blocks, threadTs: msg.Ts, ct: ct);
            return;
        }

        Enqueue(job.Id);
        await status.RefreshQueueAsync(ct);
    }

    /// <summary>Enqueues stage 2 (the upload). Also called by the interactivity handler on confirm.</summary>
    public void Enqueue(Guid jobId)
        => backgroundJobs.Enqueue<UploadJobHandler>(h => h.RunAsync(jobId, CancellationToken.None));

    private async Task ReplyAsync(SlackMessageRef msg, string text, CancellationToken ct)
    {
        var token = await workspaces.GetBotTokenForChannelAsync(msg.ChannelId, ct);
        if (token is null) return;
        await slack.PostMessageAsync(token, msg.ChannelId, text, threadTs: msg.Ts, ct: ct);
    }
}
