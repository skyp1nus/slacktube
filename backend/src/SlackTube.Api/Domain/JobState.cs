namespace SlackTube.Api.Domain;

/// <summary>
/// Lifecycle of an upload job. Persisted in Postgres as text (see DbContext config).
///
/// Allowed transitions:
///   Queued      → Downloading | Cancelled | Failed | Blocked
///   Downloading → Uploading   | Cancelled | Failed
///   Uploading   → Processing  | Failed                 (point of no return — no cancel)
///   Processing  → Done        | Failed
///
/// Cancellation is permitted ONLY while Queued or Downloading. Once the YouTube upload
/// begins the bot must never delete/modify the video (explicit product rule).
/// </summary>
public enum JobState
{
    /// <summary>Accepted, waiting for a worker.</summary>
    Queued,

    /// <summary>Streaming the source file from Google Drive to a temp path.</summary>
    Downloading,

    /// <summary>Resumable upload to YouTube in progress (point of no return).</summary>
    Uploading,

    /// <summary>All bytes sent; YouTube is still transcoding ("YouTube processing…").</summary>
    Processing,

    /// <summary>Video is live (private) on YouTube.</summary>
    Done,

    /// <summary>Cancelled by a user while still cancellable.</summary>
    Cancelled,

    /// <summary>Terminal error (download/upload/quota/etc.).</summary>
    Failed,

    /// <summary>Held back because today's YouTube quota is exhausted.</summary>
    Blocked,
}
