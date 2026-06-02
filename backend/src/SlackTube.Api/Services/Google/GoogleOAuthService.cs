using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Secrets;

namespace SlackTube.Api.Services.Google;

public sealed record GoogleConnection(bool Connected, string? Scopes, DateTimeOffset? ConnectedAt);

/// <summary>Drives the one-time admin consent and persists the encrypted refresh token.</summary>
public sealed class GoogleOAuthService(
    GoogleCredentialFactory factory,
    IOptions<GoogleOptions> options,
    AppDbContext db,
    ISecretProtector protector)
{
    /// <summary>Consent URL to redirect the admin's browser to. <paramref name="state"/> guards CSRF.</summary>
    public string BuildConsentUrl(string state)
    {
        var flow = factory.CreateFlow();
        var request = flow.CreateAuthorizationCodeRequest(options.Value.RedirectUri);
        request.State = state;
        return request.Build().AbsoluteUri;
    }

    /// <summary>Exchanges the callback code for tokens and stores the encrypted refresh token.</summary>
    public async Task ExchangeAndStoreAsync(string code, CancellationToken ct)
    {
        var flow = factory.CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync(
            "user", code, options.Value.RedirectUri, ct);

        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException(
                "Google returned no refresh token. Revoke prior access or re-consent (prompt=consent).");

        var entity = await db.GoogleTokens.FirstOrDefaultAsync(ct);
        if (entity is null)
        {
            entity = new GoogleToken { CreatedAt = DateTimeOffset.UtcNow };
            db.GoogleTokens.Add(entity);
        }
        entity.EncryptedRefreshToken = protector.Protect(token.RefreshToken);
        entity.Scopes = string.Join(' ', GoogleCredentialFactory.Scopes);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Decrypted refresh token, or null when Google is not connected yet.</summary>
    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
    {
        var t = await db.GoogleTokens.AsNoTracking().FirstOrDefaultAsync(ct);
        return protector.TryUnprotect(t?.EncryptedRefreshToken);
    }

    public async Task<GoogleConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var t = await db.GoogleTokens.AsNoTracking().FirstOrDefaultAsync(ct);
        var connected = protector.TryUnprotect(t?.EncryptedRefreshToken) is not null;
        return new GoogleConnection(connected, t?.Scopes, t?.UpdatedAt);
    }
}
