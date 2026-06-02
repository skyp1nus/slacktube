using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SlackTube.Api.Services.Settings;

namespace SlackTube.Api.Services.Slack;

public sealed record SlackChannel(string Id, string Name, bool IsMember);

/// <summary>
/// Thin Slack Web API client over HttpClient. Resolves the bot token from <see cref="ISettingsStore"/>
/// per call (it can change at runtime via the admin panel) and retries once on HTTP 429 using the
/// Retry-After header. Always sends a top-level <c>text</c> fallback alongside blocks.
/// </summary>
public sealed class SlackClient(
    HttpClient http,
    ISettingsStore settings,
    ILogger<SlackClient> logger)
{
    private const string ApiBase = "https://slack.com/api/";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Posts a message; returns its <c>ts</c> (needed for later update/delete) or null.</summary>
    public async Task<string?> PostMessageAsync(
        string channel, string text, object[]? blocks = null, string? threadTs = null, CancellationToken ct = default)
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

        var root = await CallAsync("chat.postMessage", payload, ct);
        return root?.TryGetProperty("ts", out var ts) == true ? ts.GetString() : null;
    }

    public async Task<bool> UpdateMessageAsync(
        string channel, string ts, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?> { ["channel"] = channel, ["ts"] = ts, ["text"] = text };
        if (blocks is not null) payload["blocks"] = blocks;
        var root = await CallAsync("chat.update", payload, ct);
        return root is not null && root.Value.GetProperty("ok").GetBoolean();
    }

    public async Task<bool> DeleteMessageAsync(string channel, string ts, CancellationToken ct = default)
    {
        var root = await CallAsync("chat.delete", new() { ["channel"] = channel, ["ts"] = ts }, ct);
        return root is not null && root.Value.GetProperty("ok").GetBoolean();
    }

    /// <summary>Lists channels the workspace exposes (for the admin "listening channel" dropdown).</summary>
    public async Task<IReadOnlyList<SlackChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        var result = new List<SlackChannel>();
        string? cursor = null;
        do
        {
            var payload = new Dictionary<string, object?>
            {
                ["types"] = "public_channel,private_channel",
                ["exclude_archived"] = true,
                ["limit"] = 200,
            };
            if (cursor is not null) payload["cursor"] = cursor;

            var root = await CallAsync("conversations.list", payload, ct);
            if (root is null || !root.Value.GetProperty("ok").GetBoolean()) break;

            if (root.Value.TryGetProperty("channels", out var channels))
            {
                foreach (var c in channels.EnumerateArray())
                {
                    result.Add(new SlackChannel(
                        c.GetProperty("id").GetString()!,
                        c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        c.TryGetProperty("is_member", out var m) && m.GetBoolean()));
                }
            }

            cursor = root.Value.TryGetProperty("response_metadata", out var meta)
                     && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
        } while (!string.IsNullOrEmpty(cursor));

        return result;
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

    // ---- core call with token + 429 retry -------------------------------------------
    private async Task<JsonElement?> CallAsync(string method, Dictionary<string, object?> payload, CancellationToken ct)
    {
        var slack = await settings.GetSlackAsync(ct);
        if (string.IsNullOrEmpty(slack.BotToken))
        {
            logger.LogWarning("Slack bot token not configured — skipping {Method}", method);
            return null;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBase + method)
            {
                Content = JsonContent.Create(payload, options: JsonOpts),
            };
            req.Headers.Authorization = new("Bearer", slack.BotToken);

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
                    root.TryGetProperty("error", out var e) ? e.GetString() : "unknown");
            }
            return root.Clone(); // clone so it outlives the disposed JsonDocument
        }

        return null;
    }
}
