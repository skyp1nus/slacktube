using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Services.Settings;

/// <summary>
/// Reads/writes the singleton <see cref="AppSettings"/> row (listening channel + current status
/// message ts). Slack credentials moved to per-workspace OAuth (see SlackWorkspaceService); the
/// signing secret is env-only.
/// </summary>
public interface ISettingsStore
{
    Task<AppSettings> GetOrCreateAsync(CancellationToken ct = default);
    Task<string?> GetListeningChannelAsync(CancellationToken ct = default);
    Task SetListeningChannelAsync(string channelId, CancellationToken ct = default);
    Task<string?> GetStatusMessageTsAsync(CancellationToken ct = default);
    Task SetStatusMessageTsAsync(string? ts, CancellationToken ct = default);
}

public sealed class SettingsStore(AppDbContext db) : ISettingsStore
{
    public async Task<AppSettings> GetOrCreateAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Id == AppSettings.SingletonId, ct);
        if (s is null)
        {
            s = new AppSettings { Id = AppSettings.SingletonId, UpdatedAt = DateTimeOffset.UtcNow };
            db.Settings.Add(s);
            await db.SaveChangesAsync(ct);
        }
        return s;
    }

    public async Task<string?> GetListeningChannelAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == AppSettings.SingletonId, ct);
        return s?.ListeningChannelId;
    }

    public async Task SetListeningChannelAsync(string channelId, CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        s.ListeningChannelId = channelId;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetStatusMessageTsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == AppSettings.SingletonId, ct);
        return s?.StatusMessageTs;
    }

    public async Task SetStatusMessageTsAsync(string? ts, CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        s.StatusMessageTs = ts;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
