namespace SlackTube.Api.Services.Infrastructure;

/// <summary>Central registry of Redis key shapes so TTL/semantics stay consistent.</summary>
public static class RedisKeys
{
    /// <summary>Slack event_id dedup marker (TTL ~10 min — covers Slack retry window).</summary>
    public static string Dedup(string eventId) => $"slacktube:dedup:slack:{eventId}";

    /// <summary>Per-job cancellation flag (checked atomically before each worker step).</summary>
    public static string Cancel(Guid jobId) => $"slacktube:cancel:job:{jobId}";

    /// <summary>YouTube units used by an OAuth client (Google Cloud project) on a given Pacific-Time
    /// date. YouTube quota is enforced PER PROJECT, so the counter is keyed by the OAuth client id +
    /// PT date; every account that consented through the same client shares this counter, which
    /// resets at PT midnight.</summary>
    public static string Quota(Guid oauthClientId, string ptDate)
        => $"slacktube:quota:youtube:{oauthClientId}:{ptDate}";
}
