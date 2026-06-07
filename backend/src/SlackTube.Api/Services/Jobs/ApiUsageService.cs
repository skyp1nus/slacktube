using StackExchange.Redis;
using SlackTube.Api.Services.Infrastructure;

namespace SlackTube.Api.Services.Jobs;

/// <summary>One counter's value today: <paramref name="Scope"/> is an OAuth client id (Google APIs) or
/// "slack"; <paramref name="Metric"/> is e.g. "drive.queries" / "slack.chat.postMessage".</summary>
public sealed record UsageEntry(string Scope, string Metric, long Value);

/// <summary>
/// Generic per-day API usage counter (Redis), so daily spend on the metered APIs (YouTube/Drive/Slack) is
/// observable. (Free token endpoints — Google OAuth exchange/refresh — are intentionally not counted.)
/// Each <c>(scope, metric)</c> pair is one counter keyed by the Pacific-Time date (resets at PT midnight);
/// a per-day index SET records which pairs were touched so the reader can MGET them all without a SCAN.
/// Additive/best-effort — it never gates anything (unlike <see cref="IQuotaService"/>'s upload bucket).
/// </summary>
public interface IApiUsageService
{
    /// <summary>Add <paramref name="amount"/> to today's counter for (scope, metric). No-op for amount ≤ 0.</summary>
    Task IncrementAsync(string scope, string metric, long amount = 1);
    /// <summary>Every counter touched today.</summary>
    Task<IReadOnlyList<UsageEntry>> GetTodayAsync();
}

public sealed class ApiUsageService(IConnectionMultiplexer redis, ILogger<ApiUsageService> logger) : IApiUsageService
{
    public async Task IncrementAsync(string scope, string metric, long amount = 1)
    {
        if (amount <= 0 || string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(metric)) return;
        try
        {
            var db = redis.GetDatabase();
            var pt = PacificTime.TodayKey();
            var ttl = PacificTime.UntilMidnight() + TimeSpan.FromHours(1);

            var key = RedisKeys.Usage(scope, metric, pt);
            var after = await db.StringIncrementAsync(key, amount);
            if (after == amount) // first write today → bound lifetime
                await db.KeyExpireAsync(key, ttl);

            var indexKey = RedisKeys.UsageIndex(pt);
            if (await db.SetAddAsync(indexKey, $"{scope}|{metric}"))
                await db.KeyExpireAsync(indexKey, ttl); // first member → bound the index too
        }
        catch (Exception ex)
        {
            // Metering must never break a real request — swallow Redis hiccups.
            logger.LogWarning(ex, "API usage increment failed for {Scope}/{Metric}", scope, metric);
        }
    }

    public async Task<IReadOnlyList<UsageEntry>> GetTodayAsync()
    {
        var db = redis.GetDatabase();
        var pt = PacificTime.TodayKey();
        var members = await db.SetMembersAsync(RedisKeys.UsageIndex(pt));
        if (members.Length == 0) return Array.Empty<UsageEntry>();

        var pairs = members
            .Select(m => ((string?)m ?? "").Split('|', 2))
            .Where(p => p.Length == 2 && p[0].Length > 0 && p[1].Length > 0)
            .ToArray();
        if (pairs.Length == 0) return Array.Empty<UsageEntry>();

        var keys = pairs.Select(p => (RedisKey)RedisKeys.Usage(p[0], p[1], pt)).ToArray();
        var vals = await db.StringGetAsync(keys);

        var result = new List<UsageEntry>(pairs.Length);
        for (var i = 0; i < pairs.Length; i++)
            result.Add(new UsageEntry(pairs[i][0], pairs[i][1], vals[i].HasValue ? (long)vals[i] : 0));
        return result;
    }
}
