using Microsoft.Extensions.DependencyInjection;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;

namespace SlackTube.Api.Services.Slack;

public interface ISlackStatusService
{
    /// <summary>Queue changed: delete the old status message and repost a fresh one so it pops
    /// back to the bottom of the channel.</summary>
    Task RefreshQueueAsync(CancellationToken ct = default);

    /// <summary>Active job progressed: edit the status message in place (throttled).</summary>
    Task UpdateProgressAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Singleton. Every publish runs inside its OWN DI scope (own DbContext) and is serialized by a
/// semaphore, so the high-frequency progress callbacks firing on the worker thread never race the
/// worker's own scoped DbContext. Throttle: in-place updates at most every 2.5s AND ≥5% delta
/// (phase changes always pass).
/// </summary>
public sealed class SlackStatusService(
    IServiceScopeFactory scopeFactory,
    IProgressTracker progress,
    ILogger<SlackStatusService> logger) : ISlackStatusService
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2.5);
    private const int MinPercentDelta = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _throttleLock = new();
    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    private int _lastPercent = -1;
    private string? _lastPhase;

    public async Task RefreshQueueAsync(CancellationToken ct = default)
    {
        ResetThrottle();
        await PublishAsync(repost: true, ct);
    }

    public async Task UpdateProgressAsync(Guid jobId, CancellationToken ct = default)
    {
        var p = progress.Get(jobId);
        if (p is null) return;
        if (!ShouldUpdate(p.Percent, p.Phase)) return;
        await PublishAsync(repost: false, ct);
    }

    private async Task PublishAsync(bool repost, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(10), ct)) return;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var settings = sp.GetRequiredService<ISettingsStore>();

            var channel = await settings.GetListeningChannelAsync(ct);
            if (string.IsNullOrEmpty(channel)) return;

            var view = await BuildViewAsync(sp, ct);
            var (text, blocks) = SlackBlocks.Status(view);
            var slack = sp.GetRequiredService<SlackClient>();
            var ts = await settings.GetStatusMessageTsAsync(ct);

            if (repost)
            {
                if (!string.IsNullOrEmpty(ts))
                    await slack.DeleteMessageAsync(channel, ts, ct);
                var newTs = await slack.PostMessageAsync(channel, text, blocks, ct: ct);
                await settings.SetStatusMessageTsAsync(newTs, ct);
            }
            else if (!string.IsNullOrEmpty(ts))
            {
                await slack.UpdateMessageAsync(channel, ts, text, blocks, ct);
            }
            else
            {
                var newTs = await slack.PostMessageAsync(channel, text, blocks, ct: ct);
                await settings.SetStatusMessageTsAsync(newTs, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Slack status publish failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<StatusView> BuildViewAsync(IServiceProvider sp, CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<IJobService>();
        var quotaSvc = sp.GetRequiredService<IQuotaService>();
        var progress = sp.GetRequiredService<IProgressTracker>();

        var snap = await jobs.GetStatusSnapshotAsync(5, ct);
        var quota = await quotaSvc.GetStatusAsync();

        ActiveJobView? active = null;
        if (snap.Active is { } a)
        {
            var p = progress.Get(a.Id);
            var processing = a.State == JobState.Processing;
            active = new ActiveJobView(
                DisplayName(a),
                PhaseLabel(a.State),
                p?.Percent ?? 0,
                p?.BytesTransferred ?? a.BytesTransferred,
                p?.BytesTotal ?? a.BytesTotal,
                processing);
        }

        var queued = snap.Queued.Select(q => new QueuedJobView(q.Id, DisplayName(q))).ToList();
        var recent = snap.Recent
            .Select(d => new DoneJobView(DisplayName(d), d.State, d.YouTubeUrl, d.ErrorMessage))
            .ToList();

        return new StatusView(quota.RemainingUploads, quota.TotalUploads, active, queued, recent);
    }

    private static string DisplayName(UploadJob j) =>
        j.OriginalFileName ?? j.Title ?? "video";

    private static string PhaseLabel(JobState s) => s switch
    {
        JobState.Downloading => "Downloading from Drive",
        JobState.Uploading => "Uploading to YouTube",
        JobState.Processing => "YouTube processing",
        _ => s.ToString(),
    };

    // ---- throttle --------------------------------------------------------------------
    private bool ShouldUpdate(int percent, string? phase)
    {
        lock (_throttleLock)
        {
            var now = DateTimeOffset.UtcNow;
            var phaseChanged = phase != _lastPhase;
            var enoughTime = now - _lastUpdate >= MinInterval;
            var enoughDelta = Math.Abs(percent - _lastPercent) >= MinPercentDelta;

            if (phaseChanged || (enoughTime && enoughDelta))
            {
                _lastUpdate = now;
                _lastPercent = percent;
                _lastPhase = phase;
                return true;
            }
            return false;
        }
    }

    private void ResetThrottle()
    {
        lock (_throttleLock)
        {
            _lastUpdate = DateTimeOffset.MinValue;
            _lastPercent = -1;
            _lastPhase = null;
        }
    }
}
