using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KOSHI.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling. The connection string is read from KOSHI_DESIGN_CONNECTION.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KoshiDbContext>
{
    public KoshiDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("KOSHI_DESIGN_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=koshi_design;Username=postgres";
        var options = new DbContextOptionsBuilder<KoshiDbContext>().UseNpgsql(cs).Options;
        return new KoshiDbContext(options);
    }
}
