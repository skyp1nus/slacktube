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
    string? AvatarUrl, string? AccountEmail, string Status, DateTimeOffset CreatedAt,
    Guid? OAuthClientId, string? OAuthClientLabel);

/// <summary>
/// Decrypted creds + token for ONE account, ready to build a Drive/YouTube service. The client id +
/// secret are the account's ISSUING client (never a different one). In-memory only — never serialized.
/// </summary>
public sealed record GoogleUploadCreds(
    Guid AccountId, Guid OAuthClientId, string ClientId, string ClientSecret, string RefreshToken);

/// <summary>
/// Manages connected Google/YouTube accounts. Each consent INSERTS a new account bound to the OAuth
/// client (Cloud project) the admin chose, fetching the channel id+title for display. Refresh tokens
/// are stored encrypted and can only ever be refreshed by their issuing client.
/// </summary>
public sealed class GoogleOAuthService(
    GoogleCredentialFactory factory,
    GoogleOAuthClientService clients,
    IOptions<GoogleOptions> options,
    AppDbContext db,
    ISecretProtector protector,
    YouTubeUploadService youtube)
{
    /// <summary>Builds the consent URL for a specific OAuth client. The redirect URI stays the global
    /// one (every client must register the same <c>/google/oauth/callback</c>).</summary>
    public async Task<string> BuildConsentUrlAsync(Guid oauthClientId, string state, CancellationToken ct = default)
    {
        var creds = await clients.GetClientCredsAsync(oauthClientId, ct)
            ?? throw new InvalidOperationException("Selected OAuth client was not found.");
        if (!creds.IsActive) throw new InvalidOperationException("Selected OAuth client is disabled.");

        var flow = factory.CreateFlow(creds.ClientId, creds.ClientSecret);
        var request = flow.CreateAuthorizationCodeRequest(options.Value.RedirectUri);
        request.State = state;
        return request.Build().AbsoluteUri;
    }

    /// <summary>Exchanges the code with the chosen client, fetches the channel id/title, and INSERTS a
    /// new account bound to that client (<see cref="GoogleAccount.OAuthClientId"/>).</summary>
    public async Task<GoogleAccount> ExchangeAndStoreAsync(Guid oauthClientId, string code, CancellationToken ct)
    {
        var creds = await clients.GetClientCredsAsync(oauthClientId, ct)
            ?? throw new InvalidOperationException("Selected OAuth client was not found.");
        if (!creds.IsActive) throw new InvalidOperationException("Selected OAuth client is disabled.");

        var flow = factory.CreateFlow(creds.ClientId, creds.ClientSecret);
        var token = await flow.ExchangeCodeForTokenAsync("user", code, options.Value.RedirectUri, ct);
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException(
                "Google returned no refresh token. Revoke prior access or re-consent (prompt=consent).");

        string? channelId = null, channelTitle = null, avatarUrl = null;
        try { (channelId, channelTitle, avatarUrl) = await youtube.GetChannelInfoAsync(creds.ClientId, creds.ClientSecret, token.RefreshToken, ct); }
        catch { /* channel lookup is best-effort; the account still works for upload */ }

        var now = DateTimeOffset.UtcNow;
        var account = new GoogleAccount
        {
            Label = channelTitle ?? "YouTube account",
            OAuthClientId = oauthClientId,
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
                a.Id, a.Label, a.YouTubeChannelId, a.YouTubeChannelTitle, a.AvatarUrl, a.AccountEmail, a.Status, a.CreatedAt,
                a.OAuthClientId, a.OAuthClient != null ? a.OAuthClient.Label : null))
            .ToListAsync(ct);

    public Task<GoogleAccount?> GetAccountAsync(Guid id, CancellationToken ct = default) =>
        db.GoogleAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>The default account (oldest) — used when a job carries no explicit account.</summary>
    public Task<Guid?> GetDefaultAccountIdAsync(CancellationToken ct = default) =>
        db.GoogleAccounts.AsNoTracking().OrderBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

    /// <summary>Decrypted creds (issuing-client id+secret + refresh token) for ONE account. Null if the
    /// account is missing, has no (active) issuing client, or anything is undecryptable.</summary>
    public async Task<GoogleUploadCreds?> GetAccountCredsAsync(Guid accountId, CancellationToken ct = default)
    {
        var row = await db.GoogleAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Join(db.GoogleOAuthClients.AsNoTracking(),
                  a => a.OAuthClientId, c => c.Id,
                  (a, c) => new { a.Id, a.EncryptedRefreshToken, ClientPk = c.Id, c.ClientId, c.EncryptedClientSecret })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var secret = protector.TryUnprotect(row.EncryptedClientSecret);
        var token = protector.TryUnprotect(row.EncryptedRefreshToken);
        if (secret is null || token is null) return null;
        return new GoogleUploadCreds(row.Id, row.ClientPk, row.ClientId, secret, token);
    }

    /// <summary>
    /// Every account that can upload to the SAME YouTube channel as <paramref name="targetAccountId"/>:
    /// all Active accounts sharing its channel id, each bound to an Active client. This is the rotation
    /// pool — N clients backing one channel ⇒ N independent daily quotas. Falls back to just the target
    /// account when the target has no channel id. Undecryptable rows are skipped.
    /// </summary>
    public async Task<IReadOnlyList<GoogleUploadCreds>> GetUploadCandidatesForChannelAsync(
        Guid targetAccountId, CancellationToken ct = default)
    {
        var target = await db.GoogleAccounts.AsNoTracking()
            .Where(a => a.Id == targetAccountId)
            .Select(a => new { a.Id, a.YouTubeChannelId })
            .FirstOrDefaultAsync(ct);
        if (target is null) return Array.Empty<GoogleUploadCreds>();

        var accounts = db.GoogleAccounts.AsNoTracking().Where(a => a.Status == "Active");
        accounts = string.IsNullOrEmpty(target.YouTubeChannelId)
            ? accounts.Where(a => a.Id == targetAccountId)
            : accounts.Where(a => a.YouTubeChannelId == target.YouTubeChannelId);

        var rows = await accounts
            .Join(db.GoogleOAuthClients.AsNoTracking().Where(c => c.Status == GoogleOAuthClientService.StatusActive),
                  a => a.OAuthClientId, c => c.Id,
                  (a, c) => new { a.Id, a.CreatedAt, a.EncryptedRefreshToken, ClientPk = c.Id, c.ClientId, c.EncryptedClientSecret })
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var result = new List<GoogleUploadCreds>(rows.Count);
        foreach (var r in rows)
        {
            var secret = protector.TryUnprotect(r.EncryptedClientSecret);
            var token = protector.TryUnprotect(r.EncryptedRefreshToken);
            if (secret is null || token is null) continue;
            result.Add(new GoogleUploadCreds(r.Id, r.ClientPk, r.ClientId, secret, token));
        }
        return result;
    }

    /// <summary>Flags an account as broken (revoked / wrong client) so rotation skips it; surfaced in the UI.</summary>
    public async Task MarkAccountErrorAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await db.GoogleAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null || account.Status == "Error") return;
        account.Status = "Error";
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
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
