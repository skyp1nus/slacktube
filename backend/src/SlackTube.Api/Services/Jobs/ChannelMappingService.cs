using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Services.Jobs;

public sealed record ChannelMappingDto(
    Guid Id, Guid SlackWorkspaceId, string SlackWorkspaceName,
    string SlackChannelId, string SlackChannelName,
    Guid GoogleAccountId, string GoogleAccountLabel, DateTimeOffset CreatedAt);

/// <summary>Lightweight routing record used by the ingest/status paths.</summary>
public sealed record MappingRoute(
    Guid Id, string SlackChannelId, string SlackChannelName, Guid GoogleAccountId, Guid SlackWorkspaceId);

public sealed class ChannelMappingService(AppDbContext db)
{
    public async Task<IReadOnlyList<ChannelMappingDto>> ListAsync(CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .OrderBy(m => m.SlackChannelName)
            .Select(m => new ChannelMappingDto(
                m.Id, m.SlackWorkspaceId, m.Workspace!.TeamName,
                m.SlackChannelId, m.SlackChannelName,
                m.GoogleAccountId, m.GoogleAccount!.Label, m.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MappingRoute>> ListRoutesAsync(CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId))
            .ToListAsync(ct);

    public async Task<MappingRoute?> GetByChannelAsync(string slackChannelId, CancellationToken ct = default) =>
        await db.ChannelMappings.AsNoTracking()
            .Where(m => m.SlackChannelId == slackChannelId)
            .Select(m => new MappingRoute(m.Id, m.SlackChannelId, m.SlackChannelName, m.GoogleAccountId, m.SlackWorkspaceId))
            .FirstOrDefaultAsync(ct);

    /// <summary>Creates a mapping. Returns an error code on conflict (channel already mapped, etc.).</summary>
    public async Task<(bool ok, string? error)> CreateAsync(
        Guid workspaceId, string slackChannelId, Guid googleAccountId, CancellationToken ct = default)
    {
        var channel = await db.SlackChannels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.SlackChannelId == slackChannelId, ct);
        if (channel is null) return (false, "channel_not_found");
        if (await db.ChannelMappings.AnyAsync(m => m.SlackChannelId == slackChannelId, ct)) return (false, "already_mapped");
        if (!await db.GoogleAccounts.AnyAsync(a => a.Id == googleAccountId, ct)) return (false, "account_not_found");

        db.ChannelMappings.Add(new ChannelMapping
        {
            SlackWorkspaceId = workspaceId,
            SlackChannelId = slackChannelId,
            SlackChannelName = channel.Name,
            GoogleAccountId = googleAccountId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var m = await db.ChannelMappings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        db.ChannelMappings.Remove(m);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> IsAccountMappedAsync(Guid googleAccountId, CancellationToken ct = default) =>
        db.ChannelMappings.AnyAsync(m => m.GoogleAccountId == googleAccountId, ct);
}
