using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Features.ApiKeys;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;

namespace Ophi.Api.Tests.Unit.ApiKeys;

public class ListApiKeysTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Ophi.Infrastructure.Persistence.OphiDbContext _dbContext;
    private readonly Guid _userId;

    public ListApiKeysTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _userId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = _userId,
            Email = "test@example.com",
            PasswordHash = "hash",
            Name = "Test"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_ReturnsUserKeys()
    {
        _dbContext.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Key 1",
            KeyHash = "hash1",
            Scopes = ["read"]
        });
        _dbContext.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Key 2",
            KeyHash = "hash2",
            Scopes = ["read", "write"]
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListApiKeys.Handler(_dbContext, NullLogger<ListApiKeys.Handler>.Instance);
        var result = await handler.Handle(new ListApiKeys.Query { UserId = _userId }, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Name == "Key 1");
        result.Should().Contain(r => r.Name == "Key 2");
    }

    [Fact]
    public async Task Handle_DoesNotReturnOtherUserKeys()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            PasswordHash = "hash",
            Name = "Other"
        });
        _dbContext.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Name = "Other Key",
            KeyHash = "otherhash",
            Scopes = ["read"]
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ListApiKeys.Handler(_dbContext, NullLogger<ListApiKeys.Handler>.Instance);
        var result = await handler.Handle(new ListApiKeys.Query { UserId = _userId }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyListWhenNoKeys()
    {
        var handler = new ListApiKeys.Handler(_dbContext, NullLogger<ListApiKeys.Handler>.Instance);
        var result = await handler.Handle(new ListApiKeys.Query { UserId = _userId }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
