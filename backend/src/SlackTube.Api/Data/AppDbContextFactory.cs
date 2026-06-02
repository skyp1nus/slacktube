using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SlackTube.Api.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the model without booting
/// the full host (or needing a live DB). The runtime app registers its own AddDbContext.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5432;Database=slacktube;Username=slacktube;Password=slacktube";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new AppDbContext(options);
    }
}
