using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Data;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Services.Jobs;

public sealed record NewJob(
    string SlackEventId,
    string ChannelId,
    string UserId,
    string MessageTs,
    string DriveFileId,
    string? OriginalFileName,
    string? Title,
    string? Description,
    List<string> Tags,
    bool RequiresConfirmation,
    Guid? GoogleAccountId);

public sealed record StatusSnapshot(
    UploadJob? Active,
    IReadOnlyList<UploadJob> Queued,
    IReadOnlyList<UploadJob> Recent);

public interface IJobService
{
    Task<UploadJob> CreateAsync(NewJob input, CancellationToken ct = default);
    Task<bool> ExistsForEventAsync(string slackEventId, CancellationToken ct = default);
    Task<UploadJob?> GetAsync(Guid id, CancellationToken ct = default);
    Task TransitionAsync(UploadJob job, JobState to, string? note = null, CancellationToken ct = default);
    Task SaveAsync(UploadJob job, CancellationToken ct = default);
    Task<StatusSnapshot> GetStatusSnapshotAsync(string slackChannelId, int recentCount = 5, CancellationToken ct = default);
    Task<IReadOnlyList<UploadJob>> GetHistoryAsync(int take = 50, CancellationToken ct = default);
}

public sealed class JobService(AppDbContext db) : IJobService
{
    public async Task<UploadJob> CreateAsync(NewJob i, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            SlackEventId = i.SlackEventId,
            SlackChannelId = i.ChannelId,
            SlackUserId = i.UserId,
            SlackMessageTs = i.MessageTs,
            DriveFileId = i.DriveFileId,
            GoogleAccountId = i.GoogleAccountId,
            OriginalFileName = i.OriginalFileName,
            Title = i.Title,
            Description = i.Description,
            Tags = i.Tags,
            RequiresConfirmation = i.RequiresConfirmation,
            Confirmed = i.RequiresConfirmation ? null : true,
            State = JobState.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };
        job.History.Add(new JobStateHistory { ToState = JobState.Queued, At = now, Note = "created" });
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public Task<bool> ExistsForEventAsync(string slackEventId, CancellationToken ct = default)
        => db.Jobs.AnyAsync(j => j.SlackEventId == slackEventId, ct);

    public Task<UploadJob?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task TransitionAsync(UploadJob job, JobState to, string? note = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        job.History.Add(new JobStateHistory
        {
            JobId = job.Id,
            FromState = job.State,
            ToState = to,
            At = now,
            Note = note,
        });
        job.State = to;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveAsync(UploadJob job, CancellationToken ct = default)
    {
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<StatusSnapshot> GetStatusSnapshotAsync(string slackChannelId, int recentCount = 5, CancellationToken ct = default)
    {
        var active = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && (j.State == JobState.Downloading
                      || j.State == JobState.Uploading
                      || j.State == JobState.Processing))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var queued = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && j.State == JobState.Queued && (!j.RequiresConfirmation || j.Confirmed == true))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);

        var recent = await db.Jobs.AsNoTracking()
            .Where(j => j.SlackChannelId == slackChannelId
                     && (j.State == JobState.Done
                      || j.State == JobState.Cancelled
                      || j.State == JobState.Failed
                      || j.State == JobState.Blocked))
            .OrderByDescending(j => j.UpdatedAt)
            .Take(recentCount)
            .ToListAsync(ct);

        return new StatusSnapshot(active, queued, recent);
    }

    public async Task<IReadOnlyList<UploadJob>> GetHistoryAsync(int take = 50, CancellationToken ct = default)
        => await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
}
