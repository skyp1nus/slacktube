using System.Linq;
using SlackTube.Api.Services.Google;
using Xunit;

namespace SlackTube.Tests;

public class YouTubeMetadataNormalizationTests
{
    [Fact]
    public void DescriptionStripsAngleBrackets()
        => Assert.Equal("see https://a.b|clip here",
            YouTubeUploadService.NormalizeDescription("see <https://a.b|clip> here"));

    [Fact]
    public void NullDescriptionBecomesEmpty()
        => Assert.Equal(string.Empty, YouTubeUploadService.NormalizeDescription(null));

    [Fact]
    public void DescriptionClampedTo5000()
    {
        var d = YouTubeUploadService.NormalizeDescription(new string('x', 6000));
        Assert.Equal(5000, d.Length);
    }

    [Fact]
    public void TitleStripsAngleBrackets()
        => Assert.Equal("hi there", YouTubeUploadService.NormalizeTitle("hi <there>"));

    [Fact]
    public void TitleClampedTo100()
    {
        var t = YouTubeUploadService.NormalizeTitle(new string('y', 130));
        Assert.Equal(100, t.Length);
    }

    [Fact]
    public void BlankTitleFallsBack()
        => Assert.Equal("Untitled upload", YouTubeUploadService.NormalizeTitle("   "));

    [Fact]
    public void TitleOfOnlyBracketsFallsBack()
        => Assert.Equal("Untitled upload", YouTubeUploadService.NormalizeTitle("<>"));

    // ---- Tags ----------------------------------------------------------------------------
    [Fact]
    public void TagsStripAngleBrackets()
        => Assert.Equal(new[] { "summer", "trip" },
            YouTubeUploadService.NormalizeTags(new[] { "<summer>", "tr<i>p" }));

    [Fact]
    public void TagsDropEmptyAndDuplicates()
        => Assert.Equal(new[] { "a", "b" },
            YouTubeUploadService.NormalizeTags(new[] { "a", "", "  ", "A", "b" }));

    [Fact]
    public void NullTagsReturnNull()
        => Assert.Null(YouTubeUploadService.NormalizeTags(null));

    [Fact]
    public void AllEmptyTagsReturnNull()
        => Assert.Null(YouTubeUploadService.NormalizeTags(new[] { "", "<>", "  " }));

    [Fact]
    public void LongTagClampedTo100()
    {
        var r = YouTubeUploadService.NormalizeTags(new[] { new string('x', 130) });
        Assert.Equal(100, r![0].Length);
    }

    [Fact]
    public void QuoteAwareBudgetDropsTagsOverLimit()
    {
        // 30 spaced tags of "ab cd" (5 chars + 2 quotes = 7 each) → 30×7 = 210 ok;
        // pad to exceed 480 with more spaced tags.
        var many = Enumerable.Range(0, 80).Select(i => $"tag {i:D3}").ToArray(); // each "tag NNN" = 7 chars +2
        var r = YouTubeUploadService.NormalizeTags(many)!;
        var budget = r.Sum(t => t.Length + (t.Any(char.IsWhiteSpace) ? 2 : 0));
        Assert.True(budget <= 480, $"budget {budget} must stay under 480");
        Assert.True(r.Count < many.Length, "some tags must be dropped");
    }
}
