using Hangfire;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Google;
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
    IOptions<AppOptions> appOptions,
    ILogger<UploadJobHandler> logger)
{
    private const string PhaseDownload = "Downloading from Drive";
    private const string PhaseUpload = "Uploading to YouTube";
    private const string PhaseProcessing = "YouTube processing";
    private const long CancelCheckBytes = 4_000_000;

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

        try
        {
            if (await cancelFlags.IsRequestedAsync(job.Id)) { await MarkCancelledAsync(job, tempPath); return; }

            var accountId = job.GoogleAccountId ?? await oauth.GetDefaultAccountIdAsync(ct);
            if (accountId is null) { await FailAsync(job, "No Google account is connected."); return; }
            var refreshToken = await oauth.GetRefreshTokenAsync(accountId.Value, ct);
            if (refreshToken is null) { await FailAsync(job, "Google account token is unavailable."); return; }

            Directory.CreateDirectory(tempDir);
            var chunkBytes = appOptions.Value.TransferChunkSizeBytes;

            // ============================ DOWNLOAD ============================
            await jobs.TransitionAsync(job, JobState.Downloading, "download started", ct);
            await status.RefreshQueueAsync(ct);

            var driveService = drive.BuildService(refreshToken);
            var info = await drive.GetInfoAsync(driveService, job.DriveFileId, ct);
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
                    }, chunkBytes, downloadCts.Token);
                }
                catch (OperationCanceledException) when (cancelledDuringDownload)
                {
                    // user cancel — handled right below
                }
            }

            if (cancelledDuringDownload) { await MarkCancelledAsync(job, tempPath); return; }

            // Last chance to cancel before the point of no return.
            if (await cancelFlags.IsRequestedAsync(job.Id)) { await MarkCancelledAsync(job, tempPath); return; }

            // ============================ QUOTA ============================
            if (!await quota.TryReserveUploadAsync(accountId.Value))
            {
                SafeDelete(tempPath);
                progress.Remove(job.Id);
                await jobs.TransitionAsync(job, JobState.Blocked, "daily YouTube quota reached", ct);
                await NotifyAsync(job, ":lock: Daily YouTube quota reached — this upload is blocked until after midnight PT.");
                await status.RefreshQueueAsync(ct);
                return;
            }

            // ==================== UPLOAD (point of no return) ====================
            await jobs.TransitionAsync(job, JobState.Uploading, "upload started", ct);
            job.QuotaUnitsCharged = appOptions.Value.YouTubeUploadCostUnits;
            await jobs.SaveAsync(job, ct);
            progress.Set(job.Id, new JobProgress(JobState.Uploading, 0, job.BytesTotal, PhaseUpload));
            await status.UpdateProgressAsync(job.Id);

            var ytService = youtube.BuildService(refreshToken);
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
                    chunkBytes,
                    ct);
            }

            await jobs.TransitionAsync(job, JobState.Processing, "all bytes sent; YouTube transcoding", ct);

            job.YouTubeVideoId = result.VideoId;
            job.YouTubeUrl = result.Url;
            await jobs.TransitionAsync(job, JobState.Done, "done", ct);
            progress.Remove(job.Id);
            await NotifyAsync(job, $":white_check_mark: Uploaded *{job.Title}* (private) → {result.Url}");
            await status.RefreshQueueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Upload job {Id} interrupted by shutdown", jobId);
            progress.Remove(jobId);
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
            await FailAsync(job, Summarize(ex));
            await status.RefreshQueueAsync(CancellationToken.None);
        }
        finally
        {
            SafeDelete(tempPath);
            await cancelFlags.ClearAsync(jobId);
        }
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
