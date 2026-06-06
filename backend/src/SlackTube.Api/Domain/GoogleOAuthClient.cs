namespace SlackTube.Api.Domain;

/// <summary>
/// One Google OAuth client = one Google Cloud project. YouTube Data API quota is enforced PER
/// PROJECT (OAuth client), so adding more clients raises the per-channel upload ceiling: connect
/// the same channel through several clients and the worker rotates to the next when one project's
/// daily quota is exhausted (see <see cref="Services.Jobs.UploadJobHandler"/>).
///
/// HARD RULE: a refresh token can only be refreshed by the client that issued it — every
/// <see cref="GoogleAccount"/> permanently remembers its issuing client (<c>OAuthClientId</c>) and
/// must always rebuild credentials with THAT client. The client secret is stored ENCRYPTED
/// (<see cref="Services.Secrets.ISecretProtector"/>, same as Slack secrets) and never leaves the server.
/// </summary>
public class GoogleOAuthClient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly name, e.g. "Project A".</summary>
    public string Label { get; set; } = "";

    /// <summary>Google OAuth client id — NOT a secret (it appears in the consent URL).</summary>
    public string ClientId { get; set; } = default!;

    /// <summary>Client secret, encrypted at rest via <see cref="Services.Secrets.ISecretProtector"/>.</summary>
    public string EncryptedClientSecret { get; set; } = default!;

    /// <summary>Active / Disabled. A disabled client is skipped by upload rotation.</summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
