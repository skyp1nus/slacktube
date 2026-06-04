using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;

namespace SlackTube.Api.Services.Google;

/// <summary>
/// Builds the OAuth pieces. One consent covers both scopes. A <see cref="UserCredential"/>
/// rebuilt from just the stored refresh token + client secrets auto-refreshes its access
/// token on every API call (no browser, suitable for the background worker).
/// </summary>
public sealed class GoogleCredentialFactory(IOptions<GoogleOptions> options)
{
    public static readonly string[] Scopes =
    {
        YouTubeService.Scope.YoutubeUpload,          // youtube.upload  — videos.insert
        YouTubeService.Scope.YoutubeReadonly,        // youtube.readonly — channels.list (resolve channel id/title)
        DriveService.ScopeConstants.DriveReadonly,   // drive.readonly
    };

    public GoogleAuthorizationCodeFlow CreateFlow()
    {
        var o = options.Value;
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = o.ClientId, ClientSecret = o.ClientSecret },
            Scopes = Scopes,
            // AccessType defaults to "offline"; force consent so a refresh_token is always returned.
            Prompt = "consent",
        });
    }

    public UserCredential CreateUserCredential(string refreshToken)
        => new(CreateFlow(), "user", new TokenResponse { RefreshToken = refreshToken });
}
