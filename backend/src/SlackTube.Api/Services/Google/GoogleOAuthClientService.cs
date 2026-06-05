using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Secrets;

namespace SlackTube.Api.Services.Google;

/// <summary>Read model for the admin UI — NEVER carries the client secret.</summary>
public sealed record GoogleOAuthClientDto(
    Guid Id, string Label, string ClientId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Decrypted creds for building an auth flow / credential. In-memory only — never serialized.</summary>
public sealed record GoogleClientCreds(Guid Id, string ClientId, string ClientSecret, string Status)
{
    public bool IsActive => Status == "Active";
}

/// <summary>
/// CRUD over <see cref="GoogleOAuthClient"/> (one per Google Cloud project). The client secret is
/// encrypted at rest and is write-only from the UI's perspective — it is never returned in a read.
/// </summary>
public sealed class GoogleOAuthClientService(AppDbContext db, ISecretProtector protector)
{
    public const string StatusActive = "Active";
    public const string StatusDisabled = "Disabled";

    public static string NormalizeStatus(string? status) =>
        string.Equals(status, StatusDisabled, StringComparison.OrdinalIgnoreCase) ? StatusDisabled : StatusActive;

    public async Task<IReadOnlyList<GoogleOAuthClientDto>> ListAsync(CancellationToken ct = default) =>
        await db.GoogleOAuthClients.AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .Select(c => new GoogleOAuthClientDto(c.Id, c.Label, c.ClientId, c.Status, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);

    public Task<bool> AnyActiveAsync(CancellationToken ct = default) =>
        db.GoogleOAuthClients.AnyAsync(c => c.Status == StatusActive, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.GoogleOAuthClients.CountAsync(ct);

    public Task<bool> ClientIdExistsAsync(string clientId, CancellationToken ct = default) =>
        db.GoogleOAuthClients.AnyAsync(c => c.ClientId == clientId.Trim(), ct);

    public async Task<GoogleOAuthClientDto> CreateAsync(string label, string clientId, string clientSecret, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var client = new GoogleOAuthClient
        {
            Label = string.IsNullOrWhiteSpace(label) ? clientId : label.Trim(),
            ClientId = clientId.Trim(),
            EncryptedClientSecret = protector.Protect(clientSecret),
            Status = StatusActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GoogleOAuthClients.Add(client);
        await db.SaveChangesAsync(ct);
        return new GoogleOAuthClientDto(client.Id, client.Label, client.ClientId, client.Status, client.CreatedAt, client.UpdatedAt);
    }

    /// <summary>Patch label and/or status. Returns false if the client doesn't exist.</summary>
    public async Task<bool> UpdateAsync(Guid id, string? label, string? status, CancellationToken ct = default)
    {
        var client = await db.GoogleOAuthClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return false;
        if (label is not null) client.Label = string.IsNullOrWhiteSpace(label) ? client.Label : label.Trim();
        if (status is not null) client.Status = NormalizeStatus(status);
        client.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Delete a client. FAILS (returns "client_in_use") while any account references it —
    /// the issuing-client binding is permanent, so the accounts must be disconnected first.</summary>
    public async Task<(bool ok, string? error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var client = await db.GoogleOAuthClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return (false, "not_found");
        if (await db.GoogleAccounts.AnyAsync(a => a.OAuthClientId == id, ct)) return (false, "client_in_use");
        db.GoogleOAuthClients.Remove(client);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public Task<int> CountAccountsAsync(Guid id, CancellationToken ct = default) =>
        db.GoogleAccounts.CountAsync(a => a.OAuthClientId == id, ct);

    /// <summary>Decrypted creds for the given client, or null if it doesn't exist / is undecryptable.</summary>
    public async Task<GoogleClientCreds?> GetClientCredsAsync(Guid id, CancellationToken ct = default)
    {
        var client = await db.GoogleOAuthClients.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.ClientId, c.EncryptedClientSecret, c.Status })
            .FirstOrDefaultAsync(ct);
        if (client is null) return null;
        var secret = protector.TryUnprotect(client.EncryptedClientSecret);
        return secret is null ? null : new GoogleClientCreds(client.Id, client.ClientId, secret, client.Status);
    }
}
