using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.ApiKeys;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;

namespace Ophi.Api.Tests.Unit.ApiKeys;

public class DeleteApiKeyTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly Ophi.Infrastructure.Persistence.OphiDbContext _dbContext;
    private readonly Guid _userId;

    public DeleteApiKeyTests()
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
    public async Task Handle_DeletesOwnKey()
    {
        var keyId = Guid.NewGuid();
        _dbContext.ApiKeys.Add(new ApiKey
        {
            Id = keyId,
            UserId = _userId,
            Name = "To Delete",
            KeyHash = "delhash",
            Scopes = ["read"]
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DeleteApiKey.Handler(_dbContext, NullLogger<DeleteApiKey.Handler>.Instance);
        await handler.Handle(new DeleteApiKey.Command(keyId, _userId), CancellationToken.None);

        _dbContext.ApiKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ThrowsNotFound_WhenKeyDoesNotExist()
    {
        var handler = new DeleteApiKey.Handler(_dbContext, NullLogger<DeleteApiKey.Handler>.Instance);

        Func<Task> act = () => handler.Handle(new DeleteApiKey.Command(Guid.NewGuid(), _userId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ThrowsNotFound_WhenKeyBelongsToOtherUser()
    {
        var keyId = Guid.NewGuid();
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
            Id = keyId,
            UserId = otherUserId,
            Name = "Other's Key",
            KeyHash = "otherhash",
            Scopes = ["read"]
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new DeleteApiKey.Handler(_dbContext, NullLogger<DeleteApiKey.Handler>.Instance);

        Func<Task> act = () => handler.Handle(new DeleteApiKey.Command(keyId, _userId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
