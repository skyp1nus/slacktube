using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace SlackTube.Api.Services.Google;

public sealed record YouTubeUploadResult(string VideoId, string Url);

public sealed class YouTubeUploadService(GoogleCredentialFactory factory)
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;
    private const string DefaultCategoryId = "22"; // People & Blogs

    public YouTubeService BuildService(string clientId, string clientSecret, string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(clientId, clientSecret, refreshToken),
            ApplicationName = "SlackTube",
        });

    /// <summary>The authenticated account's own channel id + title + avatar (channels.list?mine=true, ~1 unit).
    /// The single caller (<c>GoogleOAuthService.ExchangeAndStoreAsync</c>) meters this against the unit pool;
    /// any new caller must charge <c>IQuotaService.ChargeUnitsAsync</c> too.</summary>
    public async Task<(string? Id, string? Title, string? AvatarUrl)> GetChannelInfoAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
    {
        var request = BuildService(clientId, clientSecret, refreshToken).Channels.List("snippet");
        request.Mine = true;
        var response = await request.ExecuteAsync(ct);
        var item = response.Items?.FirstOrDefault();
        var thumbs = item?.Snippet?.Thumbnails;
        var avatar = thumbs?.High?.Url ?? thumbs?.Medium?.Url ?? thumbs?.Default__?.Url;
        return (item?.Id, item?.Snippet?.Title, avatar);
    }

    /// <summary>
    /// Resumable upload as PRIVATE. <paramref name="onBytes"/> fires during transfer;
    /// <paramref name="onProcessing"/> fires once all bytes are sent (YouTube still transcodes).
    /// Returns the new video id. Note: a failed resumable upload surfaces via IUploadProgress,
    /// not an exception — we inspect the result status explicitly.
    /// </summary>
    public async Task<YouTubeUploadResult> UploadAsync(
        YouTubeService service,
        Stream videoStream,
        string title,
        string? description,
        IList<string> tags,
        Action<long> onBytes,
        Action onProcessing,
        string visibility,
        int chunkSize,
        CancellationToken ct)
    {
        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = NormalizeTitle(title),
                Description = NormalizeDescription(description),
                Tags = NormalizeTags(tags),
                CategoryId = DefaultCategoryId,
            },
            Status = new VideoStatus { PrivacyStatus = NormalizeVisibility(visibility) },
        };

        var request = service.Videos.Insert(video, "snippet,status", videoStream, "video/*");
        request.NotifySubscribers = false;
        request.ChunkSize = chunkSize; // fewer resumable-upload requests on large files

        string? videoId = null;
        var processingFired = false;
        request.ProgressChanged += p =>
        {
            switch (p.Status)
            {
                case UploadStatus.Uploading:
                    onBytes(p.BytesSent);
                    break;
                case UploadStatus.Completed when !processingFired:
                    processingFired = true;
                    onProcessing();
                    break;
            }
        };
        request.ResponseReceived += v => videoId = v.Id;

        var result = await request.UploadAsync(ct);
        if (result.Status == UploadStatus.Failed)
            throw new InvalidOperationException("YouTube upload failed.", result.Exception);
        if (string.IsNullOrEmpty(videoId))
            throw new InvalidOperationException("YouTube upload completed but returned no video id.");

        return new YouTubeUploadResult(videoId, $"https://youtu.be/{videoId}");
    }

    // YouTube rejects any '<' or '>' in a title/description (invalidTitle / invalidDescription).
    // Slack markup is already unwrapped upstream; these strips are the last-line guard and also
    // sanitise jobs whose description was captured before that unwrap existed.
    internal static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Untitled upload";
        title = StripAngleBrackets(title).Trim();
        if (title.Length == 0) return "Untitled upload";
        return title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];
    }

    internal static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) return string.Empty;
        var d = StripAngleBrackets(description);
        return d.Length <= MaxDescriptionLength ? d : d[..MaxDescriptionLength];
    }

    private static string StripAngleBrackets(string s) =>
        s.Replace("<", string.Empty).Replace(">", string.Empty);

    // YouTube returns invalidTags when a tag holds a '<'/'>' or when the tags' combined length
    // exceeds ~500 chars. It serialises tags as an array and wraps any tag containing whitespace in
    // double quotes — those quotes count toward the limit, so a spaced tag costs length + 2. We strip
    // brackets, trim/dedupe, then keep tags until the quote-aware budget is spent. Margin under 500.
    private const int MaxTagLength = 100;
    private const int MaxTagsTotalChars = 480;

    internal static IList<string>? NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null) return null;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = 0;
        foreach (var raw in tags)
        {
            var t = StripAngleBrackets(raw).Trim();
            if (t.Length > MaxTagLength) t = t[..MaxTagLength].Trim();
            if (t.Length == 0 || !seen.Add(t)) continue;
            var cost = t.Length + (t.Any(char.IsWhiteSpace) ? 2 : 0);
            if (budget + cost > MaxTagsTotalChars) break;
            budget += cost;
            result.Add(t);
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Only YouTube's three privacy values are valid; anything else falls back to private.</summary>
    public static string NormalizeVisibility(string? visibility) => visibility?.Trim().ToLowerInvariant() switch
    {
        "public" => "public",
        "unlisted" => "unlisted",
        _ => "private",
    };
}
