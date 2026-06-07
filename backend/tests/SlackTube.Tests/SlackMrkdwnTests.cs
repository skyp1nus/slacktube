using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

public class SlackMrkdwnTests
{
    [Fact]
    public void LabeledLinkKeepsUrlAsPlainText()
        => Assert.Equal("Watch here (https://example.com)",
            SlackMrkdwn.ToPlainText("<https://example.com|Watch here>"));

    [Fact]
    public void BareLinkUnwrapsToUrl()
        => Assert.Equal("https://example.com",
            SlackMrkdwn.ToPlainText("<https://example.com>"));

    [Fact]
    public void LabelEqualToUrlIsNotDuplicated()
        => Assert.Equal("https://example.com",
            SlackMrkdwn.ToPlainText("<https://example.com|https://example.com>"));

    [Fact]
    public void MailtoSchemeStripped()
        => Assert.Equal("team@acme.com",
            SlackMrkdwn.ToPlainText("<mailto:team@acme.com|team@acme.com>"));

    [Fact]
    public void UserMentionWithNameBecomesAtName()
        => Assert.Equal("@dana", SlackMrkdwn.ToPlainText("<@U12345|dana>"));

    [Fact]
    public void UserMentionWithoutNameKeepsId()
        => Assert.Equal("@U12345", SlackMrkdwn.ToPlainText("<@U12345>"));

    [Fact]
    public void ChannelMentionBecomesHashName()
        => Assert.Equal("#general", SlackMrkdwn.ToPlainText("<#C0001|general>"));

    [Fact]
    public void HereSpecialBecomesAtHere()
        => Assert.Equal("@here", SlackMrkdwn.ToPlainText("<!here>"));

    [Fact]
    public void SubteamUsesLabel()
        => Assert.Equal("@admins", SlackMrkdwn.ToPlainText("<!subteam^S0001|@admins>"));

    [Fact]
    public void HtmlEscapesDecoded()
        => Assert.Equal("Tom & Jerry", SlackMrkdwn.ToPlainText("Tom &amp; Jerry"));

    [Fact]
    public void LiteralEscapedAngleBracketsStripped()
        => Assert.Equal("a b c", SlackMrkdwn.ToPlainText("a &lt;b&gt; c"));

    [Fact]
    public void MultipleEntitiesInOneMessage()
    {
        const string input = "Hey <@U1|sam> see <https://yt.be/x|the clip> in <#C2|news> &amp; enjoy";
        Assert.Equal("Hey @sam see the clip (https://yt.be/x) in #news & enjoy",
            SlackMrkdwn.ToPlainText(input));
    }

    [Fact]
    public void PlainTextWithoutEntitiesUnchanged()
        => Assert.Equal("Just a normal http://example.com line",
            SlackMrkdwn.ToPlainText("Just a normal http://example.com line"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyReturnsEmpty(string? input)
        => Assert.Equal(string.Empty, SlackMrkdwn.ToPlainText(input));

    [Fact]
    public void ResultNeverContainsAngleBrackets()
    {
        const string nasty = "<https://a.b|x> <@U1> <!channel> raw &lt;tag&gt; <#C9|y>";
        var result = SlackMrkdwn.ToPlainText(nasty);
        Assert.DoesNotContain('<', result);
        Assert.DoesNotContain('>', result);
    }
}
