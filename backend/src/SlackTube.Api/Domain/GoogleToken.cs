namespace SlackTube.Api.Domain;

/// <summary>
/// The single OAuth refresh token owned by the YouTube channel account. The refresh
/// token is stored ENCRYPTED (ASP.NET Data Protection). One consent covers both Drive
/// download and YouTube upload, so a single row is enough for the MVP.
/// </summary>
public class GoogleToken
{
    public int Id { get; set; }

    /// <summary>Data-Protection-protected refresh token (opaque base64url string).</summary>
    public string EncryptedRefreshToken { get; set; } = default!;

    /// <summary>Space-separated granted scopes (youtube.upload + drive.readonly).</summary>
    public string Scopes { get; set; } = default!;

    /// <summary>Account email returned by the consent flow (for display only).</summary>
    public string? AccountEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
