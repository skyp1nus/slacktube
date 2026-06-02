using System.Collections.Concurrent;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Services.Jobs;

/// <summary>Live byte progress for the active job. Kept in memory (a singleton) and read by
/// the Slack status renderer — never hammer the DB with per-tick writes.</summary>
public sealed record JobProgress(JobState State, long BytesTransferred, long BytesTotal, string? Phase)
{
    public int Percent => BytesTotal > 0
        ? (int)Math.Clamp(BytesTransferred * 100 / BytesTotal, 0, 100)
        : 0;
}

public interface IProgressTracker
{
    void Set(Guid jobId, JobProgress progress);
    JobProgress? Get(Guid jobId);
    void Remove(Guid jobId);
}

public sealed class ProgressTracker : IProgressTracker
{
    private readonly ConcurrentDictionary<Guid, JobProgress> _progress = new();

    public void Set(Guid jobId, JobProgress progress) => _progress[jobId] = progress;
    public JobProgress? Get(Guid jobId) => _progress.TryGetValue(jobId, out var p) ? p : null;
    public void Remove(Guid jobId) => _progress.TryRemove(jobId, out _);
}
