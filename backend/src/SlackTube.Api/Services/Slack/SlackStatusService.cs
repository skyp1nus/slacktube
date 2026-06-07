using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;

namespace SlackTube.Api.Services.Slack;

public interface ISlackStatusService
{
    /// <summary>Queue changed: for each mapped channel, delete the old status message and repost a
    /// fresh one so it pops back to the bottom.</summary>
    Task RefreshQueueAsync(CancellationToken ct = default);

    /// <summary>Active job progressed: edit that job's channel status message in place (throttled).</summary>
    Task UpdateProgressAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Periodically re-render every channel's status IN PLACE (edit, no repost/bump) so the
    /// time-bounded recent list (last 24h) prunes itself even when no new job activity triggers a refresh.</summary>
    Task RefreshAllInPlaceAsync(CancellationToken ct = default);
}

/// <summary>
/// Singleton. One status message PER mapped channel. Each publish runs in its own DI scope (own
/// DbContext) and is serialized by a semaphore so the high-frequency progress callbacks never race
/// the worker's DbContext. The current message ts is kept per channel in Redis. Throttle is
/// per-channel: in-place updates at most every 2.5s AND ≥5% delta (phase changes always pass).
/// </summary>
public sealed class SlackStatusService(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis,
    IProgressTracker progress,
    ILogger<SlackStatusService> logger) : ISlackStatusService
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2.5);
    private const int MinPercentDelta = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, int Percent, string? Phase)> _throttle = new();

    public async Task RefreshQueueAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), ct)) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var routes = await sp.GetRequiredService<ChannelMappingService>().ListRoutesAsync(ct);
            foreach (var r in routes)
            {
                _throttle.TryRemove(r.SlackChannelId, out _); // reposting resets the throttle
                await PublishChannelAsync(sp, r.SlackChannelId, r.GoogleAccountId, repost: true, ct);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Slack status refresh failed"); }
        finally { _gate.Release(); }
    }

    public async Task RefreshAllInPlaceAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), ct)) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var routes = await sp.GetRequiredService<ChannelMappingService>().ListRoutesAsync(ct);
            foreach (var r in routes)
            {
                // Isolate each channel — a transient network/parse/DB fault on one must not skip the rest.
                try
                {
                    // repost:false ⇒ chat.update an existing message (no delete/bump); posts only if none exists yet.
                    await PublishChannelAsync(sp, r.SlackChannelId, r.GoogleAccountId, repost: false, ct);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Periodic status refresh failed for {Channel}", r.SlackChannelId); }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Slack status periodic refresh failed"); }
        finally { _gate.Release(); }
    }

    public async Task UpdateProgressAsync(Guid jobId, CancellationToken ct = default)
    {
        var p = progress.Get(jobId);
        if (p is null) return;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var job = await sp.GetRequiredService<IJobService>().GetAsync(jobId, ct);
        if (job is null) return;
        if (!ShouldUpdate(job.SlackChannelId, p.Percent, p.Phase)) return;

        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), ct)) return;
        try
        {
            await PublishChannelAsync(sp, job.SlackChannelId, job.GoogleAccountId, repost: false, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Slack status update failed"); }
        finally { _gate.Release(); }
    }

    private async Task PublishChannelAsync(
        IServiceProvider sp, string channelId, Guid? accountId, bool repost, CancellationToken ct)
    {
        var botToken = await sp.GetRequiredService<SlackWorkspaceService>().GetBotTokenForChannelAsync(channelId, ct);
        if (string.IsNullOrEmpty(botToken)) return;

        var view = await BuildViewAsync(sp, channelId, accountId, ct);
        var (text, blocks) = SlackBlocks.Status(view);
        var slack = sp.GetRequiredService<SlackClient>();

        var db = redis.GetDatabase();
        var tsKey = StatusTsKey(channelId);
        var ts = (string?)await db.StringGetAsync(tsKey);

        if (repost)
        {
            if (!string.IsNullOrEmpty(ts))
                await slack.DeleteMessageAsync(botToken, channelId, ts, ct);
            var newTs = await slack.PostMessageAsync(botToken, channelId, text, blocks, ct: ct);
            if (newTs is not null) await db.StringSetAsync(tsKey, newTs);
            else await db.KeyDeleteAsync(tsKey);
        }
        else if (!string.IsNullOrEmpty(ts))
        {
            // Edit in place. If the message is gone (chat.update → message_not_found), drop the stale ts and
            // repost so the channel self-heals instead of staying blank across every periodic refresh.
            if (!await slack.UpdateMessageAsync(botToken, channelId, ts, text, blocks, ct))
            {
                var newTs = await slack.PostMessageAsync(botToken, channelId, text, blocks, ct: ct);
                if (newTs is not null) await db.StringSetAsync(tsKey, newTs);
                else await db.KeyDeleteAsync(tsKey);
            }
        }
        else
        {
            var newTs = await slack.PostMessageAsync(botToken, channelId, text, blocks, ct: ct);
            if (newTs is not null) await db.StringSetAsync(tsKey, newTs);
        }
    }

    private static async Task<StatusView> BuildViewAsync(
        IServiceProvider sp, string channelId, Guid? accountId, CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<IJobService>();
        var quotaSvc = sp.GetRequiredService<IQuotaService>();
        var google = sp.GetRequiredService<GoogleOAuthService>();
        var progress = sp.GetRequiredService<IProgressTracker>();

        var snap = await jobs.GetStatusSnapshotAsync(channelId, 5, ct);

        // Quota is keyed by OAuth CLIENT (Cloud project) and one YouTube channel can be consented through a
        // POOL of clients (each its own daily cap ⇒ N× quota), which is exactly what upload rotation uses.
        // So aggregate the pool. The old code read GetStatusAsync(accountId): the GoogleAccount id is never
        // a quota key (uploads charge the CLIENT id), so it always reported the full cap and never moved.
        var effectiveAccountId = accountId ?? await google.GetDefaultAccountIdAsync(ct);
        string? channelTitle = null;
        IReadOnlyList<Guid> clientIds = Array.Empty<Guid>();
        if (effectiveAccountId is { } accId)
        {
            channelTitle = await google.GetAccountChannelLabelAsync(accId, ct);
            clientIds = await google.GetChannelOAuthClientIdsAsync(accId, ct);
        }
        var (remainingUploads, totalUploads) = await AggregateQuotaAsync(clientIds, quotaSvc);

        ActiveJobView? active = null;
        if (snap.Active is { } a)
        {
            var p = progress.Get(a.Id);
            active = new ActiveJobView(
                DisplayName(a),
                PhaseLabel(a.State),
                p?.Percent ?? 0,
                p?.BytesTransferred ?? a.BytesTransferred,
                p?.BytesTotal ?? a.BytesTotal,
                a.State == JobState.Processing);
        }

        var queued = snap.Queued.Select(q => new QueuedJobView(q.Id, DisplayName(q))).ToList();
        var recent = snap.Recent
            .Select(d => new DoneJobView(DisplayName(d), d.State, d.YouTubeUrl, d.YouTubeVideoId, d.ErrorMessage, d.DownloadStartedAt, d.UploadStartedAt, d.UpdatedAt))
            .ToList();

        return new StatusView(remainingUploads, totalUploads, active, queued, recent, snap.UploadedLast24h, channelTitle);
    }

    /// <summary>Sum a channel's per-client daily quota into one remaining/total uploads pair. Each id is a
    /// separate Cloud-project counter; the list is already distinct. Static so it's unit-testable with a
    /// fake quota service (no Redis).</summary>
    internal static async Task<(int RemainingUploads, int TotalUploads)> AggregateQuotaAsync(
        IReadOnlyList<Guid> oauthClientIds, IQuotaService quota)
    {
        int remaining = 0, total = 0;
        foreach (var id in oauthClientIds)
        {
            var qs = await quota.GetStatusAsync(id);
            remaining += qs.RemainingUploads;
            total += qs.TotalUploads;
        }
        return (remaining, total);
    }

    private static string StatusTsKey(string channelId) => $"slacktube:status:ts:{channelId}";

    private static string DisplayName(UploadJob j) => j.OriginalFileName ?? j.Title ?? "video";

    private static string PhaseLabel(JobState s) => s switch
    {
        JobState.Downloading => "Downloading from Drive",
        JobState.Uploading => "Uploading to YouTube",
        JobState.Processing => "YouTube processing",
        _ => s.ToString(),
    };

    private bool ShouldUpdate(string channelId, int percent, string? phase)
    {
        var now = DateTimeOffset.UtcNow;
        var prev = _throttle.TryGetValue(channelId, out var v) ? v : (At: DateTimeOffset.MinValue, Percent: -1, Phase: (string?)null);
        var phaseChanged = phase != prev.Phase;
        var enoughTime = now - prev.At >= MinInterval;
        var enoughDelta = Math.Abs(percent - prev.Percent) >= MinPercentDelta;

        if (phaseChanged || (enoughTime && enoughDelta))
        {
            _throttle[channelId] = (now, percent, phase);
            return true;
        }
        return false;
    }
}
