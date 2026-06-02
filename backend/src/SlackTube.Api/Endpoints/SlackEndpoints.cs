using System.Net;
using System.Text;
using System.Text.Json;
using Hangfire;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Settings;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Endpoints;

public static class SlackEndpoints
{
    public static void MapSlackEndpoints(this WebApplication app)
    {
        app.MapPost("/slack/events", HandleEventsAsync);
        app.MapPost("/slack/interactivity", HandleInteractivityAsync);
    }

    // ---- Events API: signature → challenge → dedup → enqueue ingest -------------------
    private static async Task<IResult> HandleEventsAsync(
        HttpRequest req,
        SlackSignatureVerifier verifier,
        ISettingsStore settings,
        IDedupService dedup,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        var slack = await settings.GetSlackAsync(ct);
        if (!VerifySignature(req, verifier, slack.SigningSecret, rawBody))
            return Results.Unauthorized();

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        if (type == "url_verification")
            return Results.Text(root.GetProperty("challenge").GetString() ?? "");

        if (type == "event_callback" && root.TryGetProperty("event", out var ev))
        {
            var eventId = root.TryGetProperty("event_id", out var eid) ? eid.GetString() : null;
            var evType = ev.TryGetProperty("type", out var et) ? et.GetString() : null;

            // Only plain user messages: skip bot messages, edits, joins, deletes, etc.
            var isPlainMessage = evType == "message"
                && !ev.TryGetProperty("subtype", out _)
                && !ev.TryGetProperty("bot_id", out _);

            if (isPlainMessage && eventId is not null && await dedup.TryClaimAsync(eventId))
            {
                var msg = new SlackMessageRef(
                    eventId,
                    ev.TryGetProperty("channel", out var c) ? c.GetString() ?? "" : "",
                    ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "",
                    ev.TryGetProperty("ts", out var ts) ? ts.GetString() ?? "" : "",
                    ev.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "");

                backgroundJobs.Enqueue<SlackIngestService>(s => s.ProcessMessageAsync(msg, CancellationToken.None));
            }
        }

        return Results.Ok(); // 200 ACK within 3s
    }

    // ---- Interactivity: signature → parse payload → cancel/confirm/decline -------------
    private static async Task<IResult> HandleInteractivityAsync(
        HttpRequest req,
        SlackSignatureVerifier verifier,
        ISettingsStore settings,
        IJobService jobs,
        ICancellationFlags cancelFlags,
        ISlackStatusService status,
        SlackClient slack,
        SlackIngestService ingest,
        CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        var slackCfg = await settings.GetSlackAsync(ct);
        if (!VerifySignature(req, verifier, slackCfg.SigningSecret, rawBody))
            return Results.Unauthorized();

        var payloadJson = ExtractFormField(rawBody, "payload");
        if (string.IsNullOrEmpty(payloadJson)) return Results.Ok();

        using var doc = JsonDocument.Parse(payloadJson);
        var p = doc.RootElement;
        if (p.TryGetProperty("type", out var pt) && pt.GetString() != "block_actions")
            return Results.Ok();

        if (!p.TryGetProperty("actions", out var actions) || actions.GetArrayLength() == 0)
            return Results.Ok();

        var action = actions[0];
        var actionId = action.GetProperty("action_id").GetString();
        var value = action.TryGetProperty("value", out var v) ? v.GetString() : null;
        var responseUrl = p.TryGetProperty("response_url", out var ru) ? ru.GetString() : null;

        if (!Guid.TryParse(value, out var jobId))
            return Results.Ok();

        switch (actionId)
        {
            case SlackActions.CancelJob:
                await HandleCancelAsync(jobs, cancelFlags, status, slack, jobId, responseUrl, ct);
                break;
            case SlackActions.ConfirmUpload:
                await HandleConfirmAsync(jobs, ingest, status, slack, jobId, responseUrl, ct);
                break;
            case SlackActions.DeclineUpload:
                await HandleDeclineAsync(jobs, slack, jobId, responseUrl, ct);
                break;
        }

        return Results.Ok();
    }

    private static async Task HandleCancelAsync(
        IJobService jobs, ICancellationFlags cancelFlags, ISlackStatusService status,
        SlackClient slack, Guid jobId, string? responseUrl, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return;

        if (!job.IsCancellable)
        {
            await Respond(slack, responseUrl, "Already uploading/uploaded — remove it manually in YouTube Studio.");
            return;
        }

        // Authoritative signal for the worker; also flip queued jobs immediately for snappy UI.
        await cancelFlags.RequestAsync(jobId);
        if (job.State == JobState.Queued)
        {
            await jobs.TransitionAsync(job, JobState.Cancelled, "cancelled by user (queued)", ct);
            await status.RefreshQueueAsync(ct);
        }
        await Respond(slack, responseUrl, "Cancellation requested.");
    }

    private static async Task HandleConfirmAsync(
        IJobService jobs, SlackIngestService ingest, ISlackStatusService status,
        SlackClient slack, Guid jobId, string? responseUrl, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null || job.IsTerminal) { await Respond(slack, responseUrl, "This request is no longer pending."); return; }

        if (job.Confirmed == true)
        {
            await Respond(slack, responseUrl, "Already confirmed — uploading.");
            return;
        }

        job.Confirmed = true;
        await jobs.SaveAsync(job, ct);
        ingest.Enqueue(job.Id);
        await status.RefreshQueueAsync(ct);
        await Respond(slack, responseUrl, ":white_check_mark: Confirmed — uploading.");
    }

    private static async Task HandleDeclineAsync(
        IJobService jobs, SlackClient slack, Guid jobId, string? responseUrl, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return;

        if (job.State == JobState.Queued && job.Confirmed != true)
        {
            job.Confirmed = false;
            await jobs.TransitionAsync(job, JobState.Cancelled, "declined by user", ct);
        }
        await Respond(slack, responseUrl, "Cancelled — not uploaded.");
    }

    // ---- helpers ---------------------------------------------------------------------
    private static bool VerifySignature(HttpRequest req, SlackSignatureVerifier verifier, string? secret, string rawBody)
        => verifier.Verify(
            secret,
            req.Headers["X-Slack-Request-Timestamp"].ToString(),
            rawBody,
            req.Headers["X-Slack-Signature"].ToString());

    private static async Task<string> ReadRawBodyAsync(HttpRequest req)
    {
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        req.Body.Position = 0;
        return body;
    }

    /// <summary>Parses one field out of an x-www-form-urlencoded body (handles + as space).</summary>
    private static string? ExtractFormField(string body, string name)
    {
        foreach (var pair in body.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            if (pair[..eq] == name) return WebUtility.UrlDecode(pair[(eq + 1)..]);
        }
        return null;
    }

    private static Task Respond(SlackClient slack, string? responseUrl, string text)
        => responseUrl is null
            ? Task.CompletedTask
            : slack.PostToResponseUrlAsync(responseUrl, new { replace_original = true, text });
}
