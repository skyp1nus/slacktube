using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Secrets;

namespace SlackTube.Api.Services.Settings;

public sealed record ResolvedSlackSettings(string? BotToken, string? SigningSecret)
{
    public bool IsConfigured => !string.IsNullOrEmpty(BotToken) && !string.IsNullOrEmpty(SigningSecret);
}

/// <summary>
/// Reads/writes the singleton <see cref="AppSettings"/> row. Slack secrets resolve
/// DB (encrypted) first, then fall back to configuration/env so the app works either way.
/// </summary>
public interface ISettingsStore
{
    Task<AppSettings> GetOrCreateAsync(CancellationToken ct = default);
    Task<ResolvedSlackSettings> GetSlackAsync(CancellationToken ct = default);
    Task SetSlackCredentialsAsync(string botToken, string signingSecret, CancellationToken ct = default);
    Task<string?> GetListeningChannelAsync(CancellationToken ct = default);
    Task SetListeningChannelAsync(string channelId, CancellationToken ct = default);
    Task<string?> GetStatusMessageTsAsync(CancellationToken ct = default);
    Task SetStatusMessageTsAsync(string? ts, CancellationToken ct = default);
}

public sealed class SettingsStore(
    AppDbContext db,
    ISecretProtector protector,
    IOptions<SlackOptions> slackCfg) : ISettingsStore
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

    public async Task<ResolvedSlackSettings> GetSlackAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == AppSettings.SingletonId, ct);
        var bot = protector.TryUnprotect(s?.SlackBotTokenEncrypted) ?? NullIfEmpty(slackCfg.Value.BotToken);
        var sign = protector.TryUnprotect(s?.SlackSigningSecretEncrypted) ?? NullIfEmpty(slackCfg.Value.SigningSecret);
        return new ResolvedSlackSettings(bot, sign);
    }

    public async Task SetSlackCredentialsAsync(string botToken, string signingSecret, CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        s.SlackBotTokenEncrypted = protector.Protect(botToken);
        s.SlackSigningSecretEncrypted = protector.Protect(signingSecret);
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
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

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
}
