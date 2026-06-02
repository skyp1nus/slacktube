using System.Text.RegularExpressions;

namespace SlackTube.Api.Services.Slack;

public sealed record ParsedTemplate
{
    /// <summary>True when the message looks like an UPLOAD command (header or a known label present).</summary>
    public bool IsUploadTemplate { get; init; }
    public string? RawVideoValue { get; init; }
    public string? DriveFileId { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? Description { get; init; }
    /// <summary>Non-fatal notes (e.g. tag length trimming) to surface back in Slack.</summary>
    public List<string> Warnings { get; init; } = new();

    public bool HasVideo => !string.IsNullOrEmpty(DriveFileId);
    public bool HasTags => Tags.Count > 0;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

/// <summary>
/// Parses the upload template. Multiline <c>Description</c> goes LAST; single-line labels
/// (Video, Tags, and future Thumbnail/Category/Playlist/Visibility) sit above it. Labels are
/// English, case-insensitive, trimmed. To add a future single-line field, add it to
/// <see cref="KnownSingleLineLabels"/> and read it from the parsed values.
/// </summary>
public sealed partial class SlackTemplateParser
{
    private const int MaxTagLength = 100;
    private const int MaxTagsTotalLength = 500;

    private static readonly HashSet<string> KnownSingleLineLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "video", "tags", "thumbnail", "category", "playlist", "visibility",
    };

    public ParsedTemplate Parse(string text)
    {
        text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        // 1) Description label terminates the single-line region; everything after it is free text.
        int descIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                descIdx = i;
                break;
            }
        }

        string? description = null;
        if (descIdx >= 0)
        {
            var firstRest = lines[descIdx].TrimStart()["description:".Length..];
            var rest = string.Join('\n', lines.Skip(descIdx + 1));
            description = (firstRest + "\n" + rest).Trim();
            if (description.Length == 0) description = null;
        }

        // 2) Single-line labels above the Description block.
        int headerEnd = descIdx >= 0 ? descIdx : lines.Length;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines.Take(headerEnd))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var label = line[..colon].Trim();
            if (!KnownSingleLineLabels.Contains(label)) continue;
            values[label] = line[(colon + 1)..].Trim();
        }

        var videoRaw = values.GetValueOrDefault("video");
        var fileId = videoRaw is null ? null : ExtractDriveFileId(videoRaw);
        var (tags, warnings) = ParseTags(values.GetValueOrDefault("tags"));

        var isTemplate = HasUploadHeader(lines) || values.Count > 0 || descIdx >= 0;

        return new ParsedTemplate
        {
            IsUploadTemplate = isTemplate,
            RawVideoValue = videoRaw,
            DriveFileId = fileId,
            Tags = tags,
            Description = description,
            Warnings = warnings,
        };
    }

    /// <summary>Extracts a Drive file ID from /file/d/&lt;ID&gt;/, ?id=, &amp;id=, /uc?id=, or a bare ID.
    /// Tolerates Slack link markup &lt;url|label&gt;.</summary>
    public static string? ExtractDriveFileId(string value)
    {
        value = value.Trim();

        // Unwrap Slack auto-link: <url> or <url|text>
        var lt = value.IndexOf('<');
        var gt = value.IndexOf('>');
        if (lt >= 0 && gt > lt) value = value.Substring(lt + 1, gt - lt - 1);
        var pipe = value.IndexOf('|');
        if (pipe >= 0) value = value[..pipe];
        value = value.Trim();

        foreach (var rx in new[] { FileDPattern(), IdParamPattern(), ShortDPattern() })
        {
            var m = rx.Match(value);
            if (m.Success) return m.Groups[1].Value;
        }

        // Bare file ID pasted directly.
        return BareIdPattern().IsMatch(value) ? value : null;
    }

    private static (List<string> tags, List<string> warnings) ParseTags(string? raw)
    {
        var tags = new List<string>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return (tags, warnings);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(','))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            if (t.Length > MaxTagLength)
            {
                warnings.Add($"Tag “{Truncate(t, 20)}…” exceeds {MaxTagLength} chars — trimmed.");
                t = t[..MaxTagLength];
            }
            if (seen.Add(t)) tags.Add(t);
        }

        // YouTube caps the combined tag length (~500 chars incl. separators).
        var total = tags.Sum(t => t.Length + 1);
        if (total > MaxTagsTotalLength)
        {
            var kept = new List<string>();
            var run = 0;
            foreach (var t in tags)
            {
                if (run + t.Length + 1 > MaxTagsTotalLength) break;
                kept.Add(t);
                run += t.Length + 1;
            }
            warnings.Add($"Tags total ~{total} chars exceed YouTube's ~{MaxTagsTotalLength} limit — kept the first {kept.Count}.");
            tags = kept;
        }

        return (tags, warnings);
    }

    private static bool HasUploadHeader(string[] lines)
    {
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.Length == 0) continue;
            var letters = new string(t.Where(char.IsLetter).ToArray());
            return letters.Equals("UPLOAD", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    [GeneratedRegex(@"/file/d/([A-Za-z0-9_-]{10,})")] private static partial Regex FileDPattern();
    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]{10,})")] private static partial Regex IdParamPattern();
    [GeneratedRegex(@"/d/([A-Za-z0-9_-]{10,})")] private static partial Regex ShortDPattern();
    [GeneratedRegex(@"^[A-Za-z0-9_-]{20,}$")] private static partial Regex BareIdPattern();
}
