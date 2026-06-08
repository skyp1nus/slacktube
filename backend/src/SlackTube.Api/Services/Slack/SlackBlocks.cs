using System.Globalization;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Services.Slack;

// ---- view models the renderer consumes (decoupled from EF entities) -----------------
public sealed record ActiveJobView(
    string FileName, string Phase, int Percent, long BytesDone, long BytesTotal, bool Processing);

public sealed record QueuedJobView(Guid Id, string FileName);

public sealed record DoneJobView(
    string FileName, JobState State, string? YouTubeUrl, string? YouTubeVideoId, string? Error,
    DateTimeOffset? DownloadStartedAt, DateTimeOffset? UploadStartedAt, DateTimeOffset CompletedAt);

public sealed record StatusView(
    int RemainingUploads,
    int TotalUploads,
    ActiveJobView? Active,
    IReadOnlyList<QueuedJobView> Queued,
    IReadOnlyList<DoneJobView> Recent,
    int UploadedLast24h,
    string? ChannelTitle);

/// <summary>Builds Block Kit payloads (plain anonymous objects serialized to JSON).</summary>
public static class SlackBlocks
{
    private const int BarSegments = 10;

    public static (string text, object[] blocks) Status(StatusView v)
    {
        var channel = string.IsNullOrWhiteSpace(v.ChannelTitle) ? "" : $" · :tv: *{Escape(v.ChannelTitle!)}*";
        var header = $":clipboard: *Upload queue*{channel} · {v.RemainingUploads}/{v.TotalUploads} remaining today";
        if (v.UploadedLast24h > 0)
            header += $" · :white_check_mark: {v.UploadedLast24h} uploaded in last 24h";

        var blocks = new List<object>
        {
            Section(header),
        };

        if (v.Active is { } a)
        {
            blocks.Add(Divider());
            if (a.Processing)
            {
                blocks.Add(Section($":arrow_forward: *{Escape(a.FileName)}*\n   YouTube processing… :hourglass_flowing_sand:"));
            }
            else
            {
                var bar = Bar(a.Percent);
                var size = a.BytesTotal > 0 ? $" · {Mb(a.BytesDone)}/{Mb(a.BytesTotal)} MB" : "";
                blocks.Add(Section($":arrow_forward: *{Escape(a.FileName)}*\n   {Escape(a.Phase)}  {bar}  {a.Percent}%{size}"));
            }
        }

        foreach (var q in v.Queued)
        {
            blocks.Add(new
            {
                type = "section",
                text = new { type = "mrkdwn", text = $":hourglass: *{Escape(q.FileName)}* — queued" },
                accessory = new
                {
                    type = "button",
                    text = new { type = "plain_text", text = "✖ Cancel" },
                    action_id = SlackActions.CancelJob,
                    value = q.Id.ToString(),
                    style = "danger",
                },
            });
        }

        if (v.Recent.Count > 0)
        {
            blocks.Add(Divider());
            foreach (var d in v.Recent)
                blocks.Add(Context(RecentLine(d)));
        }

        var fallback = v.Active is { } act
            ? $"Upload queue — {act.FileName} {act.Percent}% ({v.RemainingUploads}/{v.TotalUploads} left)"
            : $"Upload queue — {v.Queued.Count} queued ({v.RemainingUploads}/{v.TotalUploads} left)";

        return (fallback, blocks.ToArray());
    }

