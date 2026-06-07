using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Jobs;
using Xunit;

namespace SlackTube.Tests;

/// <summary>The Slack status "recent" list is bounded to the last 24h, so finished jobs self-prune instead
/// of lingering forever as the top-N (the bug: day-old uploads never disappeared from the channel status).</summary>
public class JobStatusSnapshotTests
{
    [Fact]
    public async Task RecentExcludesJobsOlderThan24h()
    {
        await using var db = NewDb();
        db.Jobs.AddRange(
            Done("C1", DateTimeOffset.UtcNow.AddHours(-25), "old.mp4"),
            Done("C1", DateTimeOffset.UtcNow.AddHours(-1), "fresh.mp4"));
        await db.SaveChangesAsync();
        var svc = new JobService(db);

        var snap = await svc.GetStatusSnapshotAsync("C1");

        Assert.Single(snap.Recent);
        Assert.Equal("fresh.mp4", snap.Recent[0].OriginalFileName);
        Assert.Equal(1, snap.UploadedLast24h); // only the fresh Done counts
    }

    [Fact]
    public async Task RecentStillCapsAtCountWithinWindow()
    {
        await using var db = NewDb();
        for (var i = 0; i < 8; i++)
            db.Jobs.Add(Done("C1", DateTimeOffset.UtcNow.AddMinutes(-i * 10), $"v{i}.mp4"));
        await db.SaveChangesAsync();
        var svc = new JobService(db);

        var snap = await svc.GetStatusSnapshotAsync("C1", recentCount: 5);

        Assert.Equal(5, snap.Recent.Count);                       // capped
        Assert.Equal("v0.mp4", snap.Recent[0].OriginalFileName);  // newest first
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UploadJob Done(string channel, DateTimeOffset at, string name) => new()
    {
        Id = Guid.NewGuid(),
        SlackEventId = Guid.NewGuid().ToString(),
        SlackChannelId = channel,
        SlackUserId = "U1",
        SlackMessageTs = "1.1",
        DriveFileId = "fid",
        OriginalFileName = name,
        State = JobState.Done,
        CreatedAt = at,
        UpdatedAt = at,
    };
}
