using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Google;

namespace SlackTube.Api.Endpoints;

public static class GoogleOAuthEndpoints
{
    private const string StateCookie = "g_oauth_state";
    private const string ClientCookie = "g_oauth_client";

    public static void MapGoogleOAuthEndpoints(this WebApplication app)
    {
        // Admin clicks "Connect Google account" for a chosen project → here → redirect to Google consent.
        // ?clientId=<guid> selects WHICH OAuth client (Cloud project) to consent with.
        app.MapGet("/google/oauth/start", async (
            Guid? clientId, GoogleOAuthService oauth, IOptions<AppOptions> appOpt, HttpContext http, CancellationToken ct) =>
        {
            var panel = appOpt.Value.AdminPanelUrl.TrimEnd('/');
            if (clientId is null)
                return Results.Redirect($"{panel}/youtube-account?error=missing_client");

            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            string consentUrl;
            try { consentUrl = await oauth.BuildConsentUrlAsync(clientId.Value, state, ct); }
            catch (Exception ex) { return Results.Redirect($"{panel}/youtube-account?error={Uri.EscapeDataString(ex.Message)}"); }

            var cookie = new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/",
            };
            // Both the CSRF state AND the chosen client travel in HttpOnly cookies so the callback knows
            // which client to exchange the code with.
            http.Response.Cookies.Append(StateCookie, state, cookie);
            http.Response.Cookies.Append(ClientCookie, clientId.Value.ToString(), cookie);
            return Results.Redirect(consentUrl);
        });

        // Google redirects back with ?code & ?state → exchange with the stored client → insert account.
        app.MapGet("/google/oauth/callback", async (
            string? code, string? state, HttpContext http,
            GoogleOAuthService oauth, IOptions<AppOptions> appOpt, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var panel = appOpt.Value.AdminPanelUrl.TrimEnd('/');
            var expected = http.Request.Cookies[StateCookie];
            var clientCookie = http.Request.Cookies[ClientCookie];
            http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/" });
            http.Response.Cookies.Delete(ClientCookie, new CookieOptions { Path = "/" });

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expected) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected)))
                return Results.Redirect($"{panel}/youtube-account?error=invalid_state");

            if (!Guid.TryParse(clientCookie, out var clientId))
                return Results.Redirect($"{panel}/youtube-account?error=invalid_state");

            try
            {
                await oauth.ExchangeAndStoreAsync(clientId, code, ct);
                return Results.Redirect($"{panel}/youtube-account?connected=1");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Google.OAuth").LogError(ex, "Google OAuth callback failed");
                return Results.Redirect($"{panel}/youtube-account?error={Uri.EscapeDataString(ex.Message)}");
            }
        });
    }
}
