using SlackTube.Api.Services.Jobs;
using Xunit;

namespace SlackTube.Tests;

/// <summary>The /api/admin/usage report shaping: YouTube from the per-project quota snapshots, Drive + Slack
/// from the generic usage counters, each grouped with correct totals + per-project breakdown.</summary>
public class ApiUsageReportTests
{
    private const long DriveLimit = 1_000_000_000;

    private static UsageGroupView Group(UsageReportView r, string name) =>
        r.Groups.Single(g => g.Group == name);

    private static UsageMetricView Metric(UsageGroupView g, string key) =>
        g.Metrics.Single(m => m.Key == key);

    [Fact]
    public void YouTubeMetricsSumUploadsAndUnitsAcrossProjects()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var clients = new List<ApiUsageReport.ClientQuota>
        {
            new(a, "Project A", new QuotaStatus(UsedUploads: 4, UploadLimit: 100, UsedUnits: 3, CapUnits: 10000)),
            new(b, "Project B", new QuotaStatus(UsedUploads: 0, UploadLimit: 100, UsedUnits: 0, CapUnits: 10000)),
        };

        var report = ApiUsageReport.Build("2026-06-07", clients, Array.Empty<UsageEntry>(), DriveLimit);

        var yt = Group(report, "YouTube");
        var uploads = Metric(yt, ApiMetrics.YouTubeUpload);
        Assert.Equal(4, uploads.Used);
        Assert.Equal(200, uploads.Limit);              // 100 + 100
        Assert.Equal(2, uploads.PerScope.Count);
        Assert.Contains(uploads.PerScope, s => s.Scope == "Project A" && s.Used == 4 && s.Limit == 100);

        var units = Metric(yt, ApiMetrics.YouTubeUnits);
        Assert.Equal(3, units.Used);
        Assert.Equal(20000, units.Limit);              // 10000 + 10000
    }

    [Fact]
    public void DriveQueriesAndBytesAggregatePerProjectFromUsage()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var clients = new List<ApiUsageReport.ClientQuota>
        {
            new(a, "Project A", new QuotaStatus(0, 100, 0, 10000)),
            new(b, "Project B", new QuotaStatus(0, 100, 0, 10000)),
        };
        var usage = new List<UsageEntry>
        {
            new(a.ToString(), ApiMetrics.DriveQueries, 5),
            new(b.ToString(), ApiMetrics.DriveQueries, 2),
            new(a.ToString(), ApiMetrics.DriveBytes, 1_000_000),
        };

        var report = ApiUsageReport.Build("2026-06-07", clients, usage, DriveLimit);

        var drive = Group(report, "Drive");
        var queries = Metric(drive, ApiMetrics.DriveQueries);
        Assert.Equal(7, queries.Used);                       // 5 + 2
        Assert.Equal(DriveLimit * 2, queries.Limit);         // per-project limit × client count
        Assert.Contains(queries.PerScope, s => s.Scope == "Project A" && s.Used == 5);

        var bytes = Metric(drive, ApiMetrics.DriveBytes);
        Assert.Equal(1_000_000, bytes.Used);
        Assert.Null(bytes.Limit);                            // bytes are unbounded
    }

    [Fact]
    public void SlackMethodsBecomeRowsSortedByVolume()
    {
        var clients = new List<ApiUsageReport.ClientQuota>();
        var usage = new List<UsageEntry>
        {
            new(ApiMetrics.SlackScope, ApiMetrics.Slack("chat.update"), 9),
            new(ApiMetrics.SlackScope, ApiMetrics.Slack("chat.postMessage"), 3),
        };

        var report = ApiUsageReport.Build("2026-06-07", clients, usage, DriveLimit);

        var slack = Group(report, "Slack");
        Assert.Equal(2, slack.Metrics.Count);
        Assert.Equal("chat.update", slack.Metrics[0].Label);   // highest volume first
        Assert.Equal(9, slack.Metrics[0].Used);
        Assert.Equal("calls", slack.Metrics[0].Unit);
        Assert.Null(slack.Metrics[0].Limit);
    }

    [Fact]
    public void UnknownDriveScopeFallsBackToShortId()
    {
        // A Drive call charged to a project no longer in the active client list still shows (short id label).
        var orphan = Guid.NewGuid();
        var usage = new List<UsageEntry> { new(orphan.ToString(), ApiMetrics.DriveQueries, 1) };

        var report = ApiUsageReport.Build("2026-06-07", new List<ApiUsageReport.ClientQuota>(), usage, DriveLimit);

        var queries = Metric(Group(report, "Drive"), ApiMetrics.DriveQueries);
        Assert.Equal(1, queries.Used);
        Assert.Equal(orphan.ToString()[..8], queries.PerScope.Single().Scope);
    }
}
