namespace SlackTube.Api.Domain;

/// <summary>
/// One Slack template message = one upload job. State lives here so a mid-job restart
/// is recoverable (Hangfire re-enqueues; the worker reconciles from this record).
/// </summary>
public class UploadJob
{
    public Guid Id { get; set; }

    // ---- Slack source ----------------------------------------------------------------
    /// <summary>Slack <c>event_id</c> of the message that created this job (dedup correlation).</summary>
    public string SlackEventId { get; set; } = default!;
    public string SlackChannelId { get; set; } = default!;
    public string SlackUserId { get; set; } = default!;
    /// <summary><c>ts</c> of the original template message.</summary>
    public string SlackMessageTs { get; set; } = default!;

    // ---- Parsed payload --------------------------------------------------------------
    public string DriveFileId { get; set; } = default!;
    /// <summary>Target Google account, resolved from the channel mapping (null → default account).</summary>
    public Guid? GoogleAccountId { get; set; }
    /// <summary>YouTube title — derived from the downloaded file name (set after download).</summary>
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>Stored as jsonb. Already trimmed/deduped/limit-checked by the parser.</summary>
    public List<string> Tags { get; set; } = new();

    // ---- State -----------------------------------------------------------------------
    public JobState State { get; set; } = JobState.Queued;
    public string? ErrorMessage { get; set; }

    /// <summary>Set when Description/Tags were missing and we asked the user to confirm.</summary>
    public bool RequiresConfirmation { get; set; }
    /// <summary>null = awaiting confirm, true = user confirmed, false = user declined.</summary>
    public bool? Confirmed { get; set; }

    // ---- Progress (coarse, persisted; live ticks kept in memory) ---------------------
    public long BytesTotal { get; set; }
    public long BytesTransferred { get; set; }
    public string? OriginalFileName { get; set; }

    /// <summary>When the Drive download began (transition into <see cref="JobState.Downloading"/>) — the start
    /// of the active download+upload clock. Null until then; set once (a crash-resume keeps the first value).</summary>
    public DateTimeOffset? DownloadStartedAt { get; set; }

    /// <summary>When the YouTube upload actually started (transition into <see cref="JobState.Uploading"/>).
    /// Null until then. Used to split the reported duration into download vs upload phases in Slack.</summary>
    public DateTimeOffset? UploadStartedAt { get; set; }

    // ---- YouTube result --------------------------------------------------------------
    public string? YouTubeVideoId { get; set; }
    public string? YouTubeUrl { get; set; }
    /// <summary>videos.insert calls charged against the project's daily upload bucket for this job (1 per
    /// upload). Column name kept for migration stability; the unit-cost model it once held is retired.</summary>
    public int QuotaUnitsCharged { get; set; }

    // ---- Infra -----------------------------------------------------------------------
    public string? HangfireJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<JobStateHistory> History { get; set; } = new();

    /// <summary>Cancellable only before the YouTube upload starts.</summary>
    public bool IsCancellable => State is JobState.Queued or JobState.Downloading;
    public bool IsTerminal => State is JobState.Done or JobState.Cancelled or JobState.Failed;
}
