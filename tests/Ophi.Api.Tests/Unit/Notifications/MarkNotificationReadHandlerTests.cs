using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Ophi.Api.Common.Exceptions;
using Ophi.Api.Features.Notifications;
using Ophi.TestHelpers;
using Ophi.Domain.Entities;
using Ophi.Domain.Enums;
using Ophi.Infrastructure.Persistence;

namespace Ophi.Api.Tests.Unit.Notifications;

public class MarkNotificationReadHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OphiDbContext _dbContext;
    private readonly MarkNotificationRead.Handler _handler;
    private readonly Guid _testUserId;

    public MarkNotificationReadHandlerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _handler = new MarkNotificationRead.Handler(_dbContext, NullLogger<MarkNotificationRead.Handler>.Instance);
        _testUserId = Guid.NewGuid();

        _dbContext.Users.Add(new User
        {
            Id = _testUserId,
            Email = "test@example.com",
            Name = "Test User",
            PasswordHash = "hash"
        });
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Handle_MarksNotificationAsRead()
    {
        var notification = CreateNotification();
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkNotificationRead.Command(notification.Id, _testUserId);

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = await _dbContext.Notifications.FindAsync([notification.Id], TestContext.Current.CancellationToken);
        updated!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AlreadyRead_StaysRead()
    {
        var notification = CreateNotification();
        notification.IsRead = true;
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkNotificationRead.Command(notification.Id, _testUserId);

        await _handler.Handle(command, TestContext.Current.CancellationToken);

        var updated = await _dbContext.Notifications.FindAsync([notification.Id], TestContext.Current.CancellationToken);
        updated!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFoundException()
    {
        var command = new MarkNotificationRead.Command(Guid.NewGuid(), _testUserId);

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_OtherUsersNotification_ThrowsNotFoundException()
    {
        var otherUserId = Guid.NewGuid();
        _dbContext.Users.Add(new User
        {
            Id = otherUserId,
            Email = "other@example.com",
            Name = "Other",
            PasswordHash = "hash"
        });

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Title = "Other's",
            Message = "msg",
            Type = NotificationType.PriceAlert
        };
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new MarkNotificationRead.Command(notification.Id, _testUserId);

        var act = () => _handler.Handle(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private Notification CreateNotification()
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Title = "Test",
            Message = "msg",
            Type = NotificationType.PriceAlert
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
