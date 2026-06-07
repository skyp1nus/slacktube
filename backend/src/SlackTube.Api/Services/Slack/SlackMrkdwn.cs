using System.Text.RegularExpressions;

namespace SlackTube.Api.Services.Slack;

/// <summary>
/// Turns Slack message text (mrkdwn) into plain, human-readable text that is safe for sinks which
/// forbid Slack's angle-bracket entities — notably YouTube video metadata, where any '&lt;' or '&gt;'
/// is rejected with <c>invalidDescription</c>/<c>invalidTitle</c> (HTTP 400).
///
/// Slack wraps links and mentions in angle brackets: <c>&lt;url|label&gt;</c>, <c>&lt;@U123|name&gt;</c>,
/// <c>&lt;#C12|chan&gt;</c>, <c>&lt;!here&gt;</c>, <c>&lt;!subteam^S1|@grp&gt;</c>. Any '&lt;' '&gt;' '&amp;'
/// the user typed literally arrives HTML-escaped as <c>&amp;lt; &amp;gt; &amp;amp;</c>. We expand the
/// entities to readable text (keeping URLs as plain text so YouTube auto-links them), decode the
/// escapes, then drop any stray angle brackets that remain.
/// </summary>
public static partial class SlackMrkdwn
{
    public static string ToPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Slack only emits raw '<'..'>' for entities; literal brackets the user typed are escaped,
        // so every raw <...> here is a link/mention we expand to plain text.
        var s = EntityPattern().Replace(text, Expand);
        // Decode Slack's HTML escapes for literal characters the user typed.
        s = s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
        // Final guard: any '<'/'>' still present is a literal YouTube would reject — strip it.
        return s.Replace("<", string.Empty).Replace(">", string.Empty);
    }

    private static string Expand(Match m)
    {
        var body = m.Groups[1].Value;                 // text between < and >
        var pipe = body.IndexOf('|');
        var target = pipe >= 0 ? body[..pipe] : body;
        var label = pipe >= 0 ? body[(pipe + 1)..] : null;
        var hasLabel = !string.IsNullOrEmpty(label);

        // <@U123> user mention, <@U123|name> with display name
        if (target.StartsWith('@'))
            return hasLabel ? "@" + label : target;
        // <#C123|name> channel mention
        if (target.StartsWith('#'))
            return hasLabel ? "#" + label : target;
        // <!here>, <!channel>, <!everyone>, <!subteam^S1|@grp>, <!date^..|fallback>
        if (target.StartsWith('!'))
        {
            if (hasLabel) return label!;
            var kw = target[1..];
            return kw is "here" or "channel" or "everyone" ? "@" + kw : kw;
        }
        // Plain link <url> or <url|label> — keep the URL as plain text so YouTube makes it clickable.
        if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            target = target["mailto:".Length..];
        if (hasLabel && !string.Equals(label, target, StringComparison.Ordinal))
            return $"{label} ({target})";
        return target;
    }

    // Entities never contain a literal '>' inside, so [^>]+ matches a single entity safely.
    [GeneratedRegex(@"<([^>]+)>")]
    private static partial Regex EntityPattern();
}
