using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Infrastructure;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Slack;
using Xunit;

namespace SlackTube.Tests;

/// <summary>
/// Upload capacity is gated by the per-project <c>videos.insert</c> daily bucket (Google default
/// 100/project/day), keyed by OAuth CLIENT (Cloud project) — NOT by the separate ~10k unit pool used
/// by other endpoints. Upload rotation across a channel's projects picks the project with the most
/// remaining uploads first, skipping exhausted ones.
/// </summary>
public class QuotaRotationTests
{
    [Fact]
    public void UploadCountKeyScopedByClientAndDate()
    {
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var keyA = RedisKeys.UploadCount(clientA, "2026-06-05");

        Assert.Contains(clientA.ToString(), keyA);
        Assert.Contains("2026-06-05", keyA);
        Assert.NotEqual(keyA, RedisKeys.UploadCount(clientB, "2026-06-05")); // different client ⇒ different counter
        Assert.NotEqual(keyA, RedisKeys.UploadCount(clientA, "2026-06-06")); // different PT day ⇒ different counter
        Assert.NotEqual(keyA, RedisKeys.Quota(clientA, "2026-06-05"));       // upload bucket ≠ unit pool key
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(95, 100, 5, 100)]
    public void QuotaStatusUploadMath(int usedUploads, int uploadLimit, int expectedRemaining, int expectedTotal)
    {
        var status = new QuotaStatus(usedUploads, uploadLimit, UsedUnits: 0, CapUnits: 10000);
        Assert.Equal(expectedRemaining, status.RemainingUploads);
        Assert.Equal(expectedTotal, status.TotalUploads);
        Assert.Equal(10000, status.RemainingUnits); // unit pool is independent of the upload bucket
    }

    [Fact]
    public void UnitMeterIsIndependentOfUploads()
    {
        // Spending units (other endpoints) must NOT reduce remaining uploads, and vice versa.
        var status = new QuotaStatus(UsedUploads: 3, UploadLimit: 100, UsedUnits: 2500, CapUnits: 10000);
        Assert.Equal(97, status.RemainingUploads);
        Assert.Equal(7500, status.RemainingUnits);
    }

    [Fact]
    public async Task GetStatusNullClientReportsZeroCap()
    {
        // An unbound account (null client) must not look like it has a full daily quota available.
        var quota = new QuotaService(null!, Options.Create(new AppOptions()));
        var status = await quota.GetStatusAsync(null);
        Assert.Equal(0, status.UploadLimit);
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
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [clientLow] = 99, [clientHigh] = 0 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.NotNull(chosen);
        Assert.Equal(clientHigh, chosen!.OAuthClientId);
        Assert.Equal(1, quota.Used(clientHigh));   // one upload reserved on the chosen project only
        Assert.Equal(99, quota.Used(clientLow));   // untouched
    }

    [Fact]
    public async Task RotationSkipsExhaustedProject()
    {
        var exhausted = Guid.NewGuid();
        var open = Guid.NewGuid();
        var candidates = new[] { Creds(exhausted), Creds(open) };
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [exhausted] = 100, [open] = 50 });

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
        var quota = new FakeQuota(limit: 100,
            used: new Dictionary<Guid, int> { [a] = 100, [b] = 100 });

        var chosen = await UploadJobHandler.ReserveAcrossCandidatesAsync(candidates, quota);

        Assert.Null(chosen);
    }

    [Fact]
    public async Task StatusAggregatesPoolQuotaAcrossClients()
    {
        // A channel rotates across TWO Cloud projects ⇒ the header should show the SUMMED daily upload cap,
        // and reflect uploads already charged to a client (the bug: it read an id that's never charged).
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var quota = new FakeQuota(limit: 100,                       // 100 uploads per client ⇒ 200 total
            used: new Dictionary<Guid, int> { [clientA] = 96, [clientB] = 0 }); // A spent 96 ⇒ 4 left

        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(new[] { clientA, clientB }, quota);

        Assert.Equal(104, remaining); // 4 (A) + 100 (B)
        Assert.Equal(200, total);     // 100 + 100
    }

    [Fact]
    public async Task StatusAggregateEmptyPoolIsZero()
    {
        var quota = new FakeQuota(limit: 100);
        var (remaining, total) = await SlackStatusService.AggregateQuotaAsync(Array.Empty<Guid>(), quota);
        Assert.Equal(0, remaining);
        Assert.Equal(0, total);
    }

    private static GoogleUploadCreds Creds(Guid clientId) =>
        new(AccountId: Guid.NewGuid(), OAuthClientId: clientId, ClientId: $"cid-{clientId}", ClientSecret: "secret", RefreshToken: "rt");

    /// <summary>In-memory stand-in for the Redis-backed QuotaService (per-client daily upload-call counter).</summary>
    private sealed class FakeQuota : IQuotaService
    {
        private readonly Dictionary<Guid, int> _used; // videos.insert calls used per client today
        private readonly int _limit;

        public FakeQuota(int limit, Dictionary<Guid, int>? used = null)
        {
            _limit = limit;
            _used = used ?? new Dictionary<Guid, int>();
        }

        public int Used(Guid clientId) => _used.GetValueOrDefault(clientId);

        public Task<QuotaStatus> GetStatusAsync(Guid? oauthClientId)
        {
            var used = oauthClientId is null ? 0 : _used.GetValueOrDefault(oauthClientId.Value);
            var limit = oauthClientId is null ? 0 : _limit;
            return Task.FromResult(new QuotaStatus(used, limit, UsedUnits: 0, CapUnits: 0));
        }

        public Task<bool> TryReserveUploadAsync(Guid oauthClientId)
        {
            var used = _used.GetValueOrDefault(oauthClientId);
            if (used + 1 > _limit) return Task.FromResult(false);
            _used[oauthClientId] = used + 1;
            return Task.FromResult(true);
        }

        public Task ReleaseUploadAsync(Guid oauthClientId)
        {
            _used[oauthClientId] = Math.Max(0, _used.GetValueOrDefault(oauthClientId) - 1);
            return Task.CompletedTask;
        }

        // Non-upload unit meter is not exercised by the rotation tests — no-op.
        public Task ChargeUnitsAsync(Guid oauthClientId, int units) => Task.CompletedTask;
    }
}
