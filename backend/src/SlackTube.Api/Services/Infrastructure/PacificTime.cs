namespace SlackTube.Api.Services.Infrastructure;

/// <summary>
/// Single source of truth for the Pacific-Time "quota day". Google's YouTube/Drive quotas reset at
/// midnight Pacific, so every per-day counter keys on this date and expires at PT midnight.
/// </summary>
public static class PacificTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Today's PT date as <c>yyyy-MM-dd</c> — used as the suffix of every per-day Redis key.</summary>
    public static string TodayKey()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).ToString("yyyy-MM-dd");

    /// <summary>Time remaining until the next PT midnight (used to bound a counter key's lifetime). Computes
    /// the next-midnight instant with its own zone offset so the span stays correct across DST transitions
    /// (the two days a year a PT day is 23h or 25h long).</summary>
    public static TimeSpan UntilMidnight()
    {
        var nowPt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
        var nextMidnightLocal = nowPt.Date.AddDays(1);
        var nextMidnight = new DateTimeOffset(nextMidnightLocal, Zone.GetUtcOffset(nextMidnightLocal));
        return nextMidnight - nowPt;
    }

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
    }
}
