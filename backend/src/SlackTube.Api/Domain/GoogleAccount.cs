namespace SlackTube.Api.Domain;

/// <summary>
/// One connected Google/YouTube account. Each admin consent inserts a NEW row (multiple channels
/// supported). The refresh token is stored ENCRYPTED (<see cref="Services.Secrets.ISecretProtector"/>).
/// </summary>
public class GoogleAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display label (defaults to the YouTube channel title).</summary>
    public string Label { get; set; } = "";

    public string? YouTubeChannelId { get; set; }
    public string? YouTubeChannelTitle { get; set; }
    public string? AccountEmail { get; set; }

    public string EncryptedRefreshToken { get; set; } = default!;
    public string Scopes { get; set; } = default!;

    /// <summary>Active / Error (free-form for the MVP).</summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
