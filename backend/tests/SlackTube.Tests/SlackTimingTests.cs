using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

/// <summary>The Slack "took" suffix must report END-TO-END time (Drive download + YouTube upload), not just
/// the upload phase — the bug where a long download made "took 5s" wildly understate reality.</summary>
public class SlackTimingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TookIncludesDownloadAndSplitsPhases()
    {
        // download 12:00→12:01 (1m), upload 12:01→12:01:30 (30s) ⇒ total 1m 30s, not 30s.
        var suffix = SlackBlocks.TookSuffix(
            downloadStart: T0, uploadStart: T0.AddSeconds(60), completed: T0.AddSeconds(90));

        Assert.Contains("took 1m 30s", suffix);
        Assert.Contains(":arrow_down: 1m 0s", suffix); // download phase
        Assert.Contains(":arrow_up: 30s", suffix);     // upload phase
    }

    [Fact]
    public void FallsBackToUploadStartForJobsWithoutDownloadTiming()
    {
        // Pre-existing jobs (no DownloadStartedAt) keep the old upload-only behavior, no phase breakdown.
        var suffix = SlackBlocks.TookSuffix(
            downloadStart: null, uploadStart: T0, completed: T0.AddSeconds(5));

        Assert.Equal(" · took 5s", suffix);
        Assert.DoesNotContain(":arrow_down:", suffix);
    }

    [Fact]
    public void NoBreakdownWhenUploadPhaseUnknown()
    {
        // download started but upload never recorded ⇒ total only, no split.
        var suffix = SlackBlocks.TookSuffix(
            downloadStart: T0, uploadStart: null, completed: T0.AddSeconds(45));

        Assert.Equal(" · took 45s", suffix);
        Assert.DoesNotContain(":arrow_down:", suffix);
    }

    [Fact]
    public void TotalReconcilesWithPhasesOnSubSecondBoundaries()
    {
        // download 1.9s, upload 1.9s: each truncates to 1s; the total must be their SUM (2s), not the
        // raw 3.8s span truncated to 3s — so a reader sees ⬇ + ⬆ add up to the shown total.
        var suffix = SlackBlocks.TookSuffix(
            downloadStart: T0, uploadStart: T0.AddSeconds(1.9), completed: T0.AddSeconds(3.8));

        Assert.Equal(" · took 2s (:arrow_down: 1s · :arrow_up: 1s)", suffix);
    }

    [Fact]
    public void EmptyWhenNoElapsedInterval()
    {
        Assert.Equal("", SlackBlocks.TookSuffix(T0, T0.AddSeconds(10), T0));            // completed == began
        Assert.Equal("", SlackBlocks.TookSuffix(null, null, T0.AddSeconds(10)));        // no start at all
    }
}
