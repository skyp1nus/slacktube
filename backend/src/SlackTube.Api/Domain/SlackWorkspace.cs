namespace SlackTube.Api.Domain;

/// <summary>
/// A Slack workspace connected via OAuth v2 install. The bot token is stored ENCRYPTED
/// (<see cref="Services.Secrets.ISecretProtector"/>, AES-256-GCM) — never in plaintext.
/// Keyed by Slack's team id so re-installs upsert the same row.
/// </summary>
public class SlackWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Slack team/workspace id (unique).</summary>
    public string SlackTeamId { get; set; } = default!;
    public string TeamName { get; set; } = default!;

    /// <summary>Data-Protection/AES-encrypted bot token (xoxb-…).</summary>
    public string BotTokenEncrypted { get; set; } = default!;

    public string? BotUserId { get; set; }
    public string? Scope { get; set; }
    public string? AuthedUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SlackChannel> Channels { get; set; } = new List<SlackChannel>();
}
