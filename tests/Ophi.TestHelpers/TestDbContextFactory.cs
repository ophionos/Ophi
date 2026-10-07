using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ophi.Infrastructure.Persistence;

namespace Ophi.TestHelpers;

/// <summary>
/// Factory for creating test database contexts backed by an in-memory SQLite database.
/// Callers must dispose both returned values.
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new <see cref="OphiDbContext"/> backed by a fresh in-memory SQLite database.
    /// The connection must remain open for the lifetime of the context; closing it discards
    /// the in-memory database.
    /// </summary>
    /// <returns>Tuple containing the DbContext and the SqliteConnection (caller disposes both).</returns>
    public static (OphiDbContext context, SqliteConnection connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new OphiDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }

    /// <summary>
    /// Creates an additional <see cref="OphiDbContext"/> bound to an existing connection,
    /// so concurrent handler invocations can simulate separate change-trackers hitting the
    /// same in-memory database. Caller disposes the returned context.
    /// </summary>
    public static OphiDbContext Attach(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<OphiDbContext>()
            .UseSqlite(connection)
            .Options;
        return new OphiDbContext(options);
    }
}
