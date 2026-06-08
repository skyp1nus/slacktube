using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.YouTube.v3;
using Hangfire;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Services.Jobs;

/// <summary>
/// Stage 2 worker: Drive download → quota guard → YouTube upload (private), with live progress.
///
/// Cancellation is checked atomically before taking the job, before the upload, and periodically
/// during download. Once the YouTube upload begins it is the POINT OF NO RETURN — the bot never
/// deletes/modifies the video. AutomaticRetry is disabled (Attempts=0) to guarantee we never
/// re-upload a video; a job interrupted after upload started is failed with a "verify in YouTube
/// Studio" note rather than retried. The temp file is always cleaned up.
/// </summary>
public sealed class UploadJobHandler(
    IJobService jobs,
    GoogleOAuthService oauth,
    DriveDownloadService drive,
    YouTubeUploadService youtube,
    IQuotaService quota,
    ICancellationFlags cancelFlags,
    IProgressTracker progress,
    ISlackStatusService status,
    SlackClient slack,
    SlackWorkspaceService workspaces,
    ISettingsStore settings,
    IOptions<AppOptions> appOptions,
    ILogger<UploadJobHandler> logger)
{
    private const string PhaseDownload = "Downloading from Drive";
    private const string PhaseUpload = "Uploading to YouTube";
    private const string PhaseProcessing = "YouTube processing";
    private const long CancelCheckBytes = 4_000_000;
    private const long MaxThumbnailBytes = 2L * 1024 * 1024; // YouTube's hard limit for thumbnails.set

    // Attempts=0: never auto-retry on failure (a failed upload must not silently re-upload).
    // DisableConcurrentExecution: a per-job lock so a restart-recovery re-enqueue can never run
    // the same job twice in parallel (the re-entry guards below then make the second run a no-op).
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) { logger.LogWarning("Upload job {Id} not found", jobId); return; }
        if (job.IsTerminal) return;

        // Idempotency / point-of-no-return guards on re-entry (e.g. requeued after a crash).
        if (job.YouTubeVideoId is not null)
        {
            await jobs.TransitionAsync(job, JobState.Done, "already uploaded (idempotent)", ct);
            return;
        }
        if (job.State is JobState.Uploading or JobState.Processing)
        {
            await FailAsync(job, "A previous attempt already started the YouTube upload — verify in YouTube Studio; the bot won’t re-upload.");
            return;
        }

        var tempDir = appOptions.Value.TempDownloadDir;
        var tempPath = Path.Combine(tempDir, $"{job.Id}.tmp");

        // Rotation/quota bookkeeping, visible to the catch blocks for release + error flagging.
        Guid? reservedClientId = null;
        Guid? activeAccountId = null;

        try
        {
            if (await cancelFlags.IsRequestedAsync(job.Id)) { await MarkCancelledAsync(job, tempPath); return; }

            var targetAccountId = job.GoogleAccountId ?? await oauth.GetDefaultAccountIdAsync(ct);
            if (targetAccountId is null) { await FailAsync(job, "No Google account is connected."); return; }

            // Rotation pool: every Active account that can upload to this channel, each bound to an
            // Active OAuth client (= its own daily quota). One channel × N clients ⇒ N× the daily cap.
            var candidates = await oauth.GetUploadCandidatesForChannelAsync(targetAccountId.Value, ct);
            if (candidates.Count == 0)
            {
                await FailAsync(job, "No usable YouTube project for this channel — connect or enable one in the admin panel.");
                return;
            }

            // Download (Drive) uses the target account's creds — any candidate shares the channel's Drive
            // access, and Drive has its own quota separate from the per-project YouTube cap.
            var downloadCreds = candidates.FirstOrDefault(c => c.AccountId == targetAccountId.Value) ?? candidates[0];
            activeAccountId = downloadCreds.AccountId;

            Directory.CreateDirectory(tempDir);
            var uploadSettings = await settings.GetUploadSettingsAsync(ct);
            var chunkBytes = Math.Max(1, uploadSettings.ChunkSizeMb) * 1024 * 1024;

            // ============================ DOWNLOAD ============================
            // Start the active (download+upload) clock; ??= so a crash-resume keeps the original start.
            job.DownloadStartedAt ??= DateTimeOffset.UtcNow;
            await jobs.TransitionAsync(job, JobState.Downloading, "download started", ct);
            await status.RefreshQueueAsync(ct);

            var driveService = drive.BuildService(downloadCreds.ClientId, downloadCreds.ClientSecret, downloadCreds.RefreshToken);
            var info = await drive.GetInfoAsync(driveService, job.DriveFileId, downloadCreds.OAuthClientId, ct);
            if (info.IsGoogleNative) { await FailAsync(job, "Drive file is a Google-native doc, not a video."); return; }

            job.OriginalFileName ??= info.Name;
            job.Title ??= Path.GetFileNameWithoutExtension(info.Name);
            job.BytesTotal = info.Size ?? 0;
            await jobs.SaveAsync(job, ct);
            progress.Set(job.Id, new JobProgress(JobState.Downloading, 0, job.BytesTotal, PhaseDownload));

            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var cancelledDuringDownload = false;
            long lastCancelCheck = 0;

            await using (var fileOut = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                try
                {
                    await drive.DownloadAsync(driveService, job.DriveFileId, fileOut, bytes =>
                    {
                        progress.Set(job.Id, new JobProgress(JobState.Downloading, bytes, job.BytesTotal, PhaseDownload));
                        _ = status.UpdateProgressAsync(job.Id);

                        if (bytes - lastCancelCheck >= CancelCheckBytes)
                        {
                            lastCancelCheck = bytes;
                            if (cancelFlags.IsRequestedAsync(job.Id).GetAwaiter().GetResult())
                            {
                                cancelledDuringDownload = true;
                                downloadCts.Cancel();
                            }
                        }
                    }, chunkBytes, downloadCreds.OAuthClientId, downloadCts.Token);
                }
                catch (OperationCanceledException) when (cancelledDuringDownload)
                {
                    // user cancel — handled right below
                }
            }

            if (cancelledDuringDownload) { await MarkCancelledAsync(job, tempPath); return; }

            // Last chance to cancel before the point of no return.
            if (await cancelFlags.IsRequestedAsync(job.Id)) { await MarkCancelledAsync(job, tempPath); return; }

            // ======================= QUOTA + ROTATION =======================
            // Try the channel's projects most-remaining-first; reserve on the first whose daily cap
            // still fits. When the mapped project (A) is exhausted, the next sibling (B) is used.
            var chosen = await ReserveAcrossCandidatesAsync(candidates, quota);
            if (chosen is null)
            {
                SafeDelete(tempPath);
                progress.Remove(job.Id);
                await jobs.TransitionAsync(job, JobState.Blocked, "all projects for this channel hit today's quota", ct);
                await NotifyAsync(job, ":lock: All YouTube projects for this channel hit today's quota — blocked until after midnight PT.");
                await status.RefreshQueueAsync(ct);
                return;
            }
            reservedClientId = chosen.OAuthClientId;
            activeAccountId = chosen.AccountId;

            // ==================== UPLOAD (point of no return) ====================
            await jobs.TransitionAsync(job, JobState.Uploading, "upload started", ct);
            job.GoogleAccountId = chosen.AccountId; // record which project/account actually uploaded
            job.QuotaUnitsCharged = 1;              // one videos.insert call charged to the project's daily upload bucket
            job.UploadStartedAt = DateTimeOffset.UtcNow; // for the upload start time + upload→done duration in Slack
            await jobs.SaveAsync(job, ct);
            progress.Set(job.Id, new JobProgress(JobState.Uploading, 0, job.BytesTotal, PhaseUpload));
            await status.UpdateProgressAsync(job.Id);

            var ytService = youtube.BuildService(chosen.ClientId, chosen.ClientSecret, chosen.RefreshToken);
            YouTubeUploadResult result;
            await using (var fileIn = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                result = await youtube.UploadAsync(
                    ytService, fileIn, job.Title ?? "Untitled upload", job.Description, job.Tags,
                    bytes =>
                    {
                        progress.Set(job.Id, new JobProgress(JobState.Uploading, bytes, job.BytesTotal, PhaseUpload));
                        _ = status.UpdateProgressAsync(job.Id);
                    },
                    () =>
                    {
                        progress.Set(job.Id, new JobProgress(JobState.Processing, job.BytesTotal, job.BytesTotal, PhaseProcessing));
                        _ = status.UpdateProgressAsync(job.Id);
                    },
                    uploadSettings.Visibility,
                    uploadSettings.MadeForKids,
                    uploadSettings.ContainsSyntheticMedia,
                    chunkBytes,
                    ct);
            }

            await jobs.TransitionAsync(job, JobState.Processing, "all bytes sent; YouTube transcoding", ct);

            job.YouTubeVideoId = result.VideoId;
            job.YouTubeUrl = result.Url;
            await jobs.TransitionAsync(job, JobState.Done, "done", ct);
            progress.Remove(job.Id);

            // Custom thumbnail is best-effort and runs AFTER the video exists — it must never fail the job.
            var thumbNote = await TrySetThumbnailAsync(job, ytService, result.VideoId, ct);
            await NotifyAsync(job, $":white_check_mark: Uploaded *{job.Title}* (private) → {result.Url}{thumbNote}");
            await status.RefreshQueueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Upload job {Id} interrupted by shutdown", jobId);
            progress.Remove(jobId);
            // Reserved an upload but no video id yet → the insert never completed; release so a restart
            // doesn't strand the project's daily upload bucket on an upload that produced nothing.
            if (reservedClientId is not null && job.YouTubeVideoId is null)
            {
                try { await quota.ReleaseUploadAsync(reservedClientId.Value); } catch { /* best effort */ }
            }
            // Before the YouTube upload starts nothing exists on YouTube → re-queue so startup
            // recovery resumes it from scratch. Once Uploading/Processing it is the point of no
            // return — never re-upload (would duplicate the video).
            if (job.YouTubeVideoId is null && job.State is JobState.Queued or JobState.Downloading)
                await jobs.TransitionAsync(job, JobState.Queued, "interrupted by restart — will resume", CancellationToken.None);
            else
                await FailAsync(job, "Interrupted after the YouTube upload started — verify in YouTube Studio; the bot won’t re-upload.");
            throw; // don't let Hangfire mark this as succeeded
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload job {Id} failed", jobId);
            progress.Remove(jobId);
            // Reserved an upload but videos.insert never succeeded (no video id) → give it back so a
            // failed attempt doesn't burn the project's daily upload bucket (the counter is an estimate).
            if (reservedClientId is not null && job.YouTubeVideoId is null)
            {
                try { await quota.ReleaseUploadAsync(reservedClientId.Value); } catch { /* best effort */ }
            }
            // Revoked / wrong-client / unauthorized → flag the account so rotation skips it next time.
            if (activeAccountId is not null && job.YouTubeVideoId is null && IsAuthError(ex))
            {
                try { await oauth.MarkAccountErrorAsync(activeAccountId.Value); } catch { /* best effort */ }
            }
            await FailAsync(job, Summarize(ex));
            await status.RefreshQueueAsync(CancellationToken.None);
        }
        finally
        {
            SafeDelete(tempPath);
            await cancelFlags.ClearAsync(jobId);
        }
    }

    /// <summary>Order the channel's projects by remaining uploads (most headroom first) and reserve on the
    /// first that still fits today's upload bucket. Returns null when every project is exhausted. Internal +
    /// static so it can be unit-tested with a fake quota service (no Redis).</summary>
    internal static async Task<GoogleUploadCreds?> ReserveAcrossCandidatesAsync(
        IReadOnlyList<GoogleUploadCreds> candidates, IQuotaService quota)
    {
        var ranked = new List<(GoogleUploadCreds creds, int remaining)>(candidates.Count);
        foreach (var c in candidates)
            ranked.Add((c, (await quota.GetStatusAsync(c.OAuthClientId)).RemainingUploads));
        // OrderByDescending is a stable sort, so ties keep candidate order (oldest account first).
        foreach (var (creds, _) in ranked.OrderByDescending(r => r.remaining))
            if (await quota.TryReserveUploadAsync(creds.OAuthClientId))
                return creds;
        return null;
    }

    /// <summary>True when the failure is an OAuth token problem (revoked / wrong client / unauthorized).</summary>
    private static bool IsAuthError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is TokenResponseException) return true;
        return false;
    }

    // ---- helpers (DB/Slack ops use None so cleanup completes even during shutdown) ----
    private async Task FailAsync(UploadJob job, string reason)
    {
        job.ErrorMessage = reason;
        await jobs.TransitionAsync(job, JobState.Failed, reason, CancellationToken.None);
        await NotifyAsync(job, $":x: Upload failed: {reason}");
    }

    private async Task MarkCancelledAsync(UploadJob job, string tempPath)
    {
        SafeDelete(tempPath);
        progress.Remove(job.Id);
        await jobs.TransitionAsync(job, JobState.Cancelled, "cancelled by user", CancellationToken.None);
        await NotifyAsync(job, $":no_entry_sign: *{job.OriginalFileName ?? job.Title}* — cancelled.");
        await status.RefreshQueueAsync(CancellationToken.None);
    }

    /// <summary>Best-effort custom thumbnail. The video already exists when this runs, so ANY failure is
    /// non-fatal — we log, never fail the job, and return a short suffix for the Slack success line.
    /// No image attached → empty suffix (YouTube auto-generates a frame, exactly the old behaviour).
    /// A provided-but-unapplicable image (wrong format / &gt;2MB / API reject) gets a ⚠️ note so the
    /// operator knows to add one in Studio. Shorts silently ignore thumbnails, so a vertical/≤3min upload
    /// may report "set" yet still show the auto frame — we can't detect that from the API.</summary>
    private async Task<string> TrySetThumbnailAsync(UploadJob job, YouTubeService ytService, string videoId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(job.ThumbnailUrl)) return string.Empty; // no image → auto-frame, no note
        try
        {
            if (job.ThumbnailMimeType is not ("image/png" or "image/jpeg"))
            {
                logger.LogWarning("Job {Id} thumbnail mimetype unsupported: {Mime}", job.Id, job.ThumbnailMimeType);
                return " · :warning: thumbnail skipped (PNG/JPG only)";
            }

            var token = await workspaces.GetBotTokenForChannelAsync(job.SlackChannelId);
            if (token is null) return string.Empty;

            var bytes = await slack.DownloadFileAsync(token, job.ThumbnailUrl, MaxThumbnailBytes, ct);
            if (bytes is null)
            {
                logger.LogWarning("Job {Id} thumbnail download failed or exceeded 2MB", job.Id);
                return " · :warning: thumbnail not applied (download failed or >2MB)";
            }

            await using var ms = new MemoryStream(bytes);
            await youtube.SetThumbnailAsync(ytService, videoId, ms, job.ThumbnailMimeType, ct);
            return " · thumbnail set";
        }
        catch (Exception ex)
        {
            // 403 (custom thumbnails not enabled on the channel), quota, network, etc. — video is fine.
            logger.LogWarning(ex, "Job {Id} thumbnails.set failed (non-fatal)", job.Id);
            return " · :warning: thumbnail not applied";
        }
    }

    private async Task NotifyAsync(UploadJob job, string text)
    {
        var token = await workspaces.GetBotTokenForChannelAsync(job.SlackChannelId);
        if (token is null) return;
        await slack.PostMessageAsync(token, job.SlackChannelId, text, threadTs: job.SlackMessageTs, ct: CancellationToken.None);
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private static string Summarize(Exception ex)
    {
        var msg = ex.Message;
        return msg.Length <= 300 ? msg : msg[..300];
    }
}
