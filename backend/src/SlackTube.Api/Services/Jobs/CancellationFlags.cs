using StackExchange.Redis;
using SlackTube.Api.Services.Infrastructure;

namespace SlackTube.Api.Services.Jobs;

/// <summary>Cross-process cancellation signal in Redis. The HTTP interactivity handler sets
/// the flag; the Hangfire worker checks it atomically before each step.</summary>
public interface ICancellationFlags
{
    Task RequestAsync(Guid jobId);
    Task<bool> IsRequestedAsync(Guid jobId);
    Task ClearAsync(Guid jobId);
}

public sealed class CancellationFlags(IConnectionMultiplexer redis) : ICancellationFlags
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public Task RequestAsync(Guid jobId)
        => redis.GetDatabase().StringSetAsync(RedisKeys.Cancel(jobId), "1", Ttl);

    public async Task<bool> IsRequestedAsync(Guid jobId)
        => await redis.GetDatabase().KeyExistsAsync(RedisKeys.Cancel(jobId));

    public Task ClearAsync(Guid jobId)
        => redis.GetDatabase().KeyDeleteAsync(RedisKeys.Cancel(jobId));
}
