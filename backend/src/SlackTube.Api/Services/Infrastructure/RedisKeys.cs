namespace SlackTube.Api.Services.Infrastructure;

/// <summary>Central registry of Redis key shapes so TTL/semantics stay consistent.</summary>
public static class RedisKeys
{
    /// <summary>Slack event_id dedup marker (TTL ~10 min — covers Slack retry window).</summary>
    public static string Dedup(string eventId) => $"slacktube:dedup:slack:{eventId}";

    /// <summary>Per-job cancellation flag (checked atomically before each worker step).</summary>
    public static string Cancel(Guid jobId) => $"slacktube:cancel:job:{jobId}";

    /// <summary>YouTube units used on a given Pacific-Time date. Key name embeds the PT date,
    /// so the counter naturally resets to 0 at PT midnight (a new key is used).</summary>
    public static string Quota(string ptDate) => $"slacktube:quota:youtube:{ptDate}";
}
