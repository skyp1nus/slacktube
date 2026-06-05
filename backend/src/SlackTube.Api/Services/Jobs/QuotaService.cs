using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Infrastructure;

namespace SlackTube.Api.Services.Jobs;

public sealed record QuotaStatus(int UsedUnits, int CapUnits, int UploadCost)
{
    public int RemainingUnits => Math.Max(0, CapUnits - UsedUnits);
    public int RemainingUploads => RemainingUnits / UploadCost;
    public int TotalUploads => CapUnits / UploadCost;
}

/// <summary>
/// Per-OAuth-client daily YouTube quota counter in Redis, reset at midnight Pacific Time. YouTube
/// quota is enforced per Google Cloud project (OAuth client), so the counter is keyed by the OAuth
/// client id: every account that consented through the same client shares this daily cap, and
/// connecting one channel through several clients gives it that many independent caps.
/// </summary>
public interface IQuotaService
{
    Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId);
    /// <summary>Atomically reserve one upload's units against the client's PT cap (false if it would exceed).</summary>
    Task<bool> TryReserveUploadAsync(Guid oauthClientId);
    /// <summary>Give units back (only when reserved but the API was never called).</summary>
    Task ReleaseAsync(Guid oauthClientId, int units);
}

public sealed class QuotaService(IConnectionMultiplexer redis, IOptions<AppOptions> options) : IQuotaService
{
    private static readonly TimeZoneInfo Pacific = ResolvePacific();
    private readonly AppOptions _opt = options.Value;

    public async Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId)
    {
        // No client (e.g. an account not yet bound to one) ⇒ no usable quota. Report ZERO cap rather
        // than a full cap, so the UI shows "0 of 0 uploads" for a broken/unbound account instead of
        // a misleading "full quota available".
        if (oauthClientId is null)
            return new QuotaStatus(0, 0, _opt.YouTubeUploadCostUnits);

        var val = await redis.GetDatabase().StringGetAsync(RedisKeys.Quota(oauthClientId.Value, PtDate()));
        var used = val.HasValue ? (int)val : 0;
        return new QuotaStatus(used, _opt.YouTubeDailyQuotaUnits, _opt.YouTubeUploadCostUnits);
    }

    public async Task<bool> TryReserveUploadAsync(Guid oauthClientId)
    {
        var db = redis.GetDatabase();
        var key = RedisKeys.Quota(oauthClientId, PtDate());
        var cost = _opt.YouTubeUploadCostUnits;

        var after = await db.StringIncrementAsync(key, cost);
        if (after == cost) // first increment of this PT day -> bound the key's lifetime
            await db.KeyExpireAsync(key, TimeUntilPtMidnight() + TimeSpan.FromHours(1));

        if (after > _opt.YouTubeDailyQuotaUnits)
        {
            await db.StringDecrementAsync(key, cost); // roll back the over-reservation
            return false;
        }
        return true;
    }

    public Task ReleaseAsync(Guid oauthClientId, int units)
        => redis.GetDatabase().StringDecrementAsync(RedisKeys.Quota(oauthClientId, PtDate()), units);

    // ---- Pacific-Time helpers --------------------------------------------------------
    private static string PtDate()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific).ToString("yyyy-MM-dd");

    private static TimeSpan TimeUntilPtMidnight()
    {
        var nowPt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific);
        var nextMidnight = nowPt.Date.AddDays(1);
        return nextMidnight - nowPt.DateTime;
    }

    private static TimeZoneInfo ResolvePacific()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
    }
}
