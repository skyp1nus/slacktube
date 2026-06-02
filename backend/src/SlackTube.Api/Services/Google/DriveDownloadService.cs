using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace SlackTube.Api.Services.Google;

public sealed record DriveFileInfo(string Name, long? Size, string? MimeType)
{
    /// <summary>Native Google Docs/Sheets/etc. have no byte content and can't be uploaded as-is.</summary>
    public bool IsGoogleNative => MimeType?.StartsWith("application/vnd.google-apps", StringComparison.Ordinal) == true;
}

public sealed class DriveDownloadService(GoogleCredentialFactory factory)
{
    public DriveService BuildService(string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(refreshToken),
            ApplicationName = "SlackTube",
        });

    public async Task<DriveFileInfo> GetInfoAsync(DriveService service, string fileId, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.Fields = "name,size,mimeType";
        req.SupportsAllDrives = true;
        var file = await req.ExecuteAsync(ct);
        return new DriveFileInfo(file.Name, file.Size, file.MimeType);
    }

    /// <summary>Streams binary content to <paramref name="destination"/>, reporting bytes downloaded.
    /// Honors cancellation (used to abort + clean up on user cancel during the download phase).</summary>
    public async Task DownloadAsync(
        DriveService service, string fileId, Stream destination, Action<long> onBytes, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.SupportsAllDrives = true;
        req.MediaDownloader.ProgressChanged += p =>
        {
            if (p.Status is DownloadStatus.Downloading or DownloadStatus.Completed)
                onBytes(p.BytesDownloaded);
        };

        var result = await req.DownloadAsync(destination, ct);
        if (result.Status == DownloadStatus.Failed)
            throw new InvalidOperationException("Drive download failed.", result.Exception);
    }
}
