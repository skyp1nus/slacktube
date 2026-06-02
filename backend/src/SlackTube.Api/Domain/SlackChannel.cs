namespace SlackTube.Api.Domain;

/// <summary>A channel synced from a connected <see cref="SlackWorkspace"/> via conversations.list.</summary>
public class SlackChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    public string SlackChannelId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsPrivate { get; set; }
    /// <summary>Whether the bot is a member — it must be invited before it can read/post.</summary>
    public bool IsMember { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SlackWorkspace? Workspace { get; set; }
}
