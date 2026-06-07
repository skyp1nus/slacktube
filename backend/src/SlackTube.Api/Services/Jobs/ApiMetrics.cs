namespace SlackTube.Api.Services.Jobs;

/// <summary>Metric/scope identifiers shared by the chargers (Drive/Slack/YouTube call sites) and the usage
/// reader, so the names stay in lockstep. Google metrics are scoped by OAuth client id; Slack by "slack".</summary>
public static class ApiMetrics
{
    public const string SlackScope = "slack";

    // Per-OAuth-client (Google) metrics.
    public const string YouTubeUpload = "youtube.upload"; // videos.insert calls (sourced from the upload bucket)
    public const string YouTubeUnits = "youtube.units";   // non-upload unit pool (channels.list etc.)
    public const string DriveQueries = "drive.queries";   // files.get calls (metadata + download)
    public const string DriveBytes = "drive.bytes";       // bytes streamed from Drive

    /// <summary>Slack Web API call counter for a given method (e.g. "chat.postMessage").</summary>
    public static string Slack(string method) => $"slack.{method}";
}
