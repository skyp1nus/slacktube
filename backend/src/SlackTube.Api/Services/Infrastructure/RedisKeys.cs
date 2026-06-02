namespace SlackTube.Api.Services.Infrastructure;

/// <summary>Central registry of Redis key shapes so TTL/semantics stay consistent.</summary>
public static class RedisKeys
{
    /// <summary>Slack event_id dedup marker (TTL ~10 min — covers Slack retry window).</summary>
    public static string Dedup(string eventId) => $"slacktube:dedup:slack:{eventId}";

    /// <summary>Per-job cancellation flag (checked atomically before each worker step).</summary>
    public static string Cancel(Guid jobId) => $"slacktube:cancel:job:{jobId}";

    /// <summary>YouTube units used by a Google account on a given Pacific-Time date. The key embeds
    /// the account id + PT date, so each account has its own counter that resets at PT midnight.</summary>
    public static string Quota(Guid googleAccountId, string ptDate)
        => $"slacktube:quota:youtube:{googleAccountId}:{ptDate}";
}
