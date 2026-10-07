using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ophi.Infrastructure.Persistence;

/// <summary>
/// Factory for creating OphiDbContext at design time (dotnet ef migrations/database commands).
/// This allows running EF tooling against the Infrastructure project directly,
/// avoiding Wolverine/CodeAnalysis package conflicts in the API project.
///
/// Usage:
///   dotnet ef migrations add MigrationName --project src/Ophi.Infrastructure
///   dotnet ef database update --project src/Ophi.Infrastructure
///
/// Provider is selected by DB_PROVIDER (default: postgres — migrations are Postgres now).
/// For Postgres, set POSTGRES_CONNECTION (a throwaway DB is fine; the schema, not the data,
/// is what migration generation reads).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OphiDbContext>
{
    public OphiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OphiDbContext>();

        var provider = (Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "postgres")
            .Trim().ToLowerInvariant();

        if (provider == "sqlite")
        {
            optionsBuilder.UseSqlite("Data Source=../Ophi.Api/data/ophi.db");
        }
        else
        {
            var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=ophi;Username=postgres;Password=postgres";
            optionsBuilder.UseNpgsql(connectionString);
        }

        return new OphiDbContext(optionsBuilder.Options);
    }
}
