namespace SlackTube.Api.Services.Jobs;

// ---- view models the /api/admin/usage endpoint returns (serialized camelCase) ----
public sealed record UsageScopeView(string Scope, long Used, long? Limit);

public sealed record UsageMetricView(
    string Key, string Label, string Unit, long Used, long? Limit, IReadOnlyList<UsageScopeView> PerScope);

public sealed record UsageGroupView(string Group, IReadOnlyList<UsageMetricView> Metrics);

public sealed record UsageReportView(string Date, IReadOnlyList<UsageGroupView> Groups);

/// <summary>
/// Pure shaping of today's API spend into a grouped report: YouTube (uploads + non-upload units, from the
/// per-project quota counters), Drive (files.get calls + bytes, from the usage counters), and Slack (per
/// Web-API method). No I/O — the endpoint fetches the data and hands it here, so this is unit-testable.
/// </summary>
public static class ApiUsageReport
{
    /// <summary>One OAuth client (Cloud project) + its current quota snapshot.</summary>
    public sealed record ClientQuota(Guid ClientId, string Label, QuotaStatus Status);

    public static UsageReportView Build(
        string date,
        IReadOnlyList<ClientQuota> clients,
        IReadOnlyList<UsageEntry> usage,
        long driveQueryLimit)
    {
        var labelByScope = new Dictionary<string, string>();
        foreach (var c in clients) labelByScope[c.ClientId.ToString()] = c.Label;
        string Label(string scope) => labelByScope.TryGetValue(scope, out var l) ? l : ShortScope(scope);

        // ---- YouTube: from the quota service (upload bucket + non-upload unit pool), per project ----
        var youtube = new List<UsageMetricView>
        {
            Metric(ApiMetrics.YouTubeUpload, "Uploads (videos.insert)", "uploads",
                clients.Select(c => new UsageScopeView(c.Label, c.Status.UsedUploads, c.Status.UploadLimit))),
            Metric(ApiMetrics.YouTubeUnits, "Units (non-upload)", "units",
                clients.Select(c => new UsageScopeView(c.Label, c.Status.UsedUnits, c.Status.CapUnits))),
        };

        // ---- Drive: from the usage counters, keyed per project. The total query limit is the sum of the
        // per-project limits actually present, so headline and breakdown share one denominator. ----
        var driveQueries = usage.Where(u => u.Metric == ApiMetrics.DriveQueries)
            .Select(u => new UsageScopeView(Label(u.Scope), u.Value, driveQueryLimit));
        var driveBytes = usage.Where(u => u.Metric == ApiMetrics.DriveBytes)
            .Select(u => new UsageScopeView(Label(u.Scope), u.Value, null));
        var drive = new List<UsageMetricView>
        {
            Metric(ApiMetrics.DriveQueries, "Drive API calls (files.get)", "queries", driveQueries),
            Metric(ApiMetrics.DriveBytes, "Drive download", "bytes", driveBytes,
                limit: null, limitSet: true),
        };

        // ---- Slack: global per-method call counters ----
        var slack = usage
            .Where(u => u.Scope == ApiMetrics.SlackScope && u.Metric.StartsWith("slack.", StringComparison.Ordinal))
            .OrderByDescending(u => u.Value)
            .Select(u => new UsageMetricView(
                u.Metric, u.Metric["slack.".Length..], "calls", u.Value, null, Array.Empty<UsageScopeView>()))
            .ToList();

        return new UsageReportView(date, new List<UsageGroupView>
        {
            new("YouTube", youtube),
            new("Drive", drive),
            new("Slack", slack),
        });
    }

    /// <summary>Roll a metric's per-scope rows into a total. When <paramref name="limitSet"/> the given limit
    /// is authoritative; otherwise the total limit is the sum of per-scope limits (null if any is unbounded).</summary>
    private static UsageMetricView Metric(
        string key, string label, string unit, IEnumerable<UsageScopeView> perScopeSrc,
        long? limit = null, bool limitSet = false)
    {
        var perScope = perScopeSrc.ToList();
        var used = perScope.Sum(s => s.Used);
        long? total = limitSet
            ? limit
            : (perScope.Count > 0 && perScope.All(s => s.Limit.HasValue) ? perScope.Sum(s => s.Limit!.Value) : null);
        return new UsageMetricView(key, label, unit, used, total, perScope);
    }

    private static string ShortScope(string scope) => scope.Length <= 8 ? scope : scope[..8];
}
