namespace SlackTube.Api.Domain;

/// <summary>
/// Singleton settings row (Id == 1) editable from the admin panel. Slack secrets are
/// stored ENCRYPTED; they may also be supplied via configuration/env (config wins only
/// when the DB value is empty — see SettingsStore).
/// </summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string? SlackBotTokenEncrypted { get; set; }
    public string? SlackSigningSecretEncrypted { get; set; }

    /// <summary>The one Slack channel the bot listens to AND posts the status message in.</summary>
    public string? ListeningChannelId { get; set; }

    /// <summary><c>ts</c> of the current live status message (deleted+reposted on queue change).</summary>
    public string? StatusMessageTs { get; set; }

    // ---- upload defaults (editable from the Settings tab) ----------------------------
    /// <summary>YouTube privacy for new uploads: private | unlisted | public.</summary>
    public string DefaultVisibility { get; set; } = "private";

    /// <summary>Drive download + YouTube upload chunk size (MB). Seeded from App config on first run.</summary>
    public int TransferChunkSizeMb { get; set; } = 64;

    /// <summary>YouTube "made for kids" self-declaration (status.selfDeclaredMadeForKids).
    /// Default false = "No, it's not made for kids".</summary>
    public bool MadeForKids { get; set; }

    /// <summary>YouTube altered/synthetic (AI) content disclosure (status.containsSyntheticMedia).
    /// Default false = "No".</summary>
    public bool ContainsSyntheticMedia { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
