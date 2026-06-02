using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Google;

namespace SlackTube.Api.Endpoints;

public static class GoogleOAuthEndpoints
{
    private const string StateCookie = "g_oauth_state";

    public static void MapGoogleOAuthEndpoints(this WebApplication app)
    {
        // Admin clicks "Connect Google account" → here → redirect to Google consent.
        app.MapGet("/google/oauth/start", (GoogleOAuthService oauth, HttpContext http) =>
        {
            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            http.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/",
            });
            return Results.Redirect(oauth.BuildConsentUrl(state));
        });

        // Google redirects back with ?code & ?state → insert account → bounce to the panel.
        app.MapGet("/google/oauth/callback", async (
            string? code, string? state, HttpContext http,
            GoogleOAuthService oauth, IOptions<AppOptions> appOpt, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var panel = appOpt.Value.AdminPanelUrl.TrimEnd('/');
            var expected = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/" });

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expected) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected)))
                return Results.Redirect($"{panel}/accounts?error=invalid_state");

            try
            {
                await oauth.ExchangeAndStoreAsync(code, ct);
                return Results.Redirect($"{panel}/accounts?connected=1");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Google.OAuth").LogError(ex, "Google OAuth callback failed");
                return Results.Redirect($"{panel}/accounts?error={Uri.EscapeDataString(ex.Message)}");
            }
        });
    }
}
