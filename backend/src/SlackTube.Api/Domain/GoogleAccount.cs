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

    /// <summary>
    /// The OAuth client (Google Cloud project) that issued this account's refresh token. A refresh
    /// token can ONLY be refreshed by its issuing client, so this binding is permanent and every
    /// credential rebuild for this account MUST use this client. Nullable only during the
    /// migration backfill window; treated as required once the startup seeder has run.
    /// </summary>
    public Guid? OAuthClientId { get; set; }
    public GoogleOAuthClient? OAuthClient { get; set; }

    public string? YouTubeChannelId { get; set; }
    public string? YouTubeChannelTitle { get; set; }
    /// <summary>Channel avatar (snippet.thumbnails) URL, captured at consent for display.</summary>
    public string? AvatarUrl { get; set; }
    public string? AccountEmail { get; set; }

    public string EncryptedRefreshToken { get; set; } = default!;
    public string Scopes { get; set; } = default!;

    /// <summary>Active / Error (free-form for the MVP).</summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
