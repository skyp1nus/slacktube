namespace SlackTube.Api.Services.Slack;

/// <summary>action_id values used on Block Kit buttons (matched in the interactivity handler).</summary>
public static class SlackActions
{
    public const string CancelJob = "cancel_job";
    public const string ConfirmUpload = "confirm_upload";
    public const string DeclineUpload = "decline_upload";
}
