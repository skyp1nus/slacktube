using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Secrets;

namespace SlackTube.Api.Services.Google;

public sealed record GoogleConnection(bool Connected, string? Scopes, DateTimeOffset? ConnectedAt);

public sealed record GoogleAccountDto(
    Guid Id, string Label, string? YouTubeChannelId, string? YouTubeChannelTitle,
    string? AvatarUrl, string? AccountEmail, string Status, DateTimeOffset CreatedAt);

/// <summary>
/// Manages connected Google/YouTube accounts. Each consent INSERTS a new account (multi-account),
/// fetching the channel id+title for display. Refresh tokens are stored encrypted.
/// </summary>
public sealed class GoogleOAuthService(
    GoogleCredentialFactory factory,
    IOptions<GoogleOptions> options,
    AppDbContext db,
    ISecretProtector protector,
    YouTubeUploadService youtube)
{
    public string BuildConsentUrl(string state)
    {
        var flow = factory.CreateFlow();
        var request = flow.CreateAuthorizationCodeRequest(options.Value.RedirectUri);
        request.State = state;
        return request.Build().AbsoluteUri;
    }

    /// <summary>Exchanges the code, fetches the channel id/title, and INSERTS a new account.</summary>
    public async Task<GoogleAccount> ExchangeAndStoreAsync(string code, CancellationToken ct)
    {
        var flow = factory.CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync("user", code, options.Value.RedirectUri, ct);
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException(
                "Google returned no refresh token. Revoke prior access or re-consent (prompt=consent).");

        string? channelId = null, channelTitle = null, avatarUrl = null;
        try { (channelId, channelTitle, avatarUrl) = await youtube.GetChannelInfoAsync(token.RefreshToken, ct); }
        catch { /* channel lookup is best-effort; the account still works for upload */ }

        var now = DateTimeOffset.UtcNow;
        var account = new GoogleAccount
        {
            Label = channelTitle ?? "YouTube account",
            YouTubeChannelId = channelId,
            YouTubeChannelTitle = channelTitle,
            AvatarUrl = avatarUrl,
            EncryptedRefreshToken = protector.Protect(token.RefreshToken),
            Scopes = string.Join(' ', GoogleCredentialFactory.Scopes),
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GoogleAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<IReadOnlyList<GoogleAccountDto>> ListAccountsAsync(CancellationToken ct = default) =>
        await db.GoogleAccounts.AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => new GoogleAccountDto(
                a.Id, a.Label, a.YouTubeChannelId, a.YouTubeChannelTitle, a.AvatarUrl, a.AccountEmail, a.Status, a.CreatedAt))
            .ToListAsync(ct);

    public Task<GoogleAccount?> GetAccountAsync(Guid id, CancellationToken ct = default) =>
        db.GoogleAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>The default account (oldest) — used until per-channel mapping (Feature 3) is set.</summary>
    public Task<Guid?> GetDefaultAccountIdAsync(CancellationToken ct = default) =>
        db.GoogleAccounts.AsNoTracking().OrderBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

    public async Task<string?> GetRefreshTokenAsync(Guid accountId, CancellationToken ct = default)
    {
        var enc = await db.GoogleAccounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.EncryptedRefreshToken).FirstOrDefaultAsync(ct);
        return protector.TryUnprotect(enc);
    }

    public async Task<bool> DeleteAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await db.GoogleAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return false;
        db.GoogleAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> CountAccountsAsync(CancellationToken ct = default) => db.GoogleAccounts.CountAsync(ct);

    public async Task<GoogleConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var first = await db.GoogleAccounts.AsNoTracking().OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        return new GoogleConnection(first is not null, first?.Scopes, first?.CreatedAt);
    }
}
