using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Infrastructure;

namespace SlackTube.Api.Services.Jobs;

/// <summary>Two independent per-project/day dimensions: the upload bucket (videos.insert calls — the real
/// gate) and the unit pool (non-upload endpoints — an informational meter). They do not share a budget.</summary>
public sealed record QuotaStatus(int UsedUploads, int UploadLimit, int UsedUnits, int CapUnits)
{
    /// <summary>videos.insert calls still available today against this project's upload bucket.</summary>
    public int RemainingUploads => Math.Max(0, UploadLimit - UsedUploads);
    /// <summary>This project's daily upload-call ceiling (e.g. 100).</summary>
    public int TotalUploads => UploadLimit;
    /// <summary>Units still available in the separate non-upload pool (info only).</summary>
    public int RemainingUnits => Math.Max(0, CapUnits - UsedUnits);
}

/// <summary>
/// Per-OAuth-client daily YouTube counters in Redis, reset at midnight Pacific Time. YouTube enforces
/// quota per Google Cloud project (OAuth client), so counters are keyed by the OAuth client id: every
/// account that consented through the same client shares them, and connecting one channel through several
/// clients gives it that many independent buckets. Upload capacity is gated by the videos.insert daily
/// bucket (default 100/project), which is SEPARATE from the ~10k unit pool used by other endpoints.
/// </summary>
public interface IQuotaService
{
    Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId);
    /// <summary>Atomically reserve one videos.insert call against the client's PT upload bucket (false if it
    /// would exceed the daily upload limit).</summary>
    Task<bool> TryReserveUploadAsync(Guid oauthClientId);
    /// <summary>Give one reserved upload back (only when reserved but videos.insert was never called).</summary>
    Task ReleaseUploadAsync(Guid oauthClientId);
    /// <summary>Charge units to the client's non-upload pool meter (e.g. channels.list ≈ 1 unit). Best-effort,
    /// for the daily-spend display only — never gates anything.</summary>
    Task ChargeUnitsAsync(Guid oauthClientId, int units);
}

public sealed class QuotaService(IConnectionMultiplexer redis, IOptions<AppOptions> options) : IQuotaService
{
    private readonly AppOptions _opt = options.Value;

    public async Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId)
    {
        // No client (e.g. an account not yet bound to one) ⇒ no usable quota. Report ZERO caps rather
        // than a full cap, so the UI shows "0 of 0 uploads" for a broken/unbound account instead of
        // a misleading "full quota available".
        if (oauthClientId is null)
            return new QuotaStatus(0, 0, 0, 0);

        var db = redis.GetDatabase();
        var pt = PacificTime.TodayKey();
        var uploadsVal = await db.StringGetAsync(RedisKeys.UploadCount(oauthClientId.Value, pt));
        var unitsVal = await db.StringGetAsync(RedisKeys.Quota(oauthClientId.Value, pt));
        var usedUploads = uploadsVal.HasValue ? (int)uploadsVal : 0;
        var usedUnits = unitsVal.HasValue ? (int)unitsVal : 0;
        return new QuotaStatus(usedUploads, _opt.YouTubeDailyUploadLimit, usedUnits, _opt.YouTubeDailyQuotaUnits);
    }

    public async Task<bool> TryReserveUploadAsync(Guid oauthClientId)
    {
        var db = redis.GetDatabase();
        var key = RedisKeys.UploadCount(oauthClientId, PacificTime.TodayKey());

        var after = await db.StringIncrementAsync(key, 1); // one videos.insert call
        if (after == 1) // first upload of this PT day -> bound the key's lifetime
            await db.KeyExpireAsync(key, PacificTime.UntilMidnight() + TimeSpan.FromHours(1));

        if (after > _opt.YouTubeDailyUploadLimit)
        {
            await db.StringDecrementAsync(key, 1); // roll back the over-reservation
            return false;
        }
        return true;
    }

    public Task ReleaseUploadAsync(Guid oauthClientId)
        => redis.GetDatabase().StringDecrementAsync(RedisKeys.UploadCount(oauthClientId, PacificTime.TodayKey()), 1);

    public async Task ChargeUnitsAsync(Guid oauthClientId, int units)
    {
        if (units <= 0) return;
        try
        {
            var db = redis.GetDatabase();
            var key = RedisKeys.Quota(oauthClientId, PacificTime.TodayKey());
            var after = await db.StringIncrementAsync(key, units);
            if (after == units) // first charge of this PT day -> bound the key's lifetime
                await db.KeyExpireAsync(key, PacificTime.UntilMidnight() + TimeSpan.FromHours(1));
        }
        catch { /* best-effort meter — a Redis hiccup must never break the calling request */ }
    }
}
