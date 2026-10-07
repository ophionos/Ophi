using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ophi.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Provisions an isolated Postgres database for the integration tier and applies the migration
/// baseline to it (which doubles as the migration smoke test on the real provider).
///
/// Connection source:
///   - <c>POSTGRES_TEST_CONNECTION</c> env set → use it as the admin connection (local dev points
///     at a throwaway Postgres; CI may use a service container). No Docker-from-Windows needed.
///   - otherwise → Testcontainers starts <c>postgres:16</c>.
///
/// Each run gets its own freshly-created database (<c>ophi_test_{guid}</c>), dropped on dispose, so
/// runs against a shared server don't collide.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = "";
    private readonly string _databaseName = $"ophi_test_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var envConn = Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            _adminConnectionString = envConn;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16").Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }

        // Create an isolated database for this run.
        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;

        // Apply the full migration history on real Postgres — this IS the migration smoke test.
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public OphiDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new OphiDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (string.IsNullOrEmpty(_adminConnectionString))
        {
            return;
        }

        // Shared server: drop the throwaway database. Clear pools + terminate backends first so the
        // DROP isn't blocked by lingering connections to it.
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        await using (var terminate = admin.CreateCommand())
        {
            terminate.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()";
            terminate.Parameters.AddWithValue("db", _databaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await drop.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
