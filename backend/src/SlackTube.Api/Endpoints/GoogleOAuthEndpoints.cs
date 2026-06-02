using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Google;

namespace SlackTube.Api.Endpoints;

public static class GoogleOAuthEndpoints
{
    private const string StateCookie = "g_oauth_state";

    public static void MapGoogleOAuthEndpoints(this WebApplication app)
    {
        // Admin clicks "Connect Google" → browser lands here → redirect to Google consent.
        app.MapGet("/google/oauth/start", (GoogleOAuthService oauth, HttpResponse res) =>
        {
            var state = Guid.NewGuid().ToString("N");
            res.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = false, // localhost; set true behind HTTPS
                MaxAge = TimeSpan.FromMinutes(10),
            });
            return Results.Redirect(oauth.BuildConsentUrl(state));
        });

        // Google redirects back here with ?code & ?state.
        app.MapGet("/google/oauth/callback", async (
            string? code, string? state, HttpRequest req,
            GoogleOAuthService oauth, CancellationToken ct) =>
        {
            var cookieState = req.Cookies[StateCookie];
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || state != cookieState)
                return Results.BadRequest("Invalid OAuth state — please retry from the admin panel.");

            try
            {
                await oauth.ExchangeAndStoreAsync(code, ct);
            }
            catch (Exception ex)
            {
                return Results.Content(Page("❌ Google connection failed", ex.Message), "text/html");
            }

            return Results.Content(
                Page("✅ Google connected", "The refresh token was stored (encrypted). You can close this tab and return to the admin panel."),
                "text/html");
        });
    }

    private static string Page(string title, string body) =>
        $"<!doctype html><html><head><meta charset=\"utf-8\"><title>SlackTube</title>" +
        "<style>body{font-family:system-ui;margin:4rem auto;max-width:32rem;text-align:center;color:#222}</style></head>" +
        $"<body><h2>{title}</h2><p>{System.Net.WebUtility.HtmlEncode(body)}</p></body></html>";
}
