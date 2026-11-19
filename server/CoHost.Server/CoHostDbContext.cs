using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoHost.Server;

/// <summary>EF Core context. No entities in P0; tables arrive with Phase 2 (SRV-02).</summary>
public sealed class CoHostDbContext(DbContextOptions<CoHostDbContext> options) : DbContext(options);

/// <summary>Lets <c>dotnet ef</c> build the context without booting the host.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CoHostDbContext>
{
    public CoHostDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CoHostDbContext>()
            .UseNpgsql("Host=localhost;Database=cohost_design");
        return new CoHostDbContext(builder.Options);
    }
}
