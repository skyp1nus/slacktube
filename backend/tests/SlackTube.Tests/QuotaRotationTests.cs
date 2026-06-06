using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Infrastructure;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

/// <summary>
/// Quota is keyed by OAuth CLIENT (Cloud project), and upload rotation across a channel's projects
/// picks the project with the most remaining quota first, skipping exhausted ones.
/// </summary>
public class QuotaRotationTests
{
    [Fact]
    public void QuotaKeyScopedByClientAndDate()
    {
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var keyA = RedisKeys.Quota(clientA, "2026-06-05");
        var keyB = RedisKeys.Quota(clientB, "2026-06-05");

        Assert.Contains(clientA.ToString(), keyA);
        Assert.Contains("2026-06-05", keyA);
        Assert.NotEqual(keyA, keyB);                                   // different client ⇒ different counter
        Assert.NotEqual(keyA, RedisKeys.Quota(clientA, "2026-06-06")); // different PT day ⇒ different counter
    }

    [Theory]
    [InlineData(0, 10000, 1600, 6, 6)]
    [InlineData(9600, 10000, 1600, 0, 6)]
    [InlineData(8000, 10000, 1600, 1, 6)]
    public void QuotaStatusMath(int used, int cap, int cost, int expectedRemainingUploads, int expectedTotalUploads)
    {
        var status = new QuotaStatus(used, cap, cost);
        Assert.Equal(Math.Max(0, cap - used), status.RemainingUnits);
        Assert.Equal(expectedRemainingUploads, status.RemainingUploads);
        Assert.Equal(expectedTotalUploads, status.TotalUploads);
    }

    [Fact]
    public async Task GetStatusNullClientReportsZeroCap()
    {
        // An unbound account (null client) must not look like it has a full daily quota available.
        var quota = new QuotaService(null!, Options.Create(new AppOptions()));
        var status = await quota.GetStatusAsync(null);
        Assert.Equal(0, status.CapUnits);
        Assert.Equal(0, status.RemainingUploads);
        Assert.Equal(0, status.TotalUploads);
    }

    [Fact]
    public async Task RotationPicksMostRemainingFirst()
    {
        var clientLow = Guid.NewGuid();   // less headroom
        var clientHigh = Guid.NewGuid();  // more headroom
        // clientLow appears FIRST in the list but has used more — selection must still prefer clientHigh.
        var candidates = new[]
        {
            Creds(clientLow),
            Creds(clientHigh),
        };
        var quota = new FakeQuota(cap: 10000, cost: 1600,
            used: new Dictionary<Guid, int> { [clientLow] = 8000, [clientHigh] = 0 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.NotNull(chosen);
        Assert.Equal(clientHigh, chosen!.OAuthClientId);
        Assert.Equal(1600, quota.Used(clientHigh));  // reserved on the chosen project only
        Assert.Equal(8000, quota.Used(clientLow));   // untouched
    }

    [Fact]
    public async Task RotationSkipsExhaustedProject()
    {
        var exhausted = Guid.NewGuid();
        var open = Guid.NewGuid();
        var candidates = new[] { Creds(exhausted), Creds(open) };
        var quota = new FakeQuota(cap: 10000, cost: 1600,
            used: new Dictionary<Guid, int> { [exhausted] = 9600, [open] = 1600 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.NotNull(chosen);
        Assert.Equal(open, chosen!.OAuthClientId);
    }

    [Fact]
    public async Task RotationNullWhenAllExhausted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var candidates = new[] { Creds(a), Creds(b) };
        var quota = new FakeQuota(cap: 10000, cost: 1600,
            used: new Dictionary<Guid, int> { [a] = 9600, [b] = 10000 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.Null(chosen);
    }

    [Fact]
    public async Task StatusAggregatesPoolQuotaAcrossClients()
    {
        // A channel rotates across TWO Cloud projects ⇒ the header should show the SUMMED daily cap, and
        // it must reflect units already charged to a client (the bug: it read an id that's never charged).
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var quota = new FakeQuota(cap: 10000, cost: 1600,           // 6 uploads per client ⇒ 12 total
            used: new Dictionary<Guid, int> { [clientA] = 6400, [clientB] = 0 }); // A spent 4 ⇒ 2 left

        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(new[] { clientA, clientB }, quota);

        Assert.Equal(8, remaining); // 2 (A) + 6 (B)
        Assert.Equal(12, total);    // 6 + 6
    }

    [Fact]
    public async Task StatusAggregateEmptyPoolIsZero()
    {
        var quota = new FakeQuota(cap: 10000, cost: 1600);
        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(Array.Empty<Guid>(), quota);
        Assert.Equal(0, remaining);
        Assert.Equal(0, total);
    }

    private static GoogleUploadCreds Creds(Guid clientId) =>
        new(AccountId: Guid.NewGuid(), OAuthClientId: clientId, ClientId: $"cid-{clientId}", ClientSecret: "secret", RefreshToken: "rt");

    /// <summary>In-memory stand-in for the Redis-backed QuotaService (per-client daily counter).</summary>
    private sealed class FakeQuota : IQuotaService
    {
        private readonly Dictionary<Guid, int> _used;
        private readonly int _cap;
        private readonly int _cost;

        public FakeQuota(int cap, int cost, Dictionary<Guid, int>? used = null)
        {
            _cap = cap;
            _cost = cost;
            _used = used ?? new Dictionary<Guid, int>();
        }

        public int Used(Guid clientId) => _used.GetValueOrDefault(clientId);

        public Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId)
        {
            var used = oauthClientId is null ? 0 : _used.GetValueOrDefault(oauthClientId.Value);
            return Task.FromResult(new QuotaStatus(used, _cap, _cost));
        }

        public Task<bool> TryReserveUploadAsync(Guid oauthClientId)
        {
            var used = _used.GetValueOrDefault(oauthClientId);
            if (used + _cost > _cap) return Task.FromResult(false);
            _used[oauthClientId] = used + _cost;
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(Guid oauthClientId, int units)
        {
            _used[oauthClientId] = Math.Max(0, _used.GetValueOrDefault(oauthClientId) - units);
            return Task.CompletedTask;
        }
    }
}
