using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SlackTube.Api.Configuration;

namespace SlackTube.Api.Services.Slack;

public sealed record SlackChannelInfo(string Id, string Name, bool IsPrivate, bool IsMember);

public sealed record SlackOAuthResult(
    string TeamId, string TeamName, string AccessToken, string? BotUserId, string? Scope, string? AuthedUserId);

/// <summary>
/// Thin Slack Web API client over HttpClient. The bot token is passed per call (it is now
/// per-workspace — resolved by <see cref="SlackWorkspaceService"/>). Retries on HTTP 429 using
/// Retry-After. Always sends a top-level <c>text</c> fallback alongside blocks. App-level
/// client id/secret (for the OAuth code exchange) come from <see cref="SlackOptions"/>.
/// </summary>
public sealed class SlackClient(
    HttpClient http,
    IOptions<SlackOptions> slackOptions,
    ILogger<SlackClient> logger)
{
    private const string ApiBase = "https://slack.com/api/";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly SlackOptions _opt = slackOptions.Value;

    public async Task<string?> PostMessageAsync(
        string botToken, string channel, string text,
        object[]? blocks = null, string? threadTs = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["channel"] = channel,
            ["text"] = text,
            ["unfurl_links"] = false,
            ["unfurl_media"] = false,
        };
        if (blocks is not null) payload["blocks"] = blocks;
        if (threadTs is not null) payload["thread_ts"] = threadTs;

        var root = await CallAsync(botToken, "chat.postMessage", payload, ct);
        return root?.TryGetProperty("ts", out var ts) == true ? ts.GetString() : null;
    }

    public async Task<bool> UpdateMessageAsync(
        string botToken, string channel, string ts, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?> { ["channel"] = channel, ["ts"] = ts, ["text"] = text };
        if (blocks is not null) payload["blocks"] = blocks;
        var root = await CallAsync(botToken, "chat.update", payload, ct);
        return root is not null && root.Value.GetProperty("ok").GetBoolean();
    }

    public async Task<bool> DeleteMessageAsync(string botToken, string channel, string ts, CancellationToken ct = default)
    {
        var root = await CallAsync(botToken, "chat.delete", new() { ["channel"] = channel, ["ts"] = ts }, ct);
        return root is not null && root.Value.GetProperty("ok").GetBoolean();
    }

    /// <summary>Lists the workspace channels the bot can see — public channels plus any PRIVATE
    /// channel the bot has been invited to (cursor-paginated).</summary>
    public async Task<IReadOnlyList<SlackChannelInfo>> ListChannelsAsync(string botToken, CancellationToken ct = default)
    {
        var result = new List<SlackChannelInfo>();
        string? cursor = null;
        do
        {
            // conversations.list reads its params from the FORM/query string, NOT a JSON body. Sent as
            // JSON, `types` is silently dropped and Slack falls back to public_channel only — so private
            // channels never come back regardless of scopes/membership. Always call it form-encoded.
            var form = new Dictionary<string, string>
            {
                ["types"] = "public_channel,private_channel",
                ["exclude_archived"] = "true",
                ["limit"] = "200",
            };
            if (!string.IsNullOrEmpty(cursor)) form["cursor"] = cursor;

            var root = await CallFormAsync(botToken, "conversations.list", form, ct);
            if (root is null || !root.Value.GetProperty("ok").GetBoolean()) break;

            if (root.Value.TryGetProperty("channels", out var channels))
            {
                foreach (var c in channels.EnumerateArray())
                {
                    result.Add(new SlackChannelInfo(
                        c.GetProperty("id").GetString()!,
                        c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        c.TryGetProperty("is_private", out var p) && p.GetBoolean(),
                        c.TryGetProperty("is_member", out var m) && m.GetBoolean()));
                }
            }

            // Stop when next_cursor is an EMPTY STRING (the last page still has channels).
            cursor = root.Value.TryGetProperty("response_metadata", out var meta)
                     && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    /// <summary>Exchanges an OAuth v2 install code for a bot token + workspace identity.</summary>
    public async Task<SlackOAuthResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _opt.ClientId,
            ["client_secret"] = _opt.ClientSecret,
            ["redirect_uri"] = redirectUri,
        };
        using var res = await http.PostAsync(ApiBase + "oauth.v2.access", new FormUrlEncodedContent(form), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(
                $"oauth.v2.access failed: {(root.TryGetProperty("error", out var e) ? e.GetString() : "unknown")}");

        var team = root.GetProperty("team");
        return new SlackOAuthResult(
            team.GetProperty("id").GetString()!,
            team.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "",
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("bot_user_id", out var bu) ? bu.GetString() : null,
            root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
            root.TryGetProperty("authed_user", out var au) && au.TryGetProperty("id", out var auid) ? auid.GetString() : null);
    }

    /// <summary>Posts a follow-up to an interactivity response_url (no token needed).</summary>
    public async Task PostToResponseUrlAsync(string responseUrl, object payload, CancellationToken ct = default)
    {
        try
        {
            using var res = await http.PostAsJsonAsync(responseUrl, payload, JsonOpts, ct);
            if (!res.IsSuccessStatusCode)
                logger.LogWarning("Slack response_url POST failed: {Status}", res.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Slack response_url POST threw");
        }
    }

    // ---- core call with 429 retry ----------------------------------------------------
    private async Task<JsonElement?> CallAsync(
        string botToken, string method, Dictionary<string, object?> payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(botToken))
        {
            logger.LogWarning("No Slack bot token available — skipping {Method}", method);
            return null;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + method)
            {
                Content = JsonContent.Create(payload, options: JsonOpts),
            };
            req.Headers.Authorization = new("Bearer", botToken);

            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                logger.LogWarning("Slack {Method} rate-limited; waiting {Wait}s", method, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }

            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                logger.LogWarning("Slack {Method} returned error: {Error}", method,
                    root.TryGetProperty("error", out var er) ? er.GetString() : "unknown");
            }
            return root.Clone();
        }

        return null;
    }

    // ---- core call (FORM-encoded) for read methods whose params Slack reads only from the form/query.
    // conversations.list `types` is the case in point — a JSON body silently drops it. 429-retry mirrors
    // CallAsync. Messaging methods (chat.postMessage with blocks) stay on the JSON CallAsync.
    private async Task<JsonElement?> CallFormAsync(
        string botToken, string method, Dictionary<string, string> form, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(botToken))
        {
            logger.LogWarning("No Slack bot token available — skipping {Method}", method);
            return null;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + method)
            {
                Content = new FormUrlEncodedContent(form),
            };
            req.Headers.Authorization = new("Bearer", botToken);

            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                logger.LogWarning("Slack {Method} rate-limited; waiting {Wait}s", method, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }

            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                logger.LogWarning("Slack {Method} returned error: {Error}", method,
                    root.TryGetProperty("error", out var er) ? er.GetString() : "unknown");
            }
            return root.Clone();
        }

        return null;
    }
}
