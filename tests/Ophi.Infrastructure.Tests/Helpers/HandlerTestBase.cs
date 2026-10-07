using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using Ophi.Domain.Entities;
using Ophi.Infrastructure.Persistence;
using Ophi.TestHelpers;

namespace Ophi.Infrastructure.Tests.Helpers;

/// <summary>
/// Base class for handler tests that need an in-memory SQLite database with a seeded test user.
/// </summary>
public abstract class HandlerTestBase : IDisposable
{
    protected readonly OphiDbContext DbContext;
    protected readonly SqliteConnection Connection;
    protected readonly Mock<ILogger> LoggerMock = new();
    protected readonly Guid TestUserId;
    protected readonly User TestUser;

    protected HandlerTestBase()
    {
        (DbContext, Connection) = TestDbContextFactory.Create();

        TestUserId = Guid.NewGuid();
        TestUser = new User
        {
            Id = TestUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        };
        DbContext.Users.Add(TestUser);
        DbContext.SaveChanges();
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
