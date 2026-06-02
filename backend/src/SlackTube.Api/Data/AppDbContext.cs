using Microsoft.EntityFrameworkCore;
using SlackTube.Api.Domain;

namespace SlackTube.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UploadJob> Jobs => Set<UploadJob>();
    public DbSet<JobStateHistory> JobHistory => Set<JobStateHistory>();
    public DbSet<GoogleToken> GoogleTokens => Set<GoogleToken>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<SlackWorkspace> SlackWorkspaces => Set<SlackWorkspace>();
    public DbSet<SlackChannel> SlackChannels => Set<SlackChannel>();
    public DbSet<GoogleAccount> GoogleAccounts => Set<GoogleAccount>();
    public DbSet<ChannelMapping> ChannelMappings => Set<ChannelMapping>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var job = b.Entity<UploadJob>();
        job.ToTable("upload_jobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        // string[] / List<string> -> jsonb (without this Npgsql would pick native text[])
        job.Property(x => x.Tags).HasColumnType("jsonb");
        job.HasIndex(x => x.SlackEventId);
        job.HasIndex(x => x.State);
        job.HasIndex(x => x.CreatedAt);
        job.HasMany(x => x.History)
           .WithOne(h => h.Job!)
           .HasForeignKey(h => h.JobId)
           .OnDelete(DeleteBehavior.Cascade);
        job.HasOne<GoogleAccount>()
           .WithMany()
           .HasForeignKey(x => x.GoogleAccountId)
           .OnDelete(DeleteBehavior.SetNull);

        var hist = b.Entity<JobStateHistory>();
        hist.ToTable("job_state_history");
        hist.HasKey(x => x.Id);
        hist.Property(x => x.FromState).HasConversion<string>().HasMaxLength(32);
        hist.Property(x => x.ToState).HasConversion<string>().HasMaxLength(32);
        hist.HasIndex(x => x.JobId);

        var tok = b.Entity<GoogleToken>();
        tok.ToTable("google_tokens");
        tok.HasKey(x => x.Id);

        var settings = b.Entity<AppSettings>();
        settings.ToTable("app_settings");
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Id).ValueGeneratedNever();

        var ws = b.Entity<SlackWorkspace>();
        ws.ToTable("slack_workspaces");
        ws.HasKey(x => x.Id);
        ws.HasIndex(x => x.SlackTeamId).IsUnique();
        ws.HasMany(x => x.Channels)
          .WithOne(c => c.Workspace!)
          .HasForeignKey(c => c.WorkspaceId)
          .OnDelete(DeleteBehavior.Cascade);

        var ch = b.Entity<SlackChannel>();
        ch.ToTable("slack_channels");
        ch.HasKey(x => x.Id);
        ch.HasIndex(x => new { x.WorkspaceId, x.SlackChannelId }).IsUnique();
        ch.HasIndex(x => x.SlackChannelId);

        var ga = b.Entity<GoogleAccount>();
        ga.ToTable("google_accounts");
        ga.HasKey(x => x.Id);
        ga.HasIndex(x => x.CreatedAt);

        var cm = b.Entity<ChannelMapping>();
        cm.ToTable("channel_mappings");
        cm.HasKey(x => x.Id);
        cm.HasIndex(x => x.SlackChannelId).IsUnique();
        cm.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.SlackWorkspaceId).OnDelete(DeleteBehavior.Cascade);
        cm.HasOne(x => x.GoogleAccount).WithMany().HasForeignKey(x => x.GoogleAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
