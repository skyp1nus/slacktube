using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace SlackTube.Api.Services.Google;

public sealed record YouTubeUploadResult(string VideoId, string Url);

public sealed class YouTubeUploadService(GoogleCredentialFactory factory)
{
    private const int MaxTitleLength = 100;
    private const string DefaultCategoryId = "22"; // People & Blogs

    public YouTubeService BuildService(string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(refreshToken),
            ApplicationName = "SlackTube",
        });

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
        CancellationToken ct)
    {
        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = NormalizeTitle(title),
                Description = description ?? string.Empty,
                Tags = tags.Count > 0 ? tags : null,
                CategoryId = DefaultCategoryId,
            },
            Status = new VideoStatus { PrivacyStatus = "private" },
        };

        var request = service.Videos.Insert(video, "snippet,status", videoStream, "video/*");
        request.NotifySubscribers = false;

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

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Untitled upload";
        title = title.Trim();
        return title.Length <= MaxTitleLength ? title : title[..MaxTitleLength];
    }
}
