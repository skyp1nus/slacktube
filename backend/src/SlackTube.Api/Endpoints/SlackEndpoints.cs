using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;
using SlackTube.Api.Domain;
using SlackTube.Api.Services.Jobs;
using SlackTube.Api.Services.Slack;

namespace SlackTube.Api.Endpoints;

public static class SlackEndpoints
{
    private const string StateCookie = "slack_oauth_state";
    // Public + private channels: read the list (channels:read/groups:read) + the message text
    // (channels:history/groups:history) + post the live status (chat:write). The bot only sees a
    // private channel after it's invited to it (/invite @bot) — Slack can't auto-join private channels.
    // files:read lets the bot download an image attached to the template message (the custom thumbnail).
    // NOTE: existing installs must REINSTALL (re-run OAuth) to grant files:read — older tokens lack it.
    private const string InstallScopes =
        "chat:write,channels:read,channels:history,groups:read,groups:history,pins:write,files:read";

    public static void MapSlackEndpoints(this WebApplication app)
    {
        app.MapPost("/slack/events", HandleEventsAsync);
        app.MapPost("/slack/interactivity", HandleInteractivityAsync);
        app.MapGet("/slack/oauth/start", StartOAuth);            // AllowAnonymous: pre-auth browser hop
        app.MapGet("/slack/oauth/callback", HandleOAuthCallbackAsync);
    }

    // ---- OAuth v2 install: start → consent ------------------------------------------
    private static IResult StartOAuth(HttpContext http, IOptions<SlackOptions> opt)
    {
        var o = opt.Value;
        var state = GenerateState();
        http.Response.Cookies.Append(StateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/",
        });

        var url = "https://slack.com/oauth/v2/authorize"
            + $"?client_id={Uri.EscapeDataString(o.ClientId)}"
            + $"&scope={Uri.EscapeDataString(InstallScopes)}"
            + $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(url);
    }

    // ---- OAuth v2 install: callback → exchange + sync → bounce to the panel ----------
    private static async Task<IResult> HandleOAuthCallbackAsync(
        HttpContext http,
        IOptions<SlackOptions> slackOpt,
        IOptions<AppOptions> appOpt,
        SlackWorkspaceService workspaces,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var panel = appOpt.Value.AdminPanelUrl.TrimEnd('/');
        var code = http.Request.Query["code"].ToString();
        var state = http.Request.Query["state"].ToString();
        var slackError = http.Request.Query["error"].ToString();

        var expected = http.Request.Cookies[StateCookie];
        http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/" });

        if (!string.IsNullOrEmpty(slackError))
            return Results.Redirect($"{panel}/slack?error={Uri.EscapeDataString(slackError)}");
        if (string.IsNullOrEmpty(code))
            return Results.Redirect($"{panel}/slack?error=missing_code");
        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected)))
            return Results.Redirect($"{panel}/slack?error=invalid_state");

        try
        {
            await workspaces.HandleOAuthCallbackAsync(code, slackOpt.Value.RedirectUri, ct);
            return Results.Redirect($"{panel}/slack?connected=1");
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("Slack.OAuth").LogError(ex, "Slack OAuth callback failed");
            return Results.Redirect($"{panel}/slack?error=oauth_failed");
        }
    }

    private static string GenerateState() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // ---- Events API: signature → challenge → dedup → enqueue ingest -------------------
    private static async Task<IResult> HandleEventsAsync(
        HttpRequest req,
        SlackSignatureVerifier verifier,
        IOptions<SlackOptions> slackOptions,
        IDedupService dedup,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        if (!VerifySignature(req, verifier, slackOptions.Value.SigningSecret, rawBody))
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
            var subtype = ev.TryGetProperty("subtype", out var sub) ? sub.GetString() : null;

            // Only plain user messages: skip bot messages, edits, joins, deletes, etc. A message that
            // carries an attachment may arrive with subtype "file_share" (so the thumbnail image lands on
            // the SAME message as the template) — accept it alongside the no-subtype case.
            var isPlainMessage = evType == "message"
                && (subtype is null || subtype == "file_share")
                && !ev.TryGetProperty("bot_id", out _);

            if (isPlainMessage && eventId is not null && await dedup.TryClaimAsync(eventId))
            {
                var (thumbUrl, thumbMime) = ExtractThumbnail(ev);
                var msg = new SlackMessageRef(
                    eventId,
                    ev.TryGetProperty("channel", out var c) ? c.GetString() ?? "" : "",
                    ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "",
                    ev.TryGetProperty("ts", out var ts) ? ts.GetString() ?? "" : "",
                    ev.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "",
                    thumbUrl, thumbMime);

                backgroundJobs.Enqueue<SlackIngestService>(s => s.ProcessMessageAsync(msg, CancellationToken.None));
            }
        }

        return Results.Ok(); // 200 ACK within 3s
    }

    // ---- Interactivity: signature → parse payload → cancel/confirm/decline -------------
    private static async Task<IResult> HandleInteractivityAsync(
        HttpRequest req,
        SlackSignatureVerifier verifier,
        IOptions<SlackOptions> slackOptions,
        IJobService jobs,
        ICancellationFlags cancelFlags,
        ISlackStatusService status,
        SlackClient slack,
        SlackIngestService ingest,
        CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(req);
        if (!VerifySignature(req, verifier, slackOptions.Value.SigningSecret, rawBody))
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

    /// <summary>First PNG/JPEG file attached to the message → (url_private, mimetype) for use as the video
    /// thumbnail. Returns (null, null) when no usable image is attached. Only png/jpeg (YouTube's formats)
    /// are picked; other files (incl. the video link itself, which is text not a file) are ignored.</summary>
    private static (string? Url, string? Mime) ExtractThumbnail(JsonElement ev)
    {
        if (!ev.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return (null, null);
        foreach (var f in files.EnumerateArray())
        {
            var mime = f.TryGetProperty("mimetype", out var mt) ? mt.GetString() : null;
            if ((mime == "image/png" || mime == "image/jpeg")
                && f.TryGetProperty("url_private", out var up) && up.GetString() is { Length: > 0 } url)
                return (url, mime);
        }
        return (null, null);
    }
}
