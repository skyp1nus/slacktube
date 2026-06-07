namespace SlackTube.Api.Services.Infrastructure;

/// <summary>Central registry of Redis key shapes so TTL/semantics stay consistent.</summary>
public static class RedisKeys
{
    /// <summary>Slack event_id dedup marker (TTL ~10 min — covers Slack retry window).</summary>
    public static string Dedup(string eventId) => $"slacktube:dedup:slack:{eventId}";

    /// <summary>Per-job cancellation flag (checked atomically before each worker step).</summary>
    public static string Cancel(Guid jobId) => $"slacktube:cancel:job:{jobId}";

    /// <summary>YouTube uploads (<c>videos.insert</c> calls) an OAuth client made on a given Pacific-Time
    /// date. videos.insert has its OWN daily bucket per project (Google default 100/day), separate from the
    /// unit pool below — so this counter, not the unit math, gates daily upload capacity. Keyed by the
    /// OAuth client id + PT date; every account on the same client shares it; resets at PT midnight.</summary>
    public static string UploadCount(Guid oauthClientId, string ptDate)
        => $"slacktube:uploads:youtube:{oauthClientId}:{ptDate}";

    /// <summary>YouTube units used by an OAuth client (Google Cloud project) on a given Pacific-Time date for
    /// NON-upload endpoints (list/search/etc.) — the separate ~10k/day pool. Keyed by OAuth client id + PT
    /// date; shared by every account on the client; resets at PT midnight. Surfaced as an info meter only.</summary>
    public static string Quota(Guid oauthClientId, string ptDate)
        => $"slacktube:quota:youtube:{oauthClientId}:{ptDate}";

    /// <summary>Generic per-day API usage counter for monitoring daily spend. <paramref name="scope"/> is an
    /// OAuth client id (Google APIs) or "slack" (Slack Web API); <paramref name="metric"/> is e.g.
    /// "drive.queries", "drive.bytes", "slack.chat.postMessage". Resets at PT midnight.</summary>
    public static string Usage(string scope, string metric, string ptDate)
        => $"slacktube:usage:{scope}:{metric}:{ptDate}";

    /// <summary>Per-day SET of all <c>"{scope}|{metric}"</c> pairs seen today — lets the usage reader MGET
    /// every counter without a Redis SCAN. Bounded to PT midnight like the counters it indexes.</summary>
    public static string UsageIndex(string ptDate)
        => $"slacktube:usage:index:{ptDate}";
}
