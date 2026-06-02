using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

public class SlackTemplateParserTests
{
    private readonly SlackTemplateParser _parser = new();

    // ---- Drive file-id extraction across every supported form ------------------------
    [Theory]
    [InlineData("https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view?usp=sharing", "1A2B3C4D5E6F7G8H9I0J")]
    [InlineData("https://drive.google.com/open?id=1A2B3C4D5E6F7G8H9I0J", "1A2B3C4D5E6F7G8H9I0J")]
    [InlineData("https://drive.google.com/uc?id=1A2B3C4D5E6F7G8H9I0J&export=download", "1A2B3C4D5E6F7G8H9I0J")]
    [InlineData("https://docs.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/edit", "1A2B3C4D5E6F7G8H9I0J")]
    [InlineData("1A2B3C4D5E6F7G8H9I0JabcdefABCDEF", "1A2B3C4D5E6F7G8H9I0JabcdefABCDEF")]
    public void ExtractDriveFileId_handles_all_forms(string input, string expected)
        => Assert.Equal(expected, SlackTemplateParser.ExtractDriveFileId(input));

    [Fact]
    public void ExtractDriveFileId_unwraps_slack_link_markup()
    {
        const string slackWrapped = "<https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view|my video>";
        Assert.Equal("1A2B3C4D5E6F7G8H9I0J", SlackTemplateParser.ExtractDriveFileId(slackWrapped));
    }

    [Fact]
    public void ExtractDriveFileId_returns_null_for_garbage()
        => Assert.Null(SlackTemplateParser.ExtractDriveFileId("not a link"));

    // ---- Full template ----------------------------------------------------------------
    [Fact]
    public void Parse_full_template_extracts_all_fields()
    {
        var text = """
            🎬 UPLOAD

            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Tags: promo, june, launch
            Description:
            First line of description.
            Second line with a link http://example.com and emoji 🎉
            """;

        var r = _parser.Parse(text);

        Assert.True(r.IsUploadTemplate);
        Assert.True(r.HasVideo);
        Assert.Equal("1A2B3C4D5E6F7G8H9I0J", r.DriveFileId);
        Assert.Equal(new[] { "promo", "june", "launch" }, r.Tags);
        Assert.Contains("First line", r.Description);
        Assert.Contains("Second line", r.Description);
        Assert.Contains("🎉", r.Description);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Parse_description_is_multiline_safe_and_keeps_inner_colons()
    {
        var text = """
            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Tags: a
            Description:
            Note: this has a colon
            and multiple lines
            """;

        var r = _parser.Parse(text);
        Assert.Contains("Note: this has a colon", r.Description);
        Assert.Contains("and multiple lines", r.Description);
    }

    [Fact]
    public void Parse_inline_description_on_same_line()
    {
        var text = """
            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Description: short one-liner
            """;

        var r = _parser.Parse(text);
        Assert.Equal("short one-liner", r.Description);
    }

    // ---- Missing fields ---------------------------------------------------------------
    [Fact]
    public void Parse_missing_video_is_template_but_no_video()
    {
        var text = """
            🎬 UPLOAD
            Tags: a, b
            Description:
            hi
            """;

        var r = _parser.Parse(text);
        Assert.True(r.IsUploadTemplate);
        Assert.False(r.HasVideo);
    }

    [Fact]
    public void Parse_missing_tags_and_description_flags_soft()
    {
        var text = "Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view";
        var r = _parser.Parse(text);
        Assert.True(r.HasVideo);
        Assert.False(r.HasTags);
        Assert.False(r.HasDescription);
    }

    [Fact]
    public void Parse_non_template_message_ignored()
    {
        var r = _parser.Parse("hey team, lunch at noon?");
        Assert.False(r.IsUploadTemplate);
    }

    // ---- Tag rules --------------------------------------------------------------------
    [Fact]
    public void Parse_tags_trim_dedup_drop_empty()
    {
        var text = """
            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Tags:  promo ,  promo , , june ,
            Description: x
            """;

        var r = _parser.Parse(text);
        Assert.Equal(new[] { "promo", "june" }, r.Tags);
    }

    [Fact]
    public void Parse_tag_over_100_chars_is_trimmed_with_warning()
    {
        var longTag = new string('x', 130);
        var text = $"""
            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Tags: {longTag}
            Description: x
            """;

        var r = _parser.Parse(text);
        Assert.Equal(100, r.Tags[0].Length);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void Parse_tags_total_over_500_chars_drops_extras_with_warning()
    {
        // 10 tags × ~60 chars ≈ 600 > 500
        var tags = string.Join(", ", Enumerable.Range(0, 10).Select(i => new string((char)('a' + i), 60)));
        var text = $"""
            Video: https://drive.google.com/file/d/1A2B3C4D5E6F7G8H9I0J/view
            Tags: {tags}
            Description: x
            """;

        var r = _parser.Parse(text);
        Assert.True(r.Tags.Count < 10);
        Assert.Contains(r.Warnings, w => w.Contains("500"));
    }
}