    /// <summary>Yes/No confirmation when Description/Tags are missing.</summary>
    public static (string text, object[] blocks) Confirm(Guid jobId, string fileHint, string missing)
    {
        var text = $"Upload {fileHint} without {missing}?";
        var blocks = new object[]
        {
            Section($":warning: *{Escape(fileHint)}* — no *{Escape(missing)}* provided. Upload anyway?"),
            new
            {
                type = "actions",
                block_id = "confirm_" + jobId,
                elements = new object[]
                {
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "Upload anyway" },
                        action_id = SlackActions.ConfirmUpload,
                        value = jobId.ToString(),
                        style = "primary",
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "Cancel" },
                        action_id = SlackActions.DeclineUpload,
                        value = jobId.ToString(),
                        style = "danger",
                    },
                },
            },
        };
        return (text, blocks);
    }

    /// <summary>The copy-paste upload template posted (and pinned) in a channel when it is mapped, so
    /// members always have the exact format to hand. The fenced block gives Slack a one-click copy button.
    /// Posted by the bot, so the Events API skips it (bot_id) — it never triggers an upload itself.</summary>
    public static string UploadTemplateText() =>
        """
        :pushpin: *Upload template for this channel*
        Copy the block below, fill it in, and post it here to start an upload:

        ```
        🎬 UPLOAD

        Video: https://drive.google.com/file/d/<FILE_ID>/view
        Tags: tag1, tag2, tag3
        Description:
        Your text here — links and emoji are fine 🎉
        ```

        *Video* is required (a Google Drive link or file id). *Tags* and *Description* are optional; *Description* must come last.
        :frame_with_picture: *Thumbnail (optional):* attach a PNG/JPG image (≤2MB) to this same message to set it as the video's thumbnail. Long videos only — YouTube Shorts ignore custom thumbnails. No image → YouTube picks a frame.
        """;

    // ---- primitives ------------------------------------------------------------------
    private static object Section(string mrkdwn) => new
    {
        type = "section",
        text = new { type = "mrkdwn", text = mrkdwn },
    };

    private static object Context(string mrkdwn) => new
    {
        type = "context",
        elements = new object[] { new { type = "mrkdwn", text = mrkdwn } },
    };

    private static object Divider() => new { type = "divider" };

    private static string RecentLine(DoneJobView d) => d.State switch
    {
        JobState.Done when !string.IsNullOrEmpty(d.YouTubeUrl) =>
            $":white_check_mark: *{Escape(d.FileName)}* — done → <{d.YouTubeUrl}|watch>"
            + (string.IsNullOrEmpty(d.YouTubeVideoId)
                ? ""
                : $" · <https://studio.youtube.com/video/{d.YouTubeVideoId}/edit|edit in Studio>")
            + Timing(d),
        JobState.Done => $":white_check_mark: *{Escape(d.FileName)}* — done" + Timing(d),
        JobState.Cancelled => $":no_entry_sign: *{Escape(d.FileName)}* — cancelled",
        JobState.Blocked => $":lock: *{Escape(d.FileName)}* — blocked (daily quota reached)",
        _ => $":x: *{Escape(d.FileName)}* — failed{(string.IsNullOrEmpty(d.Error) ? "" : $": {Escape(d.Error!)}")}",
    };

    /// <summary>For a finished upload, append when work started and how long it took END-TO-END (Drive
    /// download → YouTube upload), split into ⬇ download and ⬆ upload when both phases are known. The clock
    /// starts at the download (falls back to the upload start for jobs predating download timing). The start
    /// renders in each viewer's own timezone via Slack's <c>&lt;!date&gt;</c> token; empty if neither is set.</summary>
    private static string Timing(DoneJobView d)
    {
        if ((d.DownloadStartedAt ?? d.UploadStartedAt) is not { } began) return "";
        var unix = began.ToUnixTimeSeconds();
        var fallback = began.UtcDateTime.ToString("MMM d, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var started = $"<!date^{unix}^{{date_short_pretty}} {{time}}|{fallback}>";
        return $" · :stopwatch: started {started}{TookSuffix(d.DownloadStartedAt, d.UploadStartedAt, d.CompletedAt)}";
    }

    /// <summary>The " · took 2m 13s (⬇ 1m 40s · ⬆ 33s)" suffix: total elapsed from the start of work (download
    /// start, or upload start for jobs predating download timing) to completion, split into download vs upload
    /// when the upload boundary sits strictly between the two ends. Empty when there is no usable interval.</summary>
    internal static string TookSuffix(DateTimeOffset? downloadStart, DateTimeOffset? uploadStart, DateTimeOffset completed)
    {
        if ((downloadStart ?? uploadStart) is not { } began || completed <= began) return "";
        if (downloadStart is { } dl && uploadStart is { } ul && ul > dl && completed > ul)
        {
            // Derive the total from the same truncated phase seconds so ⬇ + ⬆ always equals the shown total.
            var down = (int)(ul - dl).TotalSeconds;
            var up = (int)(completed - ul).TotalSeconds;
            return $" · took {Duration(down + up)} (:arrow_down: {Duration(down)} · :arrow_up: {Duration(up)})";
        }
        return $" · took {Duration((int)(completed - began).TotalSeconds)}";
    }

    /// <summary>Humanizes a duration like "2m 13s" / "1h 4m" / "45s".</summary>
    private static string Duration(int totalSeconds)
    {
        if (totalSeconds >= 3600) return $"{totalSeconds / 3600}h {totalSeconds % 3600 / 60}m";
        if (totalSeconds >= 60) return $"{totalSeconds / 60}m {totalSeconds % 60}s";
        return $"{totalSeconds}s";
    }

    private static string Bar(int percent)
    {
        var filled = Math.Clamp((int)Math.Round(percent / 100.0 * BarSegments), 0, BarSegments);
        return new string('▓', filled) + new string('░', BarSegments - filled);
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0");

    /// <summary>Escapes the Slack mrkdwn metacharacters &amp; &lt; &gt;.</summary>
    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
