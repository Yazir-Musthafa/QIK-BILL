using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to generate SQLite migrations. Never opens the real database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexoraDbContext>
{
    public NexoraDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NexoraDbContext>();
        new SqliteDatabaseProvider("design-time.db").Configure(builder);
        return new NexoraDbContext(builder.Options);
    }
}
