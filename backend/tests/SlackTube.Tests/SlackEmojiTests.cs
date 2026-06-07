using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

/// <summary>Slack delivers standard emoji as :shortcodes:; they must become real Unicode before upload so
/// YouTube shows 🔥 not the literal ":fire:". Unknown/custom codes and colon text must pass through.</summary>
public class SlackEmojiTests
{
    [Theory]
    [InlineData(":fire:", "🔥")]
    [InlineData("done :100:!", "done 💯!")]
    [InlineData("ship it :rocket: :tada:", "ship it 🚀 🎉")]
    [InlineData(":+1:", "👍")]
    [InlineData(":-1:", "👎")]
    [InlineData(":white_check_mark: ready", "✅ ready")]
    public void ConvertsKnownShortcodes(string input, string expected)
        => Assert.Equal(expected, SlackEmoji.ShortcodesToUnicode(input));

    [Theory]
    [InlineData(":totally_custom_company_logo:")] // custom Slack emoji — no Unicode
    [InlineData("ratio 16:9 native")]             // colon text, not a shortcode
    [InlineData("see https://example.com/x")]     // url with colon
    [InlineData("a:b:c")]                          // :b: matches the shape but isn't a known emoji
    public void LeavesUnknownOrNonEmojiUntouched(string input)
        => Assert.Equal(input, SlackEmoji.ShortcodesToUnicode(input));

    [Fact]
    public void DropsSkinToneModifiers()
        => Assert.Equal("👋", SlackEmoji.ShortcodesToUnicode(":wave::skin-tone-3:"));

    [Fact]
    public void RunsAsTheFinalStepOfToPlainText()
    {
        // A real description: a Slack link gets unwrapped AND emoji shortcodes get converted.
        var outp = SlackMrkdwn.ToPlainText("Watch <https://x.com|here> :fire: it's :100:");
        Assert.Equal("Watch here (https://x.com) 🔥 it's 💯", outp);
    }

    [Fact]
    public void UploadTimeNormalizeAlsoConvertsEmoji()
    {
        // Retroactive guard: a job queued before the parse-time fix still uploads with real emoji.
        Assert.Equal("ship it 🚀", YouTubeUploadService.NormalizeDescription("ship it :rocket:"));
    }
}
