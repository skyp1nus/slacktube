using StackExchange.Redis;
using SlackTube.Api.Services.Infrastructure;

namespace SlackTube.Api.Services.Jobs;

public interface IDedupService
{
    /// <summary>Atomically claim an event_id. Returns true the first time (process it),
    /// false if it was already seen within the TTL (a Slack retry — drop it).</summary>
    Task<bool> TryClaimAsync(string eventId, TimeSpan? ttl = null);
}

public sealed class DedupService(IConnectionMultiplexer redis) : IDedupService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task<bool> TryClaimAsync(string eventId, TimeSpan? ttl = null)
    {
        var db = redis.GetDatabase();
        // SET key 1 NX EX ttl — true only when the key did not already exist.
        return await db.StringSetAsync(
            RedisKeys.Dedup(eventId), "1", ttl ?? DefaultTtl, When.NotExists);
    }
}
