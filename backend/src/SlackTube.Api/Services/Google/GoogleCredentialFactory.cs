using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.YouTube.v3;

namespace SlackTube.Api.Services.Google;

/// <summary>
/// Builds the OAuth pieces from an EXPLICIT client id + secret (one per Google Cloud project).
/// One consent covers both scopes. A <see cref="UserCredential"/> rebuilt from the stored refresh
/// token + its issuing client's secrets auto-refreshes its access token on every API call (no
/// browser, suitable for the background worker).
///
/// HARD RULE: a refresh token can only be refreshed by the client that issued it — callers MUST
/// pass the issuing client's creds (see <see cref="Domain.GoogleAccount.OAuthClientId"/>), never a
/// different client's.
/// </summary>
public sealed class GoogleCredentialFactory
{
    public static readonly string[] Scopes =
    {
        YouTubeService.Scope.YoutubeUpload,          // youtube.upload  — videos.insert
        YouTubeService.Scope.YoutubeReadonly,        // youtube.readonly — channels.list (resolve channel id/title)
        DriveService.ScopeConstants.DriveReadonly,   // drive.readonly
    };

    public GoogleAuthorizationCodeFlow CreateFlow(string clientId, string clientSecret)
        => new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = Scopes,
            // AccessType defaults to "offline"; force consent so a refresh_token is always returned.
            Prompt = "consent",
        });

    public UserCredential CreateUserCredential(string clientId, string clientSecret, string refreshToken)
        => new(CreateFlow(clientId, clientSecret), "user", new TokenResponse { RefreshToken = refreshToken });
}
