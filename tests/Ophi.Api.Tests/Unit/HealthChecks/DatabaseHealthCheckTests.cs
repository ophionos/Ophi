using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ophi.Api.Common.HealthChecks;
using Ophi.TestHelpers;

namespace Ophi.Api.Tests.Unit.HealthChecks;

public class DatabaseHealthCheckTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Infrastructure.Persistence.OphiDbContext _dbContext;

    public DatabaseHealthCheckTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
    }

    [Fact]
    public async Task CheckHealthAsync_WithHealthyDatabase_ReturnsHealthy()
    {
        var check = new DatabaseHealthCheck(_dbContext);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("reachable");
    }

    [Fact]
    public async Task CheckHealthAsync_WithBrokenDatabase_ReturnsUnhealthy()
    {
        // Create a context pointing to a non-existent file path that will fail
        var badConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=/nonexistent/path/db.sqlite;Mode=ReadOnly");
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Infrastructure.Persistence.OphiDbContext>()
            .UseSqlite(badConnection)
            .Options;
        using var badContext = new Infrastructure.Persistence.OphiDbContext(options);
        var check = new DatabaseHealthCheck(badContext);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
