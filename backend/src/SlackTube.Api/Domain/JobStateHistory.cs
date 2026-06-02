namespace SlackTube.Api.Domain;

/// <summary>Audit row written on every state transition of an <see cref="UploadJob"/>.</summary>
public class JobStateHistory
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public UploadJob? Job { get; set; }

    public JobState? FromState { get; set; }
    public JobState ToState { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}
