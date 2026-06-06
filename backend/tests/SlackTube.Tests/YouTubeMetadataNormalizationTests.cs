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
}
