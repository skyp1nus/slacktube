using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using SlackTube.Api.Services.Jobs;

namespace SlackTube.Api.Services.Google;

public sealed record DriveFileInfo(string Name, long? Size, string? MimeType)
{
    /// <summary>Native Google Docs/Sheets/etc. have no byte content and can't be uploaded as-is.</summary>
    public bool IsGoogleNative => MimeType?.StartsWith("application/vnd.google-apps", StringComparison.Ordinal) == true;
}

public sealed class DriveDownloadService(GoogleCredentialFactory factory, IApiUsageService usage)
{
    public DriveService BuildService(string clientId, string clientSecret, string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(clientId, clientSecret, refreshToken),
            ApplicationName = "SlackTube",
        });

    /// <summary>files.get metadata. <paramref name="oauthClientId"/> meters the call against that project's
    /// daily Drive usage (one query).</summary>
    public async Task<DriveFileInfo> GetInfoAsync(DriveService service, string fileId, Guid oauthClientId, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.Fields = "name,size,mimeType";
        req.SupportsAllDrives = true;
        var file = await req.ExecuteAsync(ct);
        await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveQueries, 1);
        return new DriveFileInfo(file.Name, file.Size, file.MimeType);
    }

    /// <summary>Streams binary content to <paramref name="destination"/>, reporting bytes downloaded.
    /// Honors cancellation (used to abort + clean up on user cancel during the download phase). Meters one
    /// Drive query plus the bytes streamed against <paramref name="oauthClientId"/>'s daily usage.</summary>
    public async Task DownloadAsync(
        DriveService service, string fileId, Stream destination, Action<long> onBytes, int chunkSize,
        Guid oauthClientId, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.SupportsAllDrives = true;
        // MediaDownloader rejects a ChunkSize above its MaximumChunkSize → clamp so any configured value is valid.
        req.MediaDownloader.ChunkSize = Math.Min(chunkSize, MediaDownloader.MaximumChunkSize); // fewer range requests on large files
        long lastBytes = 0;
        req.MediaDownloader.ProgressChanged += p =>
        {
            if (p.Status is DownloadStatus.Downloading or DownloadStatus.Completed)
            {
                lastBytes = p.BytesDownloaded;
                onBytes(p.BytesDownloaded);
            }
        };

        try
        {
            var result = await req.DownloadAsync(destination, ct);
            if (result.Status == DownloadStatus.Failed)
                throw new InvalidOperationException("Drive download failed.", result.Exception);
        }
        finally
        {
            // Meter actual spend on every outcome (success, failure, or user cancel): the files.get request
            // was issued and these bytes really left Drive, so they count toward the project's daily usage.
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveQueries, 1);
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveBytes, lastBytes);
        }
    }
}
