using System.Text.Json;
using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

/// <summary>The status header reports remaining/total daily uploads (summed across the channel's project
/// pool) and, when there were any, how many uploads completed in the last 24h.</summary>
public class SlackBlocksTests
{
    private static StatusView View(int remaining, int total, int uploadedLast24h, string? channelTitle = null) =>
        new(remaining, total, Active: null,
            Queued: Array.Empty<QueuedJobView>(), Recent: Array.Empty<DoneJobView>(),
            UploadedLast24h: uploadedLast24h, ChannelTitle: channelTitle);

    private static string HeaderText(StatusView v)
    {
        var (_, blocks) = SlackBlocks.Status(v);
        var json = JsonSerializer.Serialize(blocks);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("text").GetProperty("text").GetString()!;
    }

    [Fact]
    public void HeaderShowsRemainingOverTotal()
    {
        var header = HeaderText(View(remaining: 2, total: 12, uploadedLast24h: 0));
        Assert.Contains("2/12 remaining today", header);
    }

    [Fact]
    public void HeaderShowsUploadedLast24hWhenAny()
    {
        var header = HeaderText(View(remaining: 2, total: 12, uploadedLast24h: 3));
        Assert.Contains("3 uploaded in last 24h", header);
    }

    [Fact]
    public void HeaderOmitsUploadedLast24hWhenZero()
    {
        var header = HeaderText(View(remaining: 6, total: 6, uploadedLast24h: 0));
        Assert.DoesNotContain("24h", header);
    }

    [Fact]
    public void HeaderShowsChannelTitleWhenSet()
    {
        var header = HeaderText(View(remaining: 11, total: 12, uploadedLast24h: 0, channelTitle: "My Channel"));
        Assert.Contains("My Channel", header);
    }

    [Fact]
    public void HeaderOmitsChannelWhenTitleMissing()
    {
        var header = HeaderText(View(remaining: 6, total: 6, uploadedLast24h: 0, channelTitle: null));
        Assert.DoesNotContain(":tv:", header);
    }
}
