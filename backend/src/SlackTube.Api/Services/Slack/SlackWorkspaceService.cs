using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Secrets;

namespace SlackTube.Api.Services.Slack;

public sealed record SlackWorkspaceDto(
    Guid Id, string SlackTeamId, string TeamName, string? BotUserId,
    bool IsActive, DateTimeOffset InstalledAt, int ChannelCount);

public sealed record SlackChannelDto(Guid Id, string SlackChannelId, string Name, bool IsPrivate, bool IsMember);

/// <summary>
/// Manages connected Slack workspaces: completes the OAuth install (token exchange + upsert by
/// team id, token encrypted via <see cref="ISecretProtector"/>), syncs channels, and resolves the
/// per-workspace bot token used everywhere we post to Slack.
/// </summary>
public sealed class SlackWorkspaceService(
    AppDbContext db,
    ISecretProtector protector,
    SlackClient slack,
    ILogger<SlackWorkspaceService> logger)
{
    /// <summary>Token exchange → upsert workspace (encrypt token) → best-effort channel sync.</summary>
    public async Task<SlackWorkspace> HandleOAuthCallbackAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var oauth = await slack.ExchangeCodeAsync(code, redirectUri, ct);

        var workspace = await db.SlackWorkspaces
            .Include(w => w.Channels)
            .FirstOrDefaultAsync(w => w.SlackTeamId == oauth.TeamId, ct);

        if (workspace is null)
        {
            workspace = new SlackWorkspace { SlackTeamId = oauth.TeamId, InstalledAt = DateTimeOffset.UtcNow };
            db.SlackWorkspaces.Add(workspace);
        }

        workspace.TeamName = oauth.TeamName;
        workspace.BotTokenEncrypted = protector.Protect(oauth.AccessToken);
        workspace.BotUserId = oauth.BotUserId;
        workspace.Scope = oauth.Scope;
        workspace.AuthedUserId = oauth.AuthedUserId;
        workspace.IsActive = true;
        await db.SaveChangesAsync(ct);

        try
        {
            await SyncChannelsAsync(workspace, oauth.AccessToken, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Don't fail the install if the immediate sync fails — the user can refresh later.
            logger.LogWarning(ex, "Channel sync failed right after connecting workspace {Team}", workspace.SlackTeamId);
        }

        return workspace;
    }

    public async Task<IReadOnlyList<SlackWorkspaceDto>> ListWorkspacesAsync(CancellationToken ct = default) =>
        await db.SlackWorkspaces.AsNoTracking()
            .OrderByDescending(w => w.InstalledAt)
            .Select(w => new SlackWorkspaceDto(
                w.Id, w.SlackTeamId, w.TeamName, w.BotUserId, w.IsActive, w.InstalledAt, w.Channels.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SlackChannelDto>> ListChannelsAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SlackChannels.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.Name)
            .Select(c => new SlackChannelDto(c.Id, c.SlackChannelId, c.Name, c.IsPrivate, c.IsMember))
            .ToListAsync(ct);

    /// <summary>All channels the bot is a member of, across workspaces (for the mapping picker).</summary>
    public async Task<IReadOnlyList<object>> ListAllMemberChannelsAsync(CancellationToken ct = default) =>
        await db.SlackChannels.AsNoTracking()
            .Where(c => c.IsMember)
            .OrderBy(c => c.Workspace!.TeamName).ThenBy(c => c.Name)
            .Select(c => (object)new
            {
                c.Id,
                c.SlackChannelId,
                c.Name,
                c.IsPrivate,
                workspaceId = c.WorkspaceId,
                workspaceName = c.Workspace!.TeamName,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SlackChannelDto>?> RefreshChannelsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var workspace = await db.SlackWorkspaces.Include(w => w.Channels)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (workspace is null) return null;

        var token = protector.TryUnprotect(workspace.BotTokenEncrypted);
        if (token is null) return null;

        await SyncChannelsAsync(workspace, token, ct);
        await db.SaveChangesAsync(ct);

        return workspace.Channels.OrderBy(c => c.Name)
            .Select(c => new SlackChannelDto(c.Id, c.SlackChannelId, c.Name, c.IsPrivate, c.IsMember))
            .ToList();
    }

    public async Task<bool> DeleteWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var workspace = await db.SlackWorkspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return false;
        db.SlackWorkspaces.Remove(workspace);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Decrypted bot token of the workspace that owns the given Slack channel id, or null.</summary>
    public async Task<string?> GetBotTokenForChannelAsync(string slackChannelId, CancellationToken ct = default)
    {
        var enc = await db.SlackChannels.AsNoTracking()
            .Where(c => c.SlackChannelId == slackChannelId)
            .Select(c => c.Workspace!.BotTokenEncrypted)
            .FirstOrDefaultAsync(ct);
        return protector.TryUnprotect(enc);
    }

    /// <summary>Decrypted bot token for a Slack team id (used by the events endpoint), or null.</summary>
    public async Task<string?> GetBotTokenForTeamAsync(string teamId, CancellationToken ct = default)
    {
        var enc = await db.SlackWorkspaces.AsNoTracking()
            .Where(w => w.SlackTeamId == teamId)
            .Select(w => w.BotTokenEncrypted)
            .FirstOrDefaultAsync(ct);
        return protector.TryUnprotect(enc);
    }

    public Task<int> CountActiveWorkspacesAsync(CancellationToken ct = default) =>
        db.SlackWorkspaces.CountAsync(w => w.IsActive, ct);

    // ---- channel reconciliation ------------------------------------------------------
    private async Task SyncChannelsAsync(SlackWorkspace workspace, string botToken, CancellationToken ct)
    {
        var live = await slack.ListChannelsAsync(botToken, ct);
        var existing = workspace.Channels.ToDictionary(c => c.SlackChannelId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        foreach (var info in live)
        {
            seen.Add(info.Id);
            if (existing.TryGetValue(info.Id, out var channel))
            {
                channel.Name = info.Name;
                channel.IsPrivate = info.IsPrivate;
                channel.IsMember = info.IsMember;
                channel.UpdatedAt = now;
            }
            else
            {
                // Add through the DbSet (not the nav) so EF treats it as INSERT, not UPDATE-0-rows.
                db.SlackChannels.Add(new SlackChannel
                {
                    WorkspaceId = workspace.Id,
                    SlackChannelId = info.Id,
                    Name = info.Name,
                    IsPrivate = info.IsPrivate,
                    IsMember = info.IsMember,
                    UpdatedAt = now,
                });
            }
        }

        foreach (var gone in workspace.Channels.Where(c => !seen.Contains(c.SlackChannelId)).ToList())
            workspace.Channels.Remove(gone);
    }
}
