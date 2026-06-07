using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Secrets;
using Xunit;

namespace SlackTube.Tests;

/// <summary>
/// The OAuth-client pool: secrets encrypted at rest, a client can't be deleted while accounts use it,
/// and every account is permanently bound to its ISSUING client's creds (a refresh token is only ever
/// paired with the client that minted it).
/// </summary>
public class GoogleClientBindingTests
{
    [Fact]
    public async Task CreateEncryptsSecretAndRoundTrips()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var svc = new GoogleOAuthClientService(db, protector);

        var dto = await svc.CreateAsync("Project A", "client-id-a", "super-secret");

        var stored = await db.GoogleOAuthClients.AsNoTracking().FirstAsync(c => c.Id == dto.Id);
        Assert.NotEqual("super-secret", stored.EncryptedClientSecret); // never stored in plaintext

        var creds = await svc.GetClientCredsAsync(dto.Id);
        Assert.NotNull(creds);
        Assert.Equal("client-id-a", creds!.ClientId);
        Assert.Equal("super-secret", creds.ClientSecret);
        Assert.True(creds.IsActive);
    }

    [Fact]
    public async Task DeleteBlockedWhileAccountReferencesIt()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var svc = new GoogleOAuthClientService(db, protector);

        var client = await svc.CreateAsync("A", "cid-a", "sec-a");
        AddAccount(db, protector, client.Id, channel: "CHAN", token: "rt");
        await db.SaveChangesAsync();

        var (ok, error) = await svc.DeleteAsync(client.Id);
        Assert.False(ok);
        Assert.Equal("client_in_use", error);

        db.GoogleAccounts.RemoveRange(db.GoogleAccounts);
        await db.SaveChangesAsync();

        var (ok2, error2) = await svc.DeleteAsync(client.Id);
        Assert.True(ok2);
        Assert.Null(error2);
    }

    [Theory]
    [InlineData("Disabled", "Disabled")]
    [InlineData("disabled", "Disabled")]
    [InlineData("Active", "Active")]
    [InlineData("nonsense", "Active")]
    [InlineData(null, "Active")]
    public void NormalizeStatusActiveOrDisabled(string? input, string expected)
        => Assert.Equal(expected, GoogleOAuthClientService.NormalizeStatus(input));

    [Fact]
    public async Task CandidatesBindEachAccountToItsOwnClient()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var clientA = await clientsSvc.CreateAsync("A", "cid-A", "secret-A");
        var clientB = await clientsSvc.CreateAsync("B", "cid-B", "secret-B");
        // SAME channel, DIFFERENT clients — the multi-project-per-channel rotation pool.
        var accA = AddAccount(db, protector, clientA.Id, channel: "CHAN", token: "token-A");
        var accB = AddAccount(db, protector, clientB.Id, channel: "CHAN", token: "token-B");
        await db.SaveChangesAsync();

        var candidates = await oauth.GetUploadCandidatesForChannelAsync(accA.Id);

        Assert.Equal(2, candidates.Count);
        var ca = candidates.Single(c => c.AccountId == accA.Id);
        var cb = candidates.Single(c => c.AccountId == accB.Id);
        // No crossing: each account's creds are its OWN client + its OWN token.
        Assert.Equal(clientA.Id, ca.OAuthClientId);
        Assert.Equal("cid-A", ca.ClientId);
        Assert.Equal("secret-A", ca.ClientSecret);
        Assert.Equal("token-A", ca.RefreshToken);
        Assert.Equal(clientB.Id, cb.OAuthClientId);
        Assert.Equal("cid-B", cb.ClientId);
        Assert.Equal("secret-B", cb.ClientSecret);
        Assert.Equal("token-B", cb.RefreshToken);
    }

    [Fact]
    public async Task CandidatesSkipDisabledClientsAndOtherChannels()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var active = await clientsSvc.CreateAsync("Active", "cid-act", "sec");
        var disabled = await clientsSvc.CreateAsync("Disabled", "cid-dis", "sec");
        await clientsSvc.UpdateAsync(disabled.Id, label: null, status: "Disabled");

        var target = AddAccount(db, protector, active.Id, channel: "CHAN", token: "t1");
        AddAccount(db, protector, disabled.Id, channel: "CHAN", token: "t2"); // disabled client → excluded
        AddAccount(db, protector, active.Id, channel: "OTHER", token: "t3");  // other channel → excluded
        await db.SaveChangesAsync();

        var candidates = await oauth.GetUploadCandidatesForChannelAsync(target.Id);

        Assert.Single(candidates);
        Assert.Equal(target.Id, candidates[0].AccountId);
    }

    [Fact]
    public async Task ClientIdsCoverActivePoolAndSkipDisabledAndOtherChannels()
    {
        // The quota-header read path must select the SAME pool as upload rotation: active clients on the
        // channel, excluding disabled clients and other channels. (Mirrors CandidatesSkip… but id-only.)
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var clientA = await clientsSvc.CreateAsync("A", "cid-A", "sec");
        var clientB = await clientsSvc.CreateAsync("B", "cid-B", "sec");
        var disabled = await clientsSvc.CreateAsync("Disabled", "cid-dis", "sec");
        await clientsSvc.UpdateAsync(disabled.Id, label: null, status: "Disabled");

        var target = AddAccount(db, protector, clientA.Id, channel: "CHAN", token: "t1");
        AddAccount(db, protector, clientB.Id, channel: "CHAN", token: "t2");       // sibling project → included
        AddAccount(db, protector, disabled.Id, channel: "CHAN", token: "t3");      // disabled client → excluded
        AddAccount(db, protector, clientA.Id, channel: "OTHER", token: "t4");      // other channel → excluded
        await db.SaveChangesAsync();

        var ids = await oauth.GetChannelOAuthClientIdsAsync(target.Id);

        Assert.Equal(new HashSet<Guid> { clientA.Id, clientB.Id }, ids.ToHashSet());
    }

    [Fact]
    public async Task ClientIdsCollapseAccountsSharingOneClient()
    {
        // Two accounts on the same channel bound to the SAME client = ONE quota counter. Distinct must
        // collapse them so AggregateQuotaAsync doesn't double-count that project's daily cap.
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var client = await clientsSvc.CreateAsync("A", "cid-A", "sec");
        var target = AddAccount(db, protector, client.Id, channel: "CHAN", token: "t1");
        AddAccount(db, protector, client.Id, channel: "CHAN", token: "t2");
        await db.SaveChangesAsync();

        var ids = await oauth.GetChannelOAuthClientIdsAsync(target.Id);

        Assert.Equal(new[] { client.Id }, ids);
    }

    [Fact]
    public async Task ClientIdsFallBackToTargetWhenNoChannel()
    {
        // No YouTubeChannelId on the target ⇒ pool is just the target account's own client (mirrors
        // GetUploadCandidatesForChannelAsync's fallback), never every channel-less account.
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var clientA = await clientsSvc.CreateAsync("A", "cid-A", "sec");
        var clientB = await clientsSvc.CreateAsync("B", "cid-B", "sec");
        var target = AddAccount(db, protector, clientA.Id, channel: "", token: "t1");
        AddAccount(db, protector, clientB.Id, channel: "", token: "t2"); // another channel-less account → excluded
        await db.SaveChangesAsync();

        var ids = await oauth.GetChannelOAuthClientIdsAsync(target.Id);

        Assert.Equal(new[] { clientA.Id }, ids);
    }

    [Fact]
    public async Task AccountCredsResolveToIssuingClient()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var clientA = await clientsSvc.CreateAsync("A", "cid-A", "secret-A");
        await clientsSvc.CreateAsync("B", "cid-B", "secret-B");
        var acc = AddAccount(db, protector, clientA.Id, channel: "CHAN", token: "token-A");
        await db.SaveChangesAsync();

        var creds = await oauth.GetAccountCredsAsync(acc.Id);

        Assert.NotNull(creds);
        Assert.Equal(clientA.Id, creds!.OAuthClientId);
        Assert.Equal("cid-A", creds.ClientId);
        Assert.Equal("secret-A", creds.ClientSecret);
        Assert.Equal("token-A", creds.RefreshToken);
    }

    [Fact]
    public async Task DuplicateClientIdDetected()
    {
        using var db = NewDb();
        var svc = new GoogleOAuthClientService(db, NewProtector());

        await svc.CreateAsync("A", "dup-cid", "sec");

        Assert.True(await svc.ClientIdExistsAsync("dup-cid"));
        Assert.True(await svc.ClientIdExistsAsync("  dup-cid  ")); // trimmed
        Assert.False(await svc.ClientIdExistsAsync("other-cid"));
    }

    [Fact]
    public async Task ConsentAndExchangeRejectDisabledClient()
    {
        using var db = NewDb();
        var protector = NewProtector();
        var clientsSvc = new GoogleOAuthClientService(db, protector);
        var oauth = NewOauth(db, protector, clientsSvc);

        var client = await clientsSvc.CreateAsync("A", "cid-A", "sec");
        await clientsSvc.UpdateAsync(client.Id, label: null, status: "Disabled");

        var consentEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => oauth.BuildConsentUrlAsync(client.Id, "state"));
        Assert.Contains("disabled", consentEx.Message, StringComparison.OrdinalIgnoreCase);

        var exchangeEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => oauth.ExchangeAndStoreAsync(client.Id, "code", CancellationToken.None));
        Assert.Contains("disabled", exchangeEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers -----------------------------------------------------------------------
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ISecretProtector NewProtector() =>
        new SecretProtector(Options.Create(new TokenEncryptionOptions { Key = "unit-test-key-0123456789abcdef" }));

    private static GoogleOAuthService NewOauth(AppDbContext db, ISecretProtector protector, GoogleOAuthClientService clients)
    {
        var factory = new GoogleCredentialFactory();
        var youtube = new YouTubeUploadService(factory);
        return new GoogleOAuthService(factory, clients, Options.Create(new GoogleOptions()), db, protector, youtube, new NoOpQuota());
    }

    /// <summary>Quota is irrelevant to client-binding behaviour — a no-op stand-in keeps the ctor happy.</summary>
    private sealed class NoOpQuota : IQuotaService
    {
        public Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId) => Task.FromResult(new QuotaStatus(0, 0, 0, 0));
        public Task<bool> TryReserveUploadAsync(Guid oauthClientId) => Task.FromResult(true);
        public Task ReleaseUploadAsync(Guid oauthClientId) => Task.CompletedTask;
        public Task ChargeUnitsAsync(Guid oauthClientId, int units) => Task.CompletedTask;
    }

    private static GoogleAccount AddAccount(AppDbContext db, ISecretProtector protector, Guid clientId, string channel, string token)
    {
        var now = DateTimeOffset.UtcNow;
        var account = new GoogleAccount
        {
            Id = Guid.NewGuid(),
            OAuthClientId = clientId,
            Label = "acc",
            YouTubeChannelId = channel,
            EncryptedRefreshToken = protector.Protect(token),
            Scopes = "scope",
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GoogleAccounts.Add(account);
        return account;
    }
}
