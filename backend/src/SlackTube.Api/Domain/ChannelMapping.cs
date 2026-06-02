namespace SlackTube.Api.Domain;

/// <summary>
/// Routes a Slack channel to a Google account. One Slack channel → exactly one account
/// (<see cref="SlackChannelId"/> is unique); an account may receive from many channels.
/// </summary>
public class ChannelMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SlackWorkspaceId { get; set; }
    public string SlackChannelId { get; set; } = default!;
    public string SlackChannelName { get; set; } = default!;

    public Guid GoogleAccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public SlackWorkspace? Workspace { get; set; }
    public GoogleAccount? GoogleAccount { get; set; }
}
